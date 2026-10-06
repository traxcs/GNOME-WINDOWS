using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GnomeWin.Core;
using GnomeWin.Input.KeyboardNavigation;
using GnomeWin.Platform.Win32;
using GnomeWin.Services;
using GnomeWin.Services.Logging;
using GnomeWin.Services.Notifications;
using GnomeWin.Services.Search;
using GnomeWin.Services.Settings;
using GnomeWin.Services.Wallpaper;
using GnomeWin.Shell.Dock;
using GnomeWin.UI;
using GnomeWin.UI.Animations;
using static GnomeWin.Platform.Win32.NativeMethods;

namespace GnomeWin.Shell.Overview;

public enum OverviewView { Windows, Applications, Search }

public sealed class OverviewController : IDisposable
{
    private const int LayoutMs = 240;
    private static bool Smooth => !UI.Components.ShellWindow.SoftwareRenderingEnabled;
    private static int OpenMs => Smooth ? 260 : 0;
    private static int ViewMs => Smooth ? 200 : 0;

    private sealed class Surface
    {
        public required OverviewWindow Window { get; init; }
        public List<WindowPreview> Previews { get; } = new();
    }

    private enum State { Hidden, Opening, Open, Closing }

    private readonly WindowManager _windows;
    private readonly WorkspaceManager _workspaces;
    private readonly ApplicationManager _apps;
    private readonly MonitorManager _monitors;
    private readonly SettingsService _settings;
    private readonly DockController _dock;
    private readonly SearchService _search;
    private readonly WallpaperProvider _wallpapers;
    private readonly NotificationService _notifications;
    private readonly List<Surface> _surfaces = new();
    private readonly FrameAnimation _openAnim, _layoutAnim, _viewAnim;
    private State _state = State.Hidden;
    private OverviewView _view = OverviewView.Windows;
    private double _p, _q = 1, _v = 1;
    private WindowInfo? _activateOnClose;
    private bool _chromeVisible;
    private bool _gridDirty = true;
    private int _selected = -1;
    private DateTime _ignoreForegroundUntil;
    private DateTime _lastWheel;
    private readonly System.Windows.Threading.DispatcherTimer _releaseTimer;

    private WindowPreview? _pressed;
    private Surface? _pressedSurface;
    private Point _pressPoint;
    private bool _dragging;
    private Vector _dragGrab;
    private Size _dragSize;

    public Func<MonitorInfo, RECT>? InsetProvider { get; set; }
    public event Action<bool>? OpenStateChanged;

    public OverviewController(WindowManager windows, WorkspaceManager workspaces, ApplicationManager apps, MonitorManager monitors,
                              SettingsService settings, DockController dock, SearchService search, WallpaperProvider wallpapers,
                              NotificationService notifications)
    {
        _windows = windows;
        _workspaces = workspaces;
        _apps = apps;
        _monitors = monitors;
        _settings = settings;
        _dock = dock;
        _search = search;
        _wallpapers = wallpapers;
        _notifications = notifications;
        _releaseTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        _releaseTimer.Tick += (_, _) =>
        {
            _releaseTimer.Stop();
            if (IsOpen) return;
            DisposeSurfaces();
            _wallpapers.Invalidate();
            _gridDirty = true;
            MemoryTrimmer.Trim("overview released");
        };
        _openAnim = new FrameAnimation(v => { _p = v; Render(); });
        _layoutAnim = new FrameAnimation(v => { _q = v; Render(); });
        _viewAnim = new FrameAnimation(v => { _v = v; Render(); });

        _windows.WindowAdded += OnWindowAdded;
        _windows.WindowRemoved += OnWindowRemoved;
        _windows.WindowUpdated += w => FindPreview(w)?.RefreshTexts();
        _workspaces.Switched += OnWorkspaceSwitched;
        _workspaces.Changed += () => { if (IsOpen) BuildStrip(); };
        _apps.CatalogChanged += () => _gridDirty = true;
        _monitors.MonitorsChanged += DisposeSurfaces;
        _dock.ViewSettingsChanged += () =>
        {
            foreach (var s in _surfaces)
                if (s.Window.DockHost.Child is DockView v) _dock.ApplyTo(v);
        };
    }

    public bool IsOpen => _state is State.Opening or State.Open;
    public OverviewView View => _view;

    public bool IgnoreForegroundChanges => DateTime.Now < _ignoreForegroundUntil;

    public bool OwnsWindow(IntPtr hwnd) => _surfaces.Any(s => s.Window.Handle == hwnd);

    public void Toggle(OverviewView view = OverviewView.Windows)
    {
        if (IsOpen && (view == OverviewView.Windows || _view == view)) Close(null);
        else Open(view);
    }

