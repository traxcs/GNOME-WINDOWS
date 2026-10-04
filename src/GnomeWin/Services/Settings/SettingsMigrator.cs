using System.Text.Json.Nodes;

namespace GnomeWin.Services.Settings;

public sealed class SettingsMigrator
{
    private readonly SortedDictionary<int, Action<JsonObject>> _steps = new();

    public static SettingsMigrator CreateDefault()
    {
        var m = new SettingsMigrator();
        m.Register(0, root =>
        {
        });
        m.Register(1, root =>
        {
            if (root["Dock"] is not JsonObject dock) return;
            if ((string?)dock["Position"] is null or "Bottom") dock["Position"] = "Left";
            if ((string?)dock["Visibility"] is null or "Intellihide") dock["Visibility"] = "AlwaysVisible";
            if ((string?)dock["HoverEffect"] is null or "Zoom") dock["HoverEffect"] = "Highlight";
            if (dock["BackgroundOpacity"] is null || Math.Abs(dock["BackgroundOpacity"]!.GetValue<double>() - 0.8) < 0.001) dock["BackgroundOpacity"] = 0.85;
            dock["Extended"] = true;
        });
        m.Register(2, root =>
        {
            if (root["Dock"] is JsonObject dock && (dock["IconSize"] is null || dock["IconSize"]!.GetValue<int>() == 48))
                dock["IconSize"] = 40;
        });
        m.Register(3, root =>
        {
            var general = root["General"] as JsonObject ?? new JsonObject();
            root["General"] = general;
            bool ubuntuDock = root["Dock"] is JsonObject dock && (string?)dock["Position"] == "Left" && dock["Extended"]?.GetValue<bool>() == true;
            general["Style"] ??= ubuntuDock ? "Ubuntu" : "Gnome";
            if ((string?)general["Accent"] == "Orange" && ubuntuDock) general["Accent"] = "Default";
        });
        return m;
    }

    public void Register(int fromVersion, Action<JsonObject> step) => _steps[fromVersion] = step;

    public int TargetVersion { get; set; } = AppSettings.CurrentVersion;

    public bool Migrate(JsonObject root, out int fromVersion)
    {
        fromVersion = root["Version"]?.GetValue<int>() ?? 0;
        int v = fromVersion;
        if (v >= TargetVersion) return false;
        while (v < TargetVersion)
        {
            if (_steps.TryGetValue(v, out var step)) step(root);
            v++;
            root["Version"] = v;
        }
        return true;
    }
}
