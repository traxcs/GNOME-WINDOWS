using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GnomeWin.Services.Search;
using GnomeWin.UI;

namespace GnomeWin.Shell.Search;

public sealed class SearchResultsView : Grid
{
    private const double SectionSpacing = 18, CardPadding = 12, CardMargin = 12, CardRadius = 24;
    private const double RowSpacing = 6, RowRadius = 13, TitleSpacing = 12;

    private static readonly Brush HoverBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
    private static readonly Brush SelectedBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));

    private readonly StackPanel _root = new() { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 900, MinWidth = 600 };
    private readonly ScrollViewer _scroll;
    private readonly List<(SearchResult Result, Border Element)> _items = new();
    private int _selected;

    public event Action<SearchResult>? Activated;
    public event Action<SearchResult, FrameworkElement>? ContextRequested;

    public SearchResultsView()
    {
        _scroll = new ScrollViewer { Content = _root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Padding = new Thickness(20, 8, 20, 8) };
        Children.Add(_scroll);
    }

    public void Show(SearchResults results)
    {
        _root.Children.Clear();
        _items.Clear();
        _selected = 0;

        if (results.IsEmpty)
        {
            var none = new TextBlock { Text = Loc.T("NoResults"), FontSize = 18, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 60, 0, 0) };
            none.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverviewFg");
            _root.Children.Add(none);
            return;
        }

        if (results.Apps.Count > 0) AddAppRow(results.Apps);
        if (results.Calculation != null) AddProvider("", Loc.T("Calculator"), new[] { results.Calculation }, 0);
        if (results.Windows.Count > 0) AddProvider("", Loc.T("OpenWindows"), results.Windows, results.MoreWindows);
        if (results.Settings.Count > 0) AddProvider("", Loc.T("WindowsSettings"), results.Settings, results.MoreSettings);
        if (results.Files.Count > 0) AddProvider("", Loc.T("RecentFiles"), results.Files, results.MoreFiles);
        UpdateSelection();
        _scroll.ScrollToTop();
    }

    private void AddAppRow(IEnumerable<SearchResult> apps)
    {
        var panel = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 8) };
        foreach (var r in apps)
        {
            var tile = Tile(r);
            _items.Add((r, tile));
            panel.Children.Add(tile);
        }
        _root.Children.Add(panel);
    }

    private void AddProvider(string glyph, string name, IEnumerable<SearchResult> results, int more)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(168) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var header = new Grid { Margin = new Thickness(6, 2, 10, 2), VerticalAlignment = VerticalAlignment.Top };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = new TextBlock { Text = glyph, FontSize = 20, VerticalAlignment = VerticalAlignment.Top, Opacity = 0.9 };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Icons");
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverviewFg");
        header.Children.Add(icon);
        var titles = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        var label = new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverviewFg");
        titles.Children.Add(label);
        if (more > 0)
        {
            var moreLabel = new TextBlock { Text = string.Format(Loc.T("MoreResults"), more), FontSize = 13, Opacity = 0.65, Margin = new Thickness(0, 2, 0, 0) };
            moreLabel.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverviewFg");
            titles.Children.Add(moreLabel);
        }
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);
        grid.Children.Add(header);

        var rows = new StackPanel();
        foreach (var r in results)
        {
            var row = Row(r, name);
            _items.Add((r, row));
            rows.Children.Add(row);
        }
        Grid.SetColumn(rows, 1);
        grid.Children.Add(rows);

        var card = new Border
        {
            Child = grid,
            CornerRadius = new CornerRadius(CardRadius),
            Padding = new Thickness(CardPadding),
            Margin = new Thickness(CardMargin, 0, CardMargin, SectionSpacing),
        };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.OverlayBg");
        _root.Children.Add(card);
    }

    private Border Tile(SearchResult r)
    {
        var stack = new StackPanel { Width = 104 };
        stack.Children.Add(new Image { Source = r.Icon, Width = 56, Height = 56, Margin = new Thickness(0, 8, 0, 6) });
        var t = new TextBlock { Text = r.Title, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap, MaxHeight = 34, FontSize = 12.5 };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverviewFg");
        stack.Children.Add(t);
        return Wrap(r, stack, new Thickness(4), new Thickness(4, 4, 4, 8));
    }

    private Border Row(SearchResult r, string providerName)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (r.Icon != null) g.Children.Add(new Image { Source = r.Icon, Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center });
        else
        {
            var glyph = new TextBlock { Text = r.Glyph ?? "", FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            glyph.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Icons");
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverviewFg");
            g.Children.Add(glyph);
        }

        g.ColumnDefinitions.Add(new ColumnDefinition());
        var title = new TextBlock { Text = r.Title, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverviewFg");
        Grid.SetColumn(title, 1);
        g.Children.Add(title);
        string? description = string.Equals(r.Subtitle, providerName, StringComparison.Ordinal) ? null : r.Subtitle;
        if (!string.IsNullOrEmpty(description))
        {
            var sub = new TextBlock { Text = description, FontSize = 13, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(TitleSpacing, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverviewFg");
            Grid.SetColumn(sub, 2);
            g.Children.Add(sub);
        }
        return Wrap(r, g, new Thickness(0, 0, 0, RowSpacing), new Thickness(8, 7, 10, 7), RowRadius);
    }

    private Border Wrap(SearchResult r, UIElement content, Thickness margin, Thickness padding, double radius = 12)
    {
        var b = new Border { Child = content, CornerRadius = new CornerRadius(radius), Margin = margin, Padding = padding, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        b.MouseEnter += (_, _) => { if (!IsSelectedElement(b)) b.Background = HoverBrush; };
        b.MouseLeave += (_, _) => { if (!IsSelectedElement(b)) b.Background = Brushes.Transparent; };
        b.MouseLeftButtonUp += (_, _) => Activated?.Invoke(r);
        b.MouseRightButtonUp += (_, e) => { ContextRequested?.Invoke(r, b); e.Handled = true; };
        return b;
    }

    private bool IsSelectedElement(Border b) => _selected >= 0 && _selected < _items.Count && ReferenceEquals(_items[_selected].Element, b);

    private void UpdateSelection()
    {
        for (int i = 0; i < _items.Count; i++)
            _items[i].Element.Background = i == _selected ? SelectedBrush : Brushes.Transparent;
        if (_selected >= 0 && _selected < _items.Count) _items[_selected].Element.BringIntoView();
    }

    public void MoveSelection(int delta)
    {
        if (_items.Count == 0) return;
        _selected = Math.Clamp(_selected + delta, 0, _items.Count - 1);
        UpdateSelection();
    }

    public void ActivateSelected()
    {
        if (_selected >= 0 && _selected < _items.Count) Activated?.Invoke(_items[_selected].Result);
    }
}
