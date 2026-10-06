using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using WpfDock = System.Windows.Controls.Dock;
using GnomeWin.Platform.Taskbar;
using GnomeWin.Platform.Win32;
using GnomeWin.Services;
using GnomeWin.Services.SystemStatus;
using GnomeWin.UI;
using GnomeWin.UI.Components;
using GnomeWin.UI.Themes;

namespace GnomeWin.Shell.SystemPanel;

public sealed class QuickSettingsWindow : PopupPanelWindow
{
    private readonly SystemStatusService _status;
    private readonly Action _openShellSettings;
    private readonly Func<string, string, bool> _confirm;
    private readonly Slider _volume = new() { Minimum = 0, Maximum = 100 };
    private readonly Slider _brightness = new() { Minimum = 0, Maximum = 100 };
    private readonly TextBlock _volumeIcon = Glyph("", 16);
    private readonly Grid _brightnessRow;
    private readonly QuickToggle? _wired, _wifi, _bt;
    private readonly QuickToggle _power, _night, _dark, _dnd, _airplane;
    private readonly Border _batteryPill;
    private readonly TextBlock _batteryText = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 0, 0, 0) };
    private readonly TextBlock _batteryGlyph = Glyph("", 16);
    private readonly Border _menu = new() { Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(12), Padding = new Thickness(8), Margin = new Thickness(4, 6, 4, 0) };
    private bool _updating;

    private static string L(string fr, string en) => Loc.IsFrench ? fr : en;

    public QuickSettingsWindow(SystemStatusService status, Action openShellSettings, Func<string, string, bool> confirm) : base(390)
    {
        _status = status;
        _openShellSettings = openShellSettings;
        _confirm = confirm;
        Title = "GnomeWin Quick Settings";
        _status.EnsureDetails();

        var root = new StackPanel();

        var top = new DockPanel { Margin = new Thickness(2, 0, 2, 12), LastChildFill = false };
        var battery = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        battery.Children.Add(_batteryGlyph);
        battery.Children.Add(_batteryText);
        _batteryPill = new Border { Child = battery, Height = 38, Padding = new Thickness(14, 0, 16, 0), CornerRadius = new CornerRadius(19), Cursor = Cursors.Hand };
        _batteryPill.SetResourceReference(Border.BackgroundProperty, "Brush.CardBg");
        _batteryPill.MouseLeftButtonUp += (_, _) => { CloseAnimated(); ShellLauncher.Open("ms-settings:batterysaver"); };
        Hover(_batteryPill, "Brush.CardBg");
        DockPanel.SetDock(_batteryPill, WpfDock.Left);
        top.Children.Add(_batteryPill);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(buttons, WpfDock.Right);
        buttons.Children.Add(RoundButton("", Loc.T("Screenshot"), () => { CloseAnimated(); ShellLauncher.Open("ms-screenclip:"); }));
        buttons.Children.Add(RoundButton("", Loc.T("Settings"), () => { CloseAnimated(); _openShellSettings(); }));
        buttons.Children.Add(RoundButton("", Loc.T("Lock"), () => { CloseAnimated(); PowerActions.Lock(); }));
        var power = RoundButton("", Loc.T("Power"), () => { });
        power.Click += (_, _) => ShowPowerMenu(power);
        buttons.Children.Add(power);
        top.Children.Add(buttons);
        root.Children.Add(top);

        root.Children.Add(SliderRow(_volumeIcon, _volume, () => ShellLauncher.Open("ms-settings:sound"), toggleMute: true));
        _brightnessRow = SliderRow(Glyph("", 16), _brightness, null, toggleMute: false);
        root.Children.Add(_brightnessRow);

        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 0) };
        if (_status.Network == NetworkKind.Ethernet)
        {
            _wired = new QuickToggle("", L("Filaire", "Wired"), hasMenu: true);
            _wired.Toggled += _ => { CloseAnimated(); ShellLauncher.Open("ms-settings:network-ethernet"); };
            _wired.MenuRequested += () => { CloseAnimated(); ShellLauncher.Open("ms-settings:network-ethernet"); };
            grid.Children.Add(_wired);
        }
        if (_status.WifiOn != null || _status.Network == NetworkKind.Wifi)
        {
            _wifi = new QuickToggle("", L("Wi-Fi", "Wi-Fi"), hasMenu: true);
            _wifi.Toggled += async on => { if (!await _status.SetRadioAsync(true, on)) ShellLauncher.Open("ms-settings:network-wifi"); };
            _wifi.MenuRequested += () => { CloseAnimated(); ShellLauncher.Open("ms-settings:network-wifi"); };
            grid.Children.Add(_wifi);
        }
        if (_status.BluetoothOn != null)
        {
            _bt = new QuickToggle("", "Bluetooth", hasMenu: true);
            _bt.Toggled += async on => { if (!await _status.SetRadioAsync(false, on)) ShellLauncher.Open("ms-settings:bluetooth"); };
            _bt.MenuRequested += () => { CloseAnimated(); ShellLauncher.Open("ms-settings:bluetooth"); };
            grid.Children.Add(_bt);
        }

        _power = new QuickToggle("", L("Mode puissance", "Power Mode"), hasMenu: true) { Checkable = false };
        _power.Toggled += _ => TogglePowerMenu();
        _power.MenuRequested += TogglePowerMenu;
        grid.Children.Add(_power);

        _night = new QuickToggle("", L("Mode nuit", "Night Light"), hasMenu: false) { Checkable = false };
        _night.Toggled += _ => { CloseAnimated(); ShellLauncher.Open("ms-settings:nightlight"); };
        grid.Children.Add(_night);

        _dark = new QuickToggle("", Loc.T("DarkStyle"), hasMenu: false);
        _dark.Toggled += on => ThemeManager.SetWindowsDarkMode(on);
        grid.Children.Add(_dark);

        _dnd = new QuickToggle("", L("Ne pas déranger", "Do Not Disturb"), hasMenu: false) { Checkable = false };
        _dnd.Toggled += _ => { CloseAnimated(); ShellLauncher.Open("ms-settings:notifications"); };
        grid.Children.Add(_dnd);

        _airplane = new QuickToggle("", Loc.T("AirplaneMode"), hasMenu: false) { Checkable = false };
        _airplane.Toggled += _ => { CloseAnimated(); ShellLauncher.Open("ms-settings:network-airplanemode"); };
        grid.Children.Add(_airplane);

        root.Children.Add(grid);
        _menu.SetResourceReference(Border.BackgroundProperty, "Brush.CardBg");
        root.Children.Add(_menu);

        Body = root;

        _volume.ValueChanged += (_, e) => { if (!_updating) _status.Volume = (float)(e.NewValue / 100.0); };
        _brightness.ValueChanged += (_, e) => { if (!_updating) _status.SetBrightness((int)e.NewValue); };
        _status.AudioChanged += Refresh;
        _status.PowerChanged += Refresh;
        _status.RadiosChanged += Refresh;
        _status.NetworkChanged += Refresh;
        _status.BrightnessChanged += Refresh;
        Closed += (_, _) =>
        {
            _status.AudioChanged -= Refresh;
            _status.PowerChanged -= Refresh;
            _status.RadiosChanged -= Refresh;
            _status.NetworkChanged -= Refresh;
            _status.BrightnessChanged -= Refresh;
        };
        Refresh();
    }

    private void Refresh()
    {
        _updating = true;
        try
        {
            _volume.Value = Math.Round(_status.Volume * 100);
            _volume.IsEnabled = _status.AudioAvailable;
            _volumeIcon.Text = StatusGlyphs.Volume(_status.Volume, _status.Muted);

            _brightnessRow.Visibility = _status.Brightness.HasValue ? Visibility.Visible : Visibility.Collapsed;
            if (_status.Brightness is int b) _brightness.Value = b;

            if (_wired != null)
            {
                _wired.IsChecked = _status.Network == NetworkKind.Ethernet;
                _wired.Subtitle = _status.Network == NetworkKind.Ethernet ? L("Connecté", "Connected") : null;
            }
            if (_wifi != null)
            {
                _wifi.IsChecked = _status.WifiOn ?? _status.Network == NetworkKind.Wifi;
                _wifi.Subtitle = _status.Network == NetworkKind.Wifi && !string.IsNullOrEmpty(_status.Ssid) ? _status.Ssid : null;
            }
            if (_bt != null) _bt.IsChecked = _status.BluetoothOn ?? false;

            _power.Subtitle = WindowsSettings.PowerSchemeName(WindowsSettings.ActivePowerScheme, Loc.IsFrench);
            _night.IsChecked = WindowsSettings.NightLightActive;
            _dark.IsChecked = !ThemeManager.SystemAppsUseLightTheme();

            _batteryPill.Visibility = _status.HasBattery ? Visibility.Visible : Visibility.Collapsed;
            _batteryGlyph.Text = StatusGlyphs.Battery(_status.BatteryPercent, _status.Charging);
            _batteryText.Text = $"{_status.BatteryPercent} %";
        }
        finally { _updating = false; }
    }

    private void TogglePowerMenu()
    {
        if (_menu.Visibility == Visibility.Visible) { _menu.Visibility = Visibility.Collapsed; _power.IsMenuOpen = false; return; }
        var list = new StackPanel();
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 8, 8) };
        var hIcon = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Child = Glyph("", 15) };
        hIcon.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
        ((TextBlock)hIcon.Child).HorizontalAlignment = HorizontalAlignment.Center;
        ((TextBlock)hIcon.Child).SetResourceReference(TextBlock.ForegroundProperty, "Brush.OnAccent");
        header.Children.Add(hIcon);
        header.Children.Add(new TextBlock { Text = L("Mode puissance", "Power Mode"), FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        list.Children.Add(header);

        Guid active = WindowsSettings.ActivePowerScheme;
        var schemes = WindowsSettings.AvailablePowerSchemes();
        Guid[] order = { WindowsSettings.SchemePerformance, WindowsSettings.SchemeBalanced, WindowsSettings.SchemePowerSaver };
        foreach (var s in schemes.OrderBy(s => Array.IndexOf(order, s) is int i && i >= 0 ? i : 99))
        {
            Guid scheme = s;
            list.Children.Add(MenuRow(WindowsSettings.PowerSchemeName(scheme, Loc.IsFrench), scheme == active, () =>
            {
                WindowsSettings.SetPowerScheme(scheme);
                _menu.Visibility = Visibility.Collapsed;
                _power.IsMenuOpen = false;
                Refresh();
            }));
        }
        var sep = new Border { Height = 1, Margin = new Thickness(8, 6, 8, 6) };
        sep.SetResourceReference(Border.BackgroundProperty, "Brush.Separator");
        list.Children.Add(sep);
        list.Children.Add(MenuRow(L("Paramètres d'alimentation", "Power Settings"), false, () => { CloseAnimated(); ShellLauncher.Open("ms-settings:powersleep"); }));
        _menu.Child = list;
        _menu.Visibility = Visibility.Visible;
        _power.IsMenuOpen = true;
    }

    private static Border MenuRow(string text, bool selected, Action click)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new TextBlock { Text = text, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center });
        if (selected)
        {
            var check = Glyph("", 13);
            Grid.SetColumn(check, 1);
            g.Children.Add(check);
        }
        var row = new Border { Child = g, Padding = new Thickness(12, 9, 12, 9), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        Hover(row, null);
        row.MouseLeftButtonUp += (_, _) => click();
        return row;
    }

    private static void Hover(Border b, string? normalKey)
    {
        b.MouseEnter += (_, _) => b.SetResourceReference(Border.BackgroundProperty, normalKey == null ? "Brush.ItemHover" : "Brush.CardHover");
        b.MouseLeave += (_, _) =>
        {
            if (normalKey == null) b.Background = Brushes.Transparent;
            else b.SetResourceReference(Border.BackgroundProperty, normalKey);
        };
    }

    private void ShowPowerMenu(FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        void Add(string text, Action a) { var mi = new MenuItem { Header = text }; mi.Click += (_, _) => a(); menu.Items.Add(mi); }
        Add(Loc.T("Suspend"), () => { CloseAnimated(); PowerActions.Suspend(); });
        Add(Loc.T("Restart"), () => { CloseAnimated(); if (_confirm(Loc.T("ConfirmRestart"), Loc.T("Restart"))) PowerActions.Restart(); });
        Add(Loc.T("Power"), () => { CloseAnimated(); if (_confirm(Loc.T("ConfirmPowerOff"), Loc.T("Power"))) PowerActions.PowerOff(); });
        menu.Items.Add(new Separator());
        Add(Loc.T("LogOut"), () => { CloseAnimated(); if (_confirm(Loc.T("ConfirmLogOut"), Loc.T("LogOut"))) PowerActions.LogOut(); });
        menu.IsOpen = true;
    }

    internal static TextBlock Glyph(string g, double size)
    {
        var t = new TextBlock { Text = g, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Icons");
        return t;
    }

    private Button RoundButton(string glyph, string tip, Action click)
    {
        var b = new Button { Content = glyph, ToolTip = tip, Margin = new Thickness(6, 0, 0, 0) };
        b.SetResourceReference(StyleProperty, "RoundIconButton");
        b.Click += (_, _) => click();
        return b;
    }

    private Grid SliderRow(TextBlock icon, Slider slider, Action? openSettings, bool toggleMute)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        var iconBtn = new Button { Content = icon, Width = 36, Height = 36, Background = Brushes.Transparent };
        iconBtn.SetResourceReference(StyleProperty, "RoundIconButton");
        if (toggleMute) iconBtn.Click += (_, _) => _status.Muted = !_status.Muted;
        slider.SetResourceReference(StyleProperty, "ShellSlider");
        slider.Margin = new Thickness(8, 0, 8, 0);
        slider.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(slider, 1);
        g.Children.Add(iconBtn);
        g.Children.Add(slider);
        if (openSettings != null)
        {
            var more = new Button { Content = "", Width = 32, Height = 32, FontSize = 11, Background = Brushes.Transparent };
            more.SetResourceReference(StyleProperty, "RoundIconButton");
            more.Click += (_, _) => { CloseAnimated(); openSettings(); };
            Grid.SetColumn(more, 2);
            g.Children.Add(more);
        }
        return g;
    }
}

