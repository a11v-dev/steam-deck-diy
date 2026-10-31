using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace StreamDeckPanel
{
    static class Native
    {
        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int X, int Y);
        [DllImport("user32.dll")]
        public static extern IntPtr GetMessageExtraInfo();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        public const uint SHGFI_ICON = 0x100;
        public const uint SHGFI_LARGEICON = 0x0;
    }

    public static class IconLoader
    {
        public static bool IsImageFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" ||
                   ext == ".bmp" || ext == ".gif";
        }

        public static Image Load(string path, int size)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

                // Custom user images (photos, logos, etc.) are drawn directly.
                // Keep the image's real aspect ratio; the deck scales it to fit the button.
                if (IsImageFile(path))
                {
                    using (Image src = Image.FromFile(path))
                    {
                        float scale = Math.Min((float)size / src.Width, (float)size / src.Height);
                        int w = Math.Max(1, (int)Math.Round(src.Width * scale));
                        int h = Math.Max(1, (int)Math.Round(src.Height * scale));
                        Bitmap bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.Transparent);
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawImage(src, new Rectangle(0, 0, w, h));
                        }
                        return bmp;
                    }
                }

                Native.SHFILEINFO fi = new Native.SHFILEINFO();
                IntPtr res = Native.SHGetFileInfo(path, 0, ref fi, (uint)Marshal.SizeOf(fi),
                    Native.SHGFI_ICON | Native.SHGFI_LARGEICON);
                if (res == IntPtr.Zero || fi.hIcon == IntPtr.Zero) return null;
                try
                {
                    using (Icon ic = Icon.FromHandle(fi.hIcon))
                        return ToBitmap(ic, size);
                }
                finally { Native.DestroyIcon(fi.hIcon); }
            }
            catch { return null; }
        }

        static Bitmap ToBitmap(Icon ic, int size)
        {
            Bitmap bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawIcon(ic, new Rectangle(0, 0, size, size));
            }
            return bmp;
        }

        public static List<Image> LoadAnimation(string path, int maxSize, out List<int> delays)
        {
            delays = new List<int>();
            List<Image> frames = new List<Image>();
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return frames;

                using (Image src = Image.FromFile(path))
                {
                    if (src.FrameDimensionsList.Length == 0) return frames;
                    FrameDimension dim = new FrameDimension(src.FrameDimensionsList[0]);
                    int count = src.GetFrameCount(dim);
                    if (count <= 1) return frames;

                    // Frame delays are stored in hundredths of a second.
                    int[] raw = null;
                    try
                    {
                        PropertyItem item = src.GetPropertyItem(0x5100);
                        raw = new int[item.Value.Length / 4];
                        for (int i = 0; i < raw.Length; i++) raw[i] = BitConverter.ToInt32(item.Value, i * 4);
                    }
                    catch { }

                    float scale = Math.Min((float)maxSize / src.Width, (float)maxSize / src.Height);
                    int w = Math.Max(1, (int)Math.Round(src.Width * scale));
                    int h = Math.Max(1, (int)Math.Round(src.Height * scale));

                    int limit = Math.Min(count, 120);
                    for (int i = 0; i < limit; i++)
                    {
                        src.SelectActiveFrame(dim, i);
                        Bitmap bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.Transparent);
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawImage(src, new Rectangle(0, 0, w, h));
                        }
                        frames.Add(bmp);
                        int d = (raw != null && i < raw.Length && raw[i] > 0) ? raw[i] : 10;
                        delays.Add(Math.Max(30, Math.Min(1000, d * 10)));
                    }
                }
            }
            catch
            {
                frames.Clear();
                delays.Clear();
            }
            return frames;
        }

        public static Image PowerIcon(int size)
        {
            Bitmap bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Pen p = new Pen(Color.FromArgb(254, 226, 226), Math.Max(2f, size / 10f)))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    RectangleF r = new RectangleF(size * 0.17f, size * 0.22f, size * 0.66f, size * 0.66f);
                    g.DrawArc(p, r, -55f, 290f);
                    g.DrawLine(p, size / 2f, size * 0.07f, size / 2f, size * 0.52f);
                }
            }
            return bmp;
        }
    }

    class DeckButton : Control
    {
        public Color FillColor = Color.FromArgb(30, 41, 59);
        public Color HoverColor = Color.FromArgb(51, 65, 85);
        public Color PressColor = Color.FromArgb(59, 130, 246);
        public Color BorderColor = Color.FromArgb(51, 65, 85);
        public Color TextColor = Color.FromArgb(226, 232, 240);
        public string SubText = "";
        public Image IconImage = null;
        public bool ShowLabel = true;
        public bool BigIcon = false;
        private bool hover = false;
        private bool down = false;
        private List<Image> animFrames = null;
        private List<int> animDelays = null;
        private int animIndex = 0;
        private Timer animTimer = null;

        public void PlayAnimation(string path, int maxSize)
        {
            List<int> delays;
            List<Image> frames = IconLoader.LoadAnimation(path, maxSize, out delays);
            if (frames.Count < 2) return;

            animFrames = frames;
            animDelays = delays;
            animIndex = 0;
            IconImage = animFrames[0];

            animTimer = new Timer();
            animTimer.Interval = animDelays[0];
            animTimer.Tick += delegate { AdvanceFrame(); };
            animTimer.Start();
            Invalidate();
        }

        void AdvanceFrame()
        {
            if (animFrames == null || animFrames.Count < 2) return;
            animIndex = (animIndex + 1) % animFrames.Count;
            IconImage = animFrames[animIndex];
            if (animTimer != null) animTimer.Interval = animDelays[animIndex];
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (animTimer != null) { animTimer.Stop(); animTimer.Dispose(); animTimer = null; }
                if (animFrames != null)
                {
                    IconImage = null;
                    foreach (Image img in animFrames) img.Dispose();
                    animFrames = null;
                }
            }
            base.Dispose(disposing);
        }

        public DeckButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public bool LastClickWasTouch = false;

        static bool IsTouchOrPenMessage()
        {
            // Windows marks mouse messages that were really generated by touch/pen input.
            // Normal mouse and touchpad messages use a different (or zero) signature.
            long info = Native.GetMessageExtraInfo().ToInt64();
            return (info & 0xFFFFFF00L) == 0xFF515700L && (info & 0xC0L) != 0;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { LastClickWasTouch = IsTouchOrPenMessage(); down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            Color fill = down ? PressColor : (hover ? HoverColor : FillColor);
            Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3);
            int rad = Math.Max(6, Math.Min(14, Height / 6));
            using (GraphicsPath path = RoundedRect(r, rad))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, path);
                using (Pen p = new Pen((hover || down) ? PressColor : BorderColor, 1.4f)) g.DrawPath(p, path);
            }

            bool compact = Height < 72;
            if (!ShowLabel)
            {
                if (IconImage != null)
                {
                    if (BigIcon)
                    {
                        Rectangle box = new Rectangle(4, 3, Math.Max(8, Width - 8), Math.Max(8, Height - 6));
                        DrawFit(g, IconImage, box);
                    }
                    else
                    {
                        int big = (int)(Math.Min(Width, Height) * 0.56);
                        if (big < 18) big = Math.Max(12, Math.Min(Width, Height) - 8);
                        int ix = (Width - big) / 2;
                        int iy = (Height - big) / 2;
                        g.DrawImage(IconImage, new Rectangle(ix, iy, big, big));
                    }
                }
                else
                {
                    Rectangle tr0 = new Rectangle(4, 2, Width - 8, Height - 4);
                    TextRenderer.DrawText(g, Text, Font, tr0, TextColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
            }
            else if (IconImage != null && compact)
            {
                int isz = Math.Max(16, Math.Min(BigIcon ? 34 : 30, Height - 12));
                int iy = (Height - isz) / 2;
                DrawFit(g, IconImage, new Rectangle(8, iy, isz, isz));
                Rectangle tr = new Rectangle(8 + isz + 8, 2, Width - (8 + isz + 8) - 6, Height - 4);
                TextRenderer.DrawText(g, Text, Font, tr, TextColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            else if (IconImage != null)
            {
                int isz = BigIcon ? 36 : 30;
                DrawFit(g, IconImage, new Rectangle((Width - isz) / 2, 9, isz, isz));
                Rectangle tr = new Rectangle(4, 9 + isz + 2, Width - 8, Height - (9 + isz + 2) - 4);
                TextRenderer.DrawText(g, Text, Font, tr, TextColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            else
            {
                Rectangle tr = compact
                    ? new Rectangle(6, 2, Width - 12, Height - 4)
                    : new Rectangle(6, 4, Width - 12, Height - 24);
                TextRenderer.DrawText(g, Text, Font, tr, TextColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (!string.IsNullOrEmpty(SubText) && !compact)
                {
                    using (Font f = new Font("Segoe UI", 7f))
                    {
                        Rectangle sr = new Rectangle(4, Height - 20, Width - 8, 16);
                        TextRenderer.DrawText(g, SubText, f, sr, Color.FromArgb(148, 163, 184),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    }
                }
            }
        }

        static void DrawFit(Graphics g, Image img, Rectangle box)
        {
            if (img == null || box.Width < 2 || box.Height < 2) return;
            float scale = Math.Min((float)box.Width / img.Width, (float)box.Height / img.Height);
            int w = Math.Max(1, (int)Math.Round(img.Width * scale));
            int h = Math.Max(1, (int)Math.Round(img.Height * scale));
            g.DrawImage(img, new Rectangle(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h));
        }

        static GraphicsPath RoundedRect(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    class DeckForm : Form
    {
        class Btn
        {
            public string Label;
            public string Action;
            public string Value;
            public Color Color;
            public string IconPath;
        }

        List<Btn> items = new List<Btn>();
        List<DeckButton> ui = new List<DeckButton>();
        Label lblTitle;
        DeckButton btnClose;
        int columns = 4;
        int rows = 8;
        int monitorIndex = 2;
        string title = "STREAM DECK";
        bool showLabels = true;
        Color bg = Color.FromArgb(15, 23, 42);
        ToolTip tips = new ToolTip();
        Timer cursorWatch = null;
        Point lastOutside = Point.Empty;

        public DeckForm(string cfg)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            BackColor = bg;
            KeyPreview = true;
            DoubleBuffered = true;
            ShowInTaskbar = false;
            TopMost = true;
            Opacity = 0.9;

            LoadConfig(cfg);
            PlaceOnMonitor();

            // Remember where the pointer was before a touch dragged it onto the deck,
            // so it can be sent back to the PC screen after each tap.
            lastOutside = Cursor.Position;
            if (Bounds.Contains(lastOutside))
            {
                Rectangle pb = Screen.PrimaryScreen.Bounds;
                lastOutside = new Point(pb.X + pb.Width / 2, pb.Y + pb.Height / 2);
            }
            cursorWatch = new Timer();
            cursorWatch.Interval = 150;
            cursorWatch.Tick += delegate
            {
                Point p = Cursor.Position;
                if (!Bounds.Contains(p)) lastOutside = p;
            };
            cursorWatch.Start();

            lblTitle = new Label();
            lblTitle.Text = title;
            lblTitle.ForeColor = Color.FromArgb(96, 165, 250);
            lblTitle.BackColor = Color.Transparent;
            lblTitle.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            lblTitle.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(lblTitle);

            btnClose = new DeckButton();
            btnClose.Text = "X";
            btnClose.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            btnClose.FillColor = Color.FromArgb(127, 29, 29);
            btnClose.HoverColor = Color.FromArgb(185, 28, 28);
            btnClose.TextColor = Color.White;
            btnClose.SubText = "";
            btnClose.Click += delegate
            {
                RestorePointer();
                Close();
            };
            Controls.Add(btnClose);

            for (int i = 0; i < items.Count; i++)
            {
                Btn b = items[i];
                DeckButton db = new DeckButton();
                db.Text = b.Label;
                db.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                db.FillColor = b.Color;
                db.SubText = b.Action.ToUpper();
                db.ShowLabel = showLabels;
                string iconSource = IconSourceFor(b);
                bool isCustomImage = IconLoader.IsImageFile(iconSource);
                db.BigIcon = isCustomImage;
                db.IconImage = IconLoader.Load(iconSource, isCustomImage ? 160 : 32);
                if (isCustomImage && Path.GetExtension(iconSource).ToLowerInvariant() == ".gif")
                    db.PlayAnimation(iconSource, 160);
                if ((b.Action ?? "").ToLower() == "exit")
                    db.IconImage = IconLoader.PowerIcon(30);
                db.Tag = b;
                tips.SetToolTip(db, b.Label);
                db.Click += OnDeckClick;
                Controls.Add(db);
                ui.Add(db);
            }

            Relayout();
        }

        void OnDeckClick(object sender, EventArgs e)
        {
            DeckButton db = sender as DeckButton;
            if (db == null) return;
            RestorePointer();
            Btn b = db.Tag as Btn;
            if (b != null) RunAction(b);
        }

        void RestorePointer()
        {
            try
            {
                if (!lastOutside.IsEmpty) Native.SetCursorPos(lastOutside.X, lastOutside.Y);
            }
            catch { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (cursorWatch != null) { cursorWatch.Stop(); cursorWatch.Dispose(); cursorWatch = null; }
            Cursor = Cursors.Default;
            base.OnFormClosed(e);
        }

        void RunAction(Btn b)
        {
            string act = (b.Action ?? "").ToLower();
            try
            {
                if (act == "none") return;
                if (act == "app")
                {
                    Process.Start(b.Value);
                }
                else if (act == "url")
                {
                    ProcessStartInfo psi = new ProcessStartInfo(b.Value);
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
                else if (act == "cmd")
                {
                    ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c " + b.Value);
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                    Process.Start(psi);
                }
                else if (act == "key")
                {
                    DoKey(b.Value);
                }
                else if (act == "text")
                {
                    SendKeys.SendWait(b.Value);
                }
                else if (act == "exit")
                {
                    Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Action failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void DoKey(string k)
        {
            string v = (k ?? "").ToLower();
            if (v == "volumeup") { Tap(0xAF); return; }
            if (v == "volumedown") { Tap(0xAE); return; }
            if (v == "mute") { Tap(0xAD); return; }
            if (v == "next") { Tap(0xB0); return; }
            if (v == "prev") { Tap(0xB1); return; }
            if (v == "playpause") { Tap(0xB3); return; }
            if (v == "stop") { Tap(0xB2); return; }
            SendKeys.SendWait(k);
        }

        void Tap(byte vk)
        {
            Native.keybd_event(vk, 0, 0, UIntPtr.Zero);
            Native.keybd_event(vk, 0, 2, UIntPtr.Zero);
        }

        void PlaceOnMonitor()
        {
            Screen[] s = Screen.AllScreens;
            int idx = monitorIndex - 1;
            if (idx < 0 || idx >= s.Length) idx = s.Length > 1 ? 1 : 0;
            Bounds = s[idx].Bounds;
        }

        void Relayout()
        {
            if (lblTitle == null || btnClose == null || ui == null) return;
            int pad = 12;
            int header = 44;
            int gap = 7;
            int useRows = Math.Max(rows, (ui.Count + columns - 1) / Math.Max(1, columns));
            lblTitle.Bounds = new Rectangle(pad, 2, Math.Max(10, ClientSize.Width - pad * 2 - 48), header - 8);
            btnClose.Bounds = new Rectangle(ClientSize.Width - pad - 34, 7, 34, 28);

            int availW = ClientSize.Width - pad * 2 - gap * (columns - 1);
            int availH = ClientSize.Height - header - pad - gap * (useRows - 1);
            int bw = Math.Max(20, availW / Math.Max(1, columns));
            int bh = Math.Max(20, availH / Math.Max(1, useRows));

            for (int i = 0; i < ui.Count; i++)
            {
                int r = i / columns;
                int c = i % columns;
                int x = pad + c * (bw + gap);
                int y = header + r * (bh + gap);
                ui[i].Bounds = new Rectangle(x, y, bw, bh);
                ui[i].Font = new Font("Segoe UI", bh >= 72 ? 10f : 9f, FontStyle.Bold);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (ui != null) Relayout();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Relayout();
            Activate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Close();
            base.OnKeyDown(e);
        }

        void LoadConfig(string path)
        {
            string[] lines = File.ReadAllLines(path);
            List<Btn> list = new List<Btn>();
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("monitor=", StringComparison.OrdinalIgnoreCase))
                {
                    int v;
                    if (int.TryParse(line.Substring(8), out v)) monitorIndex = v;
                    continue;
                }
                if (line.StartsWith("columns=", StringComparison.OrdinalIgnoreCase))
                {
                    int v;
                    if (int.TryParse(line.Substring(8), out v) && v > 0) columns = v;
                    continue;
                }
                if (line.StartsWith("rows=", StringComparison.OrdinalIgnoreCase))
                {
                    int v;
                    if (int.TryParse(line.Substring(5), out v) && v > 0) rows = v;
                    continue;
                }
                if (line.StartsWith("title=", StringComparison.OrdinalIgnoreCase))
                {
                    title = line.Substring(6);
                    continue;
                }
                if (line.StartsWith("showlabels=", StringComparison.OrdinalIgnoreCase))
                {
                    int v;
                    if (int.TryParse(line.Substring(11), out v)) showLabels = v != 0;
                    continue;
                }
                string[] parts = line.Split('|');
                if (parts.Length >= 3)
                {
                    Btn b = new Btn();
                    b.Label = parts[0].Trim();
                    b.Action = parts[1].Trim();
                    b.Value = parts[2].Trim();
                    b.Color = Color.FromArgb(30, 41, 59);
                    if (parts.Length >= 4 && parts[3].Trim().Length > 0)
                        b.Color = ParseColor(parts[3].Trim());
                    if (parts.Length >= 5 && parts[4].Trim().Length > 0)
                        b.IconPath = parts[4].Trim();
                    list.Add(b);
                }
            }
            items = list;
        }

        internal static string IconFileFor(string action, string value, string iconPath)
        {
            Btn b = new Btn();
            b.Action = action;
            b.Value = value;
            b.IconPath = iconPath;
            return IconSourceFor(b);
        }

        static string IconSourceFor(Btn b)
        {
            // An explicit icon path in the config always wins.
            if (!string.IsNullOrEmpty(b.IconPath))
            {
                string ip = Environment.ExpandEnvironmentVariables(b.IconPath.Trim().Trim('"'));
                if (File.Exists(ip)) return ip;
            }

            string act = (b.Action ?? "").ToLower();
            string candidate = null;
            if (act == "app") candidate = ResolveExe(b.Value);
            else if (act == "cmd")
            {
                Match m = Regex.Match(b.Value ?? "", "\"([^\"]+\\.(?:lnk|exe|ico))\"", RegexOptions.IgnoreCase);
                if (m.Success) candidate = m.Groups[1].Value;
            }
            if (string.IsNullOrEmpty(candidate) || !File.Exists(candidate)) return candidate;

            // Loading an icon straight from a .lnk adds the shortcut arrow overlay.
            // Read the shortcut's real icon location / target instead.
            if (Path.GetExtension(candidate).ToLower() == ".lnk")
                return ResolveShortcutIcon(candidate);

            return candidate;
        }

        static string ResolveShortcutIcon(string lnkPath)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return lnkPath;
                object shell = Activator.CreateInstance(shellType);
                object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                Type scType = sc.GetType();

                string iconLoc = (scType.InvokeMember("IconLocation", BindingFlags.GetProperty, null, sc, null) as string) ?? "";
                string target = (scType.InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null) as string) ?? "";
                iconLoc = Environment.ExpandEnvironmentVariables(iconLoc.Trim());
                target = Environment.ExpandEnvironmentVariables(target.Trim());

                string file = iconLoc;
                int idx = 0;
                int comma = iconLoc.LastIndexOf(',');
                if (comma >= 0)
                {
                    file = iconLoc.Substring(0, comma).Trim().Trim('"');
                    int.TryParse(iconLoc.Substring(comma + 1).Trim(), out idx);
                }

                // Non-zero resource index means the file's first icon is the wrong one,
                // so fall back to the .lnk (small arrow, but correct icon).
                if (file.Length > 0 && File.Exists(file) && (idx == 0 || Path.GetExtension(file).ToLower() == ".ico"))
                    return file;
                if (file.Length == 0 && idx == 0 && target.Length > 0 && File.Exists(target))
                    return target;
                return lnkPath;
            }
            catch { return lnkPath; }
        }

        static string ResolveExe(string v)
        {
            if (string.IsNullOrEmpty(v)) return null;
            v = v.Trim();
            if (v.StartsWith("\"") && v.EndsWith("\"") && v.Length > 1) v = v.Substring(1, v.Length - 2);
            v = Environment.ExpandEnvironmentVariables(v);
            if (File.Exists(v)) return v;
            string win = Environment.GetEnvironmentVariable("WINDIR");
            if (!Path.IsPathRooted(v) && win != null)
            {
                string c = Path.Combine(win, v);
                if (File.Exists(c)) return c;
                c = Path.Combine(Path.Combine(win, "System32"), v);
                if (File.Exists(c)) return c;
            }
            return null;
        }

        static Color ParseColor(string s)
        {
            try
            {
                int v = int.Parse(s.TrimStart('#'), System.Globalization.NumberStyles.HexNumber);
                return Color.FromArgb((v >> 16) & 255, (v >> 8) & 255, v & 255);
            }
            catch { return Color.FromArgb(30, 41, 59); }
        }
    }

    static class Program
    {
        const string DefaultConfig =
@"monitor=2
columns=4
rows=8
showlabels=0
title=STREAM DECK

Vol Up|key|volumeup
Vol Down|key|volumedown
Mute|key|mute
Play / Pause|key|playpause
Browser|url|https://www.google.com
Files|app|explorer.exe
Notepad|app|notepad.exe
Settings|app|ms-settings:
Screenshot|cmd|start ms-screenclip:
Exit|exit|
";

        [STAThread]
        static void Main(string[] args)
        {
            // CLI helper used by the customizer website to render icon previews.
            // Reads a 4-line request file: action, value, iconPath, outputPng.
            if (args != null && args.Length >= 2 && args[0] == "--icon-request")
            {
                try
                {
                    string[] req = File.ReadAllLines(args[1]);
                    if (req.Length >= 4)
                    {
                        string src = DeckForm.IconFileFor(req[0], req[1], req[2]);
                        bool isImg = IconLoader.IsImageFile(src);
                        Image img = IconLoader.Load(src, isImg ? 96 : 48);
                        if (img != null)
                        {
                            string dir = Path.GetDirectoryName(req[3]);
                            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                            img.Save(req[3], System.Drawing.Imaging.ImageFormat.Png);
                            img.Dispose();
                        }
                    }
                }
                catch { }
                return;
            }

            try
            {
                try { Native.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string cfg = Path.Combine(Application.StartupPath, "config.txt");
                if (!File.Exists(cfg)) File.WriteAllText(cfg, DefaultConfig);
                Application.Run(new DeckForm(cfg));
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(Application.StartupPath, "error.log"), ex.ToString()); } catch { }
                MessageBox.Show(ex.ToString(), "Stream Deck error");
            }
        }
    }
}
