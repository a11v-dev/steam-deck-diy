# Stream Deck Panel

A lightweight, Stream Deck–style launcher for a **second screen**, written in C# WinForms with **no installs or third-party packages**. It shows a grid of icon-only buttons and can launch apps, open websites, press media keys, type text, or run commands.

It also includes a **local customizer website** (Node.js, dependency-free) where you can edit every button, pick installed apps, upload your own images/GIFs, and save the layout.

## Features

- Icon-only grid on a chosen monitor (default: screen 2) — app names are hover tooltips
- Real app icons, with shortcut-arrow overlays removed
- **Custom images** (PNG, JPG, BMP) rendered to fit each button
- **Animated GIFs** play on the deck
- Local customizer at `http://127.0.0.1:8787`
  - Click a tile to edit it
  - Pick from installed Start Menu apps
  - Upload images or drag them onto a tile
  - Drag tiles to swap positions
  - Change screen, title, columns, rows, labels
  - Save writes `config.txt` and restarts the deck
- Everything runs locally; the server binds only to `127.0.0.1`

## Requirements

- Windows 10 or 11
- .NET Framework 4.x (included with Windows)
- [Node.js](https://nodejs.org) LTS — only for the customizer website

## Quick start

1. Run **`Start Stream Deck.bat`** (or the compiled `StreamDeckPanel.exe`).
2. Run **`Start Customizer.bat`** — it starts the local server and opens the website.
3. Edit buttons, then click **Save changes**.

## Build from source

The app compiles with the C# compiler that ships with .NET Framework:

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe ^
  /nologo /target:winexe /out:StreamDeckPanel.exe ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  StreamDeckPanel.cs
```

Stop the running deck before rebuilding.

## Config format

`config.txt` is pipe-delimited:

```
monitor=2
columns=5
rows=4
showlabels=0
title=STREAM DECK

Label|action|value|#color|iconPath
Brave|cmd|start "" "C:\Path\Brave.lnk"|#1E293B|
```

Supported actions: `app`, `cmd`, `url`, `key`, `text`, `exit`, `none`.

- `key` accepts `volumeup`, `volumedown`, `mute`, `playpause`, `next`, `prev`, `stop`, or any SendKeys string such as `^v`
- `iconPath` can point to an `.exe`, `.ico`, `.lnk`, or an image (`.png`, `.jpg`, `.bmp`, `.gif`)

The customizer rewrites this file when you save.

## Project layout

```
StreamDeckPanel.cs      deck application (WinForms)
StreamDeckPanel.exe     compiled deck
config.txt              current layout
images/                 uploaded images and GIFs
customizer/server.js    local API server
customizer/index.html   customizer UI
Start Stream Deck.bat   launch the deck
Start Customizer.bat    start the server + open the site
```

## Notes

- The deck is designed for a small touch display (for example a phone/tablet used as a spacedesk screen). After a tap, the mouse pointer is returned to its previous position on the main screen so it does not stay on the deck.
- To use a different screen, change **Screen** in the customizer, or `monitor=` in `config.txt`.
- Uploaded images are stored in `images/` and referenced by absolute path in `config.txt`.
