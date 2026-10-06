using GnomeWin.Input.GlobalHotkeys;

namespace GnomeWin.Services.Settings;

public enum ThemeMode { System, Dark, Light }
public enum AnimationSpeed { Slow, Normal, Fast }
public enum UiLanguage { System, French, English }
public enum DockPosition { Bottom, Left, Right }
public enum DockVisibility { Intellihide, AutoHide, AlwaysVisible, OverviewOnly }
public enum DockHoverEffect { None, Highlight, Zoom }
public enum DockActiveClick { Minimize, CycleWindows, ShowPreviews }
public enum MonitorPlacement { Primary, All, Active }
public enum OverviewLayout { Natural, Grid }
public enum OverviewBackground { BlurredWallpaper, Wallpaper, Solid }
public enum AccentColor { Default, Blue, Teal, Green, Yellow, Orange, Red, Pink, Purple, Slate, Bark, Sage, Olive, Viridian, PrussianGreen, Magenta }
public enum DesignStyle { Gnome, Ubuntu, PopOS }

public sealed class AppSettings
{
    public const int CurrentVersion = 5;

    public int Version { get; set; } = CurrentVersion;
    public GeneralSettings General { get; set; } = new();
    public DockSettings Dock { get; set; } = new();
    public OverviewSettings Overview { get; set; } = new();
    public WorkspaceSettings Workspaces { get; set; } = new();
    public DisplaySettings Displays { get; set; } = new();
    public KeyboardSettings Keyboard { get; set; } = new();

    public IEnumerable<ObservableObject> Sections()
    {
        yield return General; yield return Dock; yield return Overview;
        yield return Workspaces; yield return Displays; yield return Keyboard;
    }
}

public sealed class GeneralSettings : ObservableObject
{
    private bool _launchAtStartup;
    private ThemeMode _theme = ThemeMode.Dark;
    private bool _animations = true;
    private AnimationSpeed _speed = AnimationSpeed.Normal;
    private UiLanguage _language = UiLanguage.System;
    private bool _replaceTaskbar = true;
    private bool _showTopBar = true;
    private bool _firstRunDone;
    private bool _verboseLogging;
    private bool _lowMemory = true;
    private AccentColor _accent = AccentColor.Default;
    private DesignStyle _style = DesignStyle.Gnome;
    private bool _showBatteryIcon = true;
    private bool _showBatteryPercentage;

    public bool LaunchAtStartup { get => _launchAtStartup; set => Set(ref _launchAtStartup, value); }
    public ThemeMode Theme { get => _theme; set => Set(ref _theme, value); }
    public AccentColor Accent { get => _accent; set => Set(ref _accent, value); }
    public DesignStyle Style { get => _style; set => Set(ref _style, value); }
    public bool AnimationsEnabled { get => _animations; set => Set(ref _animations, value); }
    public AnimationSpeed AnimationSpeed { get => _speed; set => Set(ref _speed, value); }
    public UiLanguage Language { get => _language; set => Set(ref _language, value); }
    public bool ReplaceTaskbar { get => _replaceTaskbar; set => Set(ref _replaceTaskbar, value); }
    public bool ShowTopBar { get => _showTopBar; set => Set(ref _showTopBar, value); }
    public bool FirstRunDone { get => _firstRunDone; set => Set(ref _firstRunDone, value); }
    public bool VerboseLogging { get => _verboseLogging; set => Set(ref _verboseLogging, value); }
    public bool LowMemoryMode { get => _lowMemory; set => Set(ref _lowMemory, value); }
    public bool ShowBatteryIcon { get => _showBatteryIcon; set => Set(ref _showBatteryIcon, value); }
    public bool ShowBatteryPercentage { get => _showBatteryPercentage; set => Set(ref _showBatteryPercentage, value); }
}

public sealed class DockSettings : ObservableObject
{
    private DockPosition _position = DockPosition.Bottom;
    private int _iconSize = 40;
    private double _opacity = 0.85;
    private DockVisibility _visibility = DockVisibility.AlwaysVisible;
    private DockHoverEffect _hover = DockHoverEffect.Highlight;
    private bool _extended;
    private DockActiveClick _activeClick = DockActiveClick.Minimize;
    private bool _isolateWorkspaces;
    private bool _showAppsButton = true;
    private bool _centerIcons = true;
    private List<string> _pinned = new();

    public DockPosition Position { get => _position; set => Set(ref _position, value); }
    public bool Extended { get => _extended; set => Set(ref _extended, value); }
    public bool CenterIcons { get => _centerIcons; set => Set(ref _centerIcons, value); }
    public int IconSize { get => _iconSize; set => Set(ref _iconSize, Math.Clamp(value, 24, 96)); }
    public double BackgroundOpacity { get => _opacity; set => Set(ref _opacity, Math.Clamp(value, 0, 1)); }
    public DockVisibility Visibility { get => _visibility; set => Set(ref _visibility, value); }
    public DockHoverEffect HoverEffect { get => _hover; set => Set(ref _hover, value); }
    public DockActiveClick ActiveClick { get => _activeClick; set => Set(ref _activeClick, value); }
    public bool IsolateWorkspaces { get => _isolateWorkspaces; set => Set(ref _isolateWorkspaces, value); }
    public bool ShowAppsButton { get => _showAppsButton; set => Set(ref _showAppsButton, value); }
    public List<string> PinnedApps { get => _pinned; set => Set(ref _pinned, value ?? new()); }
    public bool TaskbarPinsImported { get; set; }
}

