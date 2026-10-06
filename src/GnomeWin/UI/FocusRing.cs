using System.Windows;
using System.Windows.Controls;

namespace GnomeWin.UI;

public static class FocusRing
{
    public static readonly DependencyProperty IsVisibleProperty =
        DependencyProperty.RegisterAttached("IsVisible", typeof(bool), typeof(FocusRing), new PropertyMetadata(false));

    public static bool GetIsVisible(DependencyObject o) => (bool)o.GetValue(IsVisibleProperty);
    public static void SetIsVisible(DependencyObject o, bool value) => o.SetValue(IsVisibleProperty, value);

    public static void Track(TextBox box)
    {
        box.PreviewMouseLeftButtonDown += (_, _) => SetIsVisible(box, true);
        box.TextChanged += (_, _) => { if (box.Text.Length > 0) SetIsVisible(box, true); };
    }

    public static void Reset(DependencyObject o) => SetIsVisible(o, false);
}
