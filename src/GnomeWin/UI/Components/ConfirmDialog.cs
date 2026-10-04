using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using GnomeWin.Core;

namespace GnomeWin.UI.Components;

public sealed class ConfirmDialog : ShellWindow
{
    private ConfirmDialog(string message, string confirmText) : base(noActivate: false, transparent: true)
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "GnomeWin";
        var card = new Border
        {
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(24),
            Margin = new Thickness(20),
            MinWidth = 340,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 2, Opacity = 0.5, Color = Colors.Black },
        };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.PopupBg");
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = message, FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = 380, Margin = new Thickness(0, 0, 0, 22), HorizontalAlignment = HorizontalAlignment.Center });
        var buttons = new UniformGrid { Columns = 2 };
        var cancel = new Button { Content = Loc.T("Cancel"), Margin = new Thickness(0, 0, 6, 0), IsCancel = true };
        cancel.SetResourceReference(StyleProperty, "FlatButton");
        cancel.Click += (_, _) => { DialogResult = false; };
        var ok = new Button { Content = confirmText, Margin = new Thickness(6, 0, 0, 0), IsDefault = true };
        ok.SetResourceReference(StyleProperty, "AccentButton");
        ok.Click += (_, _) => { DialogResult = true; };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        stack.Children.Add(buttons);
        card.Child = stack;
        Content = card;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
        Loaded += (_, _) => ok.Focus();
    }

    public static bool Ask(string message, string confirmText, MonitorInfo monitor)
    {
        var d = new ConfirmDialog(message, confirmText);
        d.EnsureHandle();
        d.ContentRendered += (_, _) =>
        {
            var (w, h) = d.PhysicalSize(new Size(d.ActualWidth, d.ActualHeight));
            d.MovePhysical(monitor.Bounds.Left + (monitor.Bounds.Width - w) / 2, monitor.Bounds.Top + (monitor.Bounds.Height - h) / 2);
            d.ForceActivate();
        };
        return d.ShowDialog() == true;
    }
}
