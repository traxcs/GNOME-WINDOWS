using System.Windows.Threading;
using GnomeWin.Platform.VirtualDesktop;
using GnomeWin.Services.Logging;
using GnomeWin.Services.Settings;

namespace GnomeWin.Core;

public sealed class Workspace
{
    public required Guid Id { get; init; }
    public required int Index { get; init; }
    public List<WindowInfo> Windows { get; } = new();
    public bool IsCurrent { get; init; }
    public bool IsEmpty => Windows.Count == 0;
}

public sealed class WorkspaceManager
{
    public const int MaxWorkspaces = 16;

    private readonly VirtualDesktopService _vd;
    private readonly WindowManager _windows;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _maintainTimer;
    private List<Workspace> _workspaces = new();
    private Guid _lastCurrent;
    private bool _maintaining;

    public WorkspaceManager(VirtualDesktopService vd, WindowManager windows, SettingsService settings)
    {
        _vd = vd;
        _windows = windows;
        _settings = settings;
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(60) };
        _refreshTimer.Tick += (_, _) => { _refreshTimer.Stop(); Refresh(); };
        _maintainTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(700) };
        _maintainTimer.Tick += (_, _) => { _maintainTimer.Stop(); Maintain(); };

        _vd.Changed += ScheduleRefresh;
        _windows.WindowsChanged += ScheduleRefresh;
    }

    public IReadOnlyList<Workspace> Workspaces => _workspaces;
    public int CurrentIndex => Math.Max(0, _workspaces.FindIndex(w => w.IsCurrent));
    public Workspace? Current => _workspaces.FirstOrDefault(w => w.IsCurrent);
    public Guid CurrentId => Current?.Id ?? Guid.Empty;
    public IVirtualDesktopBackend Backend => _vd.Backend;
    public bool CanMoveWindows => _vd.Backend.SupportsMoveWindow;
    public bool CanReorder => _vd.Backend.SupportsReorder;
    public bool DynamicActive => _settings.Current.Workspaces.Dynamic && _vd.IsFullFeatured;

    public event Action? Changed;
    public event Action<int, int>? Switched;

    public void Start()
    {
        Refresh();
        if (!_settings.Current.Workspaces.Dynamic && _vd.Backend.SupportsCreate && _vd.IsFullFeatured)
        {
            int missing = _settings.Current.Workspaces.InitialCount - _workspaces.Count;
            for (int i = 0; i < missing; i++) _vd.Run(b => b.Create(), null);
            if (missing > 0) Refresh();
        }
        ScheduleMaintain();
    }

    public void ScheduleRefresh()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    public void Refresh()
    {
        var ids = _vd.GetDesktops();
        Guid current = _vd.GetCurrent();
        if (ids.Count == 0) ids = new[] { current };
        if (!ids.Contains(current) && ids.Count > 0) current = ids[0];

        _windows.RefreshDesktopIds();
        var list = new List<Workspace>(ids.Count);
        for (int i = 0; i < ids.Count; i++)
            list.Add(new Workspace { Id = ids[i], Index = i, IsCurrent = ids[i] == current });

        foreach (var w in _windows.Windows)
        {
            var ws = list.FirstOrDefault(x => x.Id == w.DesktopId) ?? list.First(x => x.IsCurrent);
            ws.Windows.Add(w);
        }
        foreach (var ws in list) ws.Windows.Sort((a, b) => b.ActivationStamp.CompareTo(a.ActivationStamp));

        int oldIndex = _workspaces.FindIndex(w => w.Id == _lastCurrent);
        _workspaces = list;
        Changed?.Invoke();
        if (current != _lastCurrent)
        {
            Guid prev = _lastCurrent;
            _lastCurrent = current;
            if (prev != Guid.Empty) Switched?.Invoke(oldIndex, CurrentIndex);
        }
        ScheduleMaintain();
    }

    private void ScheduleMaintain()
    {
        if (!DynamicActive) return;
        _maintainTimer.Stop();
        _maintainTimer.Start();
    }

    public bool HoldMaintenance { get; set; }

    private void Maintain()
    {
        if (!DynamicActive || _maintaining || HoldMaintenance) return;
        _maintaining = true;
        try
        {
            var ws = _workspaces;
            if (ws.Count == 0) return;
            bool changed = false;

            for (int i = ws.Count - 2; i >= 0; i--)
            {
                if (!ws[i].IsEmpty || ws[i].IsCurrent) continue;
                Guid fallback = ws[i + 1].Id;
                if (_vd.Run(b => b.Remove(ws[i].Id, fallback), false))
                {
                    Log.Debug($"Dynamic workspaces: removed empty workspace {i + 1}");
                    changed = true;
                }
            }
            if (!changed && ws.Count >= 2 && ws[^1].IsEmpty && ws[^2].IsEmpty && ws[^1].IsCurrent)
            {
                if (_vd.Run(b => b.Remove(ws[^2].Id, ws[^1].Id), false)) changed = true;
            }
            if (!ws[^1].IsEmpty && ws.Count < MaxWorkspaces)
            {
                if (_vd.Run(b => b.Create(), null) != null)
                {
                    Log.Debug("Dynamic workspaces: appended an empty workspace");
                    changed = true;
                }
            }
            if (changed) Refresh();
        }
        finally { _maintaining = false; }
    }

    public void SwitchTo(int index)
    {
        if (index < 0 || index >= _workspaces.Count || index == CurrentIndex) return;
        Guid id = _workspaces[index].Id;
        _vd.Run(b => b.Switch(id), false);
        ScheduleRefresh();
    }

    public void Next() => Step(+1);
    public void Previous() => Step(-1);

    private void Step(int delta)
    {
        int count = _workspaces.Count;
        if (count == 0) return;
        int target = CurrentIndex + delta;
        if (target < 0 || target >= count)
        {
            if (!_settings.Current.Workspaces.WrapAround) return;
            target = (target + count) % count;
        }
        SwitchTo(target);
    }

    public int Create()
    {
        if (_workspaces.Count >= MaxWorkspaces) return -1;
        _vd.Run(b => b.Create(), null);
        Refresh();
        return _workspaces.Count - 1;
    }

    public bool Remove(int index)
    {
        if (index < 0 || index >= _workspaces.Count || _workspaces.Count <= 1) return false;
        if (!_vd.Backend.SupportsRemoveAny && !_workspaces[index].IsCurrent) return false;
        Guid fallback = _workspaces[index > 0 ? index - 1 : index + 1].Id;
        Guid id = _workspaces[index].Id;
        bool ok = _vd.Run(b => b.Remove(id, fallback), false);
        Refresh();
        return ok;
    }

    public bool MoveWindow(WindowInfo window, int index, bool follow)
    {
        if (index < 0) return false;
        if (index >= _workspaces.Count)
        {
            if (Create() < 0) return false;
            index = _workspaces.Count - 1;
        }
        Guid id = _workspaces[index].Id;
        bool ok = _vd.Run(b => b.MoveWindow(window.Handle, id), false);
        if (!ok)
        {
            Log.Warn($"Could not move {window} to workspace {index + 1}");
            return false;
        }
        window.DesktopId = id;
        if (follow)
        {
            _vd.Run(b => b.Switch(id), false);
            Platform.Win32.WindowActions.Activate(window.Handle);
        }
        Refresh();
        return true;
    }

    public bool MoveWorkspace(int from, int to)
    {
        if (from == to || from < 0 || from >= _workspaces.Count || to < 0 || to >= _workspaces.Count) return false;
        Guid id = _workspaces[from].Id;
        bool ok = _vd.Run(b => b.MoveDesktop(id, to), false);
        Refresh();
        return ok;
    }

    public void CleanupOnExit()
    {
        if (!DynamicActive) return;
        Refresh();
        var ws = _workspaces;
        if (ws.Count > 1 && ws[^1].IsEmpty && !ws[^1].IsCurrent)
            _vd.Run(b => b.Remove(ws[^1].Id, ws[^2].Id), false);
    }

    public IEnumerable<WindowInfo> WindowsOnCurrent() => Current?.Windows ?? Enumerable.Empty<WindowInfo>();
}
