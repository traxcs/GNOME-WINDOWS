using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using GnomeWin.Platform.Win32;
using GnomeWin.Services.Logging;
using GnomeWin.UI;
using GnomeWin.UI.Themes;

namespace GnomeWin.Terminal;

public sealed class ConsoleSession : IDisposable
{
    public TerminalBuffer Buffer { get; }
    public TerminalView View { get; }
    private PseudoConsole? _pty;

    public event Action<ConsoleSession>? Exited;
    public event Action<ConsoleSession>? TitleChanged;

    private string _title = string.Empty;
    public string Title => _title;

    public ConsoleSession(string? workingDirectory)
    {
        Buffer = new TerminalBuffer(100, 30);
        View = new TerminalView(Buffer);
        View.Input += s => _pty?.Write(s);
        View.GridResized += (c, r) => _pty?.Resize(c, r);
        Buffer.Reply += s => _pty?.Write(s);

        string cmd = ShellProfile.CommandLine();
        try
        {
            _pty = new PseudoConsole(cmd, workingDirectory, (short)Buffer.Columns, (short)Buffer.Rows);
            _pty.Output += OnOutput;
            _pty.Exited += _ => View.Dispatcher.BeginInvoke(() => Exited?.Invoke(this));
            Log.Info($"Console tab started: {cmd} (pid {_pty.ProcessId})");
        }
        catch (Exception ex)
        {
            Log.Error("Cannot start the console program", ex);
            lock (Buffer.SyncRoot) Buffer.Feed($"\x1b[1;31m{(Loc.IsFrench ? "Impossible de démarrer" : "Cannot start")}: {cmd}\r\n{ex.Message}\x1b[0m\r\n");
        }
    }

    private void OnOutput(string text)
    {
        string title;
        lock (Buffer.SyncRoot)
        {
            Buffer.Feed(text);
            title = Buffer.Title;
        }
        View.RequestRender();
        if (title != _title)
        {
            _title = title;
            View.Dispatcher.BeginInvoke(() => TitleChanged?.Invoke(this));
        }
    }

    public void Dispose()
    {
        _pty?.Dispose();
        _pty = null;
    }
}

public sealed class ConsoleWindow : Window
{
    private readonly List<ConsoleSession> _sessions = new();
    private readonly Grid _host = new();
    private readonly ScrollBar _scroll = new() { Orientation = Orientation.Vertical, Width = 10, HorizontalAlignment = HorizontalAlignment.Right, SmallChange = 1 };
    private readonly UniformGrid _tabs = new() { Rows = 1 };
    private readonly Border _tabBar = new() { Visibility = Visibility.Collapsed, Padding = new Thickness(6, 0, 6, 6) };
    private readonly TextBlock _title = new() { FontWeight = FontWeights.Bold, FontSize = 14.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private ConsoleSession? _active;
    private readonly ConsolePrefs _prefs = ConsolePrefs.Load();
    private double _fontSize = 14;
    private bool _syncingScroll;

    private static string L(string fr, string en) => Loc.IsFrench ? fr : en;

    public ConsoleWindow(string? directory = null)
    {
        _fontSize = Math.Clamp(_prefs.FontSize, 8, 32);
        Title = "Console";
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/GnomeWin;component/Assets/Console.ico"));
        Width = 860;
        Height = 560;
        MinWidth = 360;
        MinHeight = 220;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(ForegroundProperty, "Brush.Fg");
        SetResourceReference(FontFamilyProperty, "Font.Ui");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 46,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
            CornerRadius = new CornerRadius(12),
        });

        Content = BuildLayout();
        ApplyTheme();
        ThemeManager.ThemeChanged += ApplyTheme;
        Closed += (_, _) =>
        {
            ThemeManager.ThemeChanged -= ApplyTheme;
            foreach (var s in _sessions) s.Dispose();
        };
        SourceInitialized += (_, _) =>
        {
            if (UI.Components.ShellWindow.SoftwareRenderingEnabled) UI.Components.ShellWindow.UseSoftwareRendering(this);
            ApplyTitleBar();
        };
        PreviewKeyDown += OnPreviewKeyDown;
        _scroll.Scroll += (_, _) =>
        {
            if (_syncingScroll || _active == null) return;
            _active.View.ScrollTo((int)Math.Round(_scroll.Maximum - _scroll.Value));
        };

