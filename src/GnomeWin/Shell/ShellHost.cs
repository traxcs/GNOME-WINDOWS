using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using GnomeWin.Core;
using GnomeWin.Input.GlobalHotkeys;
using GnomeWin.Platform.Startup;
using GnomeWin.Platform.Taskbar;
using GnomeWin.Platform.VirtualDesktop;
using GnomeWin.Platform.Win32;
using GnomeWin.Services;
using GnomeWin.Services.AppDiscovery;
using GnomeWin.Services.Logging;
using GnomeWin.Services.Notifications;
using GnomeWin.Services.Recovery;
using GnomeWin.Services.Search;
using GnomeWin.Services.Settings;
using GnomeWin.Services.SystemStatus;
using GnomeWin.Services.Wallpaper;
using GnomeWin.Shell.Dock;
using GnomeWin.Shell.Overview;
using GnomeWin.Shell.Settings;
using GnomeWin.Shell.Switcher;
using GnomeWin.Shell.SystemPanel;
using GnomeWin.Shell.WorkspaceView;
using GnomeWin.UI;
using GnomeWin.UI.Animations;
using GnomeWin.UI.Components;
using GnomeWin.UI.Themes;
using static GnomeWin.Platform.Win32.NativeMethods;

namespace GnomeWin.Shell;

public sealed class ShellHost : IDisposable
{
    private readonly SettingsService _settings;
    private readonly StartupOptions _options;
    private readonly bool _safeMode;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

    private ShellMessageWindow _messages = null!;
    private MonitorManager _monitors = null!;
    private VirtualDesktopService _desktops = null!;
    private WindowManager _windows = null!;
    private ApplicationManager _apps = null!;
    private WorkspaceManager _workspaces = null!;
    private AppDiscoveryService _discovery = null!;
    private SystemStatusService _status = null!;
    private NotificationService _notifications = null!;
    private SearchService _search = null!;
    private WallpaperProvider _wallpapers = null!;
    private DockController _dock = null!;
    private OverviewController _overview = null!;
    private FullscreenDetector _fullscreen = null!;
    private KeyboardHookService? _hook;
    private AppSwitcherWindow? _switcher;
    private WorkspaceOsd? _osd;
    private QuickSettingsWindow? _quickSettings;
    private CalendarWindow? _calendar;
    private SettingsWindow? _settingsWindow;
    private Process? _watchdog;
    private int _watchdogRestarts;
    private readonly Dictionary<string, TopBarWindow> _topBars = new();
    private DispatcherTimer? _clock;
    private DispatcherTimer? _displayDebounce;
    private DispatcherTimer? _fullscreenDebounce;
    private bool _shuttingDown;

    public ShellHost(SettingsService settings, StartupOptions options, bool safeMode)
    {
        _settings = settings;
        _options = options;
        _safeMode = safeMode;
    }

