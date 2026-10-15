using System.Windows.Media;
using System.Windows.Threading;
using GnomeWin.Platform.Win32;
using GnomeWin.Services.Logging;

namespace GnomeWin.Services.AppDiscovery;

/// <summary>
/// Icons at an exact pixel size, so a surface that does not draw at the catalogue's size — the dock,
/// whose icon size is a setting — shows the icon the shell has for that size instead of a resampled one.
/// </summary>
public static class AppIconCache
{
    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Peek(string appId, int px)
    {
        lock (Cache) return Cache.TryGetValue(Key(appId, px), out var icon) ? icon : null;
    }

    /// <summary>
    /// Loads the icons of <paramref name="apps"/> at <paramref name="px"/> on a shell (STA) worker and
    /// reports each one on <paramref name="ui"/> as it arrives.
    /// </summary>
    public static void LoadAsync(IReadOnlyList<(string Id, string? ExePath)> apps, int px, Dispatcher ui, Action<string, ImageSource> loaded)
    {
        var pending = new List<(string Id, string? ExePath)>();
        foreach (var app in apps)
        {
            var cached = Peek(app.Id, px);
            if (cached != null) ui.BeginInvoke(() => loaded(app.Id, cached));
            else pending.Add(app);
        }
        if (pending.Count == 0) return;

        var worker = new Thread(() =>
        {
            foreach (var (id, exe) in pending)
            {
                try
                {
                    var icon = Load(id, exe, px);
                    if (icon == null) continue;
                    ui.BeginInvoke(DispatcherPriority.Background, () => loaded(id, icon));
                }
                catch (Exception ex) { Log.Debug($"Icon at {px}px failed for {id}: {ex.Message}"); }
            }
        })
        { IsBackground = true, Name = "GnomeWin.AppIcons", Priority = ThreadPriority.BelowNormal };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    private static ImageSource? Load(string appId, string? exePath, int px)
    {
        var icon = IconHelper.GetFileImage(@"shell:AppsFolder\" + appId, px);
        if (icon == null && exePath != null && File.Exists(exePath)) icon = IconHelper.GetFileImage(exePath, px);
        if (icon == null) return null;
        if (icon.CanFreeze) icon.Freeze();
        lock (Cache) Cache[Key(appId, px)] = icon;
        return icon;
    }

    public static void Clear()
    {
        lock (Cache) Cache.Clear();
    }

    private static string Key(string appId, int px) => appId + "|" + px;
}