        AddTab(directory);
    }


    private UIElement BuildLayout()
    {
        var header = new Grid { Height = 46 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
        left.Children.Add(HeaderButton("", L("Nouvel onglet", "New Tab") + " (Ctrl+Maj+T)", () => AddTab(CurrentDirectory())));
        header.Children.Add(left);

        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _title.Margin = new Thickness(12, 0, 12, 0);
        Grid.SetColumn(_title, 1);
        header.Children.Add(_title);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
        var menu = HeaderButton("", L("Menu", "Main Menu"), () => { });
        menu.Click += (_, _) => ShowMenu(menu);
        right.Children.Add(menu);
        right.Children.Add(WindowButton("", () => WindowState = WindowState.Minimized));
        right.Children.Add(WindowButton("", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized));
        right.Children.Add(WindowButton("", Close));
        Grid.SetColumn(right, 2);
        header.Children.Add(right);

        _tabBar.Child = _tabs;

        var body = new Grid();
        body.Children.Add(_host);
        body.Children.Add(_scroll);

        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_tabBar, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(_tabBar);
        root.Children.Add(body);
        return root;
    }

    private Button HeaderButton(string glyph, string tip, Action click)
    {
        var b = new Button { Content = glyph, ToolTip = tip, Width = 34, Height = 34, FontSize = 14, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(StyleProperty, "ShellButton");
        b.SetResourceReference(FontFamilyProperty, "Font.Icons");
        b.Click += (_, _) => click();
        WindowChrome.SetIsHitTestVisibleInChrome(b, true);
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

    private void ApplyTheme()
    {
        bool dark = ThemeManager.IsDark;
        var palette = dark ? TerminalPalette.Dark : TerminalPalette.Light;
        Background = new SolidColorBrush(palette.Background);
        _tabBar.Background = Background;
        foreach (var s in _sessions) s.View.SetPalette(palette);
        ApplyTitleBar();
        RebuildTabs();
    }

    private void ApplyTitleBar()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        int dark = ThemeManager.IsDark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(h, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int round = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(h, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }


    public void AddTab(string? directory)
    {
        var session = new ConsoleSession(directory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        session.View.SetPalette(ThemeManager.IsDark ? TerminalPalette.Dark : TerminalPalette.Light);
        session.View.SetFont("Cascadia Mono, Consolas, Courier New", _fontSize);
        session.View.Visibility = Visibility.Collapsed;
        session.View.ScrollChanged += () => { if (session == _active) SyncScrollBar(); };
        session.TitleChanged += _ => { RebuildTabs(); UpdateTitle(); };
        session.Exited += CloseSession;
        _sessions.Add(session);
        _host.Children.Add(session.View);
        Activate(session);
    }

    private void Activate(ConsoleSession session)
    {
        _active = session;
        foreach (var s in _sessions) s.View.Visibility = s == session ? Visibility.Visible : Visibility.Collapsed;
        RebuildTabs();
        UpdateTitle();
        SyncScrollBar();
        Dispatcher.BeginInvoke(() => session.View.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CloseSession(ConsoleSession session)
    {
        int index = _sessions.IndexOf(session);
        if (index < 0) return;
        _sessions.RemoveAt(index);
        _host.Children.Remove(session.View);
        session.Dispose();
        if (_sessions.Count == 0) { Close(); return; }
        if (session == _active) Activate(_sessions[Math.Min(index, _sessions.Count - 1)]);
        else RebuildTabs();
    }

    private string TabTitle(ConsoleSession s)
    {
        string t = s.Title;
        if (string.IsNullOrWhiteSpace(t)) return "Console";
        int colon = t.IndexOf(": ", StringComparison.Ordinal);
        return colon > 0 && t.Contains('@') ? t[(colon + 2)..] : t;
    }

    private void UpdateTitle()
    {
        string t = _active == null ? "Console" : (string.IsNullOrWhiteSpace(_active.Title) ? TabTitle(_active) : _active.Title);
        _title.Text = t;
        Title = t;
    }

    private void RebuildTabs()
    {
        _tabs.Children.Clear();
        _tabBar.Visibility = _sessions.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (_sessions.Count <= 1) return;
        bool dark = ThemeManager.IsDark;
        foreach (var s in _sessions)
        {
            var session = s;
            bool active = s == _active;
            var label = new TextBlock { Text = TabTitle(s), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 13, FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(28, 0, 28, 0) };
            var close = new Button { Content = "", Width = 22, Height = 22, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(0), ToolTip = L("Fermer l'onglet", "Close Tab") };
            close.SetResourceReference(StyleProperty, "ShellButton");
            close.SetResourceReference(FontFamilyProperty, "Font.Icons");
            close.Click += (_, _) => CloseSession(session);
            var grid = new Grid();
            grid.Children.Add(label);
            grid.Children.Add(close);
            var tab = new Border
            {
                Child = grid,
                Height = 34,
                Margin = new Thickness(3, 0, 3, 0),
                CornerRadius = new CornerRadius(8),
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(active ? (dark ? Color.FromArgb(0x26, 255, 255, 255) : Color.FromArgb(0x1A, 0, 0, 0)) : Colors.Transparent),
            };
            tab.MouseLeftButtonUp += (_, _) => Activate(session);
            tab.MouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Middle) CloseSession(session); };
            _tabs.Children.Add(tab);
        }
    }

    private string? CurrentDirectory()
    {
        string? t = _active?.Title;
        int colon = t?.IndexOf(": ", StringComparison.Ordinal) ?? -1;
        if (t == null || colon < 0) return null;
        string p = t[(colon + 2)..].Replace('/', '\\');
        if (p.StartsWith('~')) p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p[1..];
        return Directory.Exists(p) ? p : null;
    }

    private void SyncScrollBar()
    {
        if (_active == null) return;
        _syncingScroll = true;
        try
        {
            int max = _active.View.MaxOffset;
            _scroll.Maximum = max;
            _scroll.ViewportSize = Math.Max(1, _active.Buffer.Rows);
            _scroll.LargeChange = Math.Max(1, _active.Buffer.Rows - 1);
            _scroll.Value = max - _active.View.ViewOffset;
            _scroll.Visibility = max > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _syncingScroll = false; }
    }


    private void ShowMenu(FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        void Add(string text, string? gesture, Action a)
        {
            var mi = new MenuItem { Header = text, InputGestureText = gesture ?? string.Empty };
            mi.Click += (_, _) => a();
            menu.Items.Add(mi);
        }
        Add(L("Nouvelle fenêtre", "New Window"), L("Ctrl+Maj+N", "Ctrl+Shift+N"), () => new ConsoleWindow(CurrentDirectory()).Show());
        Add(L("Nouvel onglet", "New Tab"), L("Ctrl+Maj+T", "Ctrl+Shift+T"), () => AddTab(CurrentDirectory()));
        menu.Items.Add(new Separator());
        Add(L("Zoom avant", "Zoom In"), "Ctrl++", () => Zoom(+1));
        Add(L("Zoom arrière", "Zoom Out"), "Ctrl+-", () => Zoom(-1));
        Add(L("Taille normale", "Normal Size"), "Ctrl+0", () => Zoom(0));
        menu.Items.Add(new Separator());
        Add(L("Tout sélectionner", "Select All"), L("Ctrl+Maj+A", "Ctrl+Shift+A"), () => _active?.View.SelectAll());
        Add(L("Effacer l'historique", "Clear Scrollback"), null, () => { if (_active != null) { lock (_active.Buffer.SyncRoot) _active.Buffer.ClearScrollback(); _active.View.RequestRender(); } });
        menu.IsOpen = true;
    }

    private void Zoom(int direction)
    {
        _fontSize = direction == 0 ? 14 : Math.Clamp(_fontSize + direction, 8, 32);
        _prefs.FontSize = _fontSize;
        _prefs.Save();
        foreach (var s in _sessions) s.View.SetFont("Cascadia Mono, Consolas, Courier New", _fontSize);
    }

    private void Copy()
    {
        if (_active?.View.HasSelection != true) return;
        try { Clipboard.SetText(_active.View.SelectedText); } catch (Exception ex) { Log.Debug("Clipboard: " + ex.Message); }
        _active.View.ClearSelection();
    }

    private void Paste()
    {
        try { if (Clipboard.ContainsText()) _active?.View.Paste(Clipboard.GetText()); }
        catch (Exception ex) { Log.Debug("Clipboard: " + ex.Message); }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        bool ctrl = mods.HasFlag(ModifierKeys.Control), shift = mods.HasFlag(ModifierKeys.Shift), alt = mods.HasFlag(ModifierKeys.Alt);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool handled = true;
        if (ctrl && shift && !alt)
        {
            switch (key)
            {
                case Key.C: Copy(); break;
                case Key.V: Paste(); break;
                case Key.T: AddTab(CurrentDirectory()); break;
                case Key.N: new ConsoleWindow(CurrentDirectory()).Show(); break;
                case Key.W: if (_active != null) CloseSession(_active); break;
                case Key.A: _active?.View.SelectAll(); break;
                default: handled = false; break;
            }
        }
        else if (ctrl && !shift && !alt)
        {
            switch (key)
            {
                case Key.C when _active?.View.HasSelection == true: Copy(); break;
                case Key.V: Paste(); break;
                case Key.OemPlus: case Key.Add: Zoom(+1); break;
                case Key.OemMinus: case Key.Subtract: Zoom(-1); break;
                case Key.D0: case Key.NumPad0: Zoom(0); break;
                case Key.PageUp: SwitchTab(-1); break;
                case Key.PageDown: SwitchTab(+1); break;
                default: handled = false; break;
            }
        }
        else if (shift && !ctrl && !alt && key is Key.PageUp or Key.PageDown && _active != null)
            _active.View.ScrollBy((key == Key.PageUp ? 1 : -1) * Math.Max(1, _active.Buffer.Rows - 1));
        else if (shift && !ctrl && !alt && key == Key.Insert) Paste();
        else if (alt && !ctrl && key >= Key.D1 && key <= Key.D9 && key - Key.D1 < _sessions.Count) Activate(_sessions[key - Key.D1]);
        else handled = false;
        if (handled) e.Handled = true;
    }

    private void SwitchTab(int delta)
    {
        if (_active == null || _sessions.Count < 2) return;
        int i = (_sessions.IndexOf(_active) + delta + _sessions.Count) % _sessions.Count;
        Activate(_sessions[i]);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_sessions.Count > 1)
        {
            var r = MessageBox.Show(this, L($"Fermer les {_sessions.Count} onglets ?", $"Close {_sessions.Count} tabs?"), "Console",
                MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (r != MessageBoxResult.OK) e.Cancel = true;
        }
    }
}
