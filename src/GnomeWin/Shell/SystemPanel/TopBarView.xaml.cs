using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using GnomeWin.Services.SystemStatus;
using GnomeWin.UI;
using GnomeWin.UI.Animations;

namespace GnomeWin.Shell.SystemPanel;

public partial class TopBarView : UserControl
{
    public TopBarView()
    {
        InitializeComponent();
    }

    public Button Activities => ActivitiesButton;
    public Button Clock => ClockButton;
    public Button Status => StatusButton;
    public FrameworkElement HotCorner => HotCornerZone;

    public void SetSafeMode(bool safe) => SafeModeLabel.Visibility = safe ? Visibility.Visible : Visibility.Collapsed;

    public void SetClock(DateTime now)
    {
        string day = now.ToString("ddd d MMM", Loc.Culture);
        ClockText.Text = $"{day}  {now:HH:mm}";
    }
    
    public static void SetActive(Button button, bool active)
    {
        if (active) button.SetResourceReference(BackgroundProperty, "Brush.PanelActive");
        else button.Background = System.Windows.Media.Brushes.Transparent;
    }

    public void SetHasNotifications(bool any) => NotificationDot.Visibility = any ? Visibility.Visible : Visibility.Collapsed;

    public void SetWorkspaces(int count, int current)
    {
        count = Math.Max(1, count);
        while (WorkspaceDots.Children.Count < count)
        {
            var dot = new Rectangle { Height = 7, Width = 7, RadiusX = 3.5, RadiusY = 3.5, Margin = new Thickness(2.5, 0, 2.5, 0) };
            dot.SetResourceReference(Shape.FillProperty, "Brush.PanelFg");
            WorkspaceDots.Children.Add(dot);
        }
        while (WorkspaceDots.Children.Count > count) WorkspaceDots.Children.RemoveAt(WorkspaceDots.Children.Count - 1);
        for (int i = 0; i < count; i++)
        {
            var dot = (Rectangle)WorkspaceDots.Children[i];
            bool active = i == current;
            Anim.Animate(dot, WidthProperty, active ? 28 : 7, 200);
            dot.Opacity = active ? 1 : 0.55;
        }
    }

    public void SetStatus(SystemStatusService s, bool showBattery, bool showPercentage)
    {
        NetworkGlyph.Text = StatusGlyphs.Network(s.Network, s.SignalBars);
        NetworkGlyph.Opacity = s.Network == NetworkKind.None ? 0.45 : 1;
        VolumeGlyph.Text = StatusGlyphs.Volume(s.Volume, s.Muted);
        VolumeGlyph.Visibility = s.AudioAvailable ? Visibility.Visible : Visibility.Collapsed;
        if (s.HasBattery && showBattery)
        {
            BatteryGlyph.Visibility = Visibility.Visible;
            BatteryText.Visibility = showPercentage ? Visibility.Visible : Visibility.Collapsed;
            BatteryGlyph.Text = StatusGlyphs.Battery(s.BatteryPercent, s.Charging);
            BatteryText.Text = s.BatteryPercent + " %";
        }
        else BatteryGlyph.Visibility = BatteryText.Visibility = Visibility.Collapsed;
    }
}

public static class StatusGlyphs
{
    public static string Network(NetworkKind kind, int bars) => kind switch
    {
        NetworkKind.Wifi => bars switch { <= 1 when bars >= 0 => "", 2 => "", 3 => "", _ => "" },
        NetworkKind.Ethernet => "",
        NetworkKind.Other => "",
        _ => "",
    };

    public static string Volume(float v, bool muted)
    {
        if (muted || v <= 0.001f) return "";
        if (v < 0.34f) return "";
        if (v < 0.67f) return "";
        return "";
    }

    public static string Battery(int percent, bool charging)
    {
        int level = Math.Clamp((int)Math.Round(percent / 10.0), 0, 10);
        return ((char)((charging ? 0xEBAB : 0xEBA0) + level)).ToString();
    }
}
