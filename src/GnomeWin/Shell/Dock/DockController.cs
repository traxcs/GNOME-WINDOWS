using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GnomeWin.Core;
using GnomeWin.Platform.Win32;
using GnomeWin.Services.Logging;
using GnomeWin.Services.Settings;
using GnomeWin.UI;
using static GnomeWin.Platform.Win32.NativeMethods;

namespace GnomeWin.Shell.Dock;

public sealed class DockController : IDisposable
{
    private readonly ApplicationManager _apps;
    private readonly WindowManager _windows;
    private readonly WorkspaceManager _workspaces;
    private readonly MonitorManager _monitors;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _rebuildTimer;
    private readonly DispatcherTimer _visibilityTimer;
    private readonly Dictionary<string, DockWindow> _docks = new();
    private readonly Dictionary<string, AppBar> _appBars = new();
    private readonly Dictionary<string, DispatcherTimer> _launchTimers = new();
    private readonly HashSet<IntPtr> _fullscreenMonitors = new();
    private bool _overviewOpen;
    private bool _suppressed;

    public ObservableCollection<DockItemViewModel> Items { get; } = new();

    public Action<WindowInfo?>? CloseOverviewAndActivate { get; set; }
    public Action? ShowApplications { get; set; }

    public DockController(ApplicationManager apps, WindowManager windows, WorkspaceManager workspaces,
                          MonitorManager monitors, SettingsService settings)
    {
        _apps = apps;
        _windows = windows;
        _workspaces = workspaces;
        _monitors = monitors;
        _settings = settings;

        _rebuildTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        _rebuildTimer.Tick += (_, _) => { _rebuildTimer.Stop(); Rebuild(); };
        _visibilityTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        _visibilityTimer.Tick += (_, _) => { _visibilityTimer.Stop(); EvaluateVisibility(); };

        _apps.PinsChanged += ScheduleRebuild;
        _apps.CatalogChanged += ScheduleRebuild;
        _windows.WindowsChanged += ScheduleRebuild;
        _windows.WindowMoved += _ => ScheduleVisibility();
        _windows.LayoutSettled += ScheduleVisibility;
        _windows.ForegroundChanged += (_, _) => { ScheduleVisibility(); if (_settings.Current.Displays.DockMonitors == MonitorPlacement.Active) SyncWindows(); };
        _workspaces.Switched += (_, _) => { ScheduleRebuild(); ScheduleVisibility(); };
        _apps.AppLaunched += OnAppLaunched;
        _settings.Changed += OnSettingsChanged;
        Rebuild();
    }

    private void ScheduleRebuild()
    {
        if (!_rebuildTimer.IsEnabled) _rebuildTimer.Start();
    }

    public void Rebuild()
    {
        var s = _settings.Current.Dock;
        Guid? only = s.IsolateWorkspaces ? _workspaces.CurrentId : null;
        var groups = _apps.BuildGroups(only);
        var existing = Items.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        var target = new List<DockItemViewModel>(groups.Count);
        foreach (var g in groups)
        {
            if (!existing.TryGetValue(g.App.Id, out var vm)) vm = new DockItemViewModel(g.App.Id);
            vm.App = g.App;
            vm.IsPinned = g.IsPinned;
            vm.SetWindows(g.Windows.OrderByDescending(w => w.ActivationStamp).ToList());
            target.Add(vm);
        }

        for (int i = 0; i < target.Count; i++)
        {
            if (i < Items.Count && ReferenceEquals(Items[i], target[i])) continue;
            int j = Items.IndexOf(target[i]);
            if (j >= 0) Items.Move(j, i);
            else Items.Insert(i, target[i]);
        }
        while (Items.Count > target.Count) Items.RemoveAt(Items.Count - 1);
        ScheduleVisibility();
    }

    private void OnAppLaunched(AppEntry app)
    {
        var vm = Items.FirstOrDefault(i => string.Equals(i.Id, app.Id, StringComparison.OrdinalIgnoreCase));
        if (vm == null || vm.IsRunning) return;
        vm.IsLaunching = true;
        if (_launchTimers.TryGetValue(app.Id, out var old)) old.Stop();
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        t.Tick += (_, _) => { t.Stop(); vm.IsLaunching = false; _launchTimers.Remove(app.Id); };
        _launchTimers[app.Id] = t;
        t.Start();
    }