    public void Start()
    {
        var sw = Stopwatch.StartNew();
        var g = _settings.Current.General;
        Loc.Apply(g.Language);
        Anim.Configure(g.AnimationsEnabled, g.AnimationSpeed);
        ThemeManager.Apply(g.Style, g.Theme, g.Accent);

        _messages = new ShellMessageWindow();
        _monitors = new MonitorManager();
        _desktops = new VirtualDesktopService(allowInternalApi: true);
        _windows = new WindowManager(_desktops);
        _apps = new ApplicationManager(_windows, _settings);
        _workspaces = new WorkspaceManager(_desktops, _windows, _settings);
        _fullscreen = new FullscreenDetector(_monitors);

        double maxScale = _monitors.Monitors.Max(m => m.Scale);
        int iconPx = (int)Math.Clamp(Math.Ceiling(64 * maxScale), 64, 256);
        _apps.IconPixelSize = iconPx;

        _windows.Start();
        _workspaces.Start();

        _discovery = new AppDiscoveryService(_ui);
        _discovery.CatalogLoaded += list => _apps.SetCatalog(list);
        _discovery.Start(iconPx);

        _status = new SystemStatusService();
        _notifications = new NotificationService();
        _search = new SearchService();
        _wallpapers = new WallpaperProvider();

        _dock = new DockController(_apps, _windows, _workspaces, _monitors, _settings);
        _dock.TopInsetProvider = TopBarHeight;
        _overview = new OverviewController(_windows, _workspaces, _apps, _monitors, _settings, _dock, _search, _wallpapers, _notifications)
        {
            InsetProvider = m =>
            {
                var (left, right, bottom) = _dock.ReservedEdges(m);
                return new RECT(left, TopBarHeight(m), right, bottom);
            },
        };
        _dock.ShowApplications = () => _overview.Toggle(OverviewView.Applications);
        _dock.CloseOverviewAndActivate = w => _overview.Close(w);
        _overview.OpenStateChanged += OnOverviewOpenChanged;

        SyncTopBars();
        if (!_safeMode) _dock.SyncWindows();
        else _dock.SetSuppressed(true);

        if (!_safeMode && g.ReplaceTaskbar) EnableTaskbarReplacement(true);
        if (!_safeMode) LaunchWatchdog();
        ConfigureKeyboardHook();

        WireEvents();
        UpdateLocationTracking();
        StartClock();
        UpdateTopBars();

        Log.Info($"Shell started in {sw.ElapsedMilliseconds} ms (safe mode: {_safeMode}, desktops: {_desktops.BackendName}).");
        ScheduleStartupCompaction();
        if (Log.MinimumLevel == LogLevel.Debug) StartStatsLogging();

        if (_safeMode)
            _notifications.AddShellNotice(Loc.IsFrench ? "Mode sans échec" : "Safe mode", Loc.T("SafeModeBanner"));
        if (!g.FirstRunDone || _options.OpenSettings || _safeMode)
        {
            g.FirstRunDone = true;
            _ui.BeginInvoke(OpenSettingsWindow, DispatcherPriority.Background);
        }
        if (_options.OpenOverview) _ui.BeginInvoke(() => _overview.Open(), DispatcherPriority.Background);
        if (_options.OpenApps) _ui.BeginInvoke(() => _overview.Open(OverviewView.Applications), DispatcherPriority.Background);
        if (_options.SearchText != null) _ui.BeginInvoke(() => _overview.OpenWithSearch(_options.SearchText), DispatcherPriority.Background);
    }

    private void WireEvents()
    {
        _messages.ExplorerRestarted += OnExplorerRestarted;
        _messages.DisplayChanged += ScheduleDisplayRefresh;
        _messages.SettingChanged += OnSystemSettingChanged;
        _messages.PowerStatusChanged += () => _status.RefreshPower();
        _messages.Resumed += OnResumed;
        _messages.SessionLocked += () => { _overview.Close(null); _switcher?.Cancel(); };
        _messages.SessionUnlocked += OnResumed;
        _messages.SessionEnding += () => { if (TaskbarController.IsHiddenByUs) TaskbarController.Restore(); };
        _messages.SessionEnded += () =>
        {
            _shuttingDown = true;
            if (TaskbarController.IsHiddenByUs) TaskbarController.Restore();
            _settings.Save();
            RecoveryService.OnCleanExit();
            Log.Info("Session ended.");
            Log.Flush(300);
        };
        _messages.SessionEndCancelled += () => { if (!_safeMode && _settings.Current.General.ReplaceTaskbar) EnableTaskbarReplacement(true); };
        _messages.CommandReceived += HandleCommand;

        _monitors.MonitorsChanged += OnMonitorsChanged;
        _monitors.WorkAreasChanged += () => { _dock.EvaluateVisibility(); };

        _windows.ForegroundChanged += OnForegroundChanged;
        _windows.WindowMoved += w => { if (w.IsActive) ScheduleFullscreenCheck(); };
        _windows.AnyWindowShown += hwnd =>
        {
            if (TaskbarController.IsHiddenByUs && TaskbarController.IsTaskbarWindow(hwnd))
            {
                TaskbarController.EnsureHidden();
                _ui.BeginInvoke(async () => { await Task.Delay(300); TaskbarController.EnsureHidden(); });
            }
        };

        _fullscreen.Changed += OnFullscreenChanged;
        _workspaces.Changed += UpdateTopBars;
        _workspaces.Switched += OnWorkspaceSwitched;
        _status.NetworkChanged += UpdateTopBars;
        _status.AudioChanged += UpdateTopBars;
        _status.PowerChanged += UpdateTopBars;
        _notifications.Changed += UpdateTopBars;
        _settings.Changed += OnSettingsChanged;
        ThemeManager.ThemeChanged += () => _dock.RefreshTheme();
    }