internal sealed class QuickToggle : Border
{
    private readonly Border _main, _arrow;
    private readonly TextBlock _icon, _title, _subtitle, _chevron;
    private bool _checked, _menuOpen;

    public event Action<bool>? Toggled;
    public event Action? MenuRequested;

    public bool Checkable { get; init; } = true;

    public QuickToggle(string glyph, string title, bool hasMenu)
    {
        Height = 48;
        Margin = new Thickness(4);
        CornerRadius = new CornerRadius(12);
        ClipToBounds = true;
        Cursor = Cursors.Hand;
        ToolTip = title;

        _icon = QuickSettingsWindow.Glyph(glyph, 16);
        _title = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
        _subtitle = new TextBlock { FontSize = 12, Opacity = 0.85, TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed };
        var texts = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(_title);
        texts.Children.Add(_subtitle);
        var content = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_icon, WpfDock.Left);
        content.Children.Add(_icon);
        content.Children.Add(texts);

        _main = new Border { Child = content, Padding = new Thickness(16, 0, 8, 0), Background = Brushes.Transparent };
        _main.MouseLeftButtonUp += (_, e) =>
        {
            if (Checkable) { IsChecked = !IsChecked; Toggled?.Invoke(IsChecked); }
            else Toggled?.Invoke(IsChecked);
            e.Handled = true;
        };

