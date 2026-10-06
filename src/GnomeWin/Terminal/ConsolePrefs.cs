using System.Text.Json;
using GnomeWin.Services;
using GnomeWin.Services.Logging;

namespace GnomeWin.Terminal;

public sealed class ConsolePrefs
{
    public double FontSize { get; set; } = 14;

    private static string FilePath => Path.Combine(AppPaths.Root, "console", "console.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public static ConsolePrefs Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<ConsolePrefs>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch (Exception ex) { Log.Warn("Console preferences unreadable, using defaults", ex); }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex) { Log.Warn("Cannot save Console preferences", ex); }
    }
}