    private void ScheduleStartupCompaction()
    {
        var t = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(12) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            MemoryTrimmer.Trim("startup");
            if (!_overview.IsOpen) ProcessPower.SetEfficiencyMode(true);
        };
        t.Start();
    }

    private void StartStatsLogging()
    {
        var proc = Process.GetCurrentProcess();
        var lastCpu = proc.TotalProcessorTime;
        var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
        t.Tick += (_, _) =>
        {
            proc.Refresh();
            var cpu = proc.TotalProcessorTime;
            Log.Debug($"Stats: cpu {(cpu - lastCpu).TotalMilliseconds:N0} ms/30s, ws {proc.WorkingSet64 / 1048576} MB, private {proc.PrivateMemorySize64 / 1048576} MB, gc committed {GC.GetGCMemoryInfo().TotalCommittedBytes / 1048576} MB, heap {GC.GetTotalMemory(false) / 1048576} MB, " +
                      $"frame anims {FrameAnimation.Running}, events {_windows.TakeEventStats()}");
            lastCpu = cpu;
        };
        t.Start();
    }

    private void ConfigureKeyboardHook()
    {
        bool wanted = !_safeMode && _settings.Current.Keyboard.InterceptSuperKey;
        if (!wanted)
        {
            _hook?.Stop();
            _hook = null;
            return;
        }
        _hook ??= new KeyboardHookService(a => _ui.BeginInvoke(() => HandleAction(a)));
        _hook.SetBindings(ParseBindings());
        if (!_hook.IsRunning) _hook.Start();
    }

    private IEnumerable<HotkeyBinding> ParseBindings()
    {
        foreach (var (name, list) in _settings.Current.Keyboard.Bindings)
        {
            if (!Enum.TryParse(name, true, out ShellAction action)) continue;
            foreach (var text in list)
            {
                if (Hotkey.TryParse(text, out var hk)) yield return new HotkeyBinding(hk, action);
                else Log.Warn($"Invalid shortcut '{text}' for {name}");
            }
        }
    }

    public void HandleAction(ShellAction action)
    {
        try
        {
            switch (action)
            {
                case ShellAction.ToggleOverview:
                    if (_switcher?.IsSwitching == true) return;
                    _overview.Toggle();
                    break;
                case ShellAction.ShowApplications:
                    _overview.Toggle(OverviewView.Applications);
                    break;
                case ShellAction.AppSwitcher:
                    if (_overview.IsOpen) _overview.Close(null);
                    _switcher ??= new AppSwitcherWindow();
                    var current = _workspaces.CurrentId;
                    bool onlyCurrent = _settings.Current.Workspaces.SwitcherCurrentWorkspaceOnly;
                    var mru = _windows.MostRecentFirst().Where(w => !onlyCurrent || w.DesktopId == Guid.Empty || w.DesktopId == current);
                    _switcher.Begin(mru, ActiveMonitor(), Keyboard_ShiftDown());
                    break;
                case ShellAction.SwitcherNext: _switcher?.Step(1); break;
                case ShellAction.SwitcherPrevious: _switcher?.Step(-1); break;
                case ShellAction.SwitcherNextWindow: _switcher?.NextWindow(); break;
                case ShellAction.SwitcherCommit: _switcher?.Commit(); break;
                case ShellAction.SwitcherCancel: _switcher?.Cancel(); break;
                case ShellAction.WorkspacePrevious: _workspaces.Previous(); break;
                case ShellAction.WorkspaceNext: _workspaces.Next(); break;
                case ShellAction.MoveWindowToPreviousWorkspace: MoveActiveWindow(-1); break;
                case ShellAction.MoveWindowToNextWorkspace: MoveActiveWindow(+1); break;
                case ShellAction.ToggleQuickSettings: ToggleQuickSettings(null); break;
                case ShellAction.ToggleNotifications: ToggleCalendar(null); break;
                case ShellAction.OpenShellSettings: OpenSettingsWindow(); break;
                case >= ShellAction.LaunchDockItem1 and <= ShellAction.LaunchDockItem9:
                    if (_overview.IsOpen) _overview.Close(null);
                    _dock.ActivateIndex(action - ShellAction.LaunchDockItem1);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Action {action} failed", ex);
        }
    }

    private void UpdateLocationTracking()
    {
        var s = _settings.Current;
        bool needed = !_safeMode && s.Dock.Visibility is DockVisibility.Intellihide or DockVisibility.AutoHide
                      || !s.General.ShowTopBar;
        _windows.TrackLocation = needed;
    }

    private DateTime _lastHotCorner;

    private DispatcherTimer? _hotCornerDwell;

    private void OnHotCorner(MonitorInfo monitor)
    {
        if (!_settings.Current.Overview.HotCorner || (DateTime.Now - _lastHotCorner).TotalMilliseconds < 600) return;
        if (_fullscreen.AnyFullscreen) return;
        _hotCornerDwell?.Stop();
        int checks = 0;
        _hotCornerDwell = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(40) };
        _hotCornerDwell.Tick += (_, _) =>
        {
            GetCursorPos(out var p);
            bool inCorner = p.X <= monitor.Bounds.Left + 1 && p.Y <= monitor.Bounds.Top + 1;
            bool buttons = (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0;
            if (!inCorner || buttons) { _hotCornerDwell!.Stop(); return; }
            if (++checks < 4) return;
            _hotCornerDwell!.Stop();
            _lastHotCorner = DateTime.Now;
            _overview.Toggle();
        };
        _hotCornerDwell.Start();
    }

    private void TrimSoon()
    {
        var t = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(3) };
        t.Tick += (_, _) => { t.Stop(); if (!_overview.IsOpen) MemoryTrimmer.Trim("panel closed"); };
        t.Start();
    }

    private int TopBarHeight(MonitorInfo m) => _topBars.TryGetValue(m.Key, out var bar) && bar.IsVisible ? bar.PhysicalHeight : 0;

    private static bool Keyboard_ShiftDown() => (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

    private void MoveActiveWindow(int delta)
    {
        var w = _windows.ActiveWindow;
        if (w == null) return;
        int target = _workspaces.CurrentIndex + delta;
        if (target < 0) return;
        if (!_workspaces.CanMoveWindows)
        {
            _notifications.AddShellNotice("GnomeWin", Loc.T("MoveNotSupported"));
            return;
        }
        _workspaces.MoveWindow(w, target, follow: true);
    }

    private MonitorInfo ActiveMonitor()
    {
        IntPtr fg = GetForegroundWindow();
        return fg != IntPtr.Zero ? _monitors.FromWindow(fg) : _monitors.FromCursor();
    }

    private void SyncTopBars()
    {
        var s = _settings.Current;
        var wanted = !s.General.ShowTopBar ? new List<MonitorInfo>()
            : s.Displays.TopBarMonitors == MonitorPlacement.All ? _monitors.Monitors.ToList()
            : new List<MonitorInfo> { _monitors.Primary };
        var keys = wanted.Select(m => m.Key).ToHashSet();
        foreach (var key in _topBars.Keys.ToList())
        {
            if (keys.Contains(key)) continue;
            _topBars[key].Close();
            _topBars.Remove(key);
        }
        foreach (var mon in wanted)
        {
            if (!_topBars.TryGetValue(mon.Key, out var bar))
            {
                bar = new TopBarWindow(mon);
                var b = bar;
                bar.View.Activities.Click += (_, _) => _overview.Toggle();
                bar.View.HotCorner.MouseEnter += (_, _) => OnHotCorner(b.Monitor);
                bar.View.Clock.Click += (_, _) => ToggleCalendar(b);
                bar.View.Status.Click += (_, _) => ToggleQuickSettings(b);
                bar.FullscreenAppChanged += (tb, fs) => _fullscreen.Hint(tb.Monitor.Handle, fs);
                _topBars[mon.Key] = bar;
            }
            bar.Place(mon);
            bar.View.SetSafeMode(_safeMode);
            if (!_fullscreen.IsFullscreen(mon.Handle) || !s.Displays.HideTopBarInFullscreen) bar.Show();
        }
        UpdateTopBars();
    }

    private void UpdateTopBars()
    {
        int count = _workspaces?.Workspaces.Count ?? 1;
        int current = _workspaces?.CurrentIndex ?? 0;
        bool anyNotif = _notifications?.Items.Count > 0;
        foreach (var bar in _topBars.Values)
        {
            bar.View.SetWorkspaces(count, current);
            if (_status != null) bar.View.SetStatus(_status, _settings.Current.General.ShowBatteryIcon, _settings.Current.General.ShowBatteryPercentage);
            bar.View.SetHasNotifications(anyNotif);
            bar.View.SetClock(DateTime.Now);
        }
    }

    private void StartClock()
    {
        _clock = new DispatcherTimer(DispatcherPriority.Background);
        _clock.Tick += (_, _) =>
        {
            foreach (var bar in _topBars.Values) bar.View.SetClock(DateTime.Now);
            _clock!.Interval = TimeSpan.FromSeconds(60 - DateTime.Now.Second + 0.05);
        };
        _clock.Interval = TimeSpan.FromSeconds(60 - DateTime.Now.Second + 0.05);
        _clock.Start();
    }

    private TopBarWindow? PrimaryTopBar() =>
        _topBars.Values.FirstOrDefault(b => b.Monitor.IsPrimary) ?? _topBars.Values.FirstOrDefault();

    private void ToggleQuickSettings(TopBarWindow? bar)
    {
        if (_quickSettings != null && !_quickSettings.IsClosing && _quickSettings.IsVisible) { _quickSettings.CloseAnimated(); return; }
        bar ??= PrimaryTopBar();
        var mon = bar?.Monitor ?? _monitors.Primary;
        _quickSettings = new QuickSettingsWindow(_status, OpenSettingsWindow, (msg, ok) => ConfirmDialog.Ask(msg, ok, mon));
        _quickSettings.Closed += (_, _) => { _quickSettings = null; SetPanelActive(v => v.Status, false); TrimSoon(); };
        int right = mon.Bounds.Right - (int)(8 * mon.Scale);
        int top = mon.Bounds.Top + (bar?.PhysicalHeight ?? 0);
        if (bar != null)
        {
            var p = bar.View.Status.PointToScreen(new Point(bar.View.Status.ActualWidth, bar.View.Status.ActualHeight));
            right = (int)p.X;
        }
        _quickSettings.ShowAnchored(right, top, PopupAnchor.Right, mon);
        SetPanelActive(v => v.Status, true);
    }

    private void ToggleCalendar(TopBarWindow? bar)
    {
        if (_calendar != null && !_calendar.IsClosing && _calendar.IsVisible) { _calendar.CloseAnimated(); return; }
        bar ??= PrimaryTopBar();
        var mon = bar?.Monitor ?? _monitors.Primary;
        if (!_notifications.Initialized) _ = _notifications.InitializeAsync();
        _calendar = new CalendarWindow(_notifications, aumid => ShellLauncher.Open(@"shell:AppsFolder\" + aumid));
        _calendar.Closed += (_, _) => { _calendar = null; SetPanelActive(v => v.Clock, false); TrimSoon(); };
        int center = mon.Bounds.Left + mon.Bounds.Width / 2;
        _calendar.ShowAnchored(center, mon.Bounds.Top + (bar?.PhysicalHeight ?? 0), PopupAnchor.Center, mon);
        SetPanelActive(v => v.Clock, true);
    }

    public void OpenSettingsWindow()
    {
        if (_settingsWindow != null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            WindowActions.Activate(new System.Windows.Interop.WindowInteropHelper(_settingsWindow).Handle);
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, _hook, _status, _apps, Diagnostics, () => Shutdown("user request"));
        _settingsWindow.Closed += (_, _) => { _settingsWindow = null; TrimSoon(); };
        _settingsWindow.Show();
        _settingsWindow.Activate();
        WindowActions.Activate(new System.Windows.Interop.WindowInteropHelper(_settingsWindow).Handle);
    }

    private void SetPanelActive(Func<TopBarView, System.Windows.Controls.Button> button, bool active)
    {
        foreach (var bar in _topBars.Values) TopBarView.SetActive(button(bar.View), active);
    }

    private void OnOverviewOpenChanged(bool open)
    {
        SetPanelActive(v => v.Activities, open);
        if (!open) return;
        _quickSettings?.CloseAnimated();
        _calendar?.CloseAnimated();
        _switcher?.Cancel();
    }

    private void OnForegroundChanged(WindowInfo? w, IntPtr raw)
    {
        ScheduleFullscreenCheck();
        if (raw == IntPtr.Zero) return;
        GetWindowThreadProcessId(raw, out uint pid);
        bool ours = pid == (uint)Environment.ProcessId;

        if (_overview.IsOpen && !ours && !_overview.IgnoreForegroundChanges)
        {
            Log.Debug("Foreground moved to another application: closing the Overview.");
            _overview.Close(null);
        }
    }

    private void ScheduleFullscreenCheck()
    {
        if (_fullscreenDebounce == null)
        {
            _fullscreenDebounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
            _fullscreenDebounce.Tick += (_, _) => { _fullscreenDebounce.Stop(); _fullscreen.Evaluate(GetForegroundWindow()); };
        }
        _fullscreenDebounce.Stop();
        _fullscreenDebounce.Start();
    }

    private void OnFullscreenChanged(IntPtr monitor, bool fullscreen)
    {
        var d = _settings.Current.Displays;
        Log.Info($"Fullscreen on monitor 0x{monitor.ToInt64():X}: {fullscreen}");
        _dock.SetFullscreen(monitor, fullscreen);
        foreach (var bar in _topBars.Values.Where(b => b.Monitor.Handle == monitor))
        {
            if (fullscreen && d.HideTopBarInFullscreen) bar.Hide();
            else if (!bar.IsVisible) { bar.Show(); bar.BringToTopmost(); }
        }
        if (_hook != null) _hook.Suspended = _fullscreen.AnyFullscreen && d.DisableShortcutsInFullscreen;
    }

    private void OnWorkspaceSwitched(int oldIndex, int newIndex)
    {
        UpdateTopBars();
        if (_overview.IsOpen || !_settings.Current.Workspaces.ShowSwitchOsd) return;
        _osd ??= new WorkspaceOsd();
        _osd.ShowFor(_workspaces.Workspaces.Count, newIndex, ActiveMonitor());
    }

    private void OnExplorerRestarted()
    {
        _ui.BeginInvoke(async () =>
        {
            await Task.Delay(1500);
            if (_shuttingDown) return;
            _desktops.Reconnect();
            _windows.Resync();
            if (TaskbarController.IsHiddenByUs) TaskbarController.Hide();
            foreach (var bar in _topBars.Values) bar.ReRegister();
            _dock.SyncWindows();
            _workspaces.Refresh();
            UpdateTopBars();
        });
    }

    private void ScheduleDisplayRefresh()
    {
        if (_displayDebounce == null)
        {
            _displayDebounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(600) };
            _displayDebounce.Tick += (_, _) => { _displayDebounce.Stop(); _monitors.Refresh(); };
        }
        _displayDebounce.Stop();
        _displayDebounce.Start();
    }

    private void OnMonitorsChanged()
    {
        _wallpapers.Invalidate();
        if (_overview.IsOpen) _overview.Close(null);
        SyncTopBars();
        if (!_safeMode) _dock.SyncWindows();
        TaskbarController.EnsureHidden();
        _windows.Resync();
    }

    private void OnSystemSettingChanged(string? area)
    {
        switch (area)
        {
            case "WorkArea":
                ScheduleDisplayRefresh();
                break;
            case "Wallpaper":
                _wallpapers.Invalidate();
                break;
            case "ImmersiveColorSet":
                if (_settings.Current.General.Theme == ThemeMode.System) ThemeManager.Apply(ThemeMode.System);
                break;
        }
    }

    private void OnResumed()
    {
        Log.Info("Resume / unlock: refreshing shell state.");
        _hook?.Restart();
        _monitors.Refresh();
        _windows.Resync();
        _workspaces.Refresh();
        TaskbarController.EnsureHidden();
        _status.RefreshNetwork();
        _status.RefreshPower();
        UpdateTopBars();
    }

    private void OnSettingsChanged(object section, string property)
    {
        try
        {
            switch (section)
            {
                case GeneralSettings g:
                    if (property is nameof(GeneralSettings.Theme) or nameof(GeneralSettings.Accent) or nameof(GeneralSettings.Style) or "*")
                        ThemeManager.Apply(g.Style, g.Theme, g.Accent);
                    if (property is nameof(GeneralSettings.AnimationsEnabled) or nameof(GeneralSettings.AnimationSpeed) or "*") Anim.Configure(g.AnimationsEnabled, g.AnimationSpeed);
                    if (property is nameof(GeneralSettings.LaunchAtStartup) or "*") StartupManager.SetEnabled(g.LaunchAtStartup);
                    if (property is nameof(GeneralSettings.ReplaceTaskbar) or "*") { if (!_safeMode) EnableTaskbarReplacement(g.ReplaceTaskbar); UpdateTopBars(); }
                    if (property is nameof(GeneralSettings.ShowTopBar) or "*") { SyncTopBars(); if (!_safeMode) _dock.SyncWindows(); UpdateLocationTracking(); }
                    if (property is nameof(GeneralSettings.Language) or "*") Loc.Apply(g.Language);
                    if (property is nameof(GeneralSettings.ShowBatteryIcon) or nameof(GeneralSettings.ShowBatteryPercentage)) UpdateTopBars();
                    if (property is nameof(GeneralSettings.VerboseLogging)) Log.MinimumLevel = g.VerboseLogging ? LogLevel.Debug : LogLevel.Info;
                    break;
                case KeyboardSettings:
                    ConfigureKeyboardHook();
                    break;
                case DockSettings:
                    UpdateLocationTracking();
                    break;
                case DisplaySettings:
                    SyncTopBars();
                    if (!_safeMode) _dock.SyncWindows();
                    if (_hook != null) _hook.Suspended = _fullscreen.AnyFullscreen && _settings.Current.Displays.DisableShortcutsInFullscreen;
                    break;
                case WorkspaceSettings:
                    _workspaces.Refresh();
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Applying setting {property} failed", ex);
        }
    }

    private void HandleCommand(string command)
    {
        if (command.StartsWith("action:", StringComparison.OrdinalIgnoreCase))
        {
            if (Enum.TryParse(command["action:".Length..], true, out ShellAction action)) HandleAction(action);
            else Log.Warn($"Unknown action '{command}'");
            return;
        }
        if (command.StartsWith("settings:", StringComparison.OrdinalIgnoreCase))
        {
            OpenSettingsWindow();
            _settingsWindow?.ShowPanel(command["settings:".Length..].Trim().ToLowerInvariant());
            return;
        }
        if (command.StartsWith("search:", StringComparison.OrdinalIgnoreCase))
        {
            _overview.OpenWithSearch(command["search:".Length..]);
            return;
        }
        switch (command.Trim().ToLowerInvariant())
        {
            case "quit": Shutdown("command"); break;
            case "settings": OpenSettingsWindow(); break;
            case "apps": _overview.Open(OverviewView.Applications); break;
            default: _overview.Toggle(); break;
        }
    }

    private void EnableTaskbarReplacement(bool enable)
    {
        if (enable && !TaskbarController.IsHiddenByUs) TaskbarController.Hide();
        else if (!enable && TaskbarController.IsHiddenByUs) TaskbarController.Restore();
    }

    private void LaunchWatchdog()
    {
        _watchdog = Watchdog.Launch();
        if (_watchdog == null) return;
        _watchdog.EnableRaisingEvents = true;
        _watchdog.Exited += (_, _) => _ui.BeginInvoke(() =>
        {
            if (_shuttingDown) return;
            Log.Warn("Watchdog exited unexpectedly.");
            if (_watchdogRestarts++ < 3) LaunchWatchdog();
        });
    }

    public string Diagnostics()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"GnomeWin {typeof(ShellHost).Assembly.GetName().Version}  |  .NET {Environment.Version}  |  {Environment.OSVersion.VersionString}");
        sb.AppendLine($"Safe mode: {_safeMode}   Keyboard hook: {(_hook?.IsRunning == true ? "on" : "off")}   Taskbar hidden: {TaskbarController.IsHiddenByUs}");
        sb.AppendLine($"Virtual desktops: {_desktops?.BackendName} ({_workspaces?.Workspaces.Count} workspaces, dynamic {_workspaces?.DynamicActive})");
        sb.AppendLine($"Windows tracked: {_windows?.Windows.Count}   Apps discovered: {_apps?.Catalog.Count}");
        sb.AppendLine($"Notifications access: {_notifications?.WindowsAccessGranted}");
        if (_monitors != null)
            foreach (var m in _monitors.Monitors)
                sb.AppendLine($"Monitor {m.DeviceName}: {m.Bounds} work {m.WorkArea} dpi {m.Dpi}{(m.IsPrimary ? " (primary)" : "")}");
        sb.AppendLine($"Data: {AppPaths.Root}");
        return sb.ToString();
    }

    public void Shutdown(string reason)
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        Log.Info($"Shutting down ({reason}).");
        try { if (TaskbarController.IsHiddenByUs) TaskbarController.Restore(); } catch (Exception ex) { Log.Error("Restore failed", ex); }
        try { _workspaces?.CleanupOnExit(); } catch (Exception ex) { Log.Warn("Workspace cleanup failed", ex); }
        Dispose();
        RecoveryService.OnCleanExit();
        Log.Flush();
        Application.Current?.Shutdown(0);
    }

    public void Dispose()
    {
        void Safe(Action a) { try { a(); } catch (Exception ex) { Log.Warn("Dispose step failed", ex); } }
        Safe(() => _hook?.Dispose());
        Safe(() => _clock?.Stop());
        Safe(() => _overview?.Dispose());
        Safe(() => _dock?.Dispose());
        Safe(() => { foreach (var b in _topBars.Values) b.Close(); _topBars.Clear(); });
        Safe(() => _switcher?.Close());
        Safe(() => _osd?.Close());
        Safe(() => _settingsWindow?.Close());
        Safe(() => _discovery?.Dispose());
        Safe(() => _status?.Dispose());
        Safe(() => _windows?.Dispose());
        Safe(() => _desktops?.Dispose());
        Safe(() => _messages?.Dispose());
        Safe(() => _settings.Save());
        Safe(() => { if (_watchdog is { HasExited: false }) { /* exits by itself once we are gone */ } });
    }
}
