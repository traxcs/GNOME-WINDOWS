using System.Diagnostics;
using Microsoft.Win32;
using GnomeWin.Platform.Startup;
using GnomeWin.Platform.Taskbar;
using GnomeWin.Platform.Win32;
using GnomeWin.Services;
using GnomeWin.Services.Logging;

namespace GnomeWin.Setup;

public sealed class InstallOptions
{
    public bool DesktopShortcut { get; set; }
    public bool LaunchAtStartup { get; set; } = true;
    public bool ResetSettings { get; set; }
    public bool LaunchNow { get; set; } = true;
}

public static class Installer
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\GnomeWin";

    public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "GnomeWin");
    public static string InstalledExe => Path.Combine(InstallDir, "GnomeWin.exe");
    public static string StartMenuDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "GnomeWin");
    public static string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "GnomeWin.lnk");

    public static bool IsInstalled => File.Exists(InstalledExe);

    public static bool IsRunningFromInstallDir =>
        string.Equals(Path.GetDirectoryName(AppPaths.ExecutablePath)?.TrimEnd('\\'), InstallDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

#pragma warning disable IL3000
    private static bool IsSingleFile => string.IsNullOrEmpty(typeof(Installer).Assembly.Location);
#pragma warning restore IL3000

    public static string Version => FileVersionInfo.GetVersionInfo(AppPaths.ExecutablePath).ProductVersion?.Split('+')[0] ?? "1.0.0";

    public static void Install(InstallOptions o, Action<string> progress)
    {
        progress(Text("Arrêt de GnomeWin s'il est lancé…", "Stopping GnomeWin if running…"));
        StopRunningShell();

        progress(Text("Copie des fichiers…", "Copying files…"));
        Directory.CreateDirectory(InstallDir);
        if (IsSingleFile)
        {
            CopyWithRetry(AppPaths.ExecutablePath, InstalledExe);
        }
        else
        {
            string source = AppContext.BaseDirectory;
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(source, file);
                string dest = Path.Combine(InstallDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                CopyWithRetry(file, dest);
            }
        }

        progress(Text("Création des raccourcis…", "Creating shortcuts…"));
        ShortcutHelper.Create(Path.Combine(StartMenuDir, "GnomeWin.lnk"), InstalledExe, "", "GNOME Shell experience for Windows");
        ShortcutHelper.Create(Path.Combine(StartMenuDir, Text("GnomeWin (mode sans échec).lnk", "GnomeWin (safe mode).lnk")), InstalledExe, "--safe-mode", "GnomeWin safe mode");
        ShortcutHelper.Create(Path.Combine(StartMenuDir, Text("Paramètres GnomeWin.lnk", "GnomeWin Settings.lnk")), InstalledExe, "--settings", "GnomeWin settings",
            null, null, ExtractIcon("Settings.ico"));
        ShortcutHelper.Create(Path.Combine(StartMenuDir, "Console.lnk"), InstalledExe, "--terminal", Text("Terminal (PowerShell et cmd)", "Terminal (PowerShell and cmd)"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), App.ConsoleAppId, ExtractIcon("Console.ico"));
        ShortcutHelper.Create(Path.Combine(StartMenuDir, Text("Restaurer la barre des tâches Windows.lnk", "Restore the Windows taskbar.lnk")), InstalledExe, "--restore", "Quit GnomeWin and restore the Windows taskbar");
        if (o.DesktopShortcut) ShortcutHelper.Create(DesktopShortcut, InstalledExe, "", "GNOME Shell experience for Windows");

        progress(Text("Enregistrement de la désinstallation…", "Registering uninstaller…"));
        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", "GnomeWin");
            key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", "GnomeWin");
            key.SetValue("DisplayIcon", InstalledExe + ",0");
            key.SetValue("InstallLocation", InstallDir);
            key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{InstalledExe}\" --uninstall --quiet");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            long size = new DirectoryInfo(InstallDir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1024;
            key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, size), RegistryValueKind.DWord);
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        }

        if (o.ResetSettings && File.Exists(AppPaths.SettingsFile))
        {
            progress(Text("Réinitialisation de la configuration…", "Resetting configuration…"));
            File.Move(AppPaths.SettingsFile, AppPaths.SettingsFile + $".before-install-{DateTime.Now:yyyyMMddHHmmss}.bak", true);
        }

        StartupManagerFor(InstalledExe, o.LaunchAtStartup);
        Log.Info($"Installed GnomeWin {Version} to {InstallDir}");
        progress(Text("Installation terminée.", "Installation complete."));
    }

    public static void Uninstall(bool removeUserData)
    {
        Log.Info("Uninstalling GnomeWin.");
        StopRunningShell();
        TaskbarController.Restore();
        StartupManager.SetEnabled(false);
        TryDelete(() => Directory.Delete(StartMenuDir, true));
        TryDelete(() => File.Delete(DesktopShortcut));
        TryDelete(() => Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false));
        if (removeUserData)
        {
            Log.Flush();
            TryDelete(() => Directory.Delete(AppPaths.Root, true));
        }
        if (Directory.Exists(InstallDir))
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{InstallDir}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetTempPath(),
            };
            Process.Start(psi)?.Dispose();
        }
    }

    public static void LaunchInstalled()
    {
        Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true, WorkingDirectory = InstallDir })?.Dispose();
    }

    private static void StartupManagerFor(string exe, bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("GnomeWin", $"\"{exe}\" --autostart");
        else if (key.GetValue("GnomeWin") != null) key.DeleteValue("GnomeWin");
    }

    private static void StopRunningShell()
    {
        if (!ShellMessageWindow.SendToRunningInstance("quit")) return;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 8000 && Process.GetProcessesByName("GnomeWin").Any(p => p.Id != Environment.ProcessId))
            Thread.Sleep(150);
    }

    private static string ExtractIcon(string name)
    {
        string path = Path.Combine(InstallDir, name);
        try
        {
            using var src = typeof(Installer).Assembly.GetManifestResourceStream(name);
            if (src == null) return InstalledExe;
            using var dst = File.Create(path);
            src.CopyTo(dst);
            return path;
        }
        catch (Exception ex)
        {
            Log.Warn($"Cannot write the {name} icon", ex);
            return InstalledExe;
        }
    }

    private static void CopyWithRetry(string source, string dest)
    {
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)) return;
        for (int attempt = 0; ; attempt++)
        {
            try { File.Copy(source, dest, true); return; }
            catch (IOException) when (attempt < 20) { Thread.Sleep(250); }
        }
    }

    private static void TryDelete(Action a)
    {
        try { a(); } catch (Exception ex) { Log.Debug("Uninstall cleanup: " + ex.Message); }
    }

    private static string Text(string fr, string en) => UI.Loc.IsFrench ? fr : en;
}
