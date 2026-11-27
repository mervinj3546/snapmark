# Snapmark

A small, native screenshot and annotation tool for **macOS and Windows**: a lightweight alternative to Lightshot / Awesome Screenshot.

Press the hotkey, the screens dim, drag an area, mark it up in place, click Copy, paste anywhere.

## Download

| Platform | Download |
| --- | --- |
| macOS 14+ (Apple silicon) | [**Snapmark-macOS.dmg**](https://github.com/mervinj3546/snapmark/releases/latest/download/Snapmark-macOS.dmg) |
| Windows 10 / 11 (64-bit) | [**Snapmark-Windows.exe**](https://github.com/mervinj3546/snapmark/releases/latest/download/Snapmark-Windows.exe) |

All versions are on the [Releases page](https://github.com/mervinj3546/snapmark/releases).

### Install on macOS

1. Open the DMG and drag **Snapmark** into **Applications**.
2. Snapmark isn't notarized by Apple, so macOS blocks the first launch. Open **System Settings > Privacy & Security**, scroll down and click **Open Anyway** next to Snapmark. Or run this once in Terminal:
   ```bash
   xattr -dr com.apple.quarantine /Applications/Snapmark.app
   ```
3. When asked, allow Snapmark under **System Settings > Privacy & Security > Screen Recording**.

### Install on Windows

1. Run **Snapmark-Windows.exe**. If SmartScreen shows "Windows protected your PC", click **More info > Run anyway** (the exe isn't code-signed).
2. Choose **Yes** to install. Snapmark is added to the Start menu and to **Settings > Apps**, and runs in the system tray. No admin rights or .NET install needed. Choose **No** to just run it once without installing.
3. Press **Ctrl+Shift+2** or click the tray icon to capture. Right-click the tray icon for **Start with Windows** and **Quit**.

To uninstall, use **Settings > Apps > Installed apps > Snapmark > Uninstall**.

## Features

- Freezes and dims every display; drag to select an area, with resize handles and a pixel-size label
- Draw directly on the selection: arrow (default), line, rectangle, oval
- Arrowheads stay a sensible, fixed size
- Color picker (red by default) and a line-thickness box (1–50 px, default 2 px)
- Copy to the clipboard at full Retina / high-DPI resolution, or save to the Desktop
- macOS: starts from the Dock icon, the menu bar icon, or the global hotkey ⌘⇧2
- Windows: starts from the Start menu, the tray icon, or the global hotkey Ctrl+Shift+2; captures span all monitors

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

On macOS, right-click the menu bar icon to quit.

## Build from source

### macOS

Requires macOS 14+ on Apple silicon and the Xcode command-line tools.

```bash
./build.sh
```

This compiles `Sources/main.swift`, generates the app icon, signs the app (with your Apple Development identity if one exists, otherwise ad hoc), builds `build/Snapmark.dmg`, removes any older Snapmark copies, and installs and launches `/Applications/Snapmark.app`. Use `./build.sh --no-install` to only build the DMG.

### Windows

Requires Windows 10/11 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
powershell -ExecutionPolicy Bypass -File .\build-windows.ps1
```

This generates the icon, publishes a single-file `build\windows\Snapmark.exe`, installs it to `%LOCALAPPDATA%\Programs\Snapmark`, adds a Start menu shortcut, and starts it in the tray. Use `-NoInstall` to only build, or `-Release` to build the self-contained `build\windows\Snapmark-Windows.exe` that's published for download.

### Releases

Pushing a tag like `v1.1.0` runs [the release workflow](.github/workflows/release.yml), which builds the DMG on a macOS runner and the exe on a Windows runner, and publishes both as a GitHub Release.

```bash
git tag v1.1.0 && git push origin v1.1.0
```

## Project layout

- `Sources/main.swift` – the whole macOS app (AppKit + ScreenCaptureKit)
- `Icon/make_icon.swift` – draws the app icon and writes the `.iconset`
- `Info.plist` – macOS bundle metadata
- `build.sh` – macOS build, sign, package, install
- `Windows/Snapmark/` – the Windows app (C# / WinForms, .NET 8)
- `Windows/IconGen/` – writes the Windows `.ico` from the same icon design
- `build-windows.ps1` – Windows build and install
- `.github/workflows/release.yml` – builds both apps and publishes releases
