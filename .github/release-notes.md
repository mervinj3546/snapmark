## Download

| Platform | File |
| --- | --- |
| macOS 14+ (Apple silicon) | **Snapmark-macOS.dmg** |
| Windows 10 / 11 (64-bit) | **Snapmark-Windows.exe** |

### macOS
1. Open the DMG and drag **Snapmark** into **Applications**.
2. The app isn't notarized by Apple, so the first launch is blocked. Open **System Settings > Privacy & Security**, scroll down and click **Open Anyway** next to Snapmark. Or run this in Terminal:
   `xattr -dr com.apple.quarantine /Applications/Snapmark.app`
3. Allow Snapmark under **Privacy & Security > Screen Recording** when asked.

### Windows
1. Run **Snapmark-Windows.exe**. If SmartScreen says "Windows protected your PC", click **More info > Run anyway** (the exe isn't code-signed).
2. Choose **Yes** to install it. It's added to the Start menu and Settings > Apps, and runs in the tray. No admin rights or .NET install needed.
3. Press **Ctrl+Shift+2** or click the tray icon to capture.

See the [README](https://github.com/mervinj3546/snapmark#readme) for shortcuts and details.
