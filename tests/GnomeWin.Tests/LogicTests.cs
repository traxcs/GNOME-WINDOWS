using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using GnomeWin.Input.GlobalHotkeys;
using GnomeWin.Input.KeyboardNavigation;
using GnomeWin.Services.Search;
using GnomeWin.Services.Settings;
using GnomeWin.Shell.Overview;
using Xunit;

namespace GnomeWin.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Super", HotkeyModifiers.Super, 0)]
    [InlineData("Super+A", HotkeyModifiers.Super, 0x41)]
    [InlineData("Super+PageUp", HotkeyModifiers.Super, 0x21)]
    [InlineData("Ctrl+Alt+Down", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x28)]
    [InlineData("super+shift+pagedown", HotkeyModifiers.Super | HotkeyModifiers.Shift, 0x22)]
    [InlineData("Win+1", HotkeyModifiers.Super, 0x31)]
    [InlineData("Super+F5", HotkeyModifiers.Super, 0x74)]
    public void Parses_valid_shortcuts(string text, HotkeyModifiers mods, int vk)
    {
        Assert.True(Hotkey.TryParse(text, out var hk));
        Assert.Equal(mods, hk.Modifiers);
        Assert.Equal(vk, hk.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Super+Nope")]
    [InlineData("Ctrl")]
    public void Rejects_invalid_shortcuts(string text) => Assert.False(Hotkey.TryParse(text, out _));

    [Theory]
    [InlineData("Super")]
    [InlineData("Super+A")]
    [InlineData("Super+Shift+PageDown")]
    [InlineData("Ctrl+Alt+Up")]
    [InlineData("Super+Tab")]
    public void Round_trips(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var hk));
        Assert.Equal(text, hk.ToString());
    }

    [Fact]
    public void Default_bindings_are_all_valid()
    {
        foreach (var (action, list) in KeyboardSettings.DefaultBindings())
        {
            Assert.True(Enum.TryParse<ShellAction>(action, out _), action);
            foreach (var s in list) Assert.True(Hotkey.TryParse(s, out _), $"{action}: {s}");
        }
    }
}

public class SearchTests
{
    private static int Score(string q, string c) => TextMatcher.Score(TextMatcher.Normalize(q), TextMatcher.Normalize(c));

    [Fact]
    public void Is_case_and_accent_insensitive()
    {
        Assert.True(Score("PARAMETRES", "Paramètres") > 0);
        Assert.True(Score("éditeur", "Editeur de texte") > 0);
    }

    [Fact]
    public void Ranks_prefix_above_word_start_above_substring()
    {
        int prefix = Score("vis", "Visual Studio Code");
        int word = Score("stu", "Visual Studio Code");
        int sub = Score("sua", "Visual Studio Code");
        Assert.True(prefix > word && word > sub && sub > 0, $"{prefix} {word} {sub}");
    }

    [Fact]
    public void Matches_initials_and_compact_subsequences_only()
    {
        Assert.True(Score("vsc", "Visual Studio Code") > 0);
        Assert.True(Score("chrm", "Google Chrome") > 0);
        Assert.Equal(0, Score("term", "Défragmenter et optimiser les lecteurs"));
        Assert.Equal(0, Score("xyz", "Google Chrome"));
    }

    [Theory]
    [InlineData("2+3*4", 14)]
    [InlineData("(2+3)*4", 20)]
    [InlineData("2^10", 1024)]
    [InlineData("-3 + 5", 2)]
    [InlineData("10 / 4", 2.5)]
    [InlineData("1,5*2", 3)]
    [InlineData("7 % 3", 1)]
    public void Calculator_evaluates(string expr, double expected)
    {
        Assert.True(Calculator.LooksLikeExpression(expr));
        Assert.True(Calculator.TryEvaluate(expr, out double v));
        Assert.Equal(expected, v, 6);
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("42")]
    [InlineData("(2+")]
    public void Calculator_ignores_non_expressions(string text) =>
        Assert.False(Calculator.LooksLikeExpression(text) && Calculator.TryEvaluate(text, out _));
}

public class LayoutTests
{
    private static List<LayoutWindow> Windows(params (double w, double h, double x, double y)[] specs) =>
        specs.Select(s => new LayoutWindow(new Size(s.w, s.h), new Point(s.x, s.y))).ToList();

