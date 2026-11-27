# Snapmark

A small native screenshot and annotation tool for macOS and Windows, built as a personal replacement for Awesome Screenshot / Lightshot.

Click the icon (or press the hotkey), the screens dim, drag an area, mark it up in place, click Copy, paste anywhere.

## Features

- Freezes and dims every display; drag to select an area, with resize handles and a pixel-size label
- Draw directly on the selection: arrow (default), line, rectangle, oval
- Arrowheads stay a sensible, fixed size
- Color picker (red by default) and a line-thickness box (1–50 px, default 2 px)
- Copy to clipboard at full Retina / high-DPI resolution, or save to the Desktop
- macOS: starts from the Dock icon, the menu bar icon, or the global hotkey ⌘⇧2
- Windows: starts from the Start menu, the tray icon, or the global hotkey Ctrl+Shift+2

## Shortcuts

| macOS | Windows | Action |
| --- | --- | --- |
| ⌘⇧2 | Ctrl+Shift+2 | Start a capture (global) |
| A / L / R / O | A / L / R / O | Arrow / Line / Rectangle / Oval |
| W | W | Edit line thickness (Windows: also scroll over the tool panel) |
| Shift + drag | Shift + drag | 45° lines, squares, circles |
| ⌘Z | Ctrl+Z | Undo |
| Return or ⌘C | Enter or Ctrl+C | Copy and close |
| ⌘S | Ctrl+S | Save to Desktop and close |
| Esc | Esc | Cancel |

macOS: right-click the menu bar icon to quit. Windows: right-click the tray icon for **Quit** and **Start with Windows**.

## Build and install (macOS)

Requires macOS 14+ on Apple silicon and the Xcode command-line tools.

```bash
./build.sh
```

This compiles `Sources/main.swift`, generates the app icon, signs the app (with your Apple Development identity if one exists, otherwise ad hoc), builds `build/Snapmark.dmg`, removes any older Snapmark copies, and installs and launches `/Applications/Snapmark.app`.

On first use, allow Snapmark in **System Settings > Privacy & Security > Screen Recording**.

## Build and install (Windows)

Requires Windows 10/11 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
powershell -ExecutionPolicy Bypass -File .\build-windows.ps1
```

This generates the icon, publishes a single-file `build\windows\Snapmark.exe`, installs it to `%LOCALAPPDATA%\Programs\Snapmark`, adds a **Snapmark** Start menu shortcut, and starts it in the tray. Pass `-NoInstall` to only build. No screen-capture permission is needed on Windows.

Captures cover all monitors at once (you can select across them) at full physical resolution on scaled displays. The clipboard gets a PNG plus a standard bitmap, so pasting works in browsers, Slack, Office, Paint, etc.

## Project layout

- `Sources/main.swift` – the whole macOS app (AppKit + ScreenCaptureKit)
- `Icon/make_icon.swift` – draws the app icon and writes the `.iconset`
- `Info.plist` – bundle metadata
- `build.sh` – macOS build, sign, package, install
- `Windows/Snapmark/` – the Windows app (C# / WinForms, .NET 8)
- `Windows/IconGen/` – writes the Windows `.ico` from the same icon design
- `build-windows.ps1` – Windows build and install