    public void Open(OverviewView view = OverviewView.Windows)
    {
        if (_state == State.Open || _state == State.Opening)
        {
            SetView(view);
            return;
        }
        if (_state == State.Closing)
        {
            _state = State.Opening;
            _activateOnClose = null;
            _openAnim.Start(_p, 1, (int)(OpenMs * (1 - _p)), OnOpened);
            OpenStateChanged?.Invoke(true);
            return;
        }

        _releaseTimer.Stop();
        ProcessPower.SetEfficiencyMode(false);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            _windows.RefreshBounds();
            _workspaces.Refresh();
            EnsureSurfaces();
            var s = _settings.Current;
            foreach (var surface in _surfaces)
            {
                surface.Window.Place(surface.Window.Monitor, InsetProvider?.Invoke(surface.Window.Monitor) ?? default);
                ConfigureEmbeddedDock(surface.Window);
                surface.Window.SetBackground(_wallpapers.Get(surface.Window.Monitor), s.Overview.Background);
                surface.Window.SetStripVisible(s.Overview.ShowWorkspaceStrip);
                surface.Window.SetProgress(0);
                surface.Window.Show();
                surface.Window.BringToTopmost();
                surface.Window.UpdateLayout();
            }

            BuildStrip();
            foreach (var surface in _surfaces) surface.Window.UpdateLayout();
            BuildPreviews();
            _view = view;
            _v = view == OverviewView.Windows ? 1 : 0;
            ApplyViewVisibility(animate: false);
            if (view == OverviewView.Applications) PrepareGrid();
            _selected = -1;
            _q = 1;
            _p = 0;
            _chromeVisible = false;
            _state = State.Opening;
            _dock.SetOverviewOpen(true);
            _dock.BringToFront();
            OpenStateChanged?.Invoke(true);

            var primary = Primary;
            if (primary != null)
            {
                primary.Window.SearchBox.Text = string.Empty;
                UI.FocusRing.Reset(primary.Window.SearchBox);
                primary.Window.ForceActivate();
                primary.Window.SearchBox.Focus();
                Keyboard.Focus(primary.Window.SearchBox);
            }
            _search.RefreshRecentFilesIfStale();
            Render();
            Log.Debug($"Overview open ({view}) prepared in {sw.ElapsedMilliseconds} ms");
            _openAnim.Start(0, 1, OpenMs, OnOpened);
        }
        catch (Exception ex)
        {
            Log.Error("Opening the Overview failed", ex);
            HideAll();
        }
    }

    public void OpenWithSearch(string text)
    {
        Open();
        var box = Primary?.Window.SearchBox;
        if (box == null) return;
        box.Text = text;
        box.CaretIndex = text.Length;
    }

    private void OnOpened()
    {
        _state = State.Open;
        Log.Debug($"Overview opened: {_openAnim.Frames} frames in {_openAnim.ElapsedMs:0} ms");
        Render();
    }

    public void Close(WindowInfo? activate)
    {
        if (_state is State.Hidden or State.Closing) return;
        Log.Debug($"Overview closing (activate: {activate?.ToString() ?? "none"})");
        EndDrag(cancel: true);
        _activateOnClose = activate;
        _state = State.Closing;
        _chromeVisible = false;
        foreach (var s in _surfaces)
            foreach (var p in s.Previews)
                p.Real = RealRectFor(p.Window, s);
        if (_v < 1) _viewAnim.Start(_v, 1, (int)(OpenMs * _p));
        _openAnim.Start(_p, 0, (int)(OpenMs * Math.Max(0.3, _p)), OnClosed);
        OpenStateChanged?.Invoke(false);
    }

    private void OnClosed()
    {
        var target = _activateOnClose;
        _activateOnClose = null;
        if (target != null) WindowActions.Activate(target.Handle);
        HideAll();
    }

    private void HideAll()
    {
        _openAnim.Stop();
        _layoutAnim.Stop();
        _viewAnim.Stop();
        foreach (var s in _surfaces)
        {
            foreach (var p in s.Previews) p.Detach();
            s.Previews.Clear();
            s.Window.PreviewCanvas.Children.Clear();
            s.Window.Hide();
            s.Window.AppGrid.Visibility = Visibility.Collapsed;
            s.Window.Results.Visibility = Visibility.Collapsed;
        }
        _state = State.Hidden;
        _view = OverviewView.Windows;
        _p = 0; _q = 1; _v = 1;
        _dock.SetOverviewOpen(false);
        _releaseTimer.Start();
        ProcessPower.SetEfficiencyMode(true);
    }

    private Surface? Primary => _surfaces.FirstOrDefault(s => s.Window.IsPrimarySurface);

    private void EnsureSurfaces()
    {
        bool all = _settings.Current.Displays.OverviewMonitors == MonitorPlacement.All;
        var wanted = all ? _monitors.Monitors.ToList() : new List<MonitorInfo> { _monitors.Primary };
        bool same = _surfaces.Count == wanted.Count && _surfaces.All(s => wanted.Any(m => m.Key == s.Window.Monitor.Key));
        if (same)
        {
            foreach (var s in _surfaces) s.Window.Place(wanted.First(m => m.Key == s.Window.Monitor.Key), default);
            return;
        }
        DisposeSurfaces();
        foreach (var mon in wanted)
        {
            var win = new OverviewWindow(mon, mon.IsPrimary || !all);
            var surface = new Surface { Window = win };
            win.Place(mon, default);
            WireSurface(surface);
            _surfaces.Add(surface);
        }
    }

    private void WireSurface(Surface s)
    {
        var w = s.Window;
        w.BackgroundClicked += OnBackgroundClicked;
        w.WheelScrolled += OnWheel;
        w.PreviewMouseMove += (_, e) => OnDragMove(s, e);
        w.PreviewMouseLeftButtonUp += (_, e) => OnDragUp(s, e);
        if (!w.IsPrimarySurface) return;

        w.PreviewKeyDown += OnKeyDown;
        w.PreviewTextInput += (_, e) =>
        {
            if (!w.SearchBox.IsKeyboardFocused && !string.IsNullOrEmpty(e.Text) && !char.IsControl(e.Text[0]))
            {
                w.SearchBox.Focus();
                w.SearchBox.Text += e.Text;
                w.SearchBox.CaretIndex = w.SearchBox.Text.Length;
                e.Handled = true;
            }
        };
        w.SearchBox.TextChanged += (_, _) => OnSearchChanged(w.SearchBox.Text);
        w.AppGrid.LaunchRequested += app => { _apps.Launch(app); Close(null); };
        w.AppGrid.ContextRequested += ShowAppMenu;
        w.Results.Activated += ActivateResult;
        w.Results.ContextRequested += (r, el) => { if (r.App != null) ShowAppMenu(r.App, el); };
        w.Strip.SwitchRequested += i => SwitchWorkspace(i);
        w.Strip.RemoveRequested += i => _workspaces.Remove(i);
        w.Strip.CreateRequested += () => _workspaces.Create();
        w.Strip.ReorderRequested += (from, to) => _workspaces.MoveWorkspace(from, to);
        w.DockHost.Child = _dock.CreateView(inOverview: true);
    }

    private void ConfigureEmbeddedDock(OverviewWindow w)
    {
        if (!w.IsPrimarySurface) return;
        var pos = _settings.Current.Dock.Position;
        w.ConfigureDock(_dock.StaysDuringOverview ? null : pos);
        if (w.DockHost.Child is DockView v) v.ConfigureShape(pos, extended: false);
    }

    private void DisposeSurfaces()
    {
        if (IsOpen) HideAll();
        foreach (var s in _surfaces)
        {
            if (s.Window.DockHost.Child is DockView v) v.ItemsSource = null;
            s.Window.Close();
        }
        _surfaces.Clear();
    }

    private Surface SurfaceFor(WindowInfo w)
    {
        if (_surfaces.Count == 1) return _surfaces[0];
        return _surfaces.FirstOrDefault(s => s.Window.Monitor.Handle == w.Monitor) ?? Primary ?? _surfaces[0];
    }

    private Rect? RealRectFor(WindowInfo w, Surface s)
    {
        if (w.IsMinimized || IsIconic(w.Handle)) return null;
        RECT b = GetFrameBounds(w.Handle);
        if (b.IsEmpty) return null;
        if (MonitorFromWindow(w.Handle, MONITOR_DEFAULTTONEAREST) != s.Window.Monitor.Handle) return null;
        return s.Window.ToLocal(b);
    }

    private IEnumerable<WindowInfo> CurrentWorkspaceWindows() =>
        (_workspaces.Current?.Windows ?? _windows.Windows.ToList()).Where(w => IsWindow(w.Handle));

    private void BuildPreviews()
    {
        foreach (var s in _surfaces)
        {
            foreach (var p in s.Previews) p.Detach();
            s.Previews.Clear();
            s.Window.PreviewCanvas.Children.Clear();
        }
        var zOrder = ZOrder();
        var windows = CurrentWorkspaceWindows()
            .OrderByDescending(w => zOrder.TryGetValue(w.Handle, out int z) ? z : int.MaxValue)
            .ToList();
        foreach (var w in windows) AddPreview(w, animateIn: false);
        foreach (var s in _surfaces) Layout(s, animate: false);
    }

    private static Dictionary<IntPtr, int> ZOrder()
    {
        var map = new Dictionary<IntPtr, int>();
        int i = 0;
        EnumWindows((h, _) => { map[h] = i++; return true; }, IntPtr.Zero);
        return map;
    }

    private WindowPreview AddPreview(WindowInfo w, bool animateIn)
    {
        var s = SurfaceFor(w);
        var p = new WindowPreview(w);
        p.Attach(s.Window.Handle, _settings.Current.Overview.LivePreviews);
        p.Real = RealRectFor(w, s);
        if (animateIn) { p.AppearFrom = 0; p.AppearTo = 1; }
        s.Previews.Add(p);
        s.Window.PreviewCanvas.Children.Add(p.Chrome);

        p.Chrome.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && IsInside(d, p.CloseButton)) return;
            _pressed = p;
            _pressedSurface = s;
            _pressPoint = e.GetPosition(s.Window);
            e.Handled = true;
        };
        p.Chrome.MouseRightButtonUp += (_, e) => { ShowWindowMenu(p, s); e.Handled = true; };
        p.Chrome.MouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Middle) { WindowActions.Close(w.Handle); e.Handled = true; } };
        p.CloseButton.Click += (_, _) => WindowActions.Close(w.Handle);
        return p;
    }

    private static bool IsInside(DependencyObject d, DependencyObject container)
    {
        while (d != null)
        {
            if (ReferenceEquals(d, container)) return true;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    private WindowPreview? FindPreview(WindowInfo w) =>
        _surfaces.SelectMany(s => s.Previews).FirstOrDefault(p => ReferenceEquals(p.Window, w) && !p.Removing);

    private List<WindowPreview> AllPreviews() =>
        _surfaces.SelectMany(s => s.Previews).Where(p => !p.Removing).ToList();

    private void Layout(Surface s, bool animate)
    {
        var live = s.Previews.Where(p => !p.Removing).ToList();
        double scale = Math.Max(0.5, s.Window.Scale);
        var items = live.Select(p =>
        {
            var b = p.Window.Bounds;
            var size = new Size(Math.Max(80, b.Width / scale), Math.Max(60, b.Height / scale));
            var center = new Point((b.Left + b.Width / 2.0) / scale, (b.Top + b.Height / 2.0) / scale);
            return new LayoutWindow(size, center);
        }).ToList();
        var area = s.Window.WindowsArea();
        double monW = s.Window.Monitor.Bounds.Width / scale, monH = s.Window.Monitor.Bounds.Height / scale;
        double maxScale = Math.Min(1.0, Math.Min(area.Width / monW, area.Height / monH));
        var rects = WindowLayout.Compute(items, area, 36,
            WindowPreview.PadTop + WindowPreview.LabelHeight + 12, _settings.Current.Overview.Layout, maxScale);

        double eq = Anim.EaseOutCubic(_q);
        for (int i = 0; i < live.Count; i++)
        {
            var p = live[i];
            if (animate && p.Slot != default)
            {
                p.PreviousSlot = Lerp(p.PreviousSlot, p.Slot, eq);
                p.AppearFrom = p.AppearFrom + (p.AppearTo - p.AppearFrom) * eq;
            }
            else
            {
                p.PreviousSlot = rects[i];
                if (!animate) p.AppearFrom = p.AppearTo;
            }
            p.Slot = rects[i];
        }
        if (animate) StartLayoutAnimation();
    }

    private void StartLayoutAnimation()
    {
        _q = 0;
        _chromeVisible = false;
        _layoutAnim.Start(0, 1, LayoutMs, OnLayoutDone);
    }

    private void OnLayoutDone()
    {
        foreach (var s in _surfaces)
        {
            foreach (var p in s.Previews.Where(p => p.Removing).ToList())
            {
                p.Detach();
                s.Window.PreviewCanvas.Children.Remove(p.Chrome);
                s.Previews.Remove(p);
            }
            foreach (var p in s.Previews) { p.PreviousSlot = p.Slot; p.AppearFrom = p.AppearTo; }
        }
        Render();
    }

    private static Rect Lerp(Rect a, Rect b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, Math.Max(0, a.Width + (b.Width - a.Width) * t), Math.Max(0, a.Height + (b.Height - a.Height) * t));

    private static Rect ScaleAround(Rect r, double k) =>
        new(r.X + r.Width * (1 - k) / 2, r.Y + r.Height * (1 - k) / 2, r.Width * k, r.Height * k);

    private void Render()
    {
        if (_state == State.Hidden && !_openAnim.IsRunning) return;
        double e = Anim.EaseOutCubic(_p);
        double eq = Anim.EaseOutCubic(_q);
        bool chrome = _state == State.Open && _q >= 1 && _v >= 1 && !_dragging;
        bool chromeChanged = chrome != _chromeVisible;
        _chromeVisible = chrome;
        bool alwaysTitles = _settings.Current.Overview.AlwaysShowTitles;

        foreach (var s in _surfaces)
        {
            s.Window.SetProgress(_p);
            double scale = s.Window.Scale;
            foreach (var p in s.Previews)
            {
                if (p.IsDragging) continue;
                Rect slot = Lerp(p.PreviousSlot, p.Slot, eq);
                double appear = p.AppearFrom + (p.AppearTo - p.AppearFrom) * eq;
                Rect rect;
                double opacity;
                if (p.Real is Rect real)
                {
                    rect = Lerp(real, slot, e);
                    opacity = appear;
                }
                else
                {
                    rect = ScaleAround(slot, 0.85 + 0.15 * e);
                    opacity = e * appear;
                }
                opacity *= _v;
                p.Apply(rect, opacity, scale, _v > 0.01);
                if (chromeChanged) p.SetChromeVisible(chrome, alwaysTitles);
            }
        }
    }

    public void SetView(OverviewView view)
    {
        if (!IsOpen || view == _view) return;
        Log.Debug($"Overview view {_view} -> {view}");
        _view = view;
        if (view == OverviewView.Applications) PrepareGrid();
        ApplyViewVisibility(animate: true);
        _viewAnim.Start(_v, view == OverviewView.Windows ? 1 : 0, ViewMs);
        if (view != OverviewView.Search && Primary?.Window.SearchBox.Text.Length > 0) Primary.Window.SearchBox.Text = string.Empty;
    }

    private void ApplyViewVisibility(bool animate)
    {
        var w = Primary?.Window;
        if (w == null) return;
        void ShowEl(UIElement el, bool show)
        {
            if (show)
            {
                el.Visibility = Visibility.Visible;
                el.Opacity = animate ? 0 : 1;
                if (animate) Anim.Fade(el, 1, ViewMs);
            }
            else if (el.Visibility == Visibility.Visible)
            {
                if (animate) Anim.Fade(el, 0, ViewMs / 2, () => { if (!ShouldShow(el)) el.Visibility = Visibility.Collapsed; });
                else el.Visibility = Visibility.Collapsed;
            }
        }
        ShowEl(w.AppGrid, _view == OverviewView.Applications);
        ShowEl(w.Results, _view == OverviewView.Search);
    }

    private bool ShouldShow(UIElement el)
    {
        var w = Primary?.Window;
        if (w == null) return false;
        return (ReferenceEquals(el, w.AppGrid) && _view == OverviewView.Applications) ||
               (ReferenceEquals(el, w.Results) && _view == OverviewView.Search);
    }

    private void PrepareGrid()
    {
        var w = Primary?.Window;
        if (w == null) return;
        if (_gridDirty)
        {
            var apps = _apps.Catalog.Where(a => !a.IsTransient).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var gsw = System.Diagnostics.Stopwatch.StartNew();
            w.AppGrid.SetApps(apps);
            Log.Debug($"App grid built: {apps.Count} tiles in {gsw.ElapsedMilliseconds} ms");
            _gridDirty = false;
        }
        w.AppGrid.ScrollToTop();
    }

    private void OnSearchChanged(string text)
    {
        if (!IsOpen) return;
        if (string.IsNullOrWhiteSpace(text))
        {
            if (_view == OverviewView.Search) SetView(OverviewView.Windows);
            return;
        }
        var s = _settings.Current.Overview;
        var results = _search.Search(text, _apps.Catalog, _windows.Windows,
            new SearchOptions { Files = s.SearchRecentFiles, Settings = s.SearchWindowsSettings, Calculator = s.SearchCalculator });
        Primary?.Window.Results.Show(results);
        if (_view != OverviewView.Search)
        {
            _view = OverviewView.Search;
            ApplyViewVisibility(animate: true);
            _viewAnim.Start(_v, 0, ViewMs);
        }
    }

    private void ActivateResult(SearchResult r)
    {
        switch (r.Kind)
        {
            case SearchResultKind.Application when r.App != null:
                _apps.Launch(r.App);
                Close(null);
                break;
            case SearchResultKind.Window when r.Window != null:
                Close(r.Window);
                break;
            case SearchResultKind.Calculation when r.Target != null:
                try { Clipboard.SetText(r.Target); } catch { }
                Close(null);
                break;
            default:
                if (r.Target != null) ShellLauncher.Open(r.Target);
                Close(null);
                break;
        }
    }

    private void BuildStrip()
    {
        var primary = Primary;
        if (primary == null) return;
        var mon = primary.Window.Monitor;
        var wp = _wallpapers.Get(mon);
        var b = _workspaces.Backend;
        primary.Window.Strip.Build(_workspaces.Workspaces, mon, wp.Sharp ?? wp.Blurred, wp.Stretch,
            canRemove: b.SupportsRemoveAny,
            showAdd: !_workspaces.DynamicActive && b.SupportsCreate,
            canReorder: b.SupportsReorder);
    }

    private void SwitchWorkspace(int index)
    {
        _ignoreForegroundUntil = DateTime.Now.AddMilliseconds(900);
        _workspaces.SwitchTo(index);
        Primary?.Window.Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(120);
            if (IsOpen && Primary != null)
            {
                Primary.Window.ForceActivate();
                Primary.Window.SearchBox.Focus();
            }
        });
    }

    private void OnWorkspaceSwitched(int oldIndex, int newIndex)
    {
        if (!IsOpen) return;
        _ignoreForegroundUntil = DateTime.Now.AddMilliseconds(900);
        int dir = Math.Sign(newIndex - oldIndex);
        if (dir == 0) dir = 1;
        var zOrder = ZOrder();
        foreach (var s in _surfaces)
        {
            double width = s.Window.ActualWidth;
            double eq = Anim.EaseOutCubic(_q);
            foreach (var p in s.Previews.Where(p => !p.Removing))
            {
                var cur = Lerp(p.PreviousSlot, p.Slot, eq);
                p.PreviousSlot = cur;
                p.Slot = new Rect(cur.X - dir * width, cur.Y, cur.Width, cur.Height);
                p.AppearFrom = 1;
                p.AppearTo = 0;
                p.Removing = true;
            }
        }
        var incoming = CurrentWorkspaceWindows()
            .OrderByDescending(w => zOrder.TryGetValue(w.Handle, out int z) ? z : int.MaxValue).ToList();
        var added = incoming.Select(w => AddPreview(w, animateIn: true)).ToList();
        foreach (var s in _surfaces)
        {
            var mine = added.Where(a => s.Previews.Contains(a)).ToList();
            Layout(s, animate: false);
            double width = s.Window.ActualWidth;
            foreach (var p in mine)
            {
                p.PreviousSlot = new Rect(p.Slot.X + dir * width, p.Slot.Y, p.Slot.Width, p.Slot.Height);
                p.AppearFrom = 0.4;
                p.AppearTo = 1;
            }
        }
        _selected = -1;
        StartLayoutAnimation();
        BuildStrip();
    }

    private void OnWindowAdded(WindowInfo w)
    {
        if (!IsOpen) return;
        var p = AddPreview(w, animateIn: true);
        var s = SurfaceFor(w);
        p.PreviousSlot = default;
        Layout(s, animate: true);
        p.PreviousSlot = p.Slot;
    }

    private void OnWindowRemoved(WindowInfo w)
    {
        if (!IsOpen) return;
        foreach (var s in _surfaces)
        {
            var p = s.Previews.FirstOrDefault(x => ReferenceEquals(x.Window, w) && !x.Removing);
            if (p == null) continue;
            p.Detach();
            p.Removing = true;
            p.AppearTo = 0;
            Layout(s, animate: true);
        }
    }

    private void OnBackgroundClicked()
    {
        if (_dragging) return;
        if (_view == OverviewView.Search) { if (Primary != null) Primary.Window.SearchBox.Text = string.Empty; return; }
        if (_view == OverviewView.Applications) { SetView(OverviewView.Windows); return; }
        if (_settings.Current.Overview.CloseOnEmptyClick) Close(null);
    }

    private void OnWheel(MouseWheelEventArgs e)
    {
        if (_view != OverviewView.Windows || (DateTime.Now - _lastWheel).TotalMilliseconds < 250) return;
        _lastWheel = DateTime.Now;
        int target = _workspaces.CurrentIndex + (e.Delta > 0 ? -1 : 1);
        if (target >= 0 && target < _workspaces.Workspaces.Count) SwitchWorkspace(target);
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var w = Primary?.Window;
        if (w == null) return;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        switch (key)
        {
            case Key.Escape:
                if (w.SearchBox.Text.Length > 0) w.SearchBox.Text = string.Empty;
                else if (_view == OverviewView.Applications) SetView(OverviewView.Windows);
                else Close(null);
                e.Handled = true;
                return;
            case Key.Enter:
                if (_view == OverviewView.Search) w.Results.ActivateSelected();
                else if (_view == OverviewView.Applications) w.AppGrid.ActivateSelected();
                else
                {
                    var all = AllPreviews();
                    if (_selected >= 0 && _selected < all.Count) Close(all[_selected].Window);
                    else Close(null);
                }
                e.Handled = true;
                return;
            case Key.Left: case Key.Right: case Key.Up: case Key.Down:
            case Key.PageUp: case Key.PageDown: case Key.Home: case Key.End:
            case Key.Tab:
                break;
            default:
                return;
        }

        if (_view == OverviewView.Search)
        {
            if (key is Key.Up or Key.Left || (key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))) w.Results.MoveSelection(-1);
            else if (key is Key.Down or Key.Right or Key.Tab) w.Results.MoveSelection(1);
            e.Handled = true;
        }
        else if (_view == OverviewView.Applications)
        {
            w.AppGrid.MoveSelection(key == Key.Tab ? Key.Right : key);
            e.Handled = true;
        }
        else
        {
            if (key == Key.PageUp) { if (_workspaces.CurrentIndex > 0) SwitchWorkspace(_workspaces.CurrentIndex - 1); }
            else if (key == Key.PageDown) { if (_workspaces.CurrentIndex < _workspaces.Workspaces.Count - 1) SwitchWorkspace(_workspaces.CurrentIndex + 1); }
            else MoveSelection(key);
            e.Handled = true;
        }
    }

    private void MoveSelection(Key key)
    {
        var all = AllPreviews();
        if (all.Count == 0) return;
        int next;
        if (_selected < 0 || _selected >= all.Count)
        {
            next = all.FindIndex(p => p.Window.IsActive);
            if (next < 0) next = 0;
        }
        else if (key == Key.Tab)
        {
            next = (_selected + (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? all.Count - 1 : 1)) % all.Count;
        }
        else
        {
            var rects = all.Select(p =>
            {
                var s = _surfaces.First(x => x.Previews.Contains(p));
                var tl = s.Window.ToScreen(p.Slot.TopLeft);
                var br = s.Window.ToScreen(p.Slot.BottomRight);
                return new Rect(tl.X, tl.Y, Math.Max(1, br.X - tl.X), Math.Max(1, br.Y - tl.Y));
            }).ToList();
            var dir = key switch { Key.Left => NavDirection.Left, Key.Right => NavDirection.Right, Key.Up => NavDirection.Up, _ => NavDirection.Down };
            next = SpatialNavigator.Next(rects, _selected, dir);
        }
        for (int i = 0; i < all.Count; i++) all[i].SetSelected(i == next);
        _selected = next;
    }

    private void OnDragMove(Surface s, MouseEventArgs e)
    {
        if (_pressed == null || !ReferenceEquals(_pressedSurface, s) || e.LeftButton != MouseButtonState.Pressed) return;
        Point pos = e.GetPosition(s.Window);
        if (!_dragging)
        {
            if ((pos - _pressPoint).Length < 8) return;
            _dragging = true;
            _pressed.IsDragging = true;
            _workspaces.HoldMaintenance = true;
            s.Window.CaptureMouse();
            var cur = _pressed.Current;
            double k = Math.Min(1, 260 / Math.Max(1, Math.Max(cur.Width, cur.Height)));
            _dragSize = new Size(cur.Width * k, cur.Height * k);
            _dragGrab = new Vector((_pressPoint.X - cur.X) / Math.Max(1, cur.Width) * _dragSize.Width,
                                   (_pressPoint.Y - cur.Y) / Math.Max(1, cur.Height) * _dragSize.Height);
            _pressed.SetChromeVisible(false, false);
            Panel.SetZIndex(_pressed.Chrome, 100);
        }
        var rect = new Rect(pos.X - _dragGrab.X, pos.Y - _dragGrab.Y, _dragSize.Width, _dragSize.Height);
        _pressed.Apply(rect, 0.92, s.Window.Scale, true);
        if (s.Window.IsPrimarySurface)
        {
            int cell = s.Window.Strip.HitTestCell(s.Window.TranslatePoint(pos, s.Window.Strip));
            s.Window.Strip.HighlightDrop(cell != _workspaces.CurrentIndex ? cell : -1);
        }
    }

    private void OnDragUp(Surface s, MouseButtonEventArgs e)
    {
        if (_pressed == null) return;
        var preview = _pressed;
        if (!_dragging)
        {
            _pressed = null;
            if (!IsInside((DependencyObject)e.OriginalSource, preview.CloseButton)) Close(preview.Window);
            return;
        }
        int cell = -1;
        if (s.Window.IsPrimarySurface)
            cell = s.Window.Strip.HitTestCell(s.Window.TranslatePoint(e.GetPosition(s.Window), s.Window.Strip));
        EndDrag(cancel: false);

        if (cell >= 0 && cell != _workspaces.CurrentIndex)
        {
            if (!_workspaces.CanMoveWindows)
            {
                _notifications.AddShellNotice("GnomeWin", Loc.T("MoveNotSupported"));
            }
            else if (_workspaces.MoveWindow(preview.Window, cell, follow: false))
            {
                preview.PreviousSlot = preview.Current;
                preview.Removing = true;
                preview.AppearTo = 0;
                Layout(s, animate: true);
                return;
            }
        }
        preview.PreviousSlot = preview.Current;
        StartLayoutAnimation();
    }

    private void EndDrag(bool cancel)
    {
        if (_pressed != null)
        {
            _pressed.IsDragging = false;
            Panel.SetZIndex(_pressed.Chrome, 0);
            if (cancel && _dragging) _pressed.PreviousSlot = _pressed.Current;
        }
        if (_dragging)
        {
            foreach (var s in _surfaces) { s.Window.ReleaseMouseCapture(); if (s.Window.IsPrimarySurface) s.Window.Strip.HighlightDrop(-1); }
            _workspaces.HoldMaintenance = false;
            _workspaces.ScheduleRefresh();
        }
        _dragging = false;
        _pressed = null;
        _pressedSurface = null;
    }

    private void ShowWindowMenu(WindowPreview p, Surface s)
    {
        var w = p.Window;
        var menu = new ContextMenu { PlacementTarget = p.Chrome };
        void Add(string text, Action a, bool enabled = true)
        {
            var mi = new MenuItem { Header = text, IsEnabled = enabled };
            mi.Click += (_, _) => a();
            menu.Items.Add(mi);
        }
        Add(w.DisplayTitle.Length > 50 ? w.DisplayTitle[..49] + "…" : w.DisplayTitle, () => Close(w));
        menu.Items.Add(new Separator());
        Add(Loc.T("Minimize"), () => WindowActions.Minimize(w.Handle));
        Add(Loc.T("Maximize"), () => WindowActions.ToggleMaximize(w.Handle));
        if (_workspaces.Workspaces.Count > 1 || _workspaces.CanMoveWindows)
        {
            menu.Items.Add(new Separator());
            foreach (var ws in _workspaces.Workspaces)
            {
                if (ws.IsCurrent) continue;
                int idx = ws.Index;
                Add(Loc.F("MoveToWorkspace", idx + 1), () => _workspaces.MoveWindow(w, idx, follow: false), _workspaces.CanMoveWindows);
            }
            if (!_workspaces.DynamicActive)
                Add(Loc.T("MoveToNewWorkspace"), () => _workspaces.MoveWindow(w, _workspaces.Workspaces.Count, follow: false), _workspaces.CanMoveWindows);
        }
        if (_monitors.Monitors.Count > 1)
        {
            menu.Items.Add(new Separator());
            for (int i = 0; i < _monitors.Monitors.Count; i++)
            {
                var m = _monitors.Monitors[i];
                if (m.Handle == w.Monitor) continue;
                Add(Loc.F("MoveToMonitor", i + 1), () => WindowActions.MoveToMonitor(w.Handle, m.WorkArea));
            }
        }
        menu.Items.Add(new Separator());
        Add(Loc.T("Close"), () => WindowActions.Close(w.Handle));
        menu.IsOpen = true;
    }

    private void ShowAppMenu(AppEntry app, FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor };
        void Add(string text, Action a) { var mi = new MenuItem { Header = text }; mi.Click += (_, _) => a(); menu.Items.Add(mi); }
        Add(Loc.T("Launch"), () => { _apps.Launch(app); Close(null); });
        Add(_apps.IsPinned(app.Id) ? Loc.T("Unpin") : Loc.T("Pin"), () => { if (_apps.IsPinned(app.Id)) _apps.Unpin(app.Id); else _apps.Pin(app); });
        if (app.ExePath != null && File.Exists(app.ExePath)) Add(Loc.T("OpenLocation"), () => { ApplicationManager.OpenFileLocation(app); Close(null); });
        Add(Loc.T("RunAsAdmin"), () => { _apps.Launch(app, asAdmin: true); Close(null); });
        menu.IsOpen = true;
    }

    public void Dispose()
    {
        DisposeSurfaces();
    }
}
