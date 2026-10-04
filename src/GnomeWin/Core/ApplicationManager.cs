using System.Diagnostics;
using System.Windows.Threading;
using GnomeWin.Platform.Win32;
using GnomeWin.Services.Logging;
using GnomeWin.Services.Search;
using GnomeWin.Services.Settings;

namespace GnomeWin.Core;

public sealed class AppGroup
{
    public required AppEntry App { get; init; }
    public List<WindowInfo> Windows { get; } = new();
    public bool IsPinned { get; init; }
    public bool IsRunning => Windows.Count > 0;
}

public sealed class ApplicationManager
{
    private readonly WindowManager _windows;
    private readonly SettingsService _settings;
    private readonly Dispatcher _ui;
    private List<AppEntry> _catalog = new();
    private readonly Dictionary<string, AppEntry> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AppEntry> _byExe = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AppEntry> _transient = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _firstSeen = new(StringComparer.OrdinalIgnoreCase);
    private long _seq;

    public ApplicationManager(WindowManager windows, SettingsService settings)
    {
        _windows = windows;
        _settings = settings;
        _ui = Dispatcher.CurrentDispatcher;
        _windows.AppResolver = Resolve;
    }

    public IReadOnlyList<AppEntry> Catalog => _catalog;
    public int IconPixelSize { get; set; } = 64;

    public event Action? CatalogChanged;
    public event Action? PinsChanged;
    public event Action<AppEntry>? AppLaunched;

    public void SetCatalog(IReadOnlyList<AppEntry> entries)
    {
        _catalog = entries.ToList();
        _byId.Clear();
        _byExe.Clear();
        _byExeName.Clear();
        foreach (var e in _catalog)
        {
            _byId[e.Id] = e;
            if (e.ExePath == null || e.ExePath.StartsWith("::", StringComparison.Ordinal)) continue;
            if (!_byExe.TryGetValue(e.ExePath, out var existing) || e.Id.Length < existing.Id.Length)
                _byExe[e.ExePath] = e;
            string name = Path.GetFileName(e.ExePath);
            if (!_byExeName.TryGetValue(name, out var list)) _byExeName[name] = list = new List<AppEntry>();
            list.Add(e);
        }
        _transient.Clear();
        _windows.ReResolveApps();
        CatalogChanged?.Invoke();
    }

