namespace GnomeWin;

public sealed class StartupOptions
{
    public bool SafeMode { get; set; }
    public bool AutoStart { get; set; }
    public bool OpenSettings { get; set; }
    public bool OpenOverview { get; set; }
    public bool OpenApps { get; set; }
    public bool Quit { get; set; }
    public bool Restore { get; set; }
    public bool Diagnostics { get; set; }
    public bool ResetSettings { get; set; }
    public bool Help { get; set; }
    public bool Verbose { get; set; }
    public int WatchdogPid { get; set; }
    public string? SearchText { get; set; }
    public string? ActionName { get; set; }
    public bool Install { get; set; }
    public bool Uninstall { get; set; }
    public bool Quiet { get; set; }
    public bool Startup { get; set; }
    public string? SettingsPanel { get; set; }
    public bool Terminal { get; set; }
    public string? TerminalDirectory { get; set; }

    public static StartupOptions Parse(string[] args)
    {
        var o = new StartupOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant().TrimStart('/').Replace("--", "-"))
            {
                case "-safe-mode": case "-safemode": case "safe-mode": o.SafeMode = true; break;
                case "-autostart": o.AutoStart = true; break;
                case "-settings":
                    o.OpenSettings = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) { o.SettingsPanel = args[i + 1]; i++; }
                    break;
                case "-overview": o.OpenOverview = true; break;
                case "-apps": o.OpenApps = true; break;
                case "-quit": case "-exit": o.Quit = true; break;
                case "-restore": case "-restore-taskbar": o.Restore = true; break;
                case "-diag": case "-diagnostics": o.Diagnostics = true; break;
                case "-reset-settings": o.ResetSettings = true; break;
                case "-verbose": o.Verbose = true; break;
                case "-h": case "-help": case "?": case "-?": o.Help = true; break;
                case "-install": o.Install = true; break;
                case "-uninstall": o.Uninstall = true; break;
                case "-quiet": case "-silent": o.Quiet = true; break;
                case "-startup": o.Startup = true; break;
                case "-terminal": case "-console": o.Terminal = true; break;
                case "-working-directory": case "-wd":
                    if (i + 1 < args.Length) { o.TerminalDirectory = args[i + 1]; i++; }
                    break;
                case "-action":
                    if (i + 1 < args.Length) { o.ActionName = args[i + 1]; i++; }
                    break;
                case "-search":
                    if (i + 1 < args.Length) { o.SearchText = args[i + 1]; i++; }
                    break;
                case "-watchdog":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int pid)) { o.WatchdogPid = pid; i++; }
                    break;
            }
        }
        string exeName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
        if (!o.Uninstall && exeName.StartsWith("GnomeWin-Setup", StringComparison.OrdinalIgnoreCase)) o.Install = true;
        return o;
    }

    public string ForwardedCommand =>
        Quit ? "quit" : Restore ? "quit" : OpenSettings ? (SettingsPanel != null ? "settings:" + SettingsPanel : "settings") : OpenApps ? "apps" : SearchText != null ? "search:" + SearchText : ActionName != null ? "action:" + ActionName : "overview";

    public const string HelpText = """
        GnomeWin – GNOME Shell experience for Windows 11

        Usage: GnomeWin.exe [option]

          (no option)        start the shell (or open the Overview if it is already running)
          --safe-mode        start without taskbar replacement and without global keyboard hook
          --settings [panel] open the settings window (panels: wifi, network, bluetooth, displays, sound, power,
                             multitasking, appearance, dock, apps, notifications, search, mouse, keyboard, accessibility, system)
          --overview         open the Overview
          --apps             open the application grid
          --terminal [--working-directory <dir>]
                             open Console, the GNOME-style terminal (PowerShell and cmd commands in one shell)
          --search <text>    open the Overview and search for <text>
          --action <name>    run a shell action (ToggleOverview, ToggleQuickSettings, ToggleNotifications,
                             WorkspaceNext, WorkspacePrevious, AppSwitcher, LaunchDockItem1…)
          --quit             quit the running shell (the Windows taskbar is restored)
          --restore          restore the Windows taskbar (and quit the shell if running)
          --reset-settings   back up and reset the settings to defaults
          --install          install for the current user (also when the file is named GnomeWin-Setup.exe)
          --install --quiet [--startup]   silent install (optionally launch at startup)
          --uninstall        uninstall (add --quiet for no UI)
          --diag             write a diagnostic report (logs folder) and print it
          --verbose          verbose logging

        Emergency: Ctrl+Alt+Shift+F12 quits the shell and restores Windows.
        """;
}
