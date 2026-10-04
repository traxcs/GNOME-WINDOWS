using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using GnomeWin.Services.Logging;

namespace GnomeWin.Services.Settings;

public sealed class SettingsService : IDisposable
{
    private readonly string _path;
    private readonly Timer _saveTimer;
    private readonly object _gate = new();
    private bool _dirty;

    public AppSettings Current { get; private set; } = new();

    public event Action<object, string>? Changed;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public SettingsService(string path)
    {
        _path = path;
        _saveTimer = new Timer(_ => SaveIfDirty(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Load()
    {
        AppSettings settings;
        try
        {
            if (!File.Exists(_path))
            {
                settings = new AppSettings();
                Log.Info("No settings file, using defaults.");
            }
            else
            {
                string text = File.ReadAllText(_path);
                var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                           ?? throw new JsonException("Root is not an object");
                int fileVersion = node["Version"]?.GetValue<int>() ?? 0;
                if (fileVersion > AppSettings.CurrentVersion)
                {
                    File.Copy(_path, _path + $".v{fileVersion}.bak", overwrite: true);
                    Log.Warn($"Settings file version {fileVersion} is newer than supported {AppSettings.CurrentVersion}; unknown values are ignored.");
                }
                bool migrated = SettingsMigrator.CreateDefault().Migrate(node, out int from);
                settings = node.Deserialize<AppSettings>(JsonOptions) ?? new AppSettings();
                if (migrated)
                {
                    Log.Info($"Settings migrated from version {from} to {AppSettings.CurrentVersion}.");
                    File.Copy(_path, _path + $".v{from}.bak", overwrite: true);
                    _dirty = true;
                }
                Sanitize(settings);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Settings file is corrupt, falling back to defaults.", ex);
            try { File.Move(_path, _path + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}", true); } catch { }
            settings = new AppSettings();
            _dirty = true;
        }

        Attach(settings);
        if (_dirty || !File.Exists(_path)) Save();
    }

    public void ResetToDefaults()
    {
        try { if (File.Exists(_path)) File.Copy(_path, _path + $".before-reset-{DateTime.Now:yyyyMMddHHmmss}.bak", true); } catch { }
        Detach(Current);
        Attach(new AppSettings());
        Save();
        foreach (var section in Current.Sections()) Changed?.Invoke(section, "*");
    }

    public void Export(string file) => File.WriteAllText(file, JsonSerializer.Serialize(Current, JsonOptions));

    public void Import(string file)
    {
        var node = JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? throw new JsonException("Invalid settings file");
        SettingsMigrator.CreateDefault().Migrate(node, out _);
        var imported = node.Deserialize<AppSettings>(JsonOptions) ?? throw new JsonException("Invalid settings file");
        Sanitize(imported);
        Detach(Current);
        Attach(imported);
        Save();
        foreach (var section in Current.Sections()) Changed?.Invoke(section, "*");
    }

    private static void Sanitize(AppSettings s)
    {
        s.Version = AppSettings.CurrentVersion;
        s.General ??= new(); s.Dock ??= new(); s.Overview ??= new();
        s.Workspaces ??= new(); s.Displays ??= new(); s.Keyboard ??= new();
        s.Dock.PinnedApps = s.Dock.PinnedApps.Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var merged = new Dictionary<string, List<string>>(s.Keyboard.Bindings, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in KeyboardSettings.DefaultBindings())
            if (!merged.ContainsKey(kv.Key)) merged[kv.Key] = kv.Value;
        s.Keyboard.Bindings = merged;
    }

    private void Attach(AppSettings s)
    {
        Current = s;
        foreach (var section in s.Sections()) section.PropertyChanged += OnSectionChanged;
    }

    private void Detach(AppSettings s)
    {
        foreach (var section in s.Sections()) section.PropertyChanged -= OnSectionChanged;
    }

    private void OnSectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        lock (_gate) _dirty = true;
        _saveTimer.Change(500, Timeout.Infinite);
        if (sender != null) Changed?.Invoke(sender, e.PropertyName ?? "*");
    }

    private void SaveIfDirty()
    {
        lock (_gate)
        {
            if (!_dirty) return;
        }
        Save();
    }

    public void Save()
    {
        lock (_gate)
        {
            try
            {
                string json = JsonSerializer.Serialize(Current, JsonOptions);
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(_path)) File.Replace(tmp, _path, _path + ".bak", ignoreMetadataErrors: true);
                else File.Move(tmp, _path);
                _dirty = false;
            }
            catch (Exception ex)
            {
                Log.Error("Could not save settings", ex);
            }
        }
    }

    public void Dispose()
    {
        _saveTimer.Dispose();
        SaveIfDirty();
    }
}
