# Snapmark

A small native macOS screenshot and annotation tool, built as a personal replacement for Awesome Screenshot / Lightshot.

Click the icon, the screens dim, drag an area, mark it up in place, click Copy, paste anywhere.

## Features

- Freezes and dims every display; drag to select an area, with resize handles and a pixel-size label
- Draw directly on the selection: arrow (default), line, rectangle, oval
- Arrowheads stay a sensible, fixed size
- Color picker (red by default) and a line-thickness box (1–50 px, default 2 px)
- Copy to clipboard (PNG + TIFF) at full Retina resolution, or save to the Desktop
- Starts from the Dock icon, the menu bar icon, or the global hotkey ⌘⇧2

## Shortcuts

| Key | Action |
| --- | --- |
| ⌘⇧2 | Start a capture (global) |
| A / L / R / O | Arrow / Line / Rectangle / Oval |
| W | Edit line thickness |
| Shift + drag | 45° lines, squares, circles |
| ⌘Z | Undo |
| Return or ⌘C | Copy and close |
| ⌘S | Save to Desktop and close |
| Esc | Cancel |

Right-click the menu bar icon to quit.

## Build and install

Requires macOS 14+ on Apple silicon and the Xcode command-line tools.

```bash
./build.sh
```

This compiles `Sources/main.swift`, generates the app icon, signs the app (with your Apple Development identity if one exists, otherwise ad hoc), builds `build/Snapmark.dmg`, removes any older Snapmark copies, and installs and launches `/Applications/Snapmark.app`.

On first use, allow Snapmark in **System Settings > Privacy & Security > Screen Recording**.

## Project layout

- `Sources/main.swift` – the whole app (AppKit + ScreenCaptureKit)
- `Icon/make_icon.swift` – draws the app icon and writes the `.iconset`
- `Info.plist` – bundle metadata
- `build.sh` – build, sign, package, install
