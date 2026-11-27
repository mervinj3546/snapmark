using System.Diagnostics;
using Microsoft.Win32;

namespace Snapmark;

/// Per-user install (no admin): %LOCALAPPDATA%\Programs\Snapmark, a Start menu shortcut,
/// and an entry in Settings > Apps so it can be uninstalled like any other app.
static class Installer
{
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Snapmark";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Snapmark");
    public static string InstalledExe => Path.Combine(InstallDir, "Snapmark.exe");
    static string Shortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Snapmark.lnk");

    static string CurrentExe => Environment.ProcessPath ?? Application.ExecutablePath;

    public static bool IsInstalledCopy =>
        string.Equals(Path.GetFullPath(CurrentExe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    static string Version => typeof(Installer).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    /// Asks on first run from a download. Returns true if this process should exit (the installed copy was started).
    public static bool OfferInstall()
    {
        bool update = File.Exists(InstalledExe);
        var text = update
            ? "Snapmark is already installed. Update it with this version?\n\nChoose No to just run this copy without installing."
            : "Install Snapmark for your account?\n\nIt will be added to the Start menu and to Settings > Apps, and will run in the system tray. " +
              "Press Ctrl+Shift+2 or click the tray icon to take a screenshot.\n\nChoose No to just run it from here without installing.";
        var answer = MessageBox.Show(text, "Snapmark", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) return true;
        if (answer == DialogResult.No) return false;
        try
        {
            Install();
            Process.Start(new ProcessStartInfo(InstalledExe, "--background --installed") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Snapmark could not be installed:\n\n" + ex.Message, "Snapmark", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        return true;
    }

    static void StopOtherInstances()
    {
        foreach (var p in Process.GetProcessesByName("Snapmark"))
        {
            if (p.Id == Environment.ProcessId) continue;
            try { p.Kill(); p.WaitForExit(3000); } catch { }
        }
    }

    public static void Install()
    {
        StopOtherInstances();
        Directory.CreateDirectory(InstallDir);
        File.Copy(CurrentExe, InstalledExe, true);
        Register();
    }

    /// Start menu shortcut + Settings > Apps entry. Safe to call on every start of the installed copy.
    public static void Register()
    {
        try
        {
            if (!File.Exists(Shortcut)) CreateShortcut(Shortcut, InstalledExe);
        }
        catch { }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
            key.SetValue("DisplayName", "Snapmark");
            key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", "Snapmark");
            key.SetValue("DisplayIcon", InstalledExe);
            key.SetValue("InstallLocation", InstallDir);
            key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
            key.SetValue("URLInfoAbout", "https://github.com/mervinj3546/snapmark");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            if (File.Exists(InstalledExe))
                key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
        }
        catch { }
    }

    static void CreateShortcut(string lnk, string target)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell unavailable");
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic s = shell.CreateShortcut(lnk);
        s.TargetPath = target;
        s.WorkingDirectory = Path.GetDirectoryName(target);
        s.Description = "Screenshot and annotate";
        s.Save();
    }

    public static void Uninstall()
    {
        if (MessageBox.Show("Uninstall Snapmark?", "Snapmark", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        StopOtherInstances();
        try { File.Delete(Shortcut); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, true);
            run?.DeleteValue("Snapmark", false);
        }
        catch { }
        // The running exe can't delete itself; remove the folder a moment after we exit.
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{InstallDir}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetTempPath(),
            });
        }
        catch { }
        MessageBox.Show("Snapmark has been uninstalled.", "Snapmark", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