    private static readonly Dictionary<string, string> KnownExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        ["explorer.exe"] = "Microsoft.Windows.Explorer",
    };

    private readonly Dictionary<string, List<AppEntry>> _byExeName = new(StringComparer.OrdinalIgnoreCase);

    public AppEntry? FindById(string id)
    {
        if (_byId.TryGetValue(id, out var e)) return e;
        if (_transient.TryGetValue(id, out e)) return e;
        if (_byExe.TryGetValue(id, out e)) return e;
        if (id.Length > 3 && id[1] == ':' && File.Exists(id)) return CreateTransient(id, null);
        return null;
    }

    public AppEntry? Resolve(WindowInfo w)
    {
        if (w.Aumid != null && _byId.TryGetValue(w.Aumid, out var byAumid)) return byAumid;
        if (w.ProcessPath != null)
        {
            if (_byExe.TryGetValue(w.ProcessPath, out var byExe)) return byExe;
            string file = Path.GetFileName(w.ProcessPath);
            if (KnownExecutables.TryGetValue(file, out var knownId) && _byId.TryGetValue(knownId, out var known)
                && (file != "explorer.exe" || w.ClassName == "CabinetWClass"))
                return known;
            if (_byExeName.TryGetValue(file, out var candidates))
            {
                foreach (var c in candidates.OrderBy(c => c.Id.Length))
                {
                    string? root = Path.GetDirectoryName(c.ExePath);
                    if (root != null && w.ProcessPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        return c;
                }
            }
        }

        string? key = w.Aumid ?? w.ProcessPath;
        if (key == null) return null;
        return CreateTransient(key, w);
    }

    private AppEntry CreateTransient(string key, WindowInfo? w)
    {
        if (_transient.TryGetValue(key, out var t)) return t;
        string? exe = w?.ProcessPath ?? (File.Exists(key) ? key : null);

        string name = Path.GetFileNameWithoutExtension(exe) ?? key;
        try
        {
            if (exe != null)
            {
                var vi = FileVersionInfo.GetVersionInfo(exe);
                if (!string.IsNullOrWhiteSpace(vi.FileDescription)) name = vi.FileDescription.Trim();
            }
        }
        catch { /* access denied for protected processes */ }

        t = new AppEntry
        {
            Id = key,
            Name = name,
            ExePath = exe,
            IsTransient = true,
            IdIsAumid = w?.Aumid != null,
            SearchName = TextMatcher.Normalize(name),
            SearchExe = TextMatcher.Normalize(Path.GetFileNameWithoutExtension(exe)),
        };
        _transient[key] = t;
        if (exe != null)
        {
            string path = exe;
            int px = IconPixelSize;
            var entry = t;
            StaWorker.Run(() => IconHelper.GetFileImage(path, px)).ContinueWith(task =>
            {
                if (task.Status != TaskStatus.RanToCompletion || task.Result == null) return;
                var img = task.Result;
                _ui.BeginInvoke(() =>
                {
                    entry.Icon = img;
                    foreach (var win in _windows.Windows) if (ReferenceEquals(win.App, entry)) win.NotifyAppChanged();
                });
            });
        }
        return t;
    }

    public IReadOnlyList<string> PinnedIds => _settings.Current.Dock.PinnedApps;

    public bool IsPinned(string appId) => PinnedIds.Contains(appId, StringComparer.OrdinalIgnoreCase);

    public void Pin(AppEntry app, int index = -1)
    {
        var list = _settings.Current.Dock.PinnedApps.ToList();
        list.RemoveAll(p => string.Equals(p, app.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index > list.Count) list.Add(app.Id); else list.Insert(index, app.Id);
        _settings.Current.Dock.PinnedApps = list;
        PinsChanged?.Invoke();
    }

    public void Unpin(string appId)
    {
        var list = _settings.Current.Dock.PinnedApps.Where(p => !string.Equals(p, appId, StringComparison.OrdinalIgnoreCase)).ToList();
        _settings.Current.Dock.PinnedApps = list;
        PinsChanged?.Invoke();
    }

    public void MovePin(string appId, int newIndex)
    {
        var list = _settings.Current.Dock.PinnedApps.ToList();
        int old = list.FindIndex(p => string.Equals(p, appId, StringComparison.OrdinalIgnoreCase));
        if (old < 0) return;
        list.RemoveAt(old);
        list.Insert(Math.Clamp(newIndex, 0, list.Count), appId);
        _settings.Current.Dock.PinnedApps = list;
        PinsChanged?.Invoke();
    }

    public List<AppGroup> BuildGroups(Guid? onlyDesktop = null)
    {
        var groups = new List<AppGroup>();
        var byId = new Dictionary<string, AppGroup>(StringComparer.OrdinalIgnoreCase);

        foreach (string pin in PinnedIds)
        {
            var app = FindById(pin);
            if (app == null || byId.ContainsKey(app.Id)) continue;
            var g = new AppGroup { App = app, IsPinned = true };
            groups.Add(g);
            byId[app.Id] = g;
        }

        foreach (var w in _windows.Windows.OrderBy(w => w.CreationStamp))
        {
            if (onlyDesktop.HasValue && w.DesktopId != Guid.Empty && w.DesktopId != onlyDesktop.Value) continue;
            var app = w.App;
            if (app == null) continue;
            if (!byId.TryGetValue(app.Id, out var g))
            {
                g = new AppGroup { App = app, IsPinned = false };
                byId[app.Id] = g;
                if (!_firstSeen.ContainsKey(app.Id)) _firstSeen[app.Id] = ++_seq;
            }
            g.Windows.Add(w);
        }

        groups.AddRange(byId.Values.Where(g => !g.IsPinned).OrderBy(g => _firstSeen.GetValueOrDefault(g.App.Id)));
        return groups;
    }

    public bool Launch(AppEntry app, bool asAdmin = false)
    {
        try
        {
            var psi = new ProcessStartInfo(app.LaunchTarget) { UseShellExecute = true };
            if (app.IsTransient && app.ExePath != null) psi.WorkingDirectory = Path.GetDirectoryName(app.ExePath) ?? string.Empty;
            if (asAdmin) psi.Verb = "runas";
            Process.Start(psi)?.Dispose();
            Log.Info($"Launched {app.Name} ({app.LaunchTarget})");
            AppLaunched?.Invoke(app);
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return false;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not launch {app.Name}", ex);
            return false;
        }
    }

    public static void OpenFileLocation(AppEntry app)
    {
        if (app.ExePath == null || !File.Exists(app.ExePath)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{app.ExePath}\"") { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) { Log.Warn("Open file location failed", ex); }
    }
}
