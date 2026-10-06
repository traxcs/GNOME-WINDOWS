using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GnomeWin.Terminal;

public sealed class TerminalPalette
{
    public required Color Background { get; init; }
    public required Color Foreground { get; init; }
    public required Color[] Ansi { get; init; }

    private static Color C(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static readonly Color[] GnomeAnsi =
    {
        C(0x241F31), C(0xC01C28), C(0x2EC27E), C(0xF5C211), C(0x1E78E4), C(0x9841BB), C(0x0AB9DC), C(0xC0BFBC),
        C(0x5E5C64), C(0xED333B), C(0x57E389), C(0xF8E45C), C(0x51A1FF), C(0xC061CB), C(0x4FD2FD), C(0xF6F5F4),
    };

    public static readonly TerminalPalette Dark = new() { Background = C(0x1E1E1E), Foreground = C(0xFFFFFF), Ansi = GnomeAnsi };
    public static readonly TerminalPalette Light = new() { Background = C(0xFFFFFF), Foreground = C(0x000000), Ansi = GnomeAnsi };

    public Color Resolve(uint color, bool foreground)
    {
        if (color == 0) return foreground ? Foreground : Background;
        if ((color & 0x02000000u) != 0) return C(color & 0xFFFFFF);
        int i = (int)(color & 0xFF);
        if (i < 16) return Ansi[i];
        if (i < 232)
        {
            i -= 16;
            static byte V(int v) => (byte)(v == 0 ? 0 : 55 + v * 40);
            return Color.FromRgb(V(i / 36), V(i / 6 % 6), V(i % 6));
        }
        byte g = (byte)(8 + (i - 232) * 10);
        return Color.FromRgb(g, g, g);
    }
}

public sealed class TerminalView : FrameworkElement
{
    public const double Padding = 8;

    private readonly TerminalBuffer _buf;
    private readonly VisualCollection _visuals;
    private readonly List<DrawingVisual> _rowVisuals = new();
    private readonly DrawingVisual _background = new(), _cursor = new();
    private readonly Dictionary<uint, SolidColorBrush> _brushes = new();
    private TerminalPalette _palette = TerminalPalette.Dark;
    private Typeface _regular = null!, _bold = null!, _italic = null!;
    private double _fontSize = 14, _cellW, _cellH, _baseline;
    private double _pixelsPerDip = 1;
    private int _renderQueued;
    private long _renderedVersion = -1;
    private int _viewOffset;
    private int _lastScrollbackCount;
    private long _lastTrimmed;
    private (int Line, int Col)? _selStart, _selEnd;
    private bool _selecting;

    public event Action<string>? Input;
    public event Action<int, int>? GridResized;
    public event Action? ScrollChanged;

