using System.Windows;
using System.Windows.Threading;
using GnomeWin.Platform.Taskbar;
using GnomeWin.Services;
using GnomeWin.Services.Logging;
using GnomeWin.Services.Recovery;
using GnomeWin.Services.Settings;
using GnomeWin.Shell;

namespace GnomeWin;

public partial class App : Application
{
    private readonly StartupOptions _options;
    private ShellHost? _host;
    private SettingsService? _settings;
    private int _uiErrors;

    public App() : this(new StartupOptions()) { }

    public App(StartupOptions options)
    {
        _options = options;
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Warn("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (_options.Install || _options.Uninstall)
        {
            UI.Themes.ThemeManager.Apply(Services.Settings.ThemeMode.System);
            var setup = new Setup.SetupWindow(_options.Uninstall);
            MainWindow = setup;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            setup.Show();
            return;
        }
        if (_options.Terminal)
        {
            StartConsole();
            return;
        }
        try
        {
            bool forceSafe = RecoveryService.OnStartup();
            _settings = new SettingsService(AppPaths.SettingsFile);
            _settings.Load();
            if (_settings.Current.General.VerboseLogging) Log.MinimumLevel = LogLevel.Debug;
            GnomeWin.UI.Components.ShellWindow.SoftwareRenderingEnabled = _settings.Current.General.LowMemoryMode;
            if (_settings.Current.General.LowMemoryMode)
                System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            bool safe = _options.SafeMode || forceSafe;
            _host = new ShellHost(_settings, _options, safe);
            _host.Start();
        }
        catch (Exception ex)
        {
            Log.Error("Fatal error during startup", ex);
            TaskbarController.Restore();
            RecoveryService.ClearSessionLock();
            MessageBox.Show("GnomeWin could not start and Windows has been restored.\n\n" + ex.Message + "\n\nLogs: " + AppPaths.Logs,
                "GnomeWin", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    public const string ConsoleAppId = "GnomeWin.Console";

    private void StartConsole()
    {
        SetCurrentProcessExplicitAppUserModelID(ConsoleAppId);
        var s = new AppSettings();
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                s = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), SettingsService.JsonOptions) ?? s;
        }
        catch (Exception ex) { Log.Warn("Console: cannot read settings", ex); }
        GnomeWin.UI.Components.ShellWindow.SoftwareRenderingEnabled = s.General.LowMemoryMode;
        if (s.General.LowMemoryMode)
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        UI.Loc.Apply(s.General.Language);
        UI.Themes.ThemeManager.Apply(s.General.Style, s.General.Theme, s.General.Accent);
        ShutdownMode = ShutdownMode.OnLastWindowClose;
        var window = new Terminal.ConsoleWindow(_options.TerminalDirectory);
        MainWindow = window;
        window.Show();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == Microsoft.Win32.UserPreferenceCategory.General && s.General.Theme == ThemeMode.System)
                Dispatcher.BeginInvoke(() => UI.Themes.ThemeManager.Apply(s.General.Style, s.General.Theme, s.General.Accent));
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { if (TaskbarController.IsHiddenByUs) TaskbarController.Restore(); } catch { }
        _settings?.Dispose();
        Log.Info($"Exit code {e.ApplicationExitCode}.");
        Log.Flush();
        base.OnExit(e);
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);
        if (++_uiErrors <= 20)
        {
            e.Handled = true;
            return;
        }
        e.Handled = true;
        Log.Error("Too many UI errors: restoring Windows and exiting.");
        TaskbarController.Restore();
        Log.Flush();
        Environment.Exit(2);
    }

    private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Error("Fatal unhandled exception", e.ExceptionObject as Exception);
        try { TaskbarController.Restore(); } catch { }
        Log.Flush();
    }
}
