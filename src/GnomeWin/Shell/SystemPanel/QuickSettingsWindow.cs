using System.Windows;
using System.Windows.Controls;
using WpfDock = System.Windows.Controls.Dock;
using System.Windows.Controls.Primitives;
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
    private readonly ToggleButton _wifi, _bt, _dark;
    private readonly TextBlock _wifiSub = new() { FontSize = 11.5, Opacity = 0.8, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _batteryText = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 13, Margin = new Thickness(6, 0, 0, 0) };
    private readonly TextBlock _batteryGlyph = Glyph("", 16);
    private bool _updating;

    public QuickSettingsWindow(SystemStatusService status, Action openShellSettings, Func<string, string, bool> confirm) : base(390)
    {
        _status = status;
        _openShellSettings = openShellSettings;
        _confirm = confirm;
        Title = "GnomeWin Quick Settings";

        var root = new StackPanel();

        var top = new DockPanel { Margin = new Thickness(2, 0, 2, 12), LastChildFill = false };
        var battery = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        battery.Children.Add(_batteryGlyph);
        battery.Children.Add(_batteryText);
        DockPanel.SetDock(battery, WpfDock.Left);
        top.Children.Add(battery);
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
        _brightnessRow = SliderRow(Glyph("", 16), _brightness, () => ShellLauncher.Open("ms-settings:display"), toggleMute: false);
        root.Children.Add(_brightnessRow);

        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 0) };
        _wifi = Toggle("", Loc.T("WiFi"), _wifiSub, async on =>
        {
            if (!await _status.SetRadioAsync(true, on)) ShellLauncher.Open("ms-settings:network-wifi");
        });
        _bt = Toggle("", Loc.T("Bluetooth"), null, async on =>
        {
            if (!await _status.SetRadioAsync(false, on)) ShellLauncher.Open("ms-settings:bluetooth");
        });
        _dark = Toggle("", Loc.T("DarkStyle"), null, on => { ThemeManager.SetWindowsDarkMode(on); return Task.CompletedTask; });
        grid.Children.Add(_wifi);
        grid.Children.Add(_bt);
        grid.Children.Add(_dark);
        grid.Children.Add(LinkToggle("", Loc.T("PowerSaver"), "ms-settings:batterysaver", () => _status.BatterySaver));
        grid.Children.Add(LinkToggle("", Loc.T("AirplaneMode"), "ms-settings:network-airplanemode", () => false));
        grid.Children.Add(LinkToggle("", Loc.T("Vpn"), "ms-settings:network-vpn", () => false));
        grid.Children.Add(LinkToggle("", Loc.T("NightLight"), "ms-settings:nightlight", () => false));
        root.Children.Add(grid);

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
        _status.EnsureDetails();
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

            _wifi.IsChecked = _status.WifiOn ?? _status.Network == NetworkKind.Wifi;
            _wifiSub.Text = _status.Network switch
            {
                NetworkKind.Wifi => string.IsNullOrEmpty(_status.Ssid) ? Loc.T("WiFi") : _status.Ssid,
                NetworkKind.Ethernet => Loc.T("Wired"),
                NetworkKind.None => Loc.T("Disconnected"),
                _ => Loc.T("Network"),
            };
            _bt.IsChecked = _status.BluetoothOn ?? false;
            _dark.IsChecked = !ThemeManager.SystemAppsUseLightTheme();

            _batteryGlyph.Visibility = _batteryText.Visibility = _status.HasBattery ? Visibility.Visible : Visibility.Collapsed;
            _batteryGlyph.Text = StatusGlyphs.Battery(_status.BatteryPercent, _status.Charging);
            _batteryText.Text = $"{_status.BatteryPercent} %" + (_status.Charging ? "  ·  " + Loc.T("Charging") : string.Empty);
        }
        finally { _updating = false; }
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

    private static TextBlock Glyph(string g, double size)
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

    private Grid SliderRow(TextBlock icon, Slider slider, Action openSettings, bool toggleMute)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var iconBtn = new Button { Content = icon, Width = 36, Height = 36, Background = System.Windows.Media.Brushes.Transparent };
        iconBtn.SetResourceReference(StyleProperty, "RoundIconButton");
        if (toggleMute) iconBtn.Click += (_, _) => _status.Muted = !_status.Muted;
        slider.SetResourceReference(StyleProperty, "ShellSlider");
        slider.Margin = new Thickness(8, 0, 8, 0);
        slider.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(slider, 1);
        var more = new Button { Content = "", Width = 32, Height = 32, FontSize = 11, Background = System.Windows.Media.Brushes.Transparent };
        more.SetResourceReference(StyleProperty, "RoundIconButton");
        more.Click += (_, _) => { CloseAnimated(); openSettings(); };
        Grid.SetColumn(more, 2);
        g.Children.Add(iconBtn);
        g.Children.Add(slider);
        g.Children.Add(more);
        return g;
    }

    private ToggleButton Toggle(string glyph, string title, TextBlock? subtitle, Func<bool, Task> onToggle)
    {
        var t = new ToggleButton { Margin = new Thickness(4) };
        t.SetResourceReference(StyleProperty, "PillToggle");
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(Glyph(glyph, 16));
        var texts = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 122 };
        texts.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = title });
        if (subtitle != null) texts.Children.Add(subtitle);
        content.Children.Add(texts);
        t.Content = content;
        t.Click += async (_, _) => { if (!_updating) await onToggle(t.IsChecked == true); };
        return t;
    }

    private ToggleButton LinkToggle(string glyph, string title, string uri, Func<bool> state)
    {
        var t = Toggle(glyph, title, null, _ => { CloseAnimated(); ShellLauncher.Open(uri); return Task.CompletedTask; });
        t.IsChecked = state();
        t.Click += (_, _) => t.IsChecked = state();
        return t;
    }
}