    public TerminalView(TerminalBuffer buffer)
    {
        _buf = buffer;
        _visuals = new VisualCollection(this) { _background, _cursor };
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.IBeam;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        SetFont("Cascadia Mono, Consolas, Courier New", 14);
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);
        KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.None);
        Loaded += (_, _) => { _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip; MarkAllDirty(); };
        GotKeyboardFocus += (_, _) => DrawCursor();
        LostKeyboardFocus += (_, _) => DrawCursor();
    }

    public double FontSizePx => _fontSize;
    public int ViewOffset => _viewOffset;
    public int MaxOffset { get { lock (_buf.SyncRoot) return _buf.Scrollback.Count; } }

    public void SetFont(string family, double size)
    {
        _fontSize = Math.Clamp(size, 7, 40);
        var ff = new FontFamily(family);
        _regular = new Typeface(ff, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        _bold = new Typeface(ff, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        _italic = new Typeface(ff, FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);
        var probe = new FormattedText("MMMMMMMMMM", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _regular, _fontSize, Brushes.White, _pixelsPerDip);
        _cellW = probe.WidthIncludingTrailingWhitespace / 10;
        _cellH = Math.Ceiling(probe.Height + 1);
        _baseline = probe.Baseline;
        UpdateGrid();
        MarkAllDirty();
    }

    public void SetPalette(TerminalPalette palette)
    {
        _palette = palette;
        _brushes.Clear();
        MarkAllDirty();
    }

    public Size CellSize => new(_cellW, _cellH);

    private SolidColorBrush Brush(Color c)
    {
        uint key = (uint)(c.R << 16 | c.G << 8 | c.B);
        if (!_brushes.TryGetValue(key, out var b))
        {
            b = new SolidColorBrush(c);
            b.Freeze();
            _brushes[key] = b;
        }
        return b;
    }


    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateGrid();
    }

    private void UpdateGrid()
    {
        if (_cellW <= 0 || ActualWidth <= 0) return;
        int cols = Math.Max(10, (int)((ActualWidth - Padding * 2) / _cellW));
        int rows = Math.Max(2, (int)((ActualHeight - Padding * 2) / _cellH));
        bool changed;
        lock (_buf.SyncRoot)
        {
            changed = cols != _buf.Columns || rows != _buf.Rows;
            if (changed) _buf.Resize(cols, rows);
        }
        while (_rowVisuals.Count < rows)
        {
            var v = new DrawingVisual();
            _rowVisuals.Add(v);
            _visuals.Insert(_visuals.Count - 1, v);
        }
        while (_rowVisuals.Count > rows)
        {
            _visuals.Remove(_rowVisuals[^1]);
            _rowVisuals.RemoveAt(_rowVisuals.Count - 1);
        }
        using (var dc = _background.RenderOpen())
            dc.DrawRectangle(Brush(_palette.Background), null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (changed) GridResized?.Invoke(cols, rows);
        MarkAllDirty();
    }

    private void MarkAllDirty()
    {
        _renderedVersion = -1;
        lock (_buf.SyncRoot) Array.Fill(_buf.DirtyRows, true);
        RequestRender();
    }


    public void RequestRender()
    {
        if (Interlocked.Exchange(ref _renderQueued, 1) == 1) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, Render);
    }

    private void Render()
    {
        Interlocked.Exchange(ref _renderQueued, 0);
        if (!IsLoaded || _rowVisuals.Count == 0) return;
        lock (_buf.SyncRoot)
        {
            int added = _buf.Scrollback.Count - _lastScrollbackCount + (int)(_buf.Trimmed - _lastTrimmed);
            if (_viewOffset > 0 && added > 0) _viewOffset = Math.Min(_viewOffset + added, _buf.Scrollback.Count);
            bool scrolled = added != 0;
            _lastScrollbackCount = _buf.Scrollback.Count;
            _lastTrimmed = _buf.Trimmed;
            _viewOffset = Math.Clamp(_viewOffset, 0, _buf.Scrollback.Count);

            if (_buf.Version == _renderedVersion && !scrolled) return;
            bool all = _renderedVersion < 0 || _viewOffset > 0 || scrolled;
            int first = _buf.Scrollback.Count - _viewOffset;
            for (int r = 0; r < _rowVisuals.Count && r < _buf.Rows; r++)
            {
                if (!all && !_buf.DirtyRows[r]) continue;
                DrawRow(r, first + r);
            }
            Array.Fill(_buf.DirtyRows, false);
            _renderedVersion = _buf.Version;
            DrawCursorLocked();
        }
        ScrollChanged?.Invoke();
    }

    private void DrawRow(int row, int lineIndex)
    {
        var line = _buf.LineAt(lineIndex);
        double y = Padding + row * _cellH;
        using var dc = _rowVisuals[row].RenderOpen();
        if (line == null) return;
        var cells = line.Cells;
        int n = Math.Min(cells.Length, _buf.Columns);

        for (int x = 0; x < n;)
        {
            var c = cells[x];
            int start = x;
            while (x < n && cells[x].Bg == c.Bg && (cells[x].Flags & CellFlags.Inverse) == (c.Flags & CellFlags.Inverse)) x++;
            bool inverse = (c.Flags & CellFlags.Inverse) != 0;
            if (c.Bg == 0 && !inverse) continue;
            var bg = inverse ? _palette.Resolve(c.Fg, true) : _palette.Resolve(c.Bg, false);
            dc.DrawRectangle(Brush(bg), null, new Rect(Padding + start * _cellW, y, (x - start) * _cellW, _cellH));
        }
        if (SelectionOnLine(lineIndex, out int s0, out int s1))
        {
            var sel = new SolidColorBrush(Color.FromArgb(0x66, 0x35, 0x84, 0xE4));
            dc.DrawRectangle(sel, null, new Rect(Padding + s0 * _cellW, y, (s1 - s0 + 1) * _cellW, _cellH));
        }

        var sb = new System.Text.StringBuilder();
        for (int x = 0; x < n;)
        {
            var c = cells[x];
            if (c.Rune <= 0 || (c.Flags & CellFlags.Hidden) != 0) { x++; continue; }
            int start = x;
            sb.Clear();
            while (x < n && cells[x].Rune > 0 && cells[x].SameStyle(c) && TerminalBuffer.RuneWidth(cells[x].Rune) == 1 && cells[x].Rune < 0x10000)
            {
                sb.Append((char)cells[x].Rune);
                x++;
            }
            if (x == start)
            {
                sb.Append(char.ConvertFromUtf32(c.Rune));
                x += TerminalBuffer.RuneWidth(c.Rune) == 2 ? 2 : 1;
            }
            DrawRun(dc, sb.ToString(), c, Padding + start * _cellW, y, x - start);
        }
    }

    private void DrawRun(DrawingContext dc, string text, in Cell c, double x, double y, int cells)
    {
        bool inverse = (c.Flags & CellFlags.Inverse) != 0;
        var fgColor = inverse ? _palette.Resolve(c.Bg, false) : _palette.Resolve(c.Fg, true);
        if ((c.Flags & CellFlags.Bold) != 0 && !inverse && (c.Fg & 0x03000000u) == 0x01000000u && (c.Fg & 0xFF) < 8)
            fgColor = _palette.Ansi[(c.Fg & 0xFF) + 8];
        if ((c.Flags & CellFlags.Dim) != 0) fgColor = Color.FromArgb(0x99, fgColor.R, fgColor.G, fgColor.B);
        var brush = Brush(fgColor);
        var face = (c.Flags & CellFlags.Bold) != 0 ? _bold : (c.Flags & CellFlags.Italic) != 0 ? _italic : _regular;
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, _fontSize, brush, _pixelsPerDip);
        if (cells == 1 && ft.Width > _cellW * 1.2) ft.MaxTextWidth = _cellW * 2;
        dc.DrawText(ft, new Point(x, y + (_baseline - ft.Baseline)));
        if ((c.Flags & CellFlags.Underline) != 0)
            dc.DrawRectangle(brush, null, new Rect(x, y + _baseline + 1, cells * _cellW, 1));
        if ((c.Flags & CellFlags.Strike) != 0)
            dc.DrawRectangle(brush, null, new Rect(x, y + _cellH / 2, cells * _cellW, 1));
    }

    private void DrawCursor()
    {
        lock (_buf.SyncRoot) DrawCursorLocked();
    }

    private void DrawCursorLocked()
    {
        using var dc = _cursor.RenderOpen();
        if (!_buf.CursorVisible || _viewOffset > 0 || _buf.CursorY >= _buf.Rows) return;
        var rect = new Rect(Padding + _buf.CursorX * _cellW, Padding + _buf.CursorY * _cellH, _cellW, _cellH);
        var fg = Brush(_palette.Foreground);
        if (IsKeyboardFocused)
        {
            dc.DrawRectangle(fg, null, rect);
            var cell = _buf.ScreenLine(_buf.CursorY).Cells[_buf.CursorX];
            if (cell.Rune > 0)
            {
                var ft = new FormattedText(char.ConvertFromUtf32(cell.Rune), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    _regular, _fontSize, Brush(_palette.Background), _pixelsPerDip);
                dc.DrawText(ft, new Point(rect.X, rect.Y + (_baseline - ft.Baseline)));
            }
        }
        else dc.DrawRectangle(null, new Pen(fg, 1), new Rect(rect.X + 0.5, rect.Y + 0.5, rect.Width - 1, rect.Height - 1));
    }


    public void ScrollTo(int offset)
    {
        int max = MaxOffset;
        offset = Math.Clamp(offset, 0, max);
        if (offset == _viewOffset) return;
        _viewOffset = offset;
        _renderedVersion = -1;
        RequestRender();
    }

    public void ScrollBy(int lines) => ScrollTo(_viewOffset + lines);

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        int lines = Math.Max(1, SystemParameters.WheelScrollLines) * (e.Delta > 0 ? 1 : -1);
        bool alt;
        lock (_buf.SyncRoot) alt = _buf.AltScreen;
        if (alt)
        {
            string key = e.Delta > 0 ? "A" : "B";
            Input?.Invoke(string.Concat(Enumerable.Repeat(AppKeys ? "\x1bO" + key : "\x1b[" + key, Math.Abs(lines))));
        }
        else ScrollBy(lines);
        e.Handled = true;
    }

    private bool AppKeys { get { lock (_buf.SyncRoot) return _buf.AppCursorKeys; } }


    private (int Line, int Col) HitTest(Point p)
    {
        lock (_buf.SyncRoot)
        {
            int row = Math.Clamp((int)((p.Y - Padding) / _cellH), 0, _buf.Rows - 1);
            int col = Math.Clamp((int)((p.X - Padding) / _cellW), 0, _buf.Columns - 1);
            return (_buf.Scrollback.Count - _viewOffset + row, col);
        }
    }

    private bool SelectionOnLine(int line, out int from, out int to)
    {
        from = to = 0;
        if (_selStart is not { } a || _selEnd is not { } b) return false;
        if (b.Line < a.Line || (b.Line == a.Line && b.Col < a.Col)) (a, b) = (b, a);
        if (line < a.Line || line > b.Line) return false;
        from = line == a.Line ? a.Col : 0;
        to = line == b.Line ? b.Col : _buf.Columns - 1;
        return to >= from;
    }

    public bool HasSelection => _selStart != null && _selEnd != null && _selStart != _selEnd;

    public string SelectedText
    {
        get
        {
            if (_selStart is not { } a || _selEnd is not { } b) return string.Empty;
            lock (_buf.SyncRoot) return _buf.GetText(a.Line, a.Col, b.Line, b.Col);
        }
    }

    public void ClearSelection()
    {
        if (_selStart == null) return;
        _selStart = _selEnd = null;
        _renderedVersion = -1;
        RequestRender();
    }

    public void SelectAll()
    {
        lock (_buf.SyncRoot)
        {
            _selStart = (0, 0);
            _selEnd = (_buf.TotalLines - 1, _buf.Columns - 1);
        }
        _renderedVersion = -1;
        RequestRender();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var pos = HitTest(e.GetPosition(this));
        if (e.ClickCount == 2) SelectWord(pos);
        else if (e.ClickCount >= 3)
        {
            _selStart = (pos.Line, 0);
            lock (_buf.SyncRoot) _selEnd = (pos.Line, _buf.Columns - 1);
        }
        else
        {
            _selStart = _selEnd = pos;
            _selecting = true;
            CaptureMouse();
        }
        _renderedVersion = -1;
        RequestRender();
        e.Handled = true;
    }

    private void SelectWord((int Line, int Col) pos)
    {
        lock (_buf.SyncRoot)
        {
            var line = _buf.LineAt(pos.Line);
            if (line == null) return;
            static bool Word(int r) => r > 0 && (char.IsLetterOrDigit((char)Math.Min(r, 0xFFFF)) || "-_./\\:~@".Contains((char)r));
            int a = pos.Col, b = pos.Col;
            while (a > 0 && a - 1 < line.Cells.Length && Word(line.Cells[a - 1].Rune)) a--;
            while (b + 1 < line.Cells.Length && Word(line.Cells[b + 1].Rune)) b++;
            _selStart = (pos.Line, a);
            _selEnd = (pos.Line, b);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_selecting) return;
        var p = e.GetPosition(this);
        if (p.Y < 0) ScrollBy(1);
        else if (p.Y > ActualHeight) ScrollBy(-1);
        _selEnd = HitTest(p);
        _renderedVersion = -1;
        RequestRender();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_selecting) return;
        _selecting = false;
        ReleaseMouseCapture();
        if (_selStart == _selEnd) ClearSelection();
    }


    public void Paste(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        text = text.Replace("\r\n", "\r").Replace('\n', '\r');
        bool bracketed;
        lock (_buf.SyncRoot) bracketed = _buf.BracketedPaste;
        Send(bracketed ? "\x1b[200~" + text + "\x1b[201~" : text);
    }

    private void Send(string s)
    {
        if (_viewOffset != 0) ScrollTo(0);
        if (HasSelection) ClearSelection();
        Input?.Invoke(s);
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        string t = e.Text;
        if (string.IsNullOrEmpty(t) || t.Any(ch => ch < 0x20 && ch != '\t')) return;
        Send(t);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var mods = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool ctrl = (mods & ModifierKeys.Control) != 0, alt = (mods & ModifierKeys.Alt) != 0, shift = (mods & ModifierKeys.Shift) != 0;
        string? seq = KeySequence(key, ctrl, alt, shift, AppKeys);
        if (seq != null)
        {
            Send(seq);
            e.Handled = true;
        }
    }

    public static string? KeySequence(Key key, bool ctrl, bool alt, bool shift, bool appCursor)
    {
        int mod = 1 + (shift ? 1 : 0) + (alt ? 2 : 0) + (ctrl ? 4 : 0);
        string Csi(string final) => mod == 1 ? "\x1b[" + final : $"\x1b[1;{mod}{final}";
        string Tilde(int n) => mod == 1 ? $"\x1b[{n}~" : $"\x1b[{n};{mod}~";
        string Arrow(char c) => mod == 1 ? (appCursor ? "\x1bO" + c : "\x1b[" + c) : $"\x1b[1;{mod}{c}";

        switch (key)
        {
            case Key.Enter: return alt ? "\x1b\r" : "\r";
            case Key.Back: return ctrl ? "\x08" : alt ? "\x1b\x7f" : "\x7f";
            case Key.Tab: return shift ? "\x1b[Z" : "\t";
            case Key.Escape: return "\x1b";
            case Key.Up: return Arrow('A');
            case Key.Down: return Arrow('B');
            case Key.Right: return Arrow('C');
            case Key.Left: return Arrow('D');
            case Key.Home: return Csi("H");
            case Key.End: return Csi("F");
            case Key.Insert: return Tilde(2);
            case Key.Delete: return Tilde(3);
            case Key.PageUp: return Tilde(5);
            case Key.PageDown: return Tilde(6);
            case Key.F1: return mod == 1 ? "\x1bOP" : $"\x1b[1;{mod}P";
            case Key.F2: return mod == 1 ? "\x1bOQ" : $"\x1b[1;{mod}Q";
            case Key.F3: return mod == 1 ? "\x1bOR" : $"\x1b[1;{mod}R";
            case Key.F4: return mod == 1 ? "\x1bOS" : $"\x1b[1;{mod}S";
            case Key.F5: return Tilde(15);
            case Key.F6: return Tilde(17);
            case Key.F7: return Tilde(18);
            case Key.F8: return Tilde(19);
            case Key.F9: return Tilde(20);
            case Key.F10: return Tilde(21);
            case Key.F11: return Tilde(23);
            case Key.F12: return Tilde(24);
        }
        if (ctrl && !alt)
        {
            if (key >= Key.A && key <= Key.Z) return ((char)(key - Key.A + 1)).ToString();
            switch (key)
            {
                case Key.Space: case Key.D2: return "\0";
                case Key.OemOpenBrackets: return "\x1b";
                case Key.Oem5: return "\x1c";
                case Key.Oem6: return "\x1d";
            }
        }
        if (alt && !ctrl && key >= Key.A && key <= Key.Z)
        {
            char c = (char)('a' + (key - Key.A));
            return "\x1b" + (shift ? char.ToUpperInvariant(c) : c);
        }
        return null;
    }
}
