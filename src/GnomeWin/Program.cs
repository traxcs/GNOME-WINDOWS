using System.Diagnostics;
using System.Runtime.InteropServices;
using GnomeWin.Platform.Taskbar;
using GnomeWin.Platform.Win32;
using GnomeWin.Services;
using GnomeWin.Services.Logging;
using GnomeWin.Services.Recovery;

namespace GnomeWin;

public static class Program
{
    private const string MutexName = @"Local\GnomeWin.Shell.SingleInstance";

    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int pid);

    [STAThread]
    public static int Main(string[] args)
    {
        var options = StartupOptions.Parse(args);

        if (options.WatchdogPid > 0) return Watchdog.Run(options.WatchdogPid);
        return RunShellOrTool(options, args);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int RunShellOrTool(StartupOptions options, string[] args)
    {
        Log.Initialize(AppPaths.Logs, options.Terminal ? "console" : "gnomewin", options.Verbose);
        Log.Info($"GnomeWin starting: {string.Join(' ', args)}");

        if (options.Help)
        {
            Print(StartupOptions.HelpText);
            return 0;
        }
        if (options.Restore)
        {
            if (ShellMessageWindow.SendToRunningInstance("quit")) WaitForExit();
            TaskbarController.Restore();
            RecoveryService.ClearSessionLock();
            Print("Windows taskbar restored.");
            Log.Flush();
            return 0;
        }
        if (options.Diagnostics) return RunDiagnostics();
        if (options.Terminal)
        {
            var console = new App(options);
            console.InitializeComponent();
            return console.Run();
        }
        if (options.Uninstall && options.Quiet)
        {
            Setup.Installer.Uninstall(removeUserData: false);
            Log.Flush();
            return 0;
        }
        if (options.Install && options.Quiet)
        {
            Setup.Installer.Install(new Setup.InstallOptions { LaunchAtStartup = options.Startup, LaunchNow = false }, msg => Log.Info(msg));
            Log.Flush();
            return 0;
        }
        if (options.Install || options.Uninstall)
        {
            var setup = new App(options);
            setup.InitializeComponent();
            return setup.Run();
        }
        if (options.ResetSettings)
        {
            if (File.Exists(AppPaths.SettingsFile))
                File.Move(AppPaths.SettingsFile, AppPaths.SettingsFile + $".reset-{DateTime.Now:yyyyMMddHHmmss}.bak", true);
            Print("Settings reset (backup kept next to settings.json).");
        }

        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            if (options.ResetSettings) { Print("Restart GnomeWin to use the default settings."); return 0; }
            ShellMessageWindow.SendToRunningInstance(options.ForwardedCommand);
            return 0;
        }
        if (options.Quit) return 0;

        var app = new App(options);
        app.InitializeComponent();
        int code = app.Run();
        GC.KeepAlive(mutex);
        return code;
    }

    private static void WaitForExit()
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 5000)
        {
            using var m = Mutex.TryOpenExisting(MutexName, out var existing) ? existing : null;
            if (m == null) return;
            Thread.Sleep(100);
        }
    }

    private static int RunDiagnostics()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"GnomeWin diagnostics {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"OS: {Environment.OSVersion.VersionString}  .NET {Environment.Version}  64-bit: {Environment.Is64BitProcess}");
        sb.AppendLine($"Data folder: {AppPaths.Root}");
        sb.AppendLine($"Taskbar left hidden by a previous session: {TaskbarController.NeedsRecovery()}");
        sb.AppendLine($"Taskbar windows: {TaskbarController.FindTaskbars().Count}");
        try
        {
            var vd = new Platform.VirtualDesktop.VirtualDesktopService(allowInternalApi: true);
            sb.AppendLine($"Virtual desktop backend: {vd.BackendName}");
            var ids = vd.GetDesktops();
            sb.AppendLine($"Desktops: {ids.Count}, current: {vd.GetCurrent()}");
            vd.Dispose();
        }
        catch (Exception ex) { sb.AppendLine("Virtual desktops: error " + ex.Message); }
        var monitors = new Core.MonitorManager();
        foreach (var m in monitors.Monitors) sb.AppendLine($"Monitor {m.DeviceName} {m.Bounds} work {m.WorkArea} dpi {m.Dpi} primary {m.IsPrimary}");
        var catalog = Services.AppDiscovery.AppDiscoveryService.Enumerate();
        sb.AppendLine($"Applications discovered: {catalog.Count}");
        int count = 0;
        NativeMethods.EnumWindows((h, _) =>
        {
            string cls = NativeMethods.GetClassNameOf(h);
            if (!Core.WindowFilter.IsAppWindow(h, cls, _ => Guid.Empty)) return true;
            count++;
            NativeMethods.GetWindowThreadProcessId(h, out uint pid);
            string? path = NativeMethods.GetProcessPath(pid);
            string? aumid = ShellApi.GetWindowAumid(h) ?? NativeMethods.GetProcessAumid(pid);
            var match = catalog.FirstOrDefault(c => (aumid != null && string.Equals(c.Entry.Id, aumid, StringComparison.OrdinalIgnoreCase))
                                                    || (path != null && string.Equals(c.Entry.ExePath, path, StringComparison.OrdinalIgnoreCase)));
            sb.AppendLine($"  window: {NativeMethods.GetWindowTitle(h)} [{cls}] pid {pid}");
            sb.AppendLine($"          exe {path}  aumid {aumid ?? "-"}  → app {(match.Entry != null ? match.Entry.Name + " (" + match.Entry.Id + ")" : "none")}");
            if (path != null)
            {
                var img = IconHelper.GetFileImage(path, 64) as System.Windows.Media.Imaging.BitmapSource;
                int opaque = 0;
                if (img != null)
                {
                    var px = new byte[img.PixelWidth * img.PixelHeight * 4];
                    img.CopyPixels(px, img.PixelWidth * 4, 0);
                    for (int i = 3; i < px.Length; i += 4) if (px[i] > 0) opaque++;
                }
                sb.AppendLine($"          exe icon: {(img == null ? "none" : $"{img.PixelWidth}x{img.PixelHeight} {img.Format}, {opaque} visible px")}");
            }
            return true;
        }, IntPtr.Zero);
        sb.AppendLine($"Application windows (current desktop): {count}");
        foreach (var (entry, item) in catalog)
        {
            sb.AppendLine($"  app: {entry.Name} | {entry.Id} | {entry.ExePath ?? "-"}");
            Marshal.ReleaseComObject(item);
        }
        string text = sb.ToString();
        string file = Path.Combine(AppPaths.Logs, $"diag-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(file, text);
        Print(text + Environment.NewLine + "Written to " + file);
        Log.Flush();
        return 0;
    }

    private static void Print(string text)
    {
        if (AttachConsole(-1))
        {
            Console.WriteLine();
            Console.WriteLine(text);
        }
        Debug.WriteLine(text);
    }
}