    [Theory]
    [InlineData(OverviewLayout.Natural)]
    [InlineData(OverviewLayout.Grid)]
    public void Slots_are_inside_area_and_do_not_overlap(OverviewLayout mode)
    {
        var area = new Rect(50, 100, 1600, 800);
        var wins = Windows((1920, 1040, 960, 520), (800, 600, 400, 300), (1200, 900, 1000, 600),
                           (640, 480, 1500, 200), (1920, 1040, 960, 520), (300, 700, 100, 900), (1000, 300, 800, 50));
        var rects = WindowLayout.Compute(wins, area, 20, 60, mode);
        Assert.Equal(wins.Count, rects.Length);
        for (int i = 0; i < rects.Length; i++)
        {
            Assert.True(rects[i].Width > 0 && rects[i].Height > 0);
            Assert.True(area.Contains(rects[i]), $"{mode}: rect {i} {rects[i]} outside {area}");
            for (int j = i + 1; j < rects.Length; j++)
                Assert.False(rects[i].IntersectsWith(Rect.Inflate(rects[j], -0.5, -0.5)), $"{mode}: {i} overlaps {j}");
        }
    }

    [Fact]
    public void Preserves_aspect_ratio_and_never_upscales()
    {
        var wins = Windows((400, 300, 200, 150));
        var r = WindowLayout.Compute(wins, new Rect(0, 0, 2000, 1500), 20, 60)[0];
        Assert.Equal(400, r.Width, 3);
        Assert.Equal(300, r.Height, 3);
        Assert.Equal(1000, r.X + r.Width / 2, 3);
    }

    [Fact]
    public void Keeps_left_to_right_order_within_a_row()
    {
        var wins = Windows((800, 600, 1500, 300), (800, 600, 200, 300));
        var r = WindowLayout.Compute(wins, new Rect(0, 0, 2000, 800), 20, 60);
        Assert.True(r[1].X < r[0].X, "window on the left should stay on the left");
    }

    [Fact]
    public void Handles_empty_input() => Assert.Empty(WindowLayout.Compute(new List<LayoutWindow>(), new Rect(0, 0, 100, 100), 10, 10));
}

public class NavigatorTests
{
    private static readonly List<Rect> Grid3 = new()
    {
        new Rect(0, 0, 100, 100), new Rect(200, 0, 100, 100), new Rect(400, 0, 100, 100),
        new Rect(0, 200, 100, 100), new Rect(200, 200, 100, 100),
    };

    [Fact]
    public void Moves_in_the_requested_direction()
    {
        Assert.Equal(1, SpatialNavigator.Next(Grid3, 0, NavDirection.Right));
        Assert.Equal(3, SpatialNavigator.Next(Grid3, 0, NavDirection.Down));
        Assert.Equal(1, SpatialNavigator.Next(Grid3, 4, NavDirection.Up));
        Assert.Equal(3, SpatialNavigator.Next(Grid3, 4, NavDirection.Left));
    }

    [Fact]
    public void Stays_when_nothing_in_that_direction() => Assert.Equal(2, SpatialNavigator.Next(Grid3, 2, NavDirection.Right));
}

public class SettingsTests
{
    [Fact]
    public void Serializes_and_restores_settings()
    {
        var s = new AppSettings();
        s.Dock.IconSize = 56;
        s.Dock.Position = DockPosition.Left;
        s.Keyboard.Bindings = new Dictionary<string, List<string>> { ["ToggleOverview"] = new() { "Super" } };
        string json = JsonSerializer.Serialize(s, SettingsService.JsonOptions);
        Assert.Contains("\"Left\"", json);
        var back = JsonSerializer.Deserialize<AppSettings>(json, SettingsService.JsonOptions)!;
        Assert.Equal(56, back.Dock.IconSize);
        Assert.Equal(DockPosition.Left, back.Dock.Position);
    }

    [Fact]
    public void Migrator_upgrades_step_by_step()
    {
        var m = new SettingsMigrator { TargetVersion = 3 };
        m.Register(1, root => root["Renamed"] = root["Old"]?.DeepClone());
        m.Register(2, root => root.Remove("Old"));
        var doc = JsonNode.Parse("""{ "Version": 1, "Old": 42 }""")!.AsObject();
        Assert.True(m.Migrate(doc, out int from));
        Assert.Equal(1, from);
        Assert.Equal(3, doc["Version"]!.GetValue<int>());
        Assert.Equal(42, doc["Renamed"]!.GetValue<int>());
        Assert.Null(doc["Old"]);
    }