public sealed class OverviewSettings : ObservableObject
{
    private OverviewLayout _layout = OverviewLayout.Natural;
    private OverviewBackground _background = OverviewBackground.BlurredWallpaper;
    private bool _showTitlesAlways;
    private bool _closeOnEmptyClick = true;
    private bool _searchRecentFiles = true;
    private bool _searchSettings = true;
    private bool _searchCalculator = true;
    private bool _livePreviews = true;
    private bool _showWorkspaceStrip = true;
    private bool _hotCorner = true;

    public OverviewLayout Layout { get => _layout; set => Set(ref _layout, value); }
    public OverviewBackground Background { get => _background; set => Set(ref _background, value); }
    public bool AlwaysShowTitles { get => _showTitlesAlways; set => Set(ref _showTitlesAlways, value); }
    public bool CloseOnEmptyClick { get => _closeOnEmptyClick; set => Set(ref _closeOnEmptyClick, value); }
    public bool SearchRecentFiles { get => _searchRecentFiles; set => Set(ref _searchRecentFiles, value); }
    public bool SearchWindowsSettings { get => _searchSettings; set => Set(ref _searchSettings, value); }
    public bool SearchCalculator { get => _searchCalculator; set => Set(ref _searchCalculator, value); }
    public bool LivePreviews { get => _livePreviews; set => Set(ref _livePreviews, value); }
    public bool ShowWorkspaceStrip { get => _showWorkspaceStrip; set => Set(ref _showWorkspaceStrip, value); }
    public bool HotCorner { get => _hotCorner; set => Set(ref _hotCorner, value); }
}

public sealed class WorkspaceSettings : ObservableObject
{
    private bool _dynamic = true;
    private int _initialCount = 4;
    private bool _showOsd = true;
    private bool _wrapAround;
    private bool _switcherCurrentOnly = true;

    public bool Dynamic { get => _dynamic; set => Set(ref _dynamic, value); }
    public int InitialCount { get => _initialCount; set => Set(ref _initialCount, Math.Clamp(value, 1, 16)); }
    public bool ShowSwitchOsd { get => _showOsd; set => Set(ref _showOsd, value); }
    public bool WrapAround { get => _wrapAround; set => Set(ref _wrapAround, value); }
    public bool SwitcherCurrentWorkspaceOnly { get => _switcherCurrentOnly; set => Set(ref _switcherCurrentOnly, value); }
}

public sealed class DisplaySettings : ObservableObject
{
    private MonitorPlacement _dock = MonitorPlacement.Primary;
    private MonitorPlacement _topBar = MonitorPlacement.All;
    private MonitorPlacement _overview = MonitorPlacement.All;
    private bool _hideDockFullscreen = true;
    private bool _hideTopBarFullscreen = true;
    private bool _disableShortcutsFullscreen = true;

    public MonitorPlacement DockMonitors { get => _dock; set => Set(ref _dock, value); }
    public MonitorPlacement TopBarMonitors { get => _topBar; set => Set(ref _topBar, value == MonitorPlacement.Active ? MonitorPlacement.Primary : value); }
    public MonitorPlacement OverviewMonitors { get => _overview; set => Set(ref _overview, value == MonitorPlacement.Active ? MonitorPlacement.Primary : value); }
    public bool HideDockInFullscreen { get => _hideDockFullscreen; set => Set(ref _hideDockFullscreen, value); }
    public bool HideTopBarInFullscreen { get => _hideTopBarFullscreen; set => Set(ref _hideTopBarFullscreen, value); }
    public bool DisableShortcutsInFullscreen { get => _disableShortcutsFullscreen; set => Set(ref _disableShortcutsFullscreen, value); }
}

public sealed class KeyboardSettings : ObservableObject
{
    private Dictionary<string, List<string>> _bindings = DefaultBindings();
    private bool _interceptSuper = true;

    public bool InterceptSuperKey { get => _interceptSuper; set => Set(ref _interceptSuper, value); }

    public Dictionary<string, List<string>> Bindings { get => _bindings; set => Set(ref _bindings, value ?? DefaultBindings()); }

    public void NotifyBindingsChanged() => OnPropertyChanged(nameof(Bindings));

    public static Dictionary<string, List<string>> DefaultBindings()
    {
        var d = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(ShellAction.ToggleOverview)] = new() { "Super" },
            [nameof(ShellAction.ShowApplications)] = new() { "Super+A" },
            [nameof(ShellAction.AppSwitcher)] = new() { "Super+Tab" },
            [nameof(ShellAction.WorkspacePrevious)] = new() { "Super+PageUp", "Ctrl+Alt+Up" },
            [nameof(ShellAction.WorkspaceNext)] = new() { "Super+PageDown", "Ctrl+Alt+Down" },
            [nameof(ShellAction.MoveWindowToPreviousWorkspace)] = new() { "Super+Shift+PageUp", "Ctrl+Alt+Shift+Up" },
            [nameof(ShellAction.MoveWindowToNextWorkspace)] = new() { "Super+Shift+PageDown", "Ctrl+Alt+Shift+Down" },
            [nameof(ShellAction.ToggleQuickSettings)] = new(),
            [nameof(ShellAction.ToggleNotifications)] = new(),
            [nameof(ShellAction.OpenShellSettings)] = new(),
        };
        for (int i = 1; i <= 9; i++)
            d["LaunchDockItem" + i] = new() { "Super+" + i };
        return d;
    }
}
