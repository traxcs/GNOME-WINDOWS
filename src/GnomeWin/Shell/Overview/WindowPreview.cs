using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GnomeWin.Core;
using GnomeWin.Platform.Dwm;
using GnomeWin.Platform.Win32;

namespace GnomeWin.Shell.Overview;

public sealed class WindowPreview
{
    public const double PadX = 8, PadTop = 32, LabelHeight = 38;

    private readonly Border _highlight;
    private readonly Border _placeholder;
    private readonly Border _titlePill;
    private readonly TextBlock _title;
    private readonly Image _icon;
    private readonly Image _placeholderIcon;
    public Button CloseButton { get; }

    public WindowInfo Window { get; }
    public Grid Chrome { get; }
    public DwmThumbnail? Thumbnail { get; private set; }
    public RECT? SourceRect { get; private set; }

    public Rect? Real { get; set; }
    public Rect Slot { get; set; }
    public Rect PreviousSlot { get; set; }
    public double AppearFrom { get; set; } = 1;
    public double AppearTo { get; set; } = 1;
    public bool Removing { get; set; }
    public bool UsePlaceholder { get; private set; }
    public bool IsHovered { get; private set; }
    public bool IsSelected { get; private set; }
    public bool IsDragging { get; set; }
    public Rect Current { get; private set; }
    public double CurrentOpacity { get; private set; }

    public WindowPreview(WindowInfo window)
    {
        Window = window;
        Chrome = new Grid { Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };

        _highlight = new Border
        {
            Margin = new Thickness(PadX - 6, PadTop - 6, PadX - 6, LabelHeight - 6),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(4),
            BorderBrush = Brushes.Transparent,
        };
        _placeholder = new Border
        {
            Margin = new Thickness(PadX, PadTop, PadX, LabelHeight),
            CornerRadius = new CornerRadius(10),
            Visibility = Visibility.Collapsed,
        };
        _placeholder.SetResourceReference(Border.BackgroundProperty, "Brush.CardBg");
        _placeholderIcon = new Image { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(_placeholderIcon, BitmapScalingMode.HighQuality);
        _placeholder.Child = _placeholderIcon;

        CloseButton = new Button
        {
            Content = "",
            Width = 26, Height = 26, FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, PadX - 4, 0),
            Visibility = Visibility.Hidden,
            Focusable = false,
            ToolTip = UI.Loc.T("Close"),
        };
        CloseButton.SetResourceReference(FrameworkElement.StyleProperty, "RoundIconButton");

        _icon = new Image { Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(_icon, BitmapScalingMode.HighQuality);
        _title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13 };
        _title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverviewFg");
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(_icon);
        titleRow.Children.Add(_title);
        _titlePill = new Border
        {
            Child = titleRow,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 4, 12, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 4),
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)),
            Opacity = 0,
        };

        Chrome.Children.Add(_highlight);
        Chrome.Children.Add(_placeholder);
        Chrome.Children.Add(CloseButton);
        Chrome.Children.Add(_titlePill);
        Chrome.MouseEnter += (_, _) => { IsHovered = true; UpdateState(); };
        Chrome.MouseLeave += (_, _) => { IsHovered = false; UpdateState(); };
        RefreshTexts();
    }

    public void RefreshTexts()
    {
        _title.Text = Window.DisplayTitle;
        _icon.Source = Window.Icon;
        _placeholderIcon.Source = Window.Icon;
    }

    public void Attach(IntPtr surface, bool livePreviews)
    {
        Thumbnail?.Dispose();
        Thumbnail = null;
        SourceRect = DwmThumbnail.VisibleSourceRect(Window.Handle);
        if (livePreviews) Thumbnail = DwmThumbnail.Register(surface, Window.Handle);
        var size = Thumbnail?.SourceSize ?? default;
        UsePlaceholder = Thumbnail == null || size.cx < 64 || size.cy < 48;
        if (UsePlaceholder && Thumbnail != null) { Thumbnail.Dispose(); Thumbnail = null; }
        _placeholder.Visibility = UsePlaceholder ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Detach()
    {
        Thumbnail?.Dispose();
        Thumbnail = null;
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        UpdateState();
    }

    private bool _chromeVisible;
    private bool _alwaysTitles;

    public void SetChromeVisible(bool visible, bool alwaysTitles)
    {
        _chromeVisible = visible;
        _alwaysTitles = alwaysTitles;
        UpdateState();
    }

    private void UpdateState()
    {
        bool active = (IsHovered || IsSelected) && _chromeVisible && !IsDragging;
        if (active) _highlight.SetResourceReference(Border.BorderBrushProperty, "Brush.WindowHighlight");
        else _highlight.BorderBrush = Brushes.Transparent;
        CloseButton.Visibility = active && IsHovered ? Visibility.Visible : Visibility.Hidden;
        double titleTarget = _chromeVisible && !IsDragging && (active || _alwaysTitles) ? 1 : 0;
        UI.Animations.Anim.Fade(_titlePill, titleTarget, 140);
    }

    public void Apply(Rect rect, double opacity, double dpiScale, bool thumbVisible)
    {
        Current = rect;
        CurrentOpacity = opacity;
        Canvas.SetLeft(Chrome, rect.X - PadX);
        Canvas.SetTop(Chrome, rect.Y - PadTop);
        Chrome.Width = Math.Max(1, rect.Width + PadX * 2);
        Chrome.Height = Math.Max(1, rect.Height + PadTop + LabelHeight);
        Chrome.Opacity = opacity;
        _titlePill.MaxWidth = Math.Max(60, rect.Width + PadX * 2);
        _placeholderIcon.Width = _placeholderIcon.Height = Math.Clamp(Math.Min(rect.Width, rect.Height) * 0.45, 24, 96);

        if (Thumbnail != null)
        {
            var dest = new RECT(
                (int)Math.Round(rect.X * dpiScale), (int)Math.Round(rect.Y * dpiScale),
                (int)Math.Round(rect.Right * dpiScale), (int)Math.Round(rect.Bottom * dpiScale));
            byte alpha = (byte)Math.Clamp(opacity * 255, 0, 255);
            Thumbnail.Update(dest, alpha, thumbVisible && opacity > 0.01 && rect.Width > 2, SourceRect);
        }
        Chrome.Visibility = thumbVisible ? Visibility.Visible : Visibility.Hidden;
    }
}