    [Fact]
    public void Unversioned_files_are_stamped()
    {
        var doc = JsonNode.Parse("""{ "Dock": { "IconSize": 40 } }""")!.AsObject();
        Assert.True(SettingsMigrator.CreateDefault().Migrate(doc, out int from));
        Assert.Equal(0, from);
        Assert.Equal(AppSettings.CurrentVersion, doc["Version"]!.GetValue<int>());
    }

    [Fact]
    public void Service_recovers_from_corrupt_file()
    {
        string dir = Path.Combine(Path.GetTempPath(), "gnomewin-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            string file = Path.Combine(dir, "settings.json");
            File.WriteAllText(file, "{ this is not json");
            using var svc = new SettingsService(file);
            svc.Load();
            Assert.Equal(new DockSettings().IconSize, svc.Current.Dock.IconSize);
            Assert.Contains(Directory.GetFiles(dir), f => f.Contains(".corrupt-"));
            Assert.True(JsonNode.Parse(File.ReadAllText(file)) is JsonObject);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Old_files_get_the_ubuntu_dock()
    {
        var doc = JsonNode.Parse("""{ "Version": 1, "Dock": { "Position": "Bottom", "Visibility": "Intellihide", "IconSize": 48 } }""")!.AsObject();
        SettingsMigrator.CreateDefault().Migrate(doc, out _);
        var s = doc.Deserialize<AppSettings>(SettingsService.JsonOptions)!;
        Assert.Equal(DockPosition.Left, s.Dock.Position);
        Assert.Equal(DockVisibility.AlwaysVisible, s.Dock.Visibility);
        Assert.True(s.Dock.Extended);
        Assert.Equal(40, s.Dock.IconSize);
    }

    [Fact]
    public void Customised_dock_is_kept_by_migration()
    {
        var doc = JsonNode.Parse("""{ "Version": 1, "Dock": { "Position": "Right", "Visibility": "AutoHide", "IconSize": 64 } }""")!.AsObject();
        SettingsMigrator.CreateDefault().Migrate(doc, out _);
        var s = doc.Deserialize<AppSettings>(SettingsService.JsonOptions)!;
        Assert.Equal(DockPosition.Right, s.Dock.Position);
        Assert.Equal(DockVisibility.AutoHide, s.Dock.Visibility);
        Assert.Equal(64, s.Dock.IconSize);
    }

    [Fact]
    public void Existing_ubuntu_setups_keep_the_ubuntu_style()
    {
        var doc = JsonNode.Parse("""{ "Version": 3, "General": { "Accent": "Orange" }, "Dock": { "Position": "Left", "Extended": true } }""")!.AsObject();
        SettingsMigrator.CreateDefault().Migrate(doc, out _);
        var s = doc.Deserialize<AppSettings>(SettingsService.JsonOptions)!;
        Assert.Equal(DesignStyle.Ubuntu, s.General.Style);
        Assert.Equal(AccentColor.Default, s.General.Accent);
    }

    [Fact]
    public void New_installs_use_vanilla_gnome()
    {
        var s = new AppSettings();
        Assert.Equal(DesignStyle.Gnome, s.General.Style);
        Assert.Equal(DockVisibility.AlwaysVisible, s.Dock.Visibility);
        Assert.Equal(DockPosition.Bottom, s.Dock.Position);
        Assert.False(s.Overview.HotCorner);
    }

    [Fact]
    public void Hot_corner_is_turned_off_by_migration()
    {
        var doc = JsonNode.Parse("""{ "Version": 5, "Overview": { "HotCorner": true } }""")!.AsObject();
        SettingsMigrator.CreateDefault().Migrate(doc, out _);
        var s = doc.Deserialize<AppSettings>(SettingsService.JsonOptions)!;
        Assert.False(s.Overview.HotCorner);
    }

    [Fact]
    public void Values_are_clamped()
    {
        var d = new DockSettings { IconSize = 500, BackgroundOpacity = 3 };
        Assert.Equal(96, d.IconSize);
        Assert.Equal(1, d.BackgroundOpacity);
    }
}