        _chevron = QuickSettingsWindow.Glyph("", 11);
        _chevron.HorizontalAlignment = HorizontalAlignment.Center;
        _arrow = new Border { Width = 34, Child = _chevron, Background = Brushes.Transparent, Visibility = hasMenu ? Visibility.Visible : Visibility.Collapsed };
        _arrow.MouseLeftButtonUp += (_, e) => { MenuRequested?.Invoke(); e.Handled = true; };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_arrow, 1);
        grid.Children.Add(_main);
        grid.Children.Add(_arrow);
        Child = grid;

        foreach (var part in new[] { _main, _arrow })
        {
            var p = part;
            p.MouseEnter += (_, _) => p.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            p.MouseLeave += (_, _) => UpdateVisuals();
        }
        UpdateVisuals();
    }

    public bool IsChecked { get => _checked; set { _checked = value; UpdateVisuals(); } }
    public bool IsMenuOpen { get => _menuOpen; set { _menuOpen = value; _chevron.Text = value ? "" : ""; } }

    public string? Subtitle
    {
        get => _subtitle.Text;
        set
        {
            _subtitle.Text = value ?? string.Empty;
            _subtitle.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void UpdateVisuals()
    {
        SetResourceReference(BackgroundProperty, _checked ? "Brush.Accent" : "Brush.CardBg");
        SetResourceReference(TextElement.ForegroundProperty, _checked ? "Brush.OnAccent" : "Brush.Fg");
        _main.Background = Brushes.Transparent;
        _arrow.Background = new SolidColorBrush(_checked ? Color.FromArgb(0x26, 0, 0, 0) : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    }
}
