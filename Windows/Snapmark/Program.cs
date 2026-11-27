using System.Drawing.Imaging;
using System.Media;
using Microsoft.Win32;

namespace Snapmark;

static class Program
{
    const string MutexName = @"Local\Snapmark.SingleInstance";
    const string SignalName = @"Local\Snapmark.Capture";

    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // `Snapmark --open file.png` opens an existing image in the editor.
        string? open = null;
        int oi = Array.IndexOf(args, "--open");
        if (oi >= 0 && oi + 1 < args.Length) open = args[oi + 1];
        bool background = args.Contains("--background");

        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            if (open != null)
            {
                var img = LoadImage(open);
                if (img != null) Application.Run(new EditorForm(img));
                return;
            }
            // Already running: ask that instance to start a capture.
            if (!background)
            {
                Native.AllowSetForegroundWindow(Native.ASFW_ANY);
                try { EventWaitHandle.OpenExisting(SignalName).Set(); } catch { }
            }
            return;
        }

        using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
        Application.Run(new TrayApp(signal, open, background));
    }

    public static Bitmap? LoadImage(string path)
    {
        try
        {
            using var src = Image.FromFile(path);
            var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height));
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}

/// Tray icon, global hotkey, and capture lifecycle.
sealed class TrayApp : ApplicationContext
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const int HotkeyId = 1;

    readonly NotifyIcon tray;
    readonly MessageWindow messages;
    readonly ToolStripMenuItem startupItem;
    OverlayForm? overlay;
    IntPtr previousWindow;

    public TrayApp(EventWaitHandle signal, string? open, bool background)
    {
        messages = new MessageWindow(this);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Capture Area\tCtrl+Shift+2", null, (_, _) => Capture());
        menu.Items.Add(new ToolStripSeparator());
        startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup()) { Checked = StartsWithWindows() };
        menu.Items.Add(startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit Snapmark", null, (_, _) => Quit());

        tray = new NotifyIcon
        {
            Icon = AppIcon.TrayIcon(),
            Text = "Snapmark: click to capture (Ctrl+Shift+2). Right-click for options.",
            ContextMenuStrip = menu,
            Visible = true,
        };
        // Left click captures right away. Right click shows the menu.
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Capture(); };

        // Global Ctrl+Shift+2.
        if (!Native.RegisterHotKey(messages.Handle, HotkeyId, Native.MOD_CONTROL | Native.MOD_SHIFT | Native.MOD_NOREPEAT, (uint)Keys.D2))
            tray.ShowBalloonTip(5000, "Snapmark", "Ctrl+Shift+2 is already used by another app. Click the tray icon to capture.", ToolTipIcon.Warning);

        // Another launch of Snapmark.exe signals this instance to capture.
        var listener = new Thread(() =>
        {
            while (signal.WaitOne()) Native.PostMessage(messages.Handle, Native.WM_APP_CAPTURE, IntPtr.Zero, IntPtr.Zero);
        }) { IsBackground = true, Name = "Snapmark signal" };
        listener.Start();

        if (open != null)
        {
            var img = Program.LoadImage(open);
            if (img != null) new EditorForm(img).Show();
            else SystemSounds.Beep.Play();
        }
        else if (!background)
        {
            // Launched from the Start menu or Explorer: start a capture right away.
            Native.PostMessage(messages.Handle, Native.WM_APP_CAPTURE, IntPtr.Zero, IntPtr.Zero);
        }
        else
        {
            tray.ShowBalloonTip(3000, "Snapmark is running", "Press Ctrl+Shift+2 or click the tray icon to capture.", ToolTipIcon.None);
        }
    }

    /// Freezes every monitor, dims it, and lets you select and annotate in place.
    public void Capture()
    {
        if (overlay != null) { overlay.Activate(); return; }
        previousWindow = Native.GetForegroundWindow();
        Bitmap raw;
        var virt = Native.VirtualScreen();
        try
        {
            raw = new Bitmap(virt.Width, virt.Height, PixelFormat.Format32bppRgb);
            using var g = Graphics.FromImage(raw);
            g.CopyFromScreen(virt.X, virt.Y, 0, 0, virt.Size, CopyPixelOperation.SourceCopy);
        }
        catch
        {
            SystemSounds.Beep.Play();
            return;
        }

        using (raw) overlay = new OverlayForm(raw, virt);
        overlay.FormClosed += (_, _) =>
        {
            overlay = null;
            // Give focus back to the previous app so you can paste right away.
            if (previousWindow != IntPtr.Zero && Native.IsWindow(previousWindow)) Native.SetForegroundWindow(previousWindow);
            GC.Collect();
        };
        overlay.Show();
    }

    static bool StartsWithWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue("Snapmark") != null;
    }

    void ToggleStartup()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (StartsWithWindows()) key.DeleteValue("Snapmark", false);
        else key.SetValue("Snapmark", $"\"{Environment.ProcessPath}\" --background");
        startupItem.Checked = StartsWithWindows();
    }

    void Quit()
    {
        Native.UnregisterHotKey(messages.Handle, HotkeyId);
        tray.Visible = false;
        tray.Dispose();
        overlay?.Close();
        messages.DestroyHandle();
        ExitThread();
    }

    sealed class MessageWindow : NativeWindow
    {
        readonly TrayApp app;
        public MessageWindow(TrayApp app)
        {
            this.app = app;
            CreateHandle(new CreateParams { Caption = "Snapmark" });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY || m.Msg == Native.WM_APP_CAPTURE) { app.Capture(); return; }
            base.WndProc(ref m);
        }
    }
}
