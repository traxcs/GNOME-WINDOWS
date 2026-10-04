using System.Globalization;
using System.Windows.Media;
using GnomeWin.Core;

namespace GnomeWin.Services.Search;

public enum SearchResultKind { Application, Window, Setting, File, Calculation }

public sealed class SearchResult
{
    public required SearchResultKind Kind { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public ImageSource? Icon { get; init; }
    public string? Glyph { get; init; }
    public int Score { get; init; }
    public AppEntry? App { get; init; }
    public WindowInfo? Window { get; init; }
    public string? Target { get; init; }
}

public sealed class SearchResults
{
    public List<SearchResult> Apps { get; } = new();
    public List<SearchResult> Windows { get; } = new();
    public List<SearchResult> Settings { get; } = new();
    public List<SearchResult> Files { get; } = new();
    public SearchResult? Calculation { get; set; }

    public IEnumerable<SearchResult> All()
    {
        if (Calculation != null) yield return Calculation;
        foreach (var r in Apps) yield return r;
        foreach (var r in Windows) yield return r;
        foreach (var r in Settings) yield return r;
        foreach (var r in Files) yield return r;
    }

    public bool IsEmpty => !All().Any();
}

public sealed class SearchOptions
{
    public bool Files { get; init; } = true;
    public bool Settings { get; init; } = true;
    public bool Calculator { get; init; } = true;
}

public sealed class SearchService
{
    private List<(string Name, string Norm, string Path)> _recent = new();
    private DateTime _recentLoaded;

    private static readonly (string Fr, string En, string Keywords, string Uri, string Glyph)[] SettingsPages =
    {
        ("Wi-Fi", "Wi-Fi", "wifi wlan sans fil wireless reseau network", "ms-settings:network-wifi", ""),
        ("Réseau et Internet", "Network & internet", "reseau internet ethernet network proxy", "ms-settings:network", ""),
        ("Bluetooth et appareils", "Bluetooth & devices", "bluetooth appareils devices souris mouse clavier keyboard", "ms-settings:bluetooth", ""),
        ("Son", "Sound", "son sound audio volume haut-parleur speaker micro microphone", "ms-settings:sound", ""),
        ("Affichage", "Display", "affichage display ecran screen resolution echelle scale hdr", "ms-settings:display", ""),
        ("Éclairage nocturne", "Night light", "eclairage nocturne night light lumiere bleue", "ms-settings:nightlight", ""),
        ("Arrière-plan", "Background", "fond ecran arriere plan wallpaper background", "ms-settings:personalization-background", ""),
        ("Couleurs", "Colors", "couleurs colors theme sombre dark clair light accent", "ms-settings:colors", ""),
        ("Batterie et alimentation", "Power & battery", "batterie battery alimentation power veille sleep economie", "ms-settings:batterysaver", ""),
        ("Notifications", "Notifications", "notifications ne pas deranger do not disturb", "ms-settings:notifications", ""),
        ("Applications installées", "Installed apps", "applications apps installees desinstaller uninstall programmes", "ms-settings:appsfeatures", ""),
        ("Applications par défaut", "Default apps", "applications defaut default apps navigateur browser", "ms-settings:defaultapps", ""),
        ("Date et heure", "Date & time", "date heure time horloge clock fuseau timezone", "ms-settings:dateandtime", ""),
        ("Langue et région", "Language & region", "langue language region clavier keyboard", "ms-settings:regionlanguage", ""),
        ("Windows Update", "Windows Update", "mise a jour update windows update", "ms-settings:windowsupdate", ""),
        ("Confidentialité et sécurité", "Privacy & security", "confidentialite privacy securite security antivirus defender", "ms-settings:privacy", ""),
        ("Comptes", "Accounts", "compte account utilisateur user mot de passe password", "ms-settings:accounts", ""),
        ("Imprimantes et scanners", "Printers & scanners", "imprimante printer scanner", "ms-settings:printers", ""),
        ("Stockage", "Storage", "stockage storage disque disk espace space", "ms-settings:storagesense", ""),
        ("Souris", "Mouse", "souris mouse pointeur pointer", "ms-settings:mousetouchpad", ""),
        ("Pavé tactile", "Touchpad", "pave tactile touchpad trackpad", "ms-settings:devices-touchpad", ""),
        ("VPN", "VPN", "vpn", "ms-settings:network-vpn", ""),
        ("Mode avion", "Airplane mode", "avion airplane", "ms-settings:network-airplanemode", ""),
        ("Multitâche", "Multitasking", "multitache multitasking bureaux virtuels virtual desktops snap", "ms-settings:multitasking", ""),
        ("Gestionnaire des tâches", "Task Manager", "gestionnaire taches task manager processus process", "taskmgr.exe", ""),
        ("Panneau de configuration", "Control Panel", "panneau configuration control panel", "control.exe", ""),
    };

