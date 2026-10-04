using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GnomeWin.Services.Search;
using GnomeWin.UI;

namespace GnomeWin.Shell.Search;

public sealed class SearchResultsView : Grid
{
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

        if (results.Calculation != null) AddSection(Loc.T("Calculator"), new[] { results.Calculation }, asTiles: false);
        if (results.Apps.Count > 0) AddSection(Loc.T("Applications"), results.Apps, asTiles: true);
        if (results.Windows.Count > 0) AddSection(Loc.T("OpenWindows"), results.Windows, asTiles: false);
        if (results.Settings.Count > 0) AddSection(Loc.T("WindowsSettings"), results.Settings, asTiles: false);
        if (results.Files.Count > 0) AddSection(Loc.T("RecentFiles"), results.Files, asTiles: false);
        UpdateSelection();
        _scroll.ScrollToTop();
    }

    private void AddSection(string title, IEnumerable<SearchResult> results, bool asTiles)
    {
        var header = new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Opacity = 0.75, Margin = new Thickness(12, 14, 0, 6) };
        header.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverviewFg");
        var card = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(8), Background = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00)) };
        Panel panel = asTiles ? new WrapPanel() : new StackPanel();
        foreach (var r in results)
        {
            var el = asTiles ? Tile(r) : Row(r);
            _items.Add((r, el));
            panel.Children.Add(el);
        }
        card.Child = panel;
        _root.Children.Add(header);
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

    private Border Row(SearchResult r)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        if (r.Icon != null) g.Children.Add(new Image { Source = r.Icon, Width = 28, Height = 28 });
        else
        {
            var glyph = new TextBlock { Text = r.Glyph ?? "", FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            glyph.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Icons");
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverviewFg");
            g.Children.Add(glyph);
        }
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var title = new TextBlock { Text = r.Title, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverviewFg");
        texts.Children.Add(title);
        if (!string.IsNullOrEmpty(r.Subtitle))
        {
            var sub = new TextBlock { Text = r.Subtitle, FontSize = 12, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverviewFg");
            texts.Children.Add(sub);
        }
        Grid.SetColumn(texts, 1);
        g.Children.Add(texts);
        return Wrap(r, g, new Thickness(0, 1, 0, 1), new Thickness(10, 8, 10, 8));
    }

    private Border Wrap(SearchResult r, UIElement content, Thickness margin, Thickness padding)
    {
        var b = new Border { Child = content, CornerRadius = new CornerRadius(12), Margin = margin, Padding = padding, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        b.MouseEnter += (_, _) => { if (!IsSelectedElement(b)) b.Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)); };
        b.MouseLeave += (_, _) => { if (!IsSelectedElement(b)) b.Background = Brushes.Transparent; };
        b.MouseLeftButtonUp += (_, _) => Activated?.Invoke(r);
        b.MouseRightButtonUp += (_, e) => { ContextRequested?.Invoke(r, b); e.Handled = true; };
        return b;
    }

    private bool IsSelectedElement(Border b) => _selected >= 0 && _selected < _items.Count && ReferenceEquals(_items[_selected].Element, b);

    private void UpdateSelection()
    {
        for (int i = 0; i < _items.Count; i++)
            _items[i].Element.Background = i == _selected ? new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent;
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
