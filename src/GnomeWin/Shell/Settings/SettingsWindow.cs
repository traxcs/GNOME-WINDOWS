using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using GnomeWin.Core;
using GnomeWin.Input.GlobalHotkeys;
using GnomeWin.Platform.Win32;
using GnomeWin.Services;
using GnomeWin.Services.Settings;
using GnomeWin.Services.SystemStatus;
using GnomeWin.UI;
using GnomeWin.UI.Themes;
using Microsoft.Win32;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfEllipse = System.Windows.Shapes.Ellipse;

namespace GnomeWin.Shell.Settings;

public sealed class SettingsWindow : Window
{
    private sealed record PanelDef(string Id, string Fr, string En, string Glyph, string Keywords, Func<UIElement> Build);

    private readonly SettingsService _settings;
    private readonly KeyboardHookService? _hook;
    private readonly SystemStatusService? _status;
    private readonly ApplicationManager? _apps;
    private readonly Func<string> _diagnostics;
    private readonly Action _restoreAndQuit;
    private readonly List<PanelDef> _panels;
    private readonly ListBox _sidebar = new();
    private readonly TextBox _search = new();
    private readonly Border _searchHost = new();
    private readonly TextBlock _pageTitle = new();
    private readonly ScrollViewer _content = new();

    public SettingsWindow(SettingsService settings, KeyboardHookService? hook, SystemStatusService? status, ApplicationManager? apps,
                          Func<string> diagnostics, Action restoreAndQuit)
    {
        _settings = settings;
        _hook = hook;
        _status = status;
        _apps = apps;
        _diagnostics = diagnostics;
        _restoreAndQuit = restoreAndQuit;

        Title = L("Paramètres", "Settings");
        Width = 1020;
        Height = 720;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Brush.WindowBg");
        SetResourceReference(ForegroundProperty, "Brush.Fg");
        SetResourceReference(FontFamilyProperty, "Font.Ui");
        FontSize = 14;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/GnomeWin;component/Assets/GnomeWin.ico"));
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 46,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
            CornerRadius = new CornerRadius(12),
        });

        _panels = new List<PanelDef>
        {
            new("wifi", "Wi-Fi", "Wi-Fi", "", "wifi wlan sans fil wireless", BuildWifi),
            new("network", "Réseau", "Network", "", "reseau network ethernet vpn proxy", BuildNetwork),
            new("bluetooth", "Bluetooth", "Bluetooth", "", "bluetooth", BuildBluetooth),
            new("displays", "Écrans", "Displays", "", "ecrans displays moniteur monitor resolution plein ecran fullscreen barre", BuildDisplays),
            new("sound", "Son", "Sound", "", "son sound volume audio", BuildSound),
            new("power", "Énergie", "Power", "", "energie power batterie battery", BuildPower),
            new("multitasking", "Multitâche", "Multitasking", "", "multitache multitasking espaces workspaces coin actif hot corner", BuildMultitasking),
            new("appearance", "Apparence", "Appearance", "", "apparence appearance theme style accent couleur sombre dark fond", BuildAppearance),
            new("dock", "Dock", "Dock", "", "dock dash barre favoris", BuildDock),
            new("apps", "Applications", "Apps", "", "applications apps defaut demarrage favoris", BuildApps),
            new("notifications", "Notifications", "Notifications", "", "notifications ne pas deranger", BuildNotifications),
            new("search", "Recherche", "Search", "", "recherche search fichiers calculatrice", BuildSearch),
            new("mouse", "Souris et pavé tactile", "Mouse & Touchpad", "", "souris mouse touchpad pave", BuildMouse),
            new("keyboard", "Clavier", "Keyboard", "", "clavier keyboard raccourcis shortcuts super", BuildKeyboard),
            new("printers", "Imprimantes", "Printers", "", "imprimantes printers", BuildPrinters),
            new("accessibility", "Accessibilité", "Accessibility", "", "accessibilite accessibility animations", BuildAccessibility),
            new("privacy", "Confidentialité et sécurité", "Privacy & Security", "", "confidentialite privacy securite", BuildPrivacy),
            new("system", "Système", "System", "", "systeme system a propos about langue date demarrage maintenance journaux", BuildSystem),
        };

        Content = BuildLayout();
        RefreshSidebar(string.Empty);
        ShowPanel("appearance");

        SourceInitialized += (_, _) => { if (UI.Components.ShellWindow.SoftwareRenderingEnabled) UI.Components.ShellWindow.UseSoftwareRendering(this); ApplyTitleBarTheme(); };
        ThemeManager.ThemeChanged += ApplyTitleBarTheme;
        Closed += (_, _) => { ThemeManager.ThemeChanged -= ApplyTitleBarTheme; _hook?.CancelCapture(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { ToggleSearch(true); e.Handled = true; }
            else if (e.Key == Key.Escape && _searchHost.Visibility == Visibility.Visible) { ToggleSearch(false); e.Handled = true; }
        };
    }

    public void ShowPanel(string id)
    {
        if (_panels.All(p => p.Id != id)) return;
        if (_search.Text.Length > 0) ToggleSearch(false);
        _sidebar.SelectedItem = _sidebar.Items.Cast<ListBoxItem>().FirstOrDefault(x => (string)x.Tag == id);
    }

    private void ApplyTitleBarTheme()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        int dark = ThemeManager.IsDark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(h, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int round = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(h, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }

    private UIElement BuildLayout()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        root.ColumnDefinitions.Add(new ColumnDefinition());

        var side = new DockPanel();
        side.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "Brush.SidebarBg");
        var sideHeader = new Grid { Height = 46 };
        var searchBtn = HeaderButton("", L("Rechercher", "Search"), () => ToggleSearch(_searchHost.Visibility != Visibility.Visible));
        searchBtn.HorizontalAlignment = HorizontalAlignment.Left;
        searchBtn.Margin = new Thickness(8, 0, 0, 0);
        var menuBtn = HeaderButton("", L("Menu principal", "Main menu"), () => { });
        menuBtn.HorizontalAlignment = HorizontalAlignment.Right;
        menuBtn.Margin = new Thickness(0, 0, 8, 0);
        menuBtn.Click += (_, _) => ShowMainMenu(menuBtn);
        sideHeader.Children.Add(searchBtn);
        sideHeader.Children.Add(new TextBlock { Text = L("Paramètres", "Settings"), FontWeight = FontWeights.Bold, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
        sideHeader.Children.Add(menuBtn);
        DockPanel.SetDock(sideHeader, System.Windows.Controls.Dock.Top);
        side.Children.Add(sideHeader);

        _search.SetResourceReference(StyleProperty, "InputBox");
        _search.Padding = new Thickness(8, 6, 8, 6);
        _search.TextChanged += (_, _) => RefreshSidebar(_search.Text);
        _searchHost.Child = _search;
        _searchHost.Padding = new Thickness(10, 0, 10, 8);
        _searchHost.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(_searchHost, System.Windows.Controls.Dock.Top);
        side.Children.Add(_searchHost);

        _sidebar.BorderThickness = new Thickness(0);
        _sidebar.Background = Brushes.Transparent;
        _sidebar.Padding = new Thickness(6, 0, 6, 6);
        _sidebar.ItemContainerStyle = SidebarItemStyle();
        ScrollViewer.SetHorizontalScrollBarVisibility(_sidebar, ScrollBarVisibility.Disabled);
        _sidebar.SelectionChanged += (_, _) => { if (_sidebar.SelectedItem is ListBoxItem it) ShowPanelContent((string)it.Tag); };
        side.Children.Add(_sidebar);
        WindowChrome.SetIsHitTestVisibleInChrome(searchBtn, true);
        WindowChrome.SetIsHitTestVisibleInChrome(menuBtn, true);
        root.Children.Add(side);

        var main = new DockPanel();
        Grid.SetColumn(main, 1);
        var header = new Grid { Height = 46 };
        _pageTitle.FontWeight = FontWeights.Bold;
        _pageTitle.FontSize = 15;
        _pageTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _pageTitle.VerticalAlignment = VerticalAlignment.Center;
        _pageTitle.IsHitTestVisible = false;
        header.Children.Add(_pageTitle);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 10, 0) };
        buttons.Children.Add(WindowButton("", () => WindowState = WindowState.Minimized));
        buttons.Children.Add(WindowButton("", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized));
        buttons.Children.Add(WindowButton("", Close));
        header.Children.Add(buttons);
        DockPanel.SetDock(header, System.Windows.Controls.Dock.Top);
        main.Children.Add(header);
        _content.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _content.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _content.Focusable = false;
        main.Children.Add(_content);
        root.Children.Add(main);
        return root;
    }

    private void ToggleSearch(bool show)
    {
        _searchHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) { _search.Focus(); Keyboard.Focus(_search); }
        else _search.Text = string.Empty;
    }

    private void RefreshSidebar(string filter)
    {
        string q = Services.Search.TextMatcher.Normalize(filter);
        string? selected = (_sidebar.SelectedItem as ListBoxItem)?.Tag as string;
        _sidebar.Items.Clear();
        foreach (var p in _panels)
        {
            string hay = Services.Search.TextMatcher.Normalize(p.Fr + " " + p.En + " " + p.Keywords);
            if (q.Length > 0 && !hay.Contains(q)) continue;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var glyph = new TextBlock { Text = p.Glyph, FontSize = 16, Width = 22, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
            glyph.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Icons");
            row.Children.Add(glyph);
            row.Children.Add(new TextBlock { Text = Loc.IsFrench ? p.Fr : p.En, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var item = new ListBoxItem { Content = row, Tag = p.Id };
            _sidebar.Items.Add(item);
            if (p.Id == selected) _sidebar.SelectedItem = item;
        }
        if (_sidebar.SelectedItem == null && _sidebar.Items.Count > 0 && q.Length > 0) _sidebar.SelectedIndex = 0;
    }

    private void ShowPanelContent(string id)
    {
        var p = _panels.First(x => x.Id == id);
        _pageTitle.Text = Loc.IsFrench ? p.Fr : p.En;
        var clamp = new StackPanel { MaxWidth = 640, Margin = new Thickness(24, 12, 24, 36) };
        clamp.Children.Add(p.Build());
        _content.Content = clamp;
        _content.ScrollToTop();
    }

    private void ShowMainMenu(FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        void Add(string text, Action a) { var mi = new MenuItem { Header = text }; mi.Click += (_, _) => a(); menu.Items.Add(mi); }
        Add(L("Raccourcis clavier", "Keyboard Shortcuts"), () => ShowPanel("keyboard"));
        Add(L("À propos de GnomeWin", "About GnomeWin"), () => ShowPanel("system"));
        menu.IsOpen = true;
    }

    private static Style SidebarItemStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        var template = new ControlTemplate(typeof(ListBoxItem));
        var bd = new FrameworkElementFactory(typeof(Border), "Bd");
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        bd.SetValue(Border.PaddingProperty, new Thickness(10, 9, 10, 9));
        bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        bd.SetValue(Border.MarginProperty, new Thickness(0, 1, 0, 1));
        bd.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        template.VisualTree = bd;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Brush.ItemHover"), "Bd"));
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Brush.ItemSelected"), "Bd"));
        template.Triggers.Add(hover);
        template.Triggers.Add(selected);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Brush.Fg")));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        return style;
    }

    private Button HeaderButton(string glyph, string tip, Action click)
    {
        var b = new Button { Content = glyph, ToolTip = tip, Width = 34, Height = 34, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(StyleProperty, "ShellButton");
        b.SetResourceReference(FontFamilyProperty, "Font.Icons");
        b.Padding = new Thickness(0);
        b.Click += (_, _) => click();
        return b;
    }

    private Button WindowButton(string glyph, Action click)
    {
        var b = new Button { Content = glyph, Width = 24, Height = 24, FontSize = 8, Margin = new Thickness(8, 0, 0, 0) };
        b.SetResourceReference(StyleProperty, "RoundIconButton");
        b.Click += (_, _) => click();
        WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        return b;
    }

    private static string L(string fr, string en) => Loc.IsFrench ? fr : en;

    private static UIElement Group(string? title, string? description, params UIElement[] rows)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 12) };
        if (title != null)
            panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 0, 0, description == null ? 10 : 2) });
        if (description != null)
        {
            var d = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 0, 0, 10) };
            d.SetResourceReference(TextBlock.ForegroundProperty, "Brush.FgDim");
            panel.Children.Add(d);
        }
        if (rows.Length > 0)
        {
            var list = new StackPanel();
            for (int i = 0; i < rows.Length; i++)
            {
                if (i > 0)
                {
                    var sep = new Border { Height = 1 };
                    sep.SetResourceReference(Border.BackgroundProperty, "Brush.Separator");
                    list.Children.Add(sep);
                }
                list.Children.Add(rows[i]);
            }
            var card = new Border { CornerRadius = new CornerRadius(12), Child = list, ClipToBounds = true };
            card.SetResourceReference(Border.BackgroundProperty, "Brush.CardBg");
            panel.Children.Add(card);
        }
        return panel;
    }

    private static UIElement GroupBox(string title, UIElement content)
    {
        var card = new Border { CornerRadius = new CornerRadius(12), Child = content };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.CardBg");
        var p = new StackPanel { Margin = new Thickness(0, 12, 0, 12) };
        p.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 0, 0, 10) });
        p.Children.Add(card);
        return p;
    }

    private static Grid RowShell(string title, string? subtitle, UIElement? suffix, UIElement? prefix = null)
    {
        var g = new Grid { MinHeight = 52 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (prefix is FrameworkElement pf)
        {
            pf.Margin = new Thickness(14, 0, 0, 0);
            pf.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(pf);
        }
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(prefix == null ? 14 : 12, 8, 12, 8) };
        texts.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(subtitle))
        {
            var s = new TextBlock { Text = subtitle, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            s.SetResourceReference(TextBlock.ForegroundProperty, "Brush.FgDim");
            texts.Children.Add(s);
        }
        Grid.SetColumn(texts, 1);
        g.Children.Add(texts);
        if (suffix is FrameworkElement fe)
        {
            fe.VerticalAlignment = VerticalAlignment.Center;
            fe.Margin = new Thickness(0, 0, 14, 0);
            Grid.SetColumn(fe, 2);
            g.Children.Add(fe);
        }
        return g;
    }

    private static void MakeActivatable(Grid row, Action click)
    {
        row.Background = Brushes.Transparent;
        row.Cursor = Cursors.Hand;
        row.MouseEnter += (_, _) => row.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "Brush.ItemHover");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) => click();
    }

    private static bool IsWithin(object source, DependencyObject container)
    {
        var d = source as DependencyObject;
        while (d != null) { if (ReferenceEquals(d, container)) return true; d = VisualTreeHelper.GetParent(d); }
        return false;
    }

    private static UIElement SwitchRow(string title, string? subtitle, object source, string path)
    {
        var sw = new CheckBox();
        sw.SetResourceReference(StyleProperty, "Switch");
        sw.SetBinding(ToggleButton.IsCheckedProperty, new Binding(path) { Source = source, Mode = BindingMode.TwoWay });
        var row = RowShell(title, subtitle, sw);
        row.Background = Brushes.Transparent;
        row.Cursor = Cursors.Hand;
        row.MouseLeftButtonUp += (_, e) => { if (!IsWithin(e.OriginalSource, sw)) sw.IsChecked = sw.IsChecked != true; };
        return row;
    }

    private static UIElement SwitchRowAction(string title, string? subtitle, bool value, Action<bool> changed)
    {
        var sw = new CheckBox { IsChecked = value };
        sw.SetResourceReference(StyleProperty, "Switch");
        sw.Click += (_, _) => changed(sw.IsChecked == true);
        var row = RowShell(title, subtitle, sw);
        row.Background = Brushes.Transparent;
        row.Cursor = Cursors.Hand;
        row.MouseLeftButtonUp += (_, e) => { if (!IsWithin(e.OriginalSource, sw)) { sw.IsChecked = sw.IsChecked != true; changed(sw.IsChecked == true); } };
        return row;
    }

    private static UIElement ComboRow<T>(string title, string? subtitle, object source, string path, params (T Value, string Fr, string En)[] options) where T : struct, Enum
    {
        var c = new ComboBox { MinWidth = 170 };
        foreach (var o in options) c.Items.Add(new ComboBoxItem { Content = Loc.IsFrench ? o.Fr : o.En, Tag = o.Value });
        var prop = source.GetType().GetProperty(path)!;
        T current = (T)prop.GetValue(source)!;
        c.SelectedIndex = Math.Max(0, Array.FindIndex(options, o => EqualityComparer<T>.Default.Equals(o.Value, current)));
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is ComboBoxItem it) prop.SetValue(source, it.Tag); };
        return RowShell(title, subtitle, c);
    }

    private static UIElement DockPositionRow(DockSettings d)
    {
        var c = new ComboBox { MinWidth = 170 };
        string[] labels = { L("En bas", "Bottom"), L("Au centre", "Centre"), L("À gauche", "Left"), L("À droite", "Right") };
        foreach (var l in labels) c.Items.Add(new ComboBoxItem { Content = l });
        int Current() => d.Position switch
        {
            DockPosition.Left => 2,
            DockPosition.Right => 3,
            _ => d.Extended && d.CenterIcons ? 1 : 0,
        };
        c.SelectedIndex = Current();
        bool syncing = false;
        c.SelectionChanged += (_, _) =>
        {
            if (syncing) return;
            switch (c.SelectedIndex)
            {
                case 0: d.Position = DockPosition.Bottom; d.Extended = false; break;
                case 1: d.Position = DockPosition.Bottom; d.Extended = true; d.CenterIcons = true; break;
                case 2: d.Position = DockPosition.Left; break;
                case 3: d.Position = DockPosition.Right; break;
            }
        };
        void OnChanged(object? _, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(DockSettings.Extended) or nameof(DockSettings.CenterIcons) or nameof(DockSettings.Position))) return;
            syncing = true;
            try { c.SelectedIndex = Current(); }
            finally { syncing = false; }
        }
        c.Loaded += (_, _) => { d.PropertyChanged -= OnChanged; d.PropertyChanged += OnChanged; };
        c.Unloaded += (_, _) => d.PropertyChanged -= OnChanged;
        return RowShell(L("Position sur l'écran", "Position on screen"), null, c);
    }

    private static UIElement SliderRow(string title, string? subtitle, object source, string path, double min, double max, double tick, Func<double, string> format)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var s = new Slider { Minimum = min, Maximum = max, Width = 200, TickFrequency = tick, IsSnapToTickEnabled = true };
        s.SetResourceReference(StyleProperty, "ShellSlider");
        s.SetBinding(RangeBase.ValueProperty, new Binding(path) { Source = source, Mode = BindingMode.TwoWay });
        var label = new TextBlock { Width = 52, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Text = format(s.Value) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.FgDim");
        s.ValueChanged += (_, e) => label.Text = format(e.NewValue);
        panel.Children.Add(s);
        panel.Children.Add(label);
        return RowShell(title, subtitle, panel);
    }

    private static UIElement LinkRow(string title, string? subtitle, Action click, bool external = true)
    {
        var arrow = new TextBlock { Text = external ? "" : "", FontSize = 12 };
        arrow.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Icons");
        arrow.SetResourceReference(TextBlock.ForegroundProperty, "Brush.FgDim");
        var row = RowShell(title, subtitle, arrow);
        MakeActivatable(row, click);
        return row;
    }

    private static UIElement WinLink(string title, string uri, string? subtitle = null) =>
        LinkRow(title, subtitle ?? L("Ouvre les Paramètres Windows", "Opens Windows Settings"), () => ShellLauncher.Open(uri));

    private static UIElement InfoRow(string title, string value)
    {
        var v = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, MaxWidth = 340, TextAlignment = TextAlignment.Right };
        v.SetResourceReference(TextBlock.ForegroundProperty, "Brush.FgDim");
        return RowShell(title, null, v);
    }

    private static UIElement ButtonRow(string title, string? subtitle, string button, Action click, bool destructive = false)
    {
        var b = new Button { Content = button };
        b.SetResourceReference(StyleProperty, destructive ? "AccentButton" : "FlatButton");
        if (destructive) b.SetResourceReference(BackgroundProperty, "Brush.Danger");
        b.Click += (_, _) => click();
        return RowShell(title, subtitle, b);
    }

    private static UIElement Note(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(2, 0, 2, 12) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.FgDim");
        return t;
    }

    private static StackPanel Page(params UIElement[] children)
    {
        var p = new StackPanel();
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    private static UIElement RadioRow(string group, string title, string? subtitle, bool isChecked, Action selected)
    {
        var rb = new RadioButton { IsChecked = isChecked, GroupName = group, Focusable = false };
        rb.Checked += (_, _) => selected();
        var row = RowShell(title, subtitle, null, rb);
        row.Background = Brushes.Transparent;
        row.Cursor = Cursors.Hand;
        row.MouseLeftButtonUp += (_, _) => rb.IsChecked = true;
        return row;
    }

    private UIElement BuildWifi()
    {
        _status?.EnsureDetails();
        var s = _status;
        string current = s == null ? "-" : s.Network == NetworkKind.Wifi ? (string.IsNullOrEmpty(s.Ssid) ? L("Connecté", "Connected") : s.Ssid!) : L("Non connecté en Wi-Fi", "Not connected to Wi-Fi");
        var rows = new List<UIElement>();
        if (s?.WifiOn is bool on) rows.Add(SwitchRowAction("Wi-Fi", null, on, v => _ = s.SetRadioAsync(true, v)));
        rows.Add(InfoRow(L("Réseau actuel", "Current network"), current));
        return Page(
            Group(null, null, rows.ToArray()),
            Group(L("Réseaux visibles", "Visible Networks"), null,
                WinLink(L("Se connecter à un réseau", "Connect to a network"), "ms-settings:network-wifi"),
                WinLink(L("Gérer les réseaux connus", "Manage known networks"), "ms-settings:network-wifisettings"),
                WinLink(L("Point d'accès mobile", "Mobile hotspot"), "ms-settings:network-mobilehotspot")));
    }

    private UIElement BuildNetwork()
    {
        var s = _status;
        string kind = s == null ? "-" : s.Network switch
        {
            NetworkKind.Wifi => "Wi-Fi",
            NetworkKind.Ethernet => L("Filaire, connecté", "Wired, connected"),
            NetworkKind.Other => L("Connecté", "Connected"),
            _ => L("Déconnecté", "Disconnected"),
        };
        return Page(
            Group(L("Filaire", "Wired"), null, InfoRow(L("État", "Status"), kind), WinLink("Ethernet", "ms-settings:network-ethernet")),
            Group("VPN", null, WinLink(L("Ajouter une connexion VPN", "Add VPN connection"), "ms-settings:network-vpn")),
            Group(L("Mandataire réseau", "Network Proxy"), null, WinLink(L("Mandataire", "Proxy"), "ms-settings:network-proxy")),
            Group(L("Avancé", "Advanced"), null, WinLink(L("Paramètres réseau avancés", "Advanced network settings"), "ms-settings:network-advancedsettings")));
    }

    private UIElement BuildBluetooth()
    {
        _status?.EnsureDetails();
        var rows = new List<UIElement>();
        if (_status?.BluetoothOn is bool on) rows.Add(SwitchRowAction("Bluetooth", null, on, v => _ = _status.SetRadioAsync(false, v)));
        rows.Add(WinLink(L("Appareils", "Devices"), "ms-settings:bluetooth", L("Associer et gérer les appareils", "Pair and manage devices")));
        return Page(Group(null, null, rows.ToArray()));
    }

    private UIElement BuildDisplays()
    {
        var d = _settings.Current.Displays;
        var g = _settings.Current.General;
        var monitors = new MonitorManager().Monitors;
        var screenRows = new List<UIElement>();
        for (int i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            string label = monitors.Count > 1 ? $"{i + 1}. " : string.Empty;
            screenRows.Add(LinkRow(label + (m.IsPrimary ? L("Écran principal", "Primary display") : L("Écran", "Display")),
                $"{m.Bounds.Width} × {m.Bounds.Height} · {L("Échelle", "Scale")} {Math.Round(m.Scale * 100)} %",
                () => WindowsSettings.Open("ms-settings:display")));
        }
        _status?.EnsureDetails();
        if (_status?.Brightness is int bright)
        {
            var slider = new Slider { Minimum = 0, Maximum = 100, Width = 240, Value = bright };
            slider.SetResourceReference(StyleProperty, "ShellSlider");
            slider.ValueChanged += (_, e) => _status.SetBrightness((int)e.NewValue);
            screenRows.Add(RowShell(L("Luminosité", "Brightness"), null, slider));
        }
        screenRows.Add(WinLink(L("Éclairage nocturne", "Night Light"), "ms-settings:nightlight"));
        return Page(
            Group(L("Écrans", "Displays"), L("Résolution, échelle et orientation se règlent dans les Paramètres Windows.", "Resolution, scale and orientation are set in Windows Settings."), screenRows.ToArray()),
            Group(L("Bureau", "Desktop"), null,
                SwitchRow(L("Barre supérieure", "Top bar"), null, g, nameof(g.ShowTopBar)),
                ComboRow(L("Barre supérieure sur", "Top bar on"), null, d, nameof(d.TopBarMonitors), (MonitorPlacement.All, "Tous les écrans", "All displays"), (MonitorPlacement.Primary, "Écran principal", "Primary display")),
                ComboRow(L("Vue d'ensemble sur", "Overview on"), null, d, nameof(d.OverviewMonitors), (MonitorPlacement.All, "Tous les écrans", "All displays"), (MonitorPlacement.Primary, "Écran principal", "Primary display")),
                ComboRow(L("Dock sur", "Dock on"), null, d, nameof(d.DockMonitors), (MonitorPlacement.Primary, "Écran principal", "Primary display"), (MonitorPlacement.All, "Tous les écrans", "All displays"), (MonitorPlacement.Active, "Écran actif", "Active display"))),
            Group(L("Applications en plein écran", "Fullscreen applications"), L("Jeux, vidéos et présentations.", "Games, videos and presentations."),
                SwitchRow(L("Masquer le dock", "Hide the dock"), null, d, nameof(d.HideDockInFullscreen)),
                SwitchRow(L("Masquer la barre supérieure", "Hide the top bar"), null, d, nameof(d.HideTopBarInFullscreen)),
                SwitchRow(L("Désactiver les raccourcis", "Disable shortcuts"), L("La touche Super retrouve son comportement Windows.", "The Super key keeps its Windows behaviour."), d, nameof(d.DisableShortcutsInFullscreen))));
    }

    private UIElement BuildSound()
    {
        var rows = new List<UIElement>();
        if (_status is { AudioAvailable: true } s)
        {
            var slider = new Slider { Minimum = 0, Maximum = 100, Width = 240, Value = Math.Round(s.Volume * 100) };
            slider.SetResourceReference(StyleProperty, "ShellSlider");
            slider.ValueChanged += (_, e) => s.Volume = (float)(e.NewValue / 100);
            rows.Add(RowShell(L("Volume du système", "System Volume"), null, slider));
            rows.Add(SwitchRowAction(L("Couper le son", "Mute"), null, s.Muted, v => s.Muted = v));
        }
        rows.Insert(0, LinkRow(L("Périphérique de sortie", "Output Device"), _status?.AudioDeviceName ?? L("Aucun", "None"), () => WindowsSettings.Open("ms-settings:sound")));
        return Page(
            Group(L("Sortie", "Output"), null, rows.ToArray()),
            Group(L("Entrée", "Input"), null, WinLink(L("Périphérique d'entrée", "Input device"), "ms-settings:sound")),
            Group(L("Sons", "Sounds"), null, WinLink(L("Mélangeur de volume", "Volume mixer"), "ms-settings:apps-volume")));
    }

    private UIElement BuildPower()
    {
        var s = _status;
        var rows = new List<UIElement>();
        if (s is { HasBattery: true })
            rows.Add(InfoRow(L("Batterie", "Battery"), $"{s.BatteryPercent} %" + (s.Charging ? " — " + L("en charge", "charging") : string.Empty)));
        var available = WindowsSettings.AvailablePowerSchemes();
        var modes = new (Guid Id, string Fr, string En, string Desc)[]
        {
            (WindowsSettings.SchemePerformance, "Performances", "Performance", L("Hautes performances et consommation électrique élevée.", "High performance and power usage.")),
            (WindowsSettings.SchemeBalanced, "Équilibré", "Balanced", L("Performances et consommation électrique standard.", "Standard performance and power usage.")),
            (WindowsSettings.SchemePowerSaver, "Économie d'énergie", "Power Saver", L("Performances et consommation électrique réduites.", "Reduced performance and power usage.")),
        };
        Guid active = WindowsSettings.ActivePowerScheme;
        var modeRows = modes.Where(m => available.Contains(m.Id)).Select(m =>
            RadioRow("power", L(m.Fr, m.En), m.Desc, m.Id == active, () => WindowsSettings.SetPowerScheme(m.Id))).ToList();

        var delays = new (int Minutes, string Label)[] { (1, "1 min"), (2, "2 min"), (3, "3 min"), (5, "5 min"), (10, "10 min"), (15, "15 min"), (30, "30 min"), (60, "1 h"), (0, L("Jamais", "Never")) };
        UIElement DelayRow(string title, string? subtitle, int current, Action<int> set)
        {
            var c = new ComboBox { MinWidth = 150 };
            foreach (var d in delays) c.Items.Add(new ComboBoxItem { Content = d.Label, Tag = d.Minutes });
            int idx = Array.FindIndex(delays, d => d.Minutes == current);
            if (idx < 0) { c.Items.Add(new ComboBoxItem { Content = $"{current} min", Tag = current }); idx = c.Items.Count - 1; }
            c.SelectedIndex = idx;
            c.SelectionChanged += (_, _) => { if (c.SelectedItem is ComboBoxItem it) set((int)it.Tag); };
            return RowShell(title, subtitle, c);
        }

        var page = Page(Group(null, null, rows.ToArray()));
        if (s is { HasBattery: true })
        {
            var g = _settings.Current.General;
            page.Children.Add(Group(L("Général", "General"), null,
                SwitchRow(L("Afficher l'icône de batterie", "Show Battery Icon"), L("Dans la barre supérieure.", "In the top bar."), g, nameof(g.ShowBatteryIcon)),
                SwitchRow(L("Afficher le pourcentage de batterie", "Show Battery Percentage"), L("À côté de l'icône de batterie.", "Next to the battery icon."), g, nameof(g.ShowBatteryPercentage))));
        }
        if (modeRows.Count > 1) page.Children.Add(Group(L("Mode d'alimentation", "Power Mode"), null, modeRows.ToArray()));
        page.Children.Add(Group(L("Économie d'énergie", "Power Saving"), null,
            DelayRow(L("Écran vide", "Screen Blank"), L("Délai avant que l'écran ne s'éteigne.", "Turns the screen off after a period of inactivity."), WindowsSettings.ScreenBlankMinutes, v => WindowsSettings.ScreenBlankMinutes = v),
            DelayRow(L("Mise en veille automatique", "Automatic Suspend"), L("Délai avant la mise en veille.", "Pauses the computer after a period of inactivity."), WindowsSettings.SuspendMinutes, v => WindowsSettings.SuspendMinutes = v),
            WinLink(L("Économiseur de batterie", "Battery saver"), "ms-settings:batterysaver")));
        return page;
    }

    private UIElement BuildMultitasking()
    {
        var o = _settings.Current.Overview;
        var w = _settings.Current.Workspaces;
        return Page(
            Group(L("Général", "General"), null,
                SwitchRow(L("Coin actif", "Hot Corner"), L("Toucher le coin supérieur gauche pour ouvrir la vue d'ensemble des activités.", "Touch the top-left corner to open the Activities Overview."), o, nameof(o.HotCorner))),
            Group(L("Espaces de travail", "Workspaces"), null,
                RadioRow("ws", L("Espaces de travail dynamiques", "Dynamic workspaces"), L("Les espaces vides sont supprimés automatiquement.", "Automatically removes empty workspaces."), w.Dynamic, () => w.Dynamic = true),
                RadioRow("ws", L("Nombre fixe d'espaces de travail", "Fixed number of workspaces"), L("Indiquez le nombre d'espaces de travail.", "Specify a number of permanent workspaces."), !w.Dynamic, () => w.Dynamic = false),
                SliderRow(L("Nombre d'espaces de travail", "Number of Workspaces"), null, w, nameof(w.InitialCount), 1, 16, 1, v => $"{v:0}"),
                SwitchRow(L("Indicateur lors du changement", "Show switch indicator"), null, w, nameof(w.ShowSwitchOsd)),
                SwitchRow(L("Boucler à la fin", "Wrap around"), null, w, nameof(w.WrapAround))),
            Group(L("Changement d'application", "App Switching"), null,
                RadioRow("switch", L("Inclure les applications de tous les espaces de travail", "Include apps from all workspaces"), null, !w.SwitcherCurrentWorkspaceOnly, () => w.SwitcherCurrentWorkspaceOnly = false),
                RadioRow("switch", L("Inclure uniquement les applications de l'espace de travail actuel", "Include apps from the current workspace only"), null, w.SwitcherCurrentWorkspaceOnly, () => w.SwitcherCurrentWorkspaceOnly = true)),
            Note(L("Les espaces de travail sont les bureaux virtuels de Windows : la Vue des tâches et Ctrl+Win+Flèches restent cohérents.",
                   "Workspaces are Windows virtual desktops: Task View and Ctrl+Win+Arrows stay consistent.")));
    }

    private UIElement BuildAppearance()
    {
        var g = _settings.Current.General;
        var o = _settings.Current.Overview;

        var styles = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(8, 14, 8, 6) };
        foreach (var (st, name) in new[] { (DesignStyle.Gnome, "GNOME"), (DesignStyle.Ubuntu, "Ubuntu"), (DesignStyle.PopOS, "Pop!_OS") })
            styles.Children.Add(StyleCard(st, name, g.Style == st, () =>
            {
                g.Style = st;
                StylePresets.ApplyLayout(st, _settings.Current);
                ShowPanelContent("appearance");
            }));

        bool windowsDark = WindowsSettings.WindowsDarkMode;
        var modes = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(8, 14, 8, 6) };
        foreach (var (m, fr, en) in new[] { (ThemeMode.Light, "Défaut", "Default"), (ThemeMode.Dark, "Sombre", "Dark") })
        {
            bool selected = g.Theme == ThemeMode.System ? (m == ThemeMode.Dark) == windowsDark : g.Theme == m;
            modes.Children.Add(ModeCard(m, L(fr, en), selected, () =>
            {
                WindowsSettings.WindowsDarkMode = m == ThemeMode.Dark;
                g.Theme = ThemeMode.System;
                ThemeManager.Apply(ThemeMode.System);
                ShowPanelContent("appearance");
            }));
        }

        var accents = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(12, 4, 12, 16) };
        var list = g.Style == DesignStyle.Ubuntu
            ? new[] { AccentColor.Default, AccentColor.Bark, AccentColor.Sage, AccentColor.Olive, AccentColor.Viridian, AccentColor.PrussianGreen, AccentColor.Blue, AccentColor.Purple, AccentColor.Magenta, AccentColor.Red }
            : new[] { AccentColor.Default, AccentColor.Teal, AccentColor.Green, AccentColor.Yellow, AccentColor.Orange, AccentColor.Red, AccentColor.Pink, AccentColor.Purple, AccentColor.Slate };
        foreach (var a in list)
            accents.Children.Add(AccentDot(a, g.Accent == a, () => { g.Accent = a; ShowPanelContent("appearance"); }));

        var styleCard = new StackPanel();
        styleCard.Children.Add(modes);
        styleCard.Children.Add(accents);

        return Page(
            GroupBox(L("Style du bureau", "Desktop Style"), styles),
            GroupBox(L("Style", "Style"), styleCard),
            BuildBackgroundGallery(),
            Group(null, null,
                SwitchRowAction(L("Effets de transparence", "Transparency effects"), L("Paramètre Windows", "Windows setting"), WindowsSettings.TransparencyEffects, v => WindowsSettings.TransparencyEffects = v),
                ComboRow(L("Arrière-plan de la vue d'ensemble", "Overview background"), null, o, nameof(o.Background),
                    (OverviewBackground.BlurredWallpaper, "Fond d'écran flouté", "Blurred wallpaper"), (OverviewBackground.Wallpaper, "Fond d'écran assombri", "Dimmed wallpaper"), (OverviewBackground.Solid, "Couleur unie", "Solid color"))));
    }

    private UIElement BuildBackgroundGallery()
    {
        string? current = Services.Wallpaper.WallpaperProvider.CurrentWallpaperPath();
        var grid = new WrapPanel { Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Center };
        var files = Services.Wallpaper.WallpaperProvider.BuiltInWallpapers().ToList();
        if (current != null && !files.Contains(current, StringComparer.OrdinalIgnoreCase)) files.Insert(0, current);
        foreach (var file in files)
        {
            var img = new Image { Source = Services.Wallpaper.WallpaperProvider.Thumbnail(file), Width = 140, Height = 88, Stretch = Stretch.UniformToFill };
            bool selected = string.Equals(file, current, StringComparison.OrdinalIgnoreCase);
            var frame = new Border
            {
                CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(3), Padding = new Thickness(2), Margin = new Thickness(5),
                Child = new Border { CornerRadius = new CornerRadius(7), ClipToBounds = true, Child = img }, Cursor = Cursors.Hand,
                BorderBrush = Brushes.Transparent, ToolTip = Path.GetFileNameWithoutExtension(file),
            };
            if (selected) frame.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
            string path = file;
            frame.MouseLeftButtonUp += (_, _) =>
            {
                if (Services.Wallpaper.WallpaperProvider.SetWindowsWallpaper(path)) ShowPanelContent("appearance");
            };
            grid.Children.Add(frame);
        }
        var add = new Button { Content = "+ " + L("Ajouter une image…", "Add Picture…") };
        add.SetResourceReference(StyleProperty, "FlatButton");
        add.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.webp" };
            if (dlg.ShowDialog(this) == true && Services.Wallpaper.WallpaperProvider.SetWindowsWallpaper(dlg.FileName)) ShowPanelContent("appearance");
        };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(add, System.Windows.Controls.Dock.Right);
        header.Children.Add(add);
        header.Children.Add(new TextBlock { Text = L("Arrière-plan", "Background"), FontWeight = FontWeights.Bold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        var card = new Border { CornerRadius = new CornerRadius(12), Child = grid };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.CardBg");
        var p = new StackPanel { Margin = new Thickness(0, 12, 0, 12) };
        p.Children.Add(header);
        p.Children.Add(card);
        return p;
    }

    private static UIElement Thumbnail(Brush background, Brush back, Brush front, Brush? accent, bool selected, string label, Action click)
    {
        var canvas = new Canvas { Width = 148, Height = 98, ClipToBounds = true };
        canvas.Children.Add(new WpfRectangle { Width = 148, Height = 98, Fill = background });
        var w1 = new Border { Width = 70, Height = 46, CornerRadius = new CornerRadius(5), Background = back };
        Canvas.SetLeft(w1, 58); Canvas.SetTop(w1, 14);
        var w2 = new Border { Width = 74, Height = 48, CornerRadius = new CornerRadius(5), Background = front, BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), BorderThickness = new Thickness(1) };
        Canvas.SetLeft(w2, 20); Canvas.SetTop(w2, 38);
        canvas.Children.Add(w1);
        canvas.Children.Add(w2);
        if (accent != null)
        {
            var dot = new Border { Width = 22, Height = 8, CornerRadius = new CornerRadius(4), Background = accent };
            Canvas.SetLeft(dot, 30); Canvas.SetTop(dot, 72);
            canvas.Children.Add(dot);
        }
        var frame = new Border
        {
            CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(3), Padding = new Thickness(3),
            Child = new Border { CornerRadius = new CornerRadius(7), Child = canvas, ClipToBounds = true },
        };
        if (selected) frame.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
        else frame.BorderBrush = Brushes.Transparent;
        var stack = new StackPanel { Margin = new Thickness(10, 0, 10, 6), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        stack.Children.Add(frame);
        stack.Children.Add(new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), FontSize = 13 });
        stack.MouseLeftButtonUp += (_, _) => click();
        return stack;
    }

    private static UIElement StyleCard(DesignStyle st, string name, bool selected, Action click)
    {
        Brush bg = st switch
        {
            DesignStyle.Ubuntu => new LinearGradientBrush(Color.FromRgb(0x77, 0x21, 0x6F), Color.FromRgb(0xE9, 0x54, 0x20), 30),
            DesignStyle.PopOS => new LinearGradientBrush(Color.FromRgb(0x2A, 0x29, 0x28), Color.FromRgb(0x48, 0xB9, 0xC7), 30),
            _ => new LinearGradientBrush(Color.FromRgb(0x1C, 0x71, 0xD8), Color.FromRgb(0x62, 0xA0, 0xEA), 30),
        };
        var accent = new SolidColorBrush(ThemeManager.AccentValue(st switch { DesignStyle.Ubuntu => AccentColor.Orange, DesignStyle.PopOS => AccentColor.Teal, _ => AccentColor.Blue }));
        return Thumbnail(bg, new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)), new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)), accent, selected, name, click);
    }

    private static UIElement ModeCard(ThemeMode mode, string label, bool selected, Action click)
    {
        var bg = new LinearGradientBrush(Color.FromRgb(0x2A, 0x5D, 0xB0), Color.FromRgb(0x63, 0x8C, 0xD8), 45);
        Brush back = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E));
        Brush front = mode switch
        {
            ThemeMode.Dark => new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24)),
            ThemeMode.Light => new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            _ => new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(0xFA, 0xFA, 0xFA), 0), new GradientStop(Color.FromRgb(0xFA, 0xFA, 0xFA), 0.5),
                new GradientStop(Color.FromRgb(0x24, 0x24, 0x24), 0.5), new GradientStop(Color.FromRgb(0x24, 0x24, 0x24), 1),
            }, 0),
        };
        return Thumbnail(bg, back, front, null, selected, label, click);
    }

    private static UIElement AccentDot(AccentColor a, bool selected, Action click)
    {
        Color c = a == AccentColor.Default
            ? ThemeManager.AccentValue(ThemeManager.Style switch { DesignStyle.Ubuntu => AccentColor.Orange, DesignStyle.PopOS => AccentColor.Teal, _ => AccentColor.Blue })
            : ThemeManager.AccentValue(a);
        var grid = new Grid { Width = 34, Height = 34, Margin = new Thickness(5), Cursor = Cursors.Hand, Background = Brushes.Transparent,
            ToolTip = a == AccentColor.Default ? L("Couleur du style", "Style colour") : a.ToString() };
        grid.Children.Add(new WpfEllipse { StrokeThickness = 2, Stroke = selected ? new SolidColorBrush(c) : Brushes.Transparent });
        grid.Children.Add(new WpfEllipse { Margin = new Thickness(selected ? 4 : 2), Fill = new SolidColorBrush(c) });
        if (selected)
        {
            var check = new TextBlock { Text = "", Foreground = Brushes.White, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            check.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Icons");
            grid.Children.Add(check);
        }
        grid.MouseLeftButtonUp += (_, _) => click();
        return grid;
    }

    private UIElement BuildDock()
    {
        var d = _settings.Current.Dock;
        return Page(
            Group(L("Dock", "Dock"), null,
                ComboRow(L("Visibilité", "Visibility"), null, d, nameof(d.Visibility),
                    (DockVisibility.OverviewOnly, "Vue d'ensemble uniquement (GNOME)", "Overview only (GNOME)"),
                    (DockVisibility.AlwaysVisible, "Toujours visible", "Always visible"),
                    (DockVisibility.Intellihide, "Masquage intelligent", "Intellihide"),
                    (DockVisibility.AutoHide, "Masquage automatique", "Auto-hide")),
                SwitchRow(L("Mode panneau", "Panel mode"), L("Le dock s'étend jusqu'aux bords de l'écran.", "The dock extends to the screen edge."), d, nameof(d.Extended)),
                SwitchRow(L("Centrer les icônes", "Center icons"), L("En mode panneau, les applications sont centrées sur le bord de l'écran.", "In panel mode, apps are centred along the screen edge."), d, nameof(d.CenterIcons)),
                SliderRow(L("Taille des icônes", "Icon size"), null, d, nameof(d.IconSize), 24, 64, 2, v => $"{v:0}"),
                DockPositionRow(d)),
            Group(L("Comportement", "Behavior"), null,
                ComboRow(L("Clic sur l'application active", "Click on the focused app"), null, d, nameof(d.ActiveClick),
                    (DockActiveClick.Minimize, "Réduire", "Minimize"), (DockActiveClick.CycleWindows, "Fenêtre suivante", "Cycle windows"), (DockActiveClick.ShowPreviews, "Afficher les aperçus", "Show previews")),
                ComboRow(L("Effet au survol", "Hover effect"), null, d, nameof(d.HoverEffect),
                    (DockHoverEffect.Highlight, "Surbrillance", "Highlight"), (DockHoverEffect.Zoom, "Zoom", "Zoom"), (DockHoverEffect.None, "Aucun", "None")),
                SwitchRow(L("Isoler les espaces de travail", "Isolate workspaces"), L("N'indiquer comme ouvertes que les applications de l'espace actuel.", "Only show apps running on the current workspace."), d, nameof(d.IsolateWorkspaces)),
                SwitchRow(L("Bouton Afficher les applications", "Show Applications button"), null, d, nameof(d.ShowAppsButton)),
                SliderRow(L("Opacité", "Opacity"), null, d, nameof(d.BackgroundOpacity), 0, 1, 0.05, v => $"{v * 100:0} %")));
    }

    private UIElement BuildApps()
    {
        var rows = new List<UIElement>();
        if (_apps != null)
        {
            foreach (var id in _apps.PinnedIds.ToList())
            {
                var app = _apps.FindById(id);
                if (app == null) continue;
                var remove = new Button { Content = "", Width = 28, Height = 28, FontSize = 9, ToolTip = Loc.T("Unpin") };
                remove.SetResourceReference(StyleProperty, "RoundIconButton");
                remove.Click += (_, _) => { _apps.Unpin(id); ShowPanelContent("apps"); };
                var icon = new Image { Source = app.Icon, Width = 28, Height = 28 };
                rows.Add(RowShell(app.Name, null, remove, icon));
            }
        }
        var page = Page(
            Group(L("Applications", "Apps"), null,
                WinLink(L("Applications par défaut", "Default Apps"), "ms-settings:defaultapps"),
                WinLink(L("Applications au démarrage", "Startup Apps"), "ms-settings:startupapps"),
                WinLink(L("Applications installées", "Installed Apps"), "ms-settings:appsfeatures")));
        page.Children.Add(rows.Count > 0
            ? Group(L("Favoris", "Favorites"), L("Applications épinglées au dock. Épinglez une application par clic droit › « Épingler au dock » ou en la glissant de la grille vers le dock ; glissez-la hors du dock pour la retirer.", "Apps pinned to the dock. Pin with right-click › Pin to Dock or by dragging from the grid; drag out of the dock to remove."), rows.ToArray())
            : Note(L("Aucune application épinglée. Clic droit sur une application du dock ou de la grille › « Épingler au dock ».", "No pinned apps. Right-click an app in the dock or the grid › Pin to Dock.")));
        return page;
    }

    private UIElement BuildNotifications() => Page(
        Group(null, null,
            WinLink(L("Ne pas déranger", "Do Not Disturb"), "ms-settings:notifications"),
            WinLink(L("Notifications de l'écran de verrouillage", "Lock Screen Notifications"), "ms-settings:notifications")),
        Group(L("Applications", "Apps"), L("Les notifications sont gérées par Windows ; GnomeWin les affiche dans le panneau du calendrier.", "Notifications are managed by Windows; GnomeWin shows them in the calendar panel."),
            WinLink(L("Notifications par application", "Notifications per app"), "ms-settings:notifications"),
            WinLink(L("Autoriser l'accès aux notifications", "Allow notification access"), "ms-settings:privacy-notifications")));

    private UIElement BuildSearch()
    {
        var o = _settings.Current.Overview;
        return Page(
            Note(L("Les résultats de recherche apparaissent quand vous tapez dans la vue d'ensemble.", "Search results appear when typing in the Activities Overview.")),
            Group(L("Résultats de recherche", "Search Results"), null,
                InfoRow(L("Applications", "Apps"), L("Toujours", "Always")),
                InfoRow(L("Fenêtres ouvertes", "Open windows"), L("Toujours", "Always")),
                SwitchRow(L("Paramètres", "Settings"), L("Pages des Paramètres Windows", "Windows Settings pages"), o, nameof(o.SearchWindowsSettings)),
                SwitchRow(L("Fichiers récents", "Recent files"), null, o, nameof(o.SearchRecentFiles)),
                SwitchRow(L("Calculatrice", "Calculator"), null, o, nameof(o.SearchCalculator))));
    }

    private static UIElement LiveSlider(string title, string? subtitle, double min, double max, double tick, double value, Action<int> apply, string left, string right)
    {
        var s = new Slider { Minimum = min, Maximum = max, Width = 200, TickFrequency = tick, IsSnapToTickEnabled = true, Value = value };
        s.SetResourceReference(StyleProperty, "ShellSlider");
        s.ValueChanged += (_, e) => apply((int)Math.Round(e.NewValue));
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        TextBlock Cap(string t) { var x = new TextBlock { Text = t, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) }; x.SetResourceReference(TextBlock.ForegroundProperty, "Brush.FgDim"); return x; }
        panel.Children.Add(Cap(left));
        panel.Children.Add(s);
        panel.Children.Add(Cap(right));
        return RowShell(title, subtitle, panel);
    }

    private UIElement BuildMouse()
    {
        bool right = WindowsSettings.PrimaryButtonIsRight;
        return Page(
            Group(L("Général", "General"), null,
                RadioRow("primary", L("Bouton principal : gauche", "Primary Button: Left"), null, !right, () => WindowsSettings.PrimaryButtonIsRight = false),
                RadioRow("primary", L("Bouton principal : droit", "Primary Button: Right"), L("Pour les gauchers.", "For left-handed use."), right, () => WindowsSettings.PrimaryButtonIsRight = true)),
            Group(L("Souris", "Mouse"), null,
                LiveSlider(L("Vitesse du pointeur", "Pointer Speed"), null, 1, 20, 1, WindowsSettings.MouseSpeed, v => WindowsSettings.MouseSpeed = v, L("Lent", "Slow"), L("Rapide", "Fast")),
                LiveSlider(L("Délai du double-clic", "Double-Click Delay"), null, 200, 900, 50, WindowsSettings.DoubleClickTime, v => WindowsSettings.DoubleClickTime = v, L("Court", "Short"), L("Long", "Long")),
                LiveSlider(L("Défilement", "Scroll Speed"), L("Lignes par cran de molette", "Lines per wheel notch"), 1, 20, 1, WindowsSettings.WheelScrollLines, v => WindowsSettings.WheelScrollLines = v, "1", "20")),
            Group(L("Pavé tactile", "Touchpad"), null, WinLink(L("Gestes et sensibilité", "Gestures and sensitivity"), "ms-settings:devices-touchpad")));
    }

    private UIElement BuildPrinters() => Page(Group(null, null, WinLink(L("Imprimantes et scanners", "Printers & scanners"), "ms-settings:printers")));

    private UIElement BuildPrivacy() => Page(Group(null, null,
        WinLink(L("Confidentialité et sécurité", "Privacy & Security"), "ms-settings:privacy"),
        WinLink(L("Sécurité Windows", "Windows Security"), "windowsdefender:"),
        WinLink(L("Localisation", "Location"), "ms-settings:privacy-location")));

    private UIElement BuildAccessibility()
    {
        var g = _settings.Current.General;
        return Page(
            Group(L("Vision", "Seeing"), null,
                SwitchRowAction(L("Effets d'animation", "Animation Effects"), L("Shell et Windows. Les effets peuvent gêner ou ralentir les machines modestes.", "Shell and Windows. Effects can be distracting or slow on low-end machines."),
                    g.AnimationsEnabled, v => { g.AnimationsEnabled = v; WindowsSettings.ClientAreaAnimations = v; }),
                ComboRow(L("Vitesse des animations", "Animation Speed"), null, g, nameof(g.AnimationSpeed), (AnimationSpeed.Normal, "Normale", "Normal"), (AnimationSpeed.Fast, "Rapide", "Fast"), (AnimationSpeed.Slow, "Lente", "Slow")),
                WinLink(L("Contraste élevé et taille du texte", "High contrast and text size"), "ms-settings:easeofaccess-display")),
            Group(L("Autres", "Other"), null,
                WinLink(L("Narrateur", "Narrator"), "ms-settings:easeofaccess-narrator"),
                WinLink(L("Loupe", "Magnifier"), "ms-settings:easeofaccess-magnifier")));
    }

    private UIElement BuildKeyboard()
    {
        var k = _settings.Current.Keyboard;
        var names = new (string Key, string Fr, string En, bool System)[]
        {
            (nameof(ShellAction.ToggleOverview), "Afficher la vue d'ensemble", "Show the overview", true),
            (nameof(ShellAction.ShowApplications), "Afficher toutes les applications", "Show all apps", true),
            (nameof(ShellAction.ToggleNotifications), "Afficher la liste des notifications", "Show the notification list", true),
            (nameof(ShellAction.ToggleQuickSettings), "Ouvrir le menu système", "Open the quick settings menu", true),
            (nameof(ShellAction.OpenShellSettings), "Ouvrir les paramètres", "Open settings", true),
            (nameof(ShellAction.OpenTerminal), "Lancer le terminal", "Launch terminal", true),
            (nameof(ShellAction.AppSwitcher), "Changer d'application", "Switch applications", false),
            (nameof(ShellAction.WorkspacePrevious), "Aller à l'espace de travail précédent", "Switch to workspace on the left", false),
            (nameof(ShellAction.WorkspaceNext), "Aller à l'espace de travail suivant", "Switch to workspace on the right", false),
            (nameof(ShellAction.MoveWindowToPreviousWorkspace), "Déplacer la fenêtre d'un espace vers la gauche", "Move window one workspace to the left", false),
            (nameof(ShellAction.MoveWindowToNextWorkspace), "Déplacer la fenêtre d'un espace vers la droite", "Move window one workspace to the right", false),
        };
        var page = Page(Group(L("Saisie", "Input"), null,
            SwitchRow(L("Touche Super", "Super key"), L("Ouvre la vue d'ensemble. Désactivé : aucun hook clavier, la touche Windows garde son comportement.", "Opens the overview. Off: no keyboard hook, the Windows key keeps its behaviour."), k, nameof(k.InterceptSuperKey)),
            WinLink(L("Disposition du clavier", "Keyboard layout"), "ms-settings:regionlanguage")));
        page.Children.Add(Group(L("Répétition des touches", "Repeat Keys"), L("Paramètres Windows : s'applique à toutes les applications.", "Windows settings: applies to every application."),
            LiveSlider(L("Délai", "Delay"), null, 0, 3, 1, 3 - WindowsSettings.KeyboardDelay, v => WindowsSettings.KeyboardDelay = 3 - v, L("Long", "Long"), L("Court", "Short")),
            LiveSlider(L("Vitesse", "Speed"), null, 0, 31, 1, WindowsSettings.KeyboardSpeed, v => WindowsSettings.KeyboardSpeed = v, L("Lent", "Slow"), L("Rapide", "Fast"))));
        page.Children.Add(Group(L("Système", "System"), null, names.Where(n => n.System).Select(n => ShortcutRow(k, n.Key, L(n.Fr, n.En))).ToArray()));
        page.Children.Add(Group(L("Navigation", "Navigation"), null, names.Where(n => !n.System).Select(n => ShortcutRow(k, n.Key, L(n.Fr, n.En))).ToArray()));
        page.Children.Add(Group(L("Lanceurs", "Launchers"), null, Enumerable.Range(1, 9).Select(i => ShortcutRow(k, "LaunchDockItem" + i, L($"Lancer l'application {i} du dock", $"Launch dock app {i}"))).ToArray()));
        page.Children.Add(Note(L("Cliquez sur un raccourci puis tapez la nouvelle combinaison (Échap : annuler, Retour arrière : désactiver, clic droit : rétablir). Urgence : Ctrl+Alt+Maj+F12 restaure Windows.",
                                 "Click a shortcut then type the new combination (Esc: cancel, Backspace: disable, right-click: reset). Emergency: Ctrl+Alt+Shift+F12 restores Windows.")));
        return page;
    }

    private UIElement ShortcutRow(KeyboardSettings k, string key, string title)
    {
        var keys = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        void Render(string? waiting = null)
        {
            keys.Children.Clear();
            if (waiting != null) { keys.Children.Add(new TextBlock { Text = waiting, FontStyle = FontStyles.Italic }); return; }
            var list = k.Bindings.TryGetValue(key, out var l) ? l : new List<string>();
            if (list.Count == 0)
            {
                var off = new TextBlock { Text = L("Désactivé", "Disabled") };
                off.SetResourceReference(TextBlock.ForegroundProperty, "Brush.FgDim");
                keys.Children.Add(off);
                return;
            }
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) keys.Children.Add(new TextBlock { Text = "/", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
                foreach (var part in list[i].Split('+')) keys.Children.Add(Keycap(part));
            }
        }
        void SetBinding(List<string> value)
        {
            var dict = new Dictionary<string, List<string>>(k.Bindings, StringComparer.OrdinalIgnoreCase) { [key] = value };
            k.Bindings = dict;
            Render();
        }
        Render();
        var row = RowShell(title, null, keys);
        MakeActivatable(row, () =>
        {
            if (_hook == null || !_hook.IsRunning)
            {
                MessageBox.Show(this, L("Le hook clavier est inactif (mode sans échec ou touche Super désactivée).", "The keyboard hook is not running (safe mode or Super key disabled)."), Title);
                return;
            }
            Render(L("Nouveau raccourci…", "New shortcut…"));
            _hook.BeginCapture(hk => Dispatcher.BeginInvoke(() =>
            {
                if (hk is Hotkey h && h.IsValid)
                    SetBinding(h.Key == 0x08 && h.Modifiers == HotkeyModifiers.None ? new List<string>() : new List<string> { h.ToString() });
                else Render();
            }));
        });
        var menu = new ContextMenu();
        var reset = new MenuItem { Header = L("Rétablir le raccourci par défaut", "Reset to default") };
        reset.Click += (_, _) => SetBinding(KeyboardSettings.DefaultBindings()[key]);
        var clear = new MenuItem { Header = L("Désactiver", "Disable") };
        clear.Click += (_, _) => SetBinding(new List<string>());
        menu.Items.Add(reset);
        menu.Items.Add(clear);
        row.ContextMenu = menu;
        return row;
    }

    private static UIElement Keycap(string text)
    {
        var b = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(2, 0, 2, 0), BorderThickness = new Thickness(1, 1, 1, 2) };
        b.SetResourceReference(Border.BorderBrushProperty, "Brush.Separator");
        b.SetResourceReference(Border.BackgroundProperty, "Brush.InputBg");
        b.Child = new TextBlock { Text = text, FontSize = 12.5 };
        return b;
    }

    private UIElement BuildSystem()
    {
        var g = _settings.Current.General;
        string version = FileVersionInfo.GetVersionInfo(AppPaths.ExecutablePath).ProductVersion?.Split('+')[0] ?? "1.0";
        return Page(
            Group(L("Région et langue", "Region & Language"), null,
                ComboRow(L("Langue de GnomeWin", "GnomeWin language"), L("Appliquée entièrement au prochain démarrage.", "Fully applied at next start."), g, nameof(g.Language),
                    (UiLanguage.System, "Système", "System"), (UiLanguage.French, "Français", "French"), (UiLanguage.English, "Anglais", "English")),
                WinLink(L("Langue et région de Windows", "Windows language & region"), "ms-settings:regionlanguage")),
            Group(L("Date et heure", "Date & Time"), null,
                LinkRow(L("Date et heure", "Date & Time"), DateTime.Now.ToString("f", Loc.Culture), () => WindowsSettings.Open("ms-settings:dateandtime")),
                LinkRow(L("Fuseau horaire", "Time Zone"), WindowsSettings.TimeZone(), () => WindowsSettings.Open("ms-settings:dateandtime"))),
            Group(L("Utilisateurs", "Users"), null, WinLink(L("Comptes", "Accounts"), "ms-settings:yourinfo")),
            Group(L("Démarrage", "Startup"), null,
                SwitchRow(L("Lancer GnomeWin à l'ouverture de session", "Launch GnomeWin at login"), null, g, nameof(g.LaunchAtStartup)),
                SwitchRow(L("Remplacer la barre des tâches Windows", "Replace the Windows taskbar"), L("Masquage réversible, toujours restaurée à la fermeture.", "Reversible, always restored on exit."), g, nameof(g.ReplaceTaskbar))),
            Group(L("Performances", "Performance"), null,
                SwitchRow(L("Économie de mémoire", "Memory saver"), L("Rendu sans carte graphique : environ 70 Mo de moins, la vue d'ensemble et les applications s'ouvrent sans animation. Désactivez-le pour les animations fluides de GNOME. Prend effet au prochain démarrage.", "Software rendering: about 70 MB less, the Overview and app grid open without animation. Turn off for GNOME's smooth animations. Applies at next start."), g, nameof(g.LowMemoryMode))),
            Group(L("À propos", "About"), null,
                LinkRow(L("Nom de l'appareil", "Device Name"), Environment.MachineName, () => WindowsSettings.Open("ms-settings:about")),
                InfoRow(L("Système d'exploitation", "Operating System"), WindowsSettings.OsName()),
                InfoRow(L("Processeur", "Processor"), WindowsSettings.Processor()),
                InfoRow(L("Mémoire", "Memory"), WindowsSettings.Memory()),
                InfoRow(L("Capacité du disque", "Disk Capacity"), WindowsSettings.DiskCapacity()),
                InfoRow("GnomeWin", version),
                InfoRow(L("Mémoire utilisée par GnomeWin", "Memory used by GnomeWin"), $"{Environment.WorkingSet / 1048576} Mo"),
                LinkRow(L("Diagnostic", "Diagnostics"), L("Copier le rapport dans le presse-papiers", "Copy the report to the clipboard"), () => { try { Clipboard.SetText(_diagnostics()); } catch { } }, external: false)),
            Group(L("Maintenance", "Maintenance"), null,
                LinkRow(L("Journaux", "Logs"), AppPaths.Logs, () => ShellLauncher.Open(AppPaths.Logs)),
                LinkRow(L("Dossier de configuration", "Settings folder"), AppPaths.Root, () => ShellLauncher.Open(AppPaths.Root)),
                LinkRow(L("Exporter les paramètres…", "Export settings…"), null, ExportSettings, external: false),
                LinkRow(L("Importer des paramètres…", "Import settings…"), null, ImportSettings, external: false),
                ButtonRow(L("Réinitialiser les paramètres", "Reset settings"), L("Une sauvegarde est conservée.", "A backup is kept."), L("Réinitialiser", "Reset"), ResetSettings),
                ButtonRow(L("Quitter GnomeWin", "Quit GnomeWin"), L("Réaffiche la barre des tâches Windows et ferme le shell.", "Shows the Windows taskbar again and closes the shell."), L("Quitter", "Quit"), _restoreAndQuit, destructive: true)));
    }

    private void ExportSettings()
    {
        var dlg = new SaveFileDialog { FileName = "gnomewin-settings.json", Filter = "JSON|*.json" };
        if (dlg.ShowDialog(this) == true) _settings.Export(dlg.FileName);
    }

    private void ImportSettings()
    {
        var dlg = new OpenFileDialog { Filter = "JSON|*.json" };
        if (dlg.ShowDialog(this) != true) return;
        try { _settings.Import(dlg.FileName); ShowPanelContent("system"); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ResetSettings()
    {
        if (MessageBox.Show(this, L("Revenir aux paramètres par défaut ?", "Reset to defaults?"), Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _settings.ResetToDefaults();
        ShowPanelContent("system");
    }
}
