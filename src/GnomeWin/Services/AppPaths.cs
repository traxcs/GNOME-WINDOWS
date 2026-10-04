namespace GnomeWin.Services;

public static class AppPaths
{
    public const string AppName = "GnomeWin";

    private static string? _root;

    public static string Root
    {
        get
        {
            if (_root != null) return _root;
            string? overrideDir = Environment.GetEnvironmentVariable("GNOMEWIN_DATA_DIR");
            _root = !string.IsNullOrWhiteSpace(overrideDir)
                ? overrideDir
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
            Directory.CreateDirectory(_root);
            return _root;
        }
    }

    public static string Logs => Ensure(Path.Combine(Root, "logs"));
    public static string State => Ensure(Path.Combine(Root, "state"));
    public static string Cache => Ensure(Path.Combine(Root, "cache"));
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string TaskbarStateFile => Path.Combine(State, "taskbar.json");
    public static string SessionLockFile => Path.Combine(State, "session.lock");
    public static string CrashHistoryFile => Path.Combine(State, "crash-history.txt");

    public static string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, AppName + ".exe");

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
