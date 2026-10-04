using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using GnomeWin.Core;
using GnomeWin.Platform.Win32;
using GnomeWin.Services.Logging;
using GnomeWin.Services.Search;

namespace GnomeWin.Services.AppDiscovery;

public sealed partial class AppDiscoveryService : IDisposable
{
    private readonly Dispatcher _ui;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly DispatcherTimer _rescanDebounce;
    private int _iconPx = 64;
    private int _scanning;

    public event Action<IReadOnlyList<AppEntry>>? CatalogLoaded;
    public DateTime LastScan { get; private set; }

    public AppDiscoveryService(Dispatcher ui)
    {
        _ui = ui;
        _rescanDebounce = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(3) };
        _rescanDebounce.Tick += (_, _) => { _rescanDebounce.Stop(); Scan(_iconPx); };
    }

    public void Start(int iconPx)
    {
        _iconPx = iconPx;
        Scan(iconPx);
        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                 })
        {
            if (!Directory.Exists(folder)) continue;
            try
            {
                var w = new FileSystemWatcher(folder) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
                w.Created += OnStartMenuChanged; w.Deleted += OnStartMenuChanged; w.Renamed += OnStartMenuChanged;
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch (Exception ex) { Log.Warn($"Cannot watch {folder}", ex); }
        }
    }

    private void OnStartMenuChanged(object sender, FileSystemEventArgs e) =>
        _ui.BeginInvoke(() => { _rescanDebounce.Stop(); _rescanDebounce.Start(); });

    public void RefreshIfOlderThan(TimeSpan maxAge)
    {
        if (DateTime.Now - LastScan > maxAge) Scan(_iconPx);
    }

    public void Scan(int iconPx)
    {
        if (Interlocked.Exchange(ref _scanning, 1) == 1) return;
        var t = new Thread(() =>
        {
            try { ScanCore(iconPx); }
            catch (Exception ex) { Log.Error("App discovery failed", ex); }
            finally { Interlocked.Exchange(ref _scanning, 0); }
        })
        { IsBackground = true, Name = "GnomeWin.AppDiscovery", Priority = ThreadPriority.BelowNormal };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    internal static List<(AppEntry Entry, IShellItem Item)> Enumerate()
    {
        var linkTargets = ScanStartMenuLinks();
        var found = new List<(AppEntry Entry, IShellItem Item)>();

        Guid folderId = ShellApi.FOLDERID_AppsFolder, iidItem = ShellApi.IID_IShellItem;
        int hr = ShellApi.SHGetKnownFolderItem(ref folderId, 0, IntPtr.Zero, ref iidItem, out IShellItem apps);
        if (hr != 0 || apps == null) { Log.Error($"AppsFolder unavailable (0x{hr:X8})"); return found; }

        Guid bhid = ShellApi.BHID_EnumItems, iidEnum = ShellApi.IID_IEnumShellItems;
        if (apps.BindToHandler(IntPtr.Zero, ref bhid, ref iidEnum, out IntPtr pEnum) != 0 || pEnum == IntPtr.Zero)
        {
            Log.Error("Cannot enumerate AppsFolder");
            Marshal.ReleaseComObject(apps);
            return found;
        }
        var enumerator = (IEnumShellItems)Marshal.GetObjectForIUnknown(pEnum);
        Marshal.Release(pEnum);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (enumerator.Next(1, out IShellItem child, out uint fetched) == 0 && fetched == 1)
        {
            try
            {
                var entry = CreateEntry(child, linkTargets);
                if (entry != null && seen.Add(entry.Id)) found.Add((entry, child));
                else Marshal.ReleaseComObject(child);
            }
            catch (Exception ex)
            {
                Log.Debug("Skipping app entry: " + ex.Message);
                Marshal.ReleaseComObject(child);
            }
        }
        Marshal.ReleaseComObject(enumerator);
        Marshal.ReleaseComObject(apps);
        return found;
    }

    private void ScanCore(int iconPx)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var found = Enumerate();
        var entries = found.Select(f => f.Entry).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        LastScan = DateTime.Now;
        Log.Info($"App discovery: {entries.Count} apps in {sw.ElapsedMilliseconds} ms.");
        _ui.BeginInvoke(() => CatalogLoaded?.Invoke(entries));

        var batch = new List<(AppEntry, System.Windows.Media.ImageSource?)>();
        foreach (var (entry, item) in found)
        {
            try { batch.Add((entry, IconHelper.GetShellItemImage(item, iconPx))); }
            catch (Exception ex) { Log.Debug($"Icon failed for {entry.Name}: {ex.Message}"); }
            finally { Marshal.ReleaseComObject(item); }
            if (batch.Count >= 24) Flush();
        }
        Flush();
        Log.Info($"App icons loaded in {sw.ElapsedMilliseconds} ms.");

        void Flush()
        {
            var copy = batch.ToList();
            batch.Clear();
            _ui.BeginInvoke(DispatcherPriority.Background, () => { foreach (var (e, img) in copy) if (img != null) e.Icon = img; });
        }
    }

    [GeneratedRegex(@"^(uninstall|désinstaller|desinstaller|remove|supprimer)\b|\b(uninstall|désinstall\w*)\b", RegexOptions.IgnoreCase)]
    private static partial Regex UninstallRegex();

    private static readonly HashSet<string> ExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".url", ".htm", ".html", ".txt", ".pdf", ".chm", ".rtf", ".md", ".log", ".ini", ".xml", ".hlp", ".doc", ".docx",
    };

    private static AppEntry? CreateEntry(IShellItem item, Dictionary<string, string> linkTargets)
    {
        string? id = ShellApi.GetDisplayName(item, ShellApi.SIGDN_PARENTRELATIVEPARSING);
        string? name = ShellApi.GetDisplayName(item, ShellApi.SIGDN_NORMALDISPLAY);
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) return null;
        if (UninstallRegex().IsMatch(name)) return null;

        string? exe = null;
        var store = ShellApi.GetPropertyStore(item);
        if (store != null)
        {
            try { exe = ShellApi.GetStringProperty(store, ShellApi.PKEY_Link_TargetParsingPath); }
            finally { Marshal.ReleaseComObject(store); }
        }
        exe ??= ResolveKnownFolderPath(id);
        if (exe == null && linkTargets.TryGetValue(id, out var target)) exe = target;
        if (exe == null && (id.Length > 2 && id[1] == ':')) exe = id;

        bool isAumid = !id.Contains('\\');
        if (exe != null && (exe.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || exe.StartsWith("https:", StringComparison.OrdinalIgnoreCase))) return null;
        string? ext = exe != null && !exe.StartsWith("::", StringComparison.Ordinal) ? Path.GetExtension(exe) : null;
        if (ext != null && ExcludedExtensions.Contains(ext)) return null;
        if (ExcludedExtensions.Contains(Path.GetExtension(name))) return null;
        if (!isAumid && exe == null && Path.HasExtension(id) && ExcludedExtensions.Contains(Path.GetExtension(id))) return null;

        return new AppEntry
        {
            Id = id,
            Name = name,
            ExePath = exe,
            IdIsAumid = isAumid,
            SearchName = TextMatcher.Normalize(name),
            SearchExe = exe != null ? TextMatcher.Normalize(Path.GetFileNameWithoutExtension(exe)) : string.Empty,
        };
    }

    private static string? ResolveKnownFolderPath(string id)
    {
        if (id.Length < 40 || id[0] != '{' || id[37] != '}') return null;
        if (!Guid.TryParse(id.AsSpan(0, 38), out Guid folder)) return null;
        string? root = NativeMethods.GetKnownFolderPath(folder);
        if (root == null) return null;
        return Path.Combine(root, id[38..].TrimStart('\\'));
    }

    private static Dictionary<string, string> ScanStartMenuLinks()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                 })
        {
            if (!Directory.Exists(folder)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories).ToList(); }
            catch { continue; }
            foreach (var lnk in files)
            {
                object? link = null;
                try
                {
                    link = new CShellLink();
                    ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Load(lnk, 0);
                    var sb = new StringBuilder(1024);
                    ((IShellLinkW)link).GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                    string target = sb.ToString();
                    if (string.IsNullOrEmpty(target)) continue;
                    if (link is IPropertyStore ps)
                    {
                        string? aumid = ShellApi.GetStringProperty(ps, ShellApi.PKEY_AppUserModel_ID);
                        if (!string.IsNullOrEmpty(aumid)) map[aumid] = target;
                    }
                }
                catch { /* broken shortcut */ }
                finally { if (link != null) Marshal.ReleaseComObject(link); }
            }
        }
        return map;
    }

    public void Dispose()
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }
}
