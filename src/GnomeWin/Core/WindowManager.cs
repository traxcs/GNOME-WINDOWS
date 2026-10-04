using System.Windows.Threading;
using GnomeWin.Platform.VirtualDesktop;
using GnomeWin.Platform.Win32;
using GnomeWin.Services.Logging;
using static GnomeWin.Platform.Win32.NativeMethods;
using static GnomeWin.Platform.Win32.WinEventHooks;

namespace GnomeWin.Core;

public sealed class WindowManager : IDisposable
{
    private readonly Dictionary<IntPtr, WindowInfo> _windows = new();
    private readonly Dictionary<uint, string?> _processPaths = new();
    private readonly WinEventHooks _hooks = new();
    private readonly VirtualDesktopService _desktops;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _coalesce;
    private long _stamp;

    public WindowManager(VirtualDesktopService desktops)
    {
        _desktops = desktops;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _coalesce = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromMilliseconds(40) };
        _coalesce.Tick += (_, _) => { _coalesce.Stop(); WindowsChanged?.Invoke(); };
    }

    public Func<WindowInfo, AppEntry?>? AppResolver { get; set; }

    public event Action<WindowInfo>? WindowAdded;
    public event Action<WindowInfo>? WindowRemoved;
    public event Action<WindowInfo>? WindowUpdated;
    public event Action<WindowInfo>? WindowMoved;
    public event Action<WindowInfo?, IntPtr>? ForegroundChanged;
    public event Action? WindowsChanged;
    public event Action<IntPtr>? AnyWindowShown;
    public event Action? LayoutSettled;

    public IReadOnlyCollection<WindowInfo> Windows => _windows.Values;

    public bool TrackLocation
    {
        get => _hooks.IsTrackingLocation;
        set
        {
            if (value == _hooks.IsTrackingLocation) return;
            _hooks.SetLocationTracking(value);
            Log.Info($"Live window geometry tracking: {(value ? "on" : "off")}");
            if (value) RefreshBounds();
        }
    }

    public void RefreshBounds()
    {
        foreach (var w in _windows.Values) if (IsWindow(w.Handle)) UpdateBounds(w, raise: false);
    }

    public string TakeEventStats()
    {
        string s = $"raw {_hooks.RawCount}: " + string.Join(", ", _hooks.Counts.OrderByDescending(k => k.Value).Select(k => $"0x{k.Key:X}={k.Value}"));
        _hooks.RawCount = 0;
        _hooks.Counts.Clear();
        return s;
    }
    public WindowInfo? ActiveWindow { get; private set; }
    public IntPtr ForegroundHandle { get; private set; }

    public IEnumerable<WindowInfo> MostRecentFirst() =>
        _windows.Values.OrderByDescending(w => w.ActivationStamp).ThenByDescending(w => w.CreationStamp);

    public WindowInfo? Get(IntPtr hwnd) => _windows.TryGetValue(hwnd, out var w) ? w : null;

    public void Start()
    {
        var handles = new List<IntPtr>();
        EnumWindows((h, _) => { handles.Add(h); return true; }, IntPtr.Zero);
        for (int i = handles.Count - 1; i >= 0; i--) TryTrack(handles[i], raiseEvents: false);
        HandleForeground(GetForegroundWindow());
        _hooks.WindowEvent += OnWindowEvent;
        _hooks.Install();
        Log.Info($"WindowManager started, {_windows.Count} windows tracked.");
        WindowsChanged?.Invoke();
    }

    public void Resync()
    {
        var alive = new HashSet<IntPtr>();
        EnumWindows((h, _) => { alive.Add(h); return true; }, IntPtr.Zero);
        foreach (var w in _windows.Values.ToList())
            if (!alive.Contains(w.Handle) || !IsEligible(w.Handle, w.ClassName)) Remove(w.Handle);
        foreach (var h in alive) if (!_windows.ContainsKey(h)) TryTrack(h, raiseEvents: true);
        RefreshDesktopIds();
        HandleForeground(GetForegroundWindow());
        ScheduleChanged();
    }

    public void RefreshDesktopIds()
    {
        foreach (var w in _windows.Values)
        {
            w.DesktopId = _desktops.GetWindowDesktop(w.Handle);
            w.IsCloakedByShell = GetCloakedState(w.Handle) == DWM_CLOAKED_SHELL;
        }
    }

    public void ReResolveApps()
    {
        foreach (var w in _windows.Values)
        {
            w.App = AppResolver?.Invoke(w);
            if (w.App != null) w.AppId = w.App.Id;
            w.NotifyAppChanged();
        }
        ScheduleChanged();
    }

    private void OnWindowEvent(uint evt, IntPtr hwnd)
    {
        switch (evt)
        {
            case EVENT_SYSTEM_FOREGROUND:
                HandleForeground(hwnd);
                break;
            case EVENT_OBJECT_SHOW:
                AnyWindowShown?.Invoke(hwnd);
                goto case EVENT_OBJECT_CREATE;
            case EVENT_OBJECT_CREATE:
            case EVENT_OBJECT_UNCLOAKED:
                if (_windows.TryGetValue(hwnd, out var existing)) { RefreshState(existing); RefreshDesktop(existing); }
                else TryTrack(hwnd, raiseEvents: true);
                break;
            case EVENT_OBJECT_HIDE:
                if (_windows.ContainsKey(hwnd) && !IsWindowVisible(hwnd)) Remove(hwnd);
                break;
            case EVENT_OBJECT_DESTROY:
                Remove(hwnd);
                break;
            case EVENT_OBJECT_CLOAKED:
                if (_windows.TryGetValue(hwnd, out var cloaked))
                {
                    if (!IsEligible(hwnd, cloaked.ClassName)) Remove(hwnd);
                    else RefreshDesktop(cloaked);
                }
                break;
            case EVENT_OBJECT_NAMECHANGE:
                if (_windows.TryGetValue(hwnd, out var named))
                {
                    string t = GetWindowTitle(hwnd);
                    if (t != named.Title)
                    {
                        named.Title = t;
                        if (named.App?.IsTransient != false && named.ClassName == "ApplicationFrameWindow") ResolveApp(named);
                        WindowUpdated?.Invoke(named);
                    }
                }
                else TryTrack(hwnd, raiseEvents: true);
                break;
            case EVENT_SYSTEM_MINIMIZESTART:
            case EVENT_SYSTEM_MINIMIZEEND:
                if (_windows.TryGetValue(hwnd, out var min))
                {
                    RefreshState(min);
                    WindowUpdated?.Invoke(min);
                    LayoutSettled?.Invoke();
                    ScheduleChanged();
                }
                break;
            case EVENT_OBJECT_LOCATIONCHANGE:
                if (_windows.TryGetValue(hwnd, out var moved)) UpdateBounds(moved, raise: true);
                break;
            case EVENT_SYSTEM_MOVESIZEEND:
                if (_windows.TryGetValue(hwnd, out var sized)) { UpdateBounds(sized, raise: true); LayoutSettled?.Invoke(); }
                break;
        }
    }

    private void HandleForeground(IntPtr hwnd)
    {
        ForegroundHandle = hwnd;
        IntPtr root = hwnd == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hwnd, GA_ROOTOWNER);
        WindowInfo? w = null;
        if (hwnd != IntPtr.Zero && !_windows.TryGetValue(hwnd, out w) && root != IntPtr.Zero)
            _windows.TryGetValue(root, out w);
        if (w == null && hwnd != IntPtr.Zero && TryTrack(hwnd, raiseEvents: true)) _windows.TryGetValue(hwnd, out w);

        if (!ReferenceEquals(ActiveWindow, w))
        {
            if (ActiveWindow != null) ActiveWindow.IsActive = false;
            ActiveWindow = w;
            if (w != null) w.IsActive = true;
        }
        if (w != null)
        {
            w.ActivationStamp = ++_stamp;
            if (w.IsMinimized) RefreshState(w);
        }
        ForegroundChanged?.Invoke(w, hwnd);
        ScheduleChanged();
    }

    private bool IsEligible(IntPtr hwnd, string className) =>
        WindowFilter.IsAppWindow(hwnd, className, _desktops.GetWindowDesktop);

    private bool TryTrack(IntPtr hwnd, bool raiseEvents)
    {
        if (_windows.ContainsKey(hwnd)) return true;
        if (GetAncestor(hwnd, GA_ROOT) != hwnd) return false;
        string cls = GetClassNameOf(hwnd);
        if (!IsEligible(hwnd, cls)) return false;

        GetWindowThreadProcessId(hwnd, out uint pid);


        var w = new WindowInfo(hwnd)
        {
            ClassName = cls,
            ProcessId = pid,
            Title = GetWindowTitle(hwnd),
            CreationStamp = ++_stamp,
            ActivationStamp = raiseEvents ? 0 : _stamp,
        };
        w.ProcessPath = GetCachedProcessPath(pid);
        RefreshState(w);
        RefreshDesktop(w);
        ResolveApp(w);
        _windows[hwnd] = w;
        LoadIconAsync(w);

        if (raiseEvents)
        {
            Log.Debug($"Window added {w}");
            WindowAdded?.Invoke(w);
            ScheduleChanged();
        }
        return true;
    }

    private void Remove(IntPtr hwnd)
    {
        if (!_windows.Remove(hwnd, out var w)) return;
        if (ReferenceEquals(ActiveWindow, w)) ActiveWindow = null;
        Log.Debug($"Window removed {w}");
        WindowRemoved?.Invoke(w);
        ScheduleChanged();
    }

    private void ScheduleChanged()
    {
        if (!_coalesce.IsEnabled) _coalesce.Start();
    }

    private string? GetCachedProcessPath(uint pid)
    {
        if (_processPaths.TryGetValue(pid, out var p)) return p;
        p = GetProcessPath(pid);
        if (_processPaths.Count > 512) _processPaths.Clear();
        _processPaths[pid] = p;
        return p;
    }

    private void ResolveApp(WindowInfo w)
    {
        w.Aumid = ShellApi.GetWindowAumid(w.Handle);
        string? path = w.ProcessPath;

        if (w.ClassName == "ApplicationFrameWindow")
        {
            uint hostPid = w.ProcessId;
            uint childPid = 0;
            EnumChildWindows(w.Handle, (child, _) =>
            {
                GetWindowThreadProcessId(child, out uint cp);
                if (cp != hostPid) { childPid = cp; return false; }
                return true;
            }, IntPtr.Zero);
            if (childPid != 0)
            {
                path = GetCachedProcessPath(childPid) ?? path;
                w.Aumid ??= GetProcessAumid(childPid);
            }
        }
        w.Aumid ??= GetProcessAumid(w.ProcessId);
        w.ProcessPath = path;
        w.App = AppResolver?.Invoke(w);
        w.AppId = w.App?.Id ?? w.Aumid ?? path ?? $"pid:{w.ProcessId}";
        w.NotifyAppChanged();
    }

    private void RefreshState(WindowInfo w)
    {
        w.IsMinimized = IsIconic(w.Handle);
        w.IsMaximized = !w.IsMinimized && IsZoomed(w.Handle);
        UpdateBounds(w, raise: false);
    }

    private void RefreshDesktop(WindowInfo w)
    {
        w.DesktopId = _desktops.GetWindowDesktop(w.Handle);
        w.IsCloakedByShell = GetCloakedState(w.Handle) == DWM_CLOAKED_SHELL;
        ScheduleChanged();
    }

    private void UpdateBounds(WindowInfo w, bool raise)
    {
        RECT r;
        bool minimized = IsIconic(w.Handle);
        if (minimized)
        {
            var wp = new WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf<WINDOWPLACEMENT>() };
            GetWindowPlacement(w.Handle, ref wp);
            r = wp.rcNormalPosition;
        }
        else r = GetFrameBounds(w.Handle);

        IntPtr mon = MonitorFromWindow(w.Handle, MONITOR_DEFAULTTONEAREST);
        bool changed = r != w.Bounds || mon != w.Monitor || minimized != w.IsMinimized;
        w.Bounds = r;
        w.Monitor = mon;
        if (minimized != w.IsMinimized) { w.IsMinimized = minimized; w.IsMaximized = !minimized && IsZoomed(w.Handle); }
        else if (!minimized) w.IsMaximized = IsZoomed(w.Handle);
        if (raise && changed) WindowMoved?.Invoke(w);
    }

    private void LoadIconAsync(WindowInfo w)
    {
        IntPtr hwnd = w.Handle;
        Task.Run(() =>
        {
            var icon = IconHelper.GetWindowIcon(hwnd);
            if (icon != null) _dispatcher.BeginInvoke(() => w.Icon = icon);
        });
    }

    public void Dispose()
    {
        _hooks.Dispose();
        _coalesce.Stop();
    }
}
