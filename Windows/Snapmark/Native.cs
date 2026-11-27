using System.Runtime.InteropServices;

namespace Snapmark;

static class Native
{
    public const int WM_HOTKEY = 0x0312;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_APP_CAPTURE = 0x8001;
    public const uint MOD_SHIFT = 0x4, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
    public const int ASFW_ANY = -1;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// Virtual screen (all monitors) in physical pixels.
    public static Rectangle VirtualScreen() =>
        new(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));

    /// UI scale (1.0 = 96 DPI) of the monitor under a screen point.
    public static float ScaleAt(Point screenPt)
    {
        try
        {
            var mon = MonitorFromPoint(new POINT { X = screenPt.X, Y = screenPt.Y }, 2 /* NEAREST */);
            if (GetDpiForMonitor(mon, 0 /* EFFECTIVE */, out var dx, out _) == 0 && dx > 0) return dx / 96f;
        }
        catch { }
        return 1f;
    }

    /// Makes the window appear instantly, with no fade animation.
    public static void DisableTransitions(IntPtr hwnd)
    {
        int on = 1;
        try { DwmSetWindowAttribute(hwnd, 3 /* DWMWA_TRANSITIONS_FORCEDISABLED */, ref on, sizeof(int)); } catch { }
    }
}
