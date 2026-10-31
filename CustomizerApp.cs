using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace StreamDeckCustomizer
{
    static class Program
    {
        private const string WindowTitle = "Stream Deck Customizer";

        [STAThread]
        static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, "StreamDeckCustomizerApp", out created))
            {
                if (!created)
                {
                    ActivateExistingWindow();
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int cmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private static void ActivateExistingWindow()
        {
            IntPtr hwnd = FindWindow(null, WindowTitle);
            if (hwnd == IntPtr.Zero) return;
            ShowWindow(hwnd, 9); // SW_RESTORE
            SetForegroundWindow(hwnd);
        }
    }

    sealed class MainForm : Form
    {
        private const string Host = "127.0.0.1";
        private const int Port = 8787;
        private const string HomeUrl = "http://127.0.0.1:8787/";

        private readonly string root;
        private readonly StringBuilder log = new StringBuilder();
        private readonly Label status;

        private WebView2 web;
        private Process server;
        private bool serverStartedHere;
        private bool closing;

        public MainForm()
        {
            root = FindRoot();

            Text = "Stream Deck Customizer";
            AutoScaleMode = AutoScaleMode.None;

            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            int width = Math.Min(1280, Math.Max(880, area.Width - 60));
            int height = Math.Min(820, Math.Max(600, area.Height - 60));
            ClientSize = new Size(width, height);
            MinimumSize = new Size(880, 560);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(7, 12, 22);

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            status = new Label();
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleCenter;
            status.BackColor = Color.FromArgb(7, 12, 22);
            status.ForeColor = Color.FromArgb(148, 163, 184);
            status.Font = new Font("Segoe UI", 11f);
            status.Text = "Starting Stream Deck Customizer\u2026";
            Controls.Add(status);
        }

        private static string FindRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            if (File.Exists(Path.Combine(dir, "customizer", "server.js"))) return dir;

            string parent = Path.GetDirectoryName(dir);
            if (parent != null && File.Exists(Path.Combine(parent, "customizer", "server.js"))) return parent;

            return dir;
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            await StartAsync();
        }

        private async Task StartAsync()
        {
            try
            {
                if (!await EnsureServerAsync()) return;

                status.Text = "Loading the customizer\u2026";

                string dataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "StreamDeckCustomizer", "WebView2");
                Directory.CreateDirectory(dataDir);

                web = new WebView2();
                web.Dock = DockStyle.Fill;
                web.DefaultBackgroundColor = Color.FromArgb(7, 12, 22);
                web.Visible = false;
                Controls.Add(web);

                CoreWebView2Environment environment =
                    await CoreWebView2Environment.CreateAsync(null, dataDir, null);
                await web.EnsureCoreWebView2Async(environment);

                CoreWebView2 core = web.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.NewWindowRequested += OnNewWindow;
                core.NavigationCompleted += OnNavigationCompleted;
                core.ProcessFailed += OnProcessFailed;

                core.Navigate(HomeUrl);
            }
            catch (Exception ex)
            {
                ShowFatal("The customizer window could not start.\r\n\r\n" + ex.Message);
            }
        }

        private async Task<bool> EnsureServerAsync()
        {
            if (PortOpen(300)) return true; // already running (for example from the old .bat)

            string nodeExe = FindNode();
            if (nodeExe == null)
            {
                ShowFatal("Node.js was not found.\r\n\r\n" +
                          "Install the LTS version from https://nodejs.org and start this app again.");
                return false;
            }

            string serverJs = Path.Combine(root, "customizer", "server.js");
            if (!File.Exists(serverJs))
            {
                ShowFatal("customizer\\server.js was not found next to this app.\r\n\r\n" + root);
                return false;
            }

            try
            {
                ProcessStartInfo info = new ProcessStartInfo(nodeExe, "\"" + serverJs + "\"");
                info.WorkingDirectory = root;
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;

                server = new Process();
                server.StartInfo = info;
                server.OutputDataReceived += delegate(object s, DataReceivedEventArgs a) { AddLog(a.Data); };
                server.ErrorDataReceived += delegate(object s, DataReceivedEventArgs a) { AddLog(a.Data); };
                server.Start();
                server.BeginOutputReadLine();
                server.BeginErrorReadLine();
                serverStartedHere = true;
            }
            catch (Exception ex)
            {
                ShowFatal("The local server could not start.\r\n\r\n" + ex.Message);
                return false;
            }

            for (int i = 0; i < 100; i++)
            {
                if (PortOpen(250)) return true;
                if (server.HasExited) break;
                await Task.Delay(200);
            }

            ShowFatal("The local server did not respond on port " + Port + ".\r\n\r\n" + Tail(1200));
            return false;
        }

        private static string FindNode()
        {
            string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string raw in pathVariable.Split(';'))
            {
                string dir = raw.Trim().Trim('"');
                if (dir.Length == 0) continue;
                try
                {
                    string candidate = Path.Combine(dir, "node.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }

            string[] known =
            {
                @"C:\Program Files\nodejs\node.exe",
                @"C:\Program Files (x86)\nodejs\node.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                             @"Programs\nodejs\node.exe")
            };

            foreach (string candidate in known)
            {
                if (File.Exists(candidate)) return candidate;
            }

            return null;
        }

        private static bool PortOpen(int timeoutMs)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    IAsyncResult result = client.BeginConnect(Host, Port, null, null);
                    if (!result.AsyncWaitHandle.WaitOne(timeoutMs)) return false;
                    client.EndConnect(result);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (closing) return;

            if (e.IsSuccess)
            {
                web.Visible = true;
                web.BringToFront();
                status.Visible = false;
                web.Focus();
            }
            else
            {
                status.Text = "The customizer could not load.\r\n\r\n" + e.WebErrorStatus;
                status.Visible = true;
            }
        }

        private void OnProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            if (closing) return;
            status.Text = "The browser process stopped unexpectedly (" + e.ProcessFailedKind + ").\r\n" +
                          "Close and start the app again.";
            status.Visible = true;
            web.Visible = false;
        }

        private void OnNewWindow(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            OpenExternal(e.Uri);
        }

        private static void OpenExternal(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return;

            try { Process.Start(url); }
            catch { }
        }

        private void AddLog(string line)
        {
            if (line == null) return;
            lock (log)
            {
                log.AppendLine(line);
                if (log.Length > 4000) log.Remove(0, log.Length - 4000);
            }
        }

        private string Tail(int max)
        {
            lock (log)
            {
                string text = log.ToString().Trim();
                if (text.Length == 0) return "Run Start Customizer.bat to see the server output.";
                if (text.Length <= max) return text;
                return text.Substring(text.Length - max);
            }
        }

        private void ShowFatal(string message)
        {
            status.Text = message;
            status.Visible = true;
            MessageBox.Show(this, message, "Stream Deck Customizer",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            closing = true;

            if (serverStartedHere && server != null)
            {
                try { if (!server.HasExited) server.Kill(); }
                catch { }
                try { server.Dispose(); }
                catch { }
            }

            if (web != null)
            {
                try { web.Dispose(); }
                catch { }
            }

            base.OnFormClosing(e);
        }
    }
}
