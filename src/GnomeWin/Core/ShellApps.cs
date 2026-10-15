using GnomeWin.Services.Search;
using GnomeWin.UI;

namespace GnomeWin.Core;

/// <summary>
/// GnomeWin's own windows (settings, console) all run from the same executable, so the shell cannot tell
/// them apart by process path: each one tags its window with an AUMID and gets its entry — and its icon — here.
/// </summary>
public static class ShellApps
{
    private static readonly Dictionary<string, AppEntry> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static AppEntry? Find(string aumid)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(aumid, out var cached)) return cached;
            var entry = Create(aumid);
            if (entry != null) Cache[aumid] = entry;
            return entry;
        }
    }

    private static AppEntry? Create(string aumid)
    {
        var (name, icon, args) = aumid switch
        {
            App.SettingsAppId => (Loc.IsFrench ? "Paramètres GnomeWin" : "GnomeWin Settings", "Settings.ico", "--settings"),
            App.ConsoleAppId => ("Console", "Console.ico", "--terminal"),
            _ => (null, null, null),
        };
        if (name == null || icon == null) return null;

        var entry = new AppEntry
        {
            Id = aumid,
            Name = name,
            ExePath = Environment.ProcessPath,
            IsTransient = true,
            IdIsAumid = true,
            LaunchArguments = args,
            SearchName = TextMatcher.Normalize(name),
            SearchExe = "gnomewin",
        };
        try { entry.Icon = EmbeddedIcon.Load(icon, 64); }
        catch { /* missing resource in a trimmed build */ }
        return entry;
    }
}
