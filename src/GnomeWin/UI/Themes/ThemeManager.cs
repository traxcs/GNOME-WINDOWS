using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using GnomeWin.Services.Settings;

namespace GnomeWin.UI.Themes;

public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string FontBase = "pack://application:,,,/GnomeWin;component/Assets/Fonts/";

    private static DesignStyle _style = DesignStyle.Gnome;
    private static ThemeMode _mode = ThemeMode.Dark;
    private static AccentColor _accent = AccentColor.Default;

    public static bool IsDark { get; private set; } = true;
    public static DesignStyle Style => _style;
    public static event Action? ThemeChanged;

    public static void Apply(DesignStyle style, ThemeMode mode, AccentColor accent)
    {
        _style = style;
        _mode = mode;
        _accent = accent;
        var app = Application.Current;
        if (app == null) return;

        bool dark = mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => !SystemAppsUseLightTheme(),
        };
        string name = style switch { DesignStyle.Ubuntu => "Yaru", DesignStyle.PopOS => "Pop", _ => "Adwaita" };
        var palette = new ResourceDictionary { Source = new Uri($"/GnomeWin;component/UI/Themes/{name}.{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative) };
        var dicts = app.Resources.MergedDictionaries;
        if (dicts.Count > 0) dicts[0] = palette; else dicts.Insert(0, palette);
        IsDark = dark;

        app.Resources["Font.Ui"] = new FontFamily(style switch
        {
            DesignStyle.Ubuntu => FontBase + "#Ubuntu, Segoe UI Variable Text, Segoe UI",
            DesignStyle.PopOS => FontBase + "#Fira Sans, Segoe UI Variable Text, Segoe UI",
            _ => FontBase + "#Cantarell, Segoe UI Variable Text, Segoe UI",
        });

        foreach (var key in new[] { "Color.Accent", "Brush.Accent", "Brush.AccentHover", "Brush.Indicator" }) app.Resources.Remove(key);
        if (accent != AccentColor.Default)
        {
            var c = AccentValue(accent);
            app.Resources["Color.Accent"] = c;
            app.Resources["Brush.Accent"] = Frozen(c);
            app.Resources["Brush.AccentHover"] = Frozen(Color.FromRgb((byte)Math.Min(255, c.R + 20), (byte)Math.Min(255, c.G + 20), (byte)Math.Min(255, c.B + 20)));
            if (style == DesignStyle.Ubuntu) app.Resources["Brush.Indicator"] = Frozen(c);
        }
        ThemeChanged?.Invoke();
    }

    public static void Apply(ThemeMode mode) => Apply(_style, mode, _accent);
    public static void ApplyAccent(AccentColor accent) => Apply(_style, _mode, accent);

    public static Color AccentValue(AccentColor a) => a switch
    {
        AccentColor.Blue => Rgb(0x35, 0x84, 0xE4),
        AccentColor.Teal => Rgb(0x21, 0x90, 0xA4),
        AccentColor.Green => Rgb(0x3A, 0x94, 0x4A),
        AccentColor.Yellow => Rgb(0xC8, 0x88, 0x00),
        AccentColor.Orange => Rgb(0xE9, 0x54, 0x20),
        AccentColor.Red => Rgb(0xE6, 0x2D, 0x42),
        AccentColor.Pink => Rgb(0xD5, 0x61, 0x99),
        AccentColor.Purple => Rgb(0x91, 0x41, 0xAC),
        AccentColor.Slate => Rgb(0x6F, 0x83, 0x96),
        AccentColor.Bark => Rgb(0x78, 0x78, 0x59),
        AccentColor.Sage => Rgb(0x65, 0x7B, 0x69),
        AccentColor.Olive => Rgb(0x4B, 0x85, 0x01),
        AccentColor.Viridian => Rgb(0x03, 0x87, 0x5B),
        AccentColor.PrussianGreen => Rgb(0x30, 0x82, 0x80),
        AccentColor.Magenta => Rgb(0xB3, 0x4C, 0xB3),
        _ => Rgb(0x35, 0x84, 0xE4),
    };

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static bool SystemAppsUseLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
    }

    public static void SetWindowsDarkMode(bool dark)
    {
        using var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey);
        key.SetValue("AppsUseLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
        key.SetValue("SystemUsesLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
        Platform.Win32.NativeMethods.SendMessageTimeout(Platform.Win32.NativeMethods.HWND_BROADCAST,
            Platform.Win32.NativeMethods.WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveColorSet",
            Platform.Win32.NativeMethods.SMTO_ABORTIFHUNG, 200, out _);
    }
}

public static class StylePresets
{
    public static void ApplyLayout(DesignStyle style, AppSettings s)
    {
        var d = s.Dock;
        switch (style)
        {
            case DesignStyle.Ubuntu:
                d.Position = DockPosition.Left; d.Extended = true; d.Visibility = DockVisibility.AlwaysVisible;
                d.IconSize = 40; d.HoverEffect = DockHoverEffect.Highlight; d.BackgroundOpacity = 0.85;
                break;
            case DesignStyle.PopOS:
                d.Position = DockPosition.Bottom; d.Extended = false; d.Visibility = DockVisibility.Intellihide;
                d.IconSize = 40; d.HoverEffect = DockHoverEffect.Highlight; d.BackgroundOpacity = 0.9;
                break;
            default:
                d.Position = DockPosition.Bottom; d.Extended = false; d.Visibility = DockVisibility.OverviewOnly;
                d.IconSize = 40; d.HoverEffect = DockHoverEffect.Highlight; d.BackgroundOpacity = 0.8;
                break;
        }
        s.General.Accent = AccentColor.Default;
    }
}
