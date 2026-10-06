using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using GnomeWin.Platform.Win32;
using GnomeWin.Services.Logging;
using GnomeWin.UI;

namespace GnomeWin.Setup;

public sealed class SetupWindow : Window
{
    private readonly bool _uninstall;
    private readonly StackPanel _options = new();
    private readonly TextBlock _status = new() { Opacity = 0.75, Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly Button _primary = new() { Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button _cancel = new();
    private readonly CheckBox _desktop, _startup, _reset, _launch, _removeData;
    private bool _done;

    public SetupWindow(bool uninstall)
    {
        _uninstall = uninstall;
        Title = uninstall ? T("Désinstaller GnomeWin", "Uninstall GnomeWin") : T("Installer GnomeWin", "Install GnomeWin");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Brush.WindowBg");
        SetResourceReference(ForegroundProperty, "Brush.Fg");
        SetResourceReference(FontFamilyProperty, "Font.Ui");
        FontSize = 13.5;
        Icon = EmbeddedIcon.Window("GnomeWin.ico");
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 42,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
            CornerRadius = new CornerRadius(12),
        });

        var root = new StackPanel { Margin = new Thickness(28, 6, 28, 28) };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
        header.Children.Add(new Image { Source = EmbeddedIcon.Load("GnomeWin.ico", 64), Width = 56, Height = 56, Margin = new Thickness(0, 0, 16, 0) });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "GnomeWin " + Installer.Version, FontSize = 22, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(new TextBlock { Text = T("L'expérience GNOME Shell pour Windows", "The GNOME Shell experience for Windows"), Opacity = 0.7 });
        header.Children.Add(titles);
        root.Children.Add(header);

        _desktop = Switch(T("Créer un raccourci sur le bureau", "Create a desktop shortcut"), false);
        _startup = Switch(T("Lancer GnomeWin au démarrage de Windows", "Launch GnomeWin at startup"), true);
        _reset = Switch(T("Réinitialiser la configuration existante (sauvegarde conservée)", "Reset the existing configuration (backup kept)"), false);
        _launch = Switch(T("Lancer GnomeWin après l'installation", "Launch GnomeWin when done"), true);
        _removeData = Switch(T("Supprimer aussi mes paramètres et journaux", "Also remove my settings and logs"), false);

        if (uninstall)
        {
            _options.Children.Add(new TextBlock
            {
                Text = T("GnomeWin va être fermé, la barre des tâches Windows restaurée et les fichiers supprimés.",
                         "GnomeWin will be closed, the Windows taskbar restored and the files removed."),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
            });
            _options.Children.Add(_removeData);
        }
        else
        {
            _options.Children.Add(new TextBlock
            {
                Text = T("Installation pour l'utilisateur courant (aucun droit administrateur) dans :", "Per-user installation (no administrator rights) into:"),
                TextWrapping = TextWrapping.Wrap,
            });
            _options.Children.Add(new TextBlock { Text = Installer.InstallDir, Opacity = 0.7, FontSize = 12, Margin = new Thickness(0, 2, 0, 14), TextWrapping = TextWrapping.Wrap });
            _options.Children.Add(_startup);
            _options.Children.Add(_desktop);
            if (File.Exists(Services.AppPaths.SettingsFile)) _options.Children.Add(_reset);
            _options.Children.Add(_launch);
        }
        root.Children.Add(_options);
        root.Children.Add(_status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        _cancel.Content = T("Annuler", "Cancel");
        _cancel.SetResourceReference(StyleProperty, "FlatButton");
        _cancel.Click += (_, _) => Close();
        _primary.Content = uninstall ? T("Désinstaller", "Uninstall") : Installer.IsInstalled ? T("Mettre à jour", "Update") : T("Installer", "Install");
        _primary.SetResourceReference(StyleProperty, "AccentButton");
        _primary.Click += async (_, _) => await RunAsync();
        buttons.Children.Add(_cancel);
        buttons.Children.Add(_primary);
        root.Children.Add(buttons);

        var page = new DockPanel { LastChildFill = true };
        var titleBar = BuildTitleBar();
        DockPanel.SetDock(titleBar, Dock.Top);
        page.Children.Add(titleBar);
        page.Children.Add(root);
        Content = page;

        SourceInitialized += (_, _) =>
        {
            UI.Components.ShellWindow.UseSoftwareRendering(this);
            var h = new WindowInteropHelper(this).Handle;
            int dark = UI.Themes.ThemeManager.IsDark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(h, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            int round = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(h, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        };
    }

    private UIElement BuildTitleBar()
    {
        var bar = new Grid { Height = 42 };
        bar.Children.Add(new TextBlock
        {
            Text = Title,
            FontWeight = FontWeights.Bold,
            FontSize = 13.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        buttons.Children.Add(WindowButton("", () => WindowState = WindowState.Minimized));
        buttons.Children.Add(WindowButton("", Close));
        bar.Children.Add(buttons);
        return bar;
    }

    private static Button WindowButton(string glyph, Action click)
    {
        var b = new Button { Content = glyph, Width = 24, Height = 24, FontSize = 8, Margin = new Thickness(8, 0, 0, 0) };
        b.SetResourceReference(StyleProperty, "RoundIconButton");
        b.Click += (_, _) => click();
        WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        return b;
    }


    private static CheckBox Switch(string text, bool value)
    {
        var c = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = value, Margin = new Thickness(0, 6, 0, 6) };
        c.SetResourceReference(StyleProperty, "Switch");
        return c;
    }

    private async Task RunAsync()
    {
        if (_done) { Close(); return; }
        _primary.IsEnabled = _cancel.IsEnabled = false;
        _options.IsEnabled = false;
        try
        {
            if (_uninstall)
            {
                bool removeData = _removeData.IsChecked == true;
                _status.Text = T("Désinstallation…", "Uninstalling…");
                await Task.Run(() => Installer.Uninstall(removeData));
                _status.Text = T("GnomeWin a été désinstallé. La barre des tâches Windows est restaurée.", "GnomeWin has been uninstalled. The Windows taskbar is restored.");
            }
            else
            {
                var o = new InstallOptions
                {
                    DesktopShortcut = _desktop.IsChecked == true,
                    LaunchAtStartup = _startup.IsChecked == true,
                    ResetSettings = _reset.IsChecked == true,
                    LaunchNow = _launch.IsChecked == true,
                };
                await Task.Run(() => Installer.Install(o, msg => Dispatcher.BeginInvoke(() => _status.Text = msg)));
                if (o.LaunchNow) Installer.LaunchInstalled();
            }
            _done = true;
            _primary.Content = T("Fermer", "Close");
            _primary.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Log.Error("Setup failed", ex);
            _status.Foreground = Brushes.IndianRed;
            _status.Text = ex.Message;
            _primary.IsEnabled = _cancel.IsEnabled = true;
            _options.IsEnabled = true;
        }
    }

    private static string T(string fr, string en) => Loc.IsFrench ? fr : en;
}