    public DockView CreateView(bool inOverview)
    {
        var view = new DockView { ItemsSource = Items };
        ApplyViewSettings(view);
        view.ItemClicked += (item, el) => OnClick(item, el, inOverview || _overviewOpen);
        view.ItemMiddleClicked += (item, _) => { if (item.App != null) _apps.Launch(item.App); if (inOverview || _overviewOpen) CloseOverviewAndActivate?.Invoke(null); };
        view.ItemContextRequested += (item, el) => ShowContextMenu(item, el, inOverview || _overviewOpen);
        view.ShowAppsClicked += () => ShowApplications?.Invoke();
        view.ItemScrolled += (item, dir) => CycleWindows(item, dir);
        view.AppDropped += OnAppDropped;
        return view;
    }

    private void ApplyViewSettings(DockView view)
    {
        var s = _settings.Current.Dock;
        view.IconSize = s.IconSize;
        view.ZoomOnHover = s.HoverEffect == DockHoverEffect.Zoom;
        view.ShowAppsVisible = s.ShowAppsButton;
        var baseColor = (Application.Current.TryFindResource("Color.DockBase") as Color?) ?? Color.FromRgb(0x24, 0x24, 0x28);
        var brush = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(s.BackgroundOpacity, 0, 1) * 255), baseColor.R, baseColor.G, baseColor.B));
        brush.Freeze();
        view.DockBackground = brush;
    }

    public void RefreshTheme()
    {
        foreach (var d in _docks.Values) ApplyViewSettings(d.View);
        ViewSettingsChanged?.Invoke();
    }

    public event Action? ViewSettingsChanged;
    public void ApplyTo(DockView view) => ApplyViewSettings(view);

    private void OnAppDropped(string appId, int index)
    {
        var app = _apps.FindById(appId);
        if (app == null) return;
        if (_apps.IsPinned(appId)) _apps.MovePin(appId, index);
        else _apps.Pin(app, index);
    }

    public void OnClick(DockItemViewModel item, FrameworkElement anchor, bool inOverview)
    {
        if (item.App == null) return;

        var wins = OrderedWindows(item);
        if (wins.Count == 0)
        {
            _apps.Launch(item.App);
            if (inOverview) CloseOverviewAndActivate?.Invoke(null);
            return;
        }
        if (inOverview)
        {
            CloseOverviewAndActivate?.Invoke(wins[0]);
            return;
        }

        bool appIsActive = wins.Any(w => w.IsActive && !w.IsMinimized);
        if (wins.Count == 1)
        {
            var w = wins[0];
            if (appIsActive && _settings.Current.Dock.ActiveClick != DockActiveClick.ShowPreviews) WindowActions.Minimize(w.Handle);
            else if (appIsActive) ShowPicker(item, anchor);
            else WindowActions.Activate(w.Handle);
            return;
        }

        if (!appIsActive) { ShowPicker(item, anchor); return; }
        switch (_settings.Current.Dock.ActiveClick)
        {
            case DockActiveClick.Minimize:
                foreach (var w in wins) WindowActions.Minimize(w.Handle);
                break;
            case DockActiveClick.CycleWindows:
                CycleWindows(item, 1);
                break;
            default:
                ShowPicker(item, anchor);
                break;
        }
    }

    private List<WindowInfo> OrderedWindows(DockItemViewModel item)
    {
        Guid cur = _workspaces.CurrentId;
        return item.Windows
            .OrderByDescending(w => w.DesktopId == cur || w.DesktopId == Guid.Empty)
            .ThenByDescending(w => w.ActivationStamp)
            .ToList();
    }

    private void CycleWindows(DockItemViewModel item, int direction)
    {
        var wins = item.Windows.OrderBy(w => w.CreationStamp).ToList();
        if (wins.Count == 0) return;
        int idx = wins.FindIndex(w => w.IsActive);
        int next = idx < 0 ? 0 : (idx + direction + wins.Count) % wins.Count;
        WindowActions.Activate(wins[next].Handle);
    }

    private void ShowPicker(DockItemViewModel item, FrameworkElement anchor)
    {
        var picker = new WindowPickerWindow(OrderedWindows(item),
            w => WindowActions.Activate(w.Handle),
            w => WindowActions.Close(w.Handle));
        var p = anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0));
        var mon = _monitors.FromPoint((int)p.X, (int)p.Y);
        picker.ShowAt(new POINT((int)p.X, (int)p.Y), mon);
    }

    public void ShowContextMenu(DockItemViewModel item, FrameworkElement anchor, bool inOverview)
    {
        if (item.App == null) return;
        var app = item.App;
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };

        foreach (var w in OrderedWindows(item))
        {
            var wi = new MenuItem { Header = Trim(w.DisplayTitle, 48) };
            var target = w;
            wi.Click += (_, _) =>
            {
                if (inOverview) CloseOverviewAndActivate?.Invoke(target);
                else WindowActions.Activate(target.Handle);
            };
            menu.Items.Add(wi);
        }
        if (item.IsRunning) menu.Items.Add(new Separator());

        var newWin = new MenuItem { Header = item.IsRunning ? Loc.T("NewWindow") : Loc.T("Launch") };
        newWin.Click += (_, _) => { _apps.Launch(app); if (inOverview) CloseOverviewAndActivate?.Invoke(null); };
        menu.Items.Add(newWin);

        var pin = new MenuItem { Header = item.IsPinned ? Loc.T("Unpin") : Loc.T("Pin") };
        pin.Click += (_, _) => { if (item.IsPinned) _apps.Unpin(app.Id); else _apps.Pin(app); };
        menu.Items.Add(pin);

        if (app.ExePath != null && File.Exists(app.ExePath))
        {
            var loc = new MenuItem { Header = Loc.T("OpenLocation") };
            loc.Click += (_, _) => ApplicationManager.OpenFileLocation(app);
            menu.Items.Add(loc);
        }
        if (item.IsRunning)
        {
            menu.Items.Add(new Separator());
            var quit = new MenuItem { Header = item.Windows.Count > 1 ? Loc.F("QuitAll", item.Windows.Count) : Loc.T("Quit") };
            quit.Click += (_, _) => { foreach (var w in item.Windows) WindowActions.Close(w.Handle); };
            menu.Items.Add(quit);
        }
        menu.IsOpen = true;
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    public void ActivateIndex(int index)
    {
        var apps = Items.ToList();
        if (index < 0 || index >= apps.Count) return;
        var item = apps[index];
        if (item.App == null) return;
        var wins = OrderedWindows(item);
        if (wins.Count == 0) _apps.Launch(item.App);
        else if (wins[0].IsActive && wins.Count > 1) CycleWindows(item, 1);
        else WindowActions.Activate(wins[0].Handle);
    }

    public void SyncWindows()
    {
        var s = _settings.Current;
        var wanted = s.Displays.DockMonitors switch
        {
            MonitorPlacement.All => _monitors.Monitors.ToList(),
            MonitorPlacement.Active => new List<MonitorInfo> { ActiveMonitor() },
            _ => new List<MonitorInfo> { _monitors.Primary },
        };
        var wantedKeys = wanted.Select(m => m.Key).ToHashSet();
        foreach (var key in _docks.Keys.ToList())
        {
            if (wantedKeys.Contains(key)) continue;
            if (_appBars.Remove(key, out var bar)) bar.Dispose();
            _docks[key].Close();
            _docks.Remove(key);
        }
        foreach (var mon in wanted)
        {
            if (!_docks.TryGetValue(mon.Key, out var dock))
            {
                dock = new DockWindow(mon, CreateView(inOverview: false));
                dock.PointerChanged += _ => EvaluateVisibility();
                _docks[mon.Key] = dock;
            }
            dock.ApplyLayout(mon, s.Dock, TopInsetProvider?.Invoke(mon) ?? 0);
            ApplyViewSettings(dock.View);
            UpdateAppBar(dock);
        }
        EvaluateVisibility();
    }

    private void UpdateAppBar(DockWindow dock)
    {
        var s = _settings.Current.Dock;
        string key = dock.Monitor.Key;
        if (s.Visibility == DockVisibility.AlwaysVisible)
        {
            if (!_appBars.TryGetValue(key, out var bar))
            {
                bar = new AppBar(dock.EnsureHandle());
                _appBars[key] = bar;
                bar.PositionAssigned += _ => dock.ApplyLayout(dock.Monitor, _settings.Current.Dock, TopInsetProvider?.Invoke(dock.Monitor) ?? 0);
            }
            int thickness = DockWindow.ReservedPx(s, dock.Monitor);
            uint edge = s.Position switch { DockPosition.Left => ABE_LEFT, DockPosition.Right => ABE_RIGHT, _ => ABE_BOTTOM };
            bar.Register(dock.Monitor.Bounds, thickness, edge);
        }
        else if (_appBars.Remove(key, out var bar)) bar.Dispose();
    }

    private MonitorInfo ActiveMonitor()
    {
        IntPtr fg = _windows.ForegroundHandle;
        return fg != IntPtr.Zero ? _monitors.FromWindow(fg) : _monitors.Primary;
    }

    public Func<MonitorInfo, int>? TopInsetProvider { get; set; }

    public bool StaysDuringOverview => _settings.Current.Dock.Visibility == DockVisibility.AlwaysVisible;

    public (int Left, int Right, int Bottom) ReservedEdges(MonitorInfo m)
    {
        var s = _settings.Current.Dock;
        if (!StaysDuringOverview || !_docks.ContainsKey(m.Key)) return (0, 0, 0);
        int px = DockWindow.ReservedPx(s, m);
        return s.Position switch
        {
            DockPosition.Left => (px, 0, 0),
            DockPosition.Right => (0, px, 0),
            _ => (0, 0, px),
        };
    }

    public void BringToFront()
    {
        foreach (var d in _docks.Values) if (d.IsVisible) d.BringToTopmost();
    }

    public void SetOverviewOpen(bool open)
    {
        _overviewOpen = open;
        EvaluateVisibility();
    }

    public void SetSuppressed(bool suppressed)
    {
        _suppressed = suppressed;
        EvaluateVisibility();
    }

    public void SetFullscreen(IntPtr monitor, bool fullscreen)
    {
        bool changed = fullscreen ? _fullscreenMonitors.Add(monitor) : _fullscreenMonitors.Remove(monitor);
        if (changed) EvaluateVisibility();
    }

    private void ScheduleVisibility()
    {
        if (!_visibilityTimer.IsEnabled) _visibilityTimer.Start();
    }

    public void EvaluateVisibility()
    {
        var s = _settings.Current;
        foreach (var dock in _docks.Values)
        {
            bool fullscreen = _fullscreenMonitors.Contains(dock.Monitor.Handle) && s.Displays.HideDockInFullscreen;
            bool hidden = (_overviewOpen && !StaysDuringOverview) || fullscreen || _suppressed || s.Dock.Visibility == DockVisibility.OverviewOnly;
            if (hidden)
            {
                if (dock.IsVisible) dock.Hide();
                continue;
            }
            if (!dock.IsVisible)
            {
                dock.Show();
                dock.BringToTopmost();
            }
            bool reveal = s.Dock.Visibility switch
            {
                DockVisibility.AlwaysVisible => true,
                DockVisibility.AutoHide => dock.IsPointerInside,
                _ => dock.IsPointerInside || !Overlaps(dock),
            };
            dock.SetRevealed(reveal);
        }
    }

    private bool Overlaps(DockWindow dock)
    {
        RECT panel = dock.PanelScreenRect();
        if (panel.IsEmpty) return false;
        Guid cur = _workspaces.CurrentId;
        foreach (var w in _windows.Windows)
        {
            if (w.IsMinimized) continue;
            if (w.DesktopId != Guid.Empty && w.DesktopId != cur) continue;
            if (w.Bounds.Intersects(panel)) return true;
        }
        return false;
    }

    private void OnSettingsChanged(object section, string property)
    {
        if (section is DockSettings)
        {
            if (property is nameof(DockSettings.PinnedApps) or nameof(DockSettings.ShowAppsButton) or nameof(DockSettings.IsolateWorkspaces) or "*")
                ScheduleRebuild();
            SyncWindows();
            ViewSettingsChanged?.Invoke();
        }
        else if (section is DisplaySettings) SyncWindows();
    }

    public void Dispose()
    {
        foreach (var bar in _appBars.Values) bar.Dispose();
        _appBars.Clear();
        foreach (var d in _docks.Values) d.Close();
        _docks.Clear();
    }
}
