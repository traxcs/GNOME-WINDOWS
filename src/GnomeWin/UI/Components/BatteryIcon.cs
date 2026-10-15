using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfPath = System.Windows.Shapes.Path;

namespace GnomeWin.UI.Components;

/// <summary>
/// The GNOME battery: an upright outline filled to the charge level, with a bolt while it charges.
/// The icon font only has landscape batteries whose level is a separate glyph, so this is drawn.
/// </summary>
public sealed class BatteryIcon : Grid
{
    private const double BodyWidth = 11, BodyHeight = 18, CapWidth = 5, CapHeight = 2, Inset = 1.6;

    private readonly Border _body = new();
    private readonly Border _cap = new();
    private readonly Border _level = new();
    private readonly WpfPath _bolt = new();

    private int _percent = -1;
    private bool _charging;

    public BatteryIcon()
    {
        Width = BodyWidth + 2;
        Height = BodyHeight + CapHeight + 2;
        VerticalAlignment = VerticalAlignment.Center;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        _cap.Width = CapWidth;
        _cap.Height = CapHeight;
        _cap.CornerRadius = new CornerRadius(1, 1, 0, 0);
        _cap.HorizontalAlignment = HorizontalAlignment.Center;
        _cap.VerticalAlignment = VerticalAlignment.Top;

        _body.Width = BodyWidth;
        _body.Height = BodyHeight;
        _body.CornerRadius = new CornerRadius(3);
        _body.BorderThickness = new Thickness(1.3);
        _body.Background = Brushes.Transparent;
        _body.HorizontalAlignment = HorizontalAlignment.Center;
        _body.VerticalAlignment = VerticalAlignment.Bottom;

        _level.Width = BodyWidth - Inset * 2;
        _level.CornerRadius = new CornerRadius(1.4);
        _level.HorizontalAlignment = HorizontalAlignment.Center;
        _level.VerticalAlignment = VerticalAlignment.Bottom;
        _level.Margin = new Thickness(0, 0, 0, Inset);

        // A lightning bolt sized for the inside of the body.
        _bolt.Data = Geometry.Parse("M 6.2,0 L 1.4,6.4 L 4.4,6.4 L 3.4,11.4 L 8.4,4.6 L 5.2,4.6 Z");
        _bolt.Stretch = Stretch.Uniform;
        _bolt.Width = 7;
        _bolt.Height = 11;
        _bolt.HorizontalAlignment = HorizontalAlignment.Center;
        _bolt.VerticalAlignment = VerticalAlignment.Bottom;
        _bolt.Margin = new Thickness(0, 0, 0, (BodyHeight - 11) / 2);
        _bolt.Visibility = Visibility.Collapsed;

        Children.Add(_level);
        Children.Add(_body);
        Children.Add(_cap);
        Children.Add(_bolt);
        ApplyForeground();
    }

    /// <summary>Colour of the outline, of the charge level and of the bolt.</summary>
    public Brush IconBrush
    {
        get => (Brush)GetValue(IconBrushProperty);
        set => SetValue(IconBrushProperty, value);
    }

    public static readonly DependencyProperty IconBrushProperty =
        DependencyProperty.Register(nameof(IconBrush), typeof(Brush), typeof(BatteryIcon),
            new PropertyMetadata(Brushes.White, (d, _) => ((BatteryIcon)d).ApplyForeground()));

    public void SetState(int percent, bool charging)
    {
        percent = Math.Clamp(percent, 0, 100);
        if (percent == _percent && charging == _charging) return;
        _percent = percent;
        _charging = charging;

        double inner = BodyHeight - Inset * 2;
        _level.Height = Math.Max(percent > 0 ? 1.5 : 0, inner * percent / 100.0);
        // While charging the body holds the bolt instead of the level, as in GNOME.
        _bolt.Visibility = charging ? Visibility.Visible : Visibility.Collapsed;
        _level.Visibility = charging ? Visibility.Collapsed : Visibility.Visible;
        ApplyForeground();
    }

    public int Percent => Math.Max(0, _percent);
    public bool IsCharging => _charging;

    private void ApplyForeground()
    {
        var brush = IconBrush;
        _body.BorderBrush = brush;
        _cap.Background = brush;
        // Below 15 % the level turns red, as GNOME does, so an empty battery is noticed.
        _level.Background = !_charging && _percent >= 0 && _percent <= 15
            ? new SolidColorBrush(Color.FromRgb(0xE6, 0x2D, 0x42))
            : brush;
        _bolt.Fill = brush;
    }
}