    public void RefreshRecentFilesIfStale()
    {
        if (DateTime.Now - _recentLoaded < TimeSpan.FromSeconds(30)) return;
        _recentLoaded = DateTime.Now;
        Task.Run(() =>
        {
            var list = new List<(string, string, string)>();
            try
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*.lnk").OrderByDescending(f => f.LastWriteTimeUtc).Take(300))
                {
                    string name = Path.GetFileNameWithoutExtension(f.Name);
                    list.Add((name, TextMatcher.Normalize(name), f.FullName));
                }
            }
            catch { /* Recent folder unavailable */ }
            _recent = list;
        });
    }

    public SearchResults Search(string query, IReadOnlyList<AppEntry> apps, IEnumerable<WindowInfo> windows, SearchOptions options)
    {
        var results = new SearchResults();
        string q = TextMatcher.Normalize(query.Trim());
        if (q.Length == 0) return results;

        if (options.Calculator && Calculator.LooksLikeExpression(query) && Calculator.TryEvaluate(query, out double value))
        {
            string text = value.ToString("G12", CultureInfo.CurrentCulture);
            results.Calculation = new SearchResult { Kind = SearchResultKind.Calculation, Title = "= " + text, Subtitle = query.Trim(), Glyph = "", Target = text, Score = 2000 };
        }

        results.Apps.AddRange(apps
            .Select(a => (a, s: Math.Max(TextMatcher.Score(q, a.SearchName), TextMatcher.Score(q, a.SearchExe) * 8 / 10)))
            .Where(x => x.s > 0)
            .OrderByDescending(x => x.s).ThenBy(x => x.a.Name.Length)
            .Take(12)
            .Select(x => new SearchResult { Kind = SearchResultKind.Application, Title = x.a.Name, Icon = x.a.Icon, App = x.a, Score = x.s }));

        results.Windows.AddRange(windows
            .Select(w => (w, s: Math.Max(TextMatcher.Score(q, TextMatcher.Normalize(w.Title)), TextMatcher.Score(q, TextMatcher.Normalize(w.App?.Name)))))
            .Where(x => x.s > 0)
            .OrderByDescending(x => x.s)
            .Take(8)
            .Select(x => new SearchResult { Kind = SearchResultKind.Window, Title = x.w.DisplayTitle, Subtitle = x.w.App?.Name, Icon = x.w.Icon, Window = x.w, Score = x.s }));

        if (options.Settings)
        {
            bool fr = UI.Loc.IsFrench;
            foreach (var p in SettingsPages)
            {
                int s = Math.Max(TextMatcher.Score(q, TextMatcher.Normalize(fr ? p.Fr : p.En)), KeywordScore(q, p.Keywords));
                if (s > 0) results.Settings.Add(new SearchResult { Kind = SearchResultKind.Setting, Title = fr ? p.Fr : p.En, Subtitle = UI.Loc.T("WindowsSettings"), Glyph = p.Glyph, Target = p.Uri, Score = s });
            }
            results.Settings.Sort((a, b) => b.Score.CompareTo(a.Score));
            if (results.Settings.Count > 5) results.Settings.RemoveRange(5, results.Settings.Count - 5);
        }

        if (options.Files && q.Length >= 2)
        {
            results.Files.AddRange(_recent
                .Select(f => (f, s: TextMatcher.Score(q, f.Norm)))
                .Where(x => x.s >= 600)
                .OrderByDescending(x => x.s)
                .Take(6)
                .Select(x => new SearchResult { Kind = SearchResultKind.File, Title = x.f.Name, Subtitle = UI.Loc.T("RecentFiles"), Glyph = "", Target = x.f.Path, Score = x.s }));
        }
        return results;
    }

    private static int KeywordScore(string q, string keywords)
    {
        int best = 0;
        foreach (var k in keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            best = Math.Max(best, k.StartsWith(q, StringComparison.Ordinal) ? 750 : 0);
        return best;
    }
}
