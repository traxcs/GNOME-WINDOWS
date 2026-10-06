using System.Text;

namespace GnomeWin.Terminal;

[Flags]
public enum CellFlags : byte
{
    None = 0,
    Bold = 1,
    Dim = 2,
    Italic = 4,
    Underline = 8,
    Inverse = 16,
    Hidden = 32,
    Strike = 64,
}

public struct Cell
{
    public int Rune;
    public uint Fg, Bg;
    public CellFlags Flags;

    public readonly bool SameStyle(in Cell o) => Fg == o.Fg && Bg == o.Bg && Flags == o.Flags;
}

public sealed class TerminalLine
{
    public Cell[] Cells;
    public bool Wrapped;

    public TerminalLine(int columns, in Cell blank)
    {
        Cells = new Cell[columns];
        if (blank.Bg != 0) Array.Fill(Cells, blank);
    }

    public string Text(int from = 0, int to = int.MaxValue)
    {
        var sb = new StringBuilder();
        to = Math.Min(to, Cells.Length);
        for (int i = Math.Max(0, from); i < to; i++)
        {
            int r = Cells[i].Rune;
            if (r == -1) continue;
            sb.Append(r == 0 ? " " : char.ConvertFromUtf32(r));
        }
        return sb.ToString();
    }
}

public sealed class TerminalBuffer
{
    public const int MaxScrollback = 5000;

    public object SyncRoot { get; } = new();
    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public int CursorX { get; private set; }
    public int CursorY { get; private set; }
    public bool CursorVisible { get; private set; } = true;
    public bool AppCursorKeys { get; private set; }
    public bool BracketedPaste { get; private set; }
    public bool AltScreen => _screen == _alt;
    public string Title { get; private set; } = string.Empty;

    public IReadOnlyList<TerminalLine> Scrollback => _scrollback;
    public long Trimmed { get; private set; }
    public long Version { get; private set; }
    public bool[] DirtyRows { get; private set; } = Array.Empty<bool>();

    public event Action<string>? Reply;
    public event Action? Bell;

    private TerminalLine[] _main = Array.Empty<TerminalLine>(), _alt = Array.Empty<TerminalLine>(), _screen = Array.Empty<TerminalLine>();
    private readonly List<TerminalLine> _scrollback = new();
    private Cell _pen;
    private bool _pendingWrap, _autoWrap = true, _insert, _originMode;
    private int _top, _bottom;
    private (int X, int Y, Cell Pen, bool Origin) _saved, _savedMain;
    private int _lastRune = ' ';

    public TerminalBuffer(int columns, int rows)
    {
        Columns = Math.Max(2, columns);
        Rows = Math.Max(1, rows);
        _main = NewScreen(Columns, Rows);
        _alt = NewScreen(Columns, Rows);
        _screen = _main;
        _bottom = Rows - 1;
        DirtyRows = new bool[Rows];
        MarkAll();
    }

    private static TerminalLine[] NewScreen(int cols, int rows)
    {
        var s = new TerminalLine[rows];
        var blank = default(Cell);
        for (int i = 0; i < rows; i++) s[i] = new TerminalLine(cols, blank);
        return s;
    }

    public TerminalLine ScreenLine(int row) => _screen[row];

    public TerminalLine? LineAt(int index)
    {
        if (index < 0) return null;
        if (index < _scrollback.Count) return _scrollback[index];
        index -= _scrollback.Count;
        return index < Rows ? _screen[index] : null;
    }

    public int TotalLines => _scrollback.Count + Rows;

    private void Mark(int row) { if ((uint)row < (uint)DirtyRows.Length) DirtyRows[row] = true; Version++; }
    private void MarkAll() { Array.Fill(DirtyRows, true); Version++; }
    private void MarkRange(int from, int to) { for (int r = Math.Max(0, from); r <= Math.Min(Rows - 1, to); r++) DirtyRows[r] = true; Version++; }


    public void Resize(int columns, int rows)
    {
        columns = Math.Max(2, columns);
        rows = Math.Max(1, rows);
        if (columns == Columns && rows == Rows) return;

        int drop = Math.Max(0, CursorY - (rows - 1));
        if (!AltScreen) for (int i = 0; i < drop && i < Rows; i++) PushScrollback(_main[i]);
        _main = Remap(_main, columns, rows, AltScreen ? 0 : drop);
        _alt = Remap(_alt, columns, rows, AltScreen ? drop : 0);
        _screen = _screen == _alt ? _alt : _main;
        foreach (var l in _scrollback) if (l.Cells.Length > columns) l.Cells = l.Cells[..columns];

        Columns = columns;
        Rows = rows;
        CursorY = Math.Clamp(CursorY - drop, 0, rows - 1);
        CursorX = Math.Clamp(CursorX, 0, columns - 1);
        _pendingWrap = false;
        _top = 0;
        _bottom = rows - 1;
        DirtyRows = new bool[rows];
        MarkAll();
    }

    private static TerminalLine[] Remap(TerminalLine[] old, int cols, int rows, int skip)
    {
        var blank = default(Cell);
        var n = new TerminalLine[rows];
        for (int r = 0; r < rows; r++)
        {
            var line = new TerminalLine(cols, blank);
            int src = r + skip;
            if (src < old.Length)
            {
                Array.Copy(old[src].Cells, line.Cells, Math.Min(cols, old[src].Cells.Length));
                line.Wrapped = old[src].Wrapped;
            }
            n[r] = line;
        }
        return n;
    }

    private void PushScrollback(TerminalLine line)
    {
        int end = line.Cells.Length;
        while (end > 0 && line.Cells[end - 1].Rune == 0 && line.Cells[end - 1].Bg == 0 && line.Cells[end - 1].Flags == 0) end--;
        var copy = new TerminalLine(0, default) { Cells = line.Cells[..end], Wrapped = line.Wrapped };
        _scrollback.Add(copy);
        if (_scrollback.Count > MaxScrollback)
        {
            int extra = _scrollback.Count - MaxScrollback;
            _scrollback.RemoveRange(0, extra);
            Trimmed += extra;
        }
    }

    public void ClearScrollback()
    {
        Trimmed += _scrollback.Count;
        _scrollback.Clear();
        MarkAll();
    }


    private enum State { Ground, Escape, EscapeCharset, Csi, Osc, OscEscape, Dcs, DcsEscape }

    private State _state;
    private readonly List<int> _params = new(16);
    private int _param = -1;
    private char _private;
    private char _intermediate;
    private readonly StringBuilder _osc = new();
    private char _highSurrogate;

    public void Feed(string text)
    {
        foreach (char ch in text) FeedChar(ch);
    }

    private void FeedChar(char c)
    {
        switch (_state)
        {
            case State.Ground:
                if (c < 0x20 || c == 0x7F) { Control(c); return; }
                if (char.IsHighSurrogate(c)) { _highSurrogate = c; return; }
                if (char.IsLowSurrogate(c))
                {
                    if (_highSurrogate != 0) Print(char.ConvertToUtf32(_highSurrogate, c));
                    _highSurrogate = '\0';
                    return;
                }
                Print(c);
                return;

            case State.Escape:
                EscapeChar(c);
                return;

            case State.EscapeCharset:
                _state = State.Ground;
                return;

            case State.Csi:
                if (c >= '0' && c <= '9') { _param = (_param < 0 ? 0 : _param) * 10 + (c - '0'); if (_param > 65535) _param = 65535; return; }
                if (c == ';' || c == ':') { _params.Add(_param); _param = -1; return; }
                if (c is '?' or '>' or '=' or '<') { _private = c; return; }
                if (c >= 0x20 && c <= 0x2F) { _intermediate = c; return; }
                if (c >= 0x40 && c <= 0x7E)
                {
                    _params.Add(_param);
                    _state = State.Ground;
                    try { Csi(c); } catch (Exception) { /* malformed sequence: ignore */ }
                    return;
                }
                if (c < 0x20) { Control(c); return; }
                _state = State.Ground;
                return;

            case State.Osc:
                if (c == '\a') { Osc(); _state = State.Ground; return; }
                if (c == '\x1b') { _state = State.OscEscape; return; }
                if (_osc.Length < 4096) _osc.Append(c);
                return;

            case State.OscEscape:
                Osc();
                _state = State.Ground;
                if (c != '\\') FeedChar(c);
                return;

            case State.Dcs:
                if (c == '\x1b') _state = State.DcsEscape;
                return;

            case State.DcsEscape:
                _state = c == '\\' ? State.Ground : State.Dcs;
                return;
        }
    }

    private void Control(char c)
    {
        switch (c)
        {
            case '\x1b':
                _state = State.Escape;
                return;
            case '\a': Bell?.Invoke(); return;
            case '\b':
                _pendingWrap = false;
                if (CursorX > 0) CursorX--;
                Mark(CursorY);
                return;
            case '\t':
                _pendingWrap = false;
                CursorX = Math.Min(Columns - 1, (CursorX / 8 + 1) * 8);
                Mark(CursorY);
                return;
            case '\n': case '\v': case '\f':
                LineFeed();
                return;
            case '\r':
                _pendingWrap = false;
                CursorX = 0;
                Mark(CursorY);
                return;
        }
    }

    private void EscapeChar(char c)
    {
        _state = State.Ground;
        switch (c)
        {
            case '[':
                _state = State.Csi;
                _params.Clear();
                _param = -1;
                _private = '\0';
                _intermediate = '\0';
                return;
            case ']': _state = State.Osc; _osc.Clear(); return;
            case 'P': _state = State.Dcs; return;
            case '(': case ')': case '*': case '+': _state = State.EscapeCharset; return;
            case '7': SaveCursor(); return;
            case '8': RestoreCursor(); return;
            case 'D': LineFeed(); return;
            case 'E': CursorX = 0; LineFeed(); return;
            case 'M': ReverseIndex(); return;
            case 'c': FullReset(); return;
            default: return;
        }
    }

    private void Osc()
    {
        string s = _osc.ToString();
        int semi = s.IndexOf(';');
        if (semi > 0 && (s.StartsWith("0;") || s.StartsWith("2;")))
        {
            Title = s[(semi + 1)..];
            Version++;
        }
    }


    public static int RuneWidth(int r)
    {
        if (r < 0x300) return 1;
        if (r is >= 0x300 and <= 0x36F or 0x200B or 0x200C or 0x200D or >= 0xFE00 and <= 0xFE0F) return 0;
        if (r is >= 0x1100 and <= 0x115F or >= 0x2E80 and <= 0xA4CF or >= 0xAC00 and <= 0xD7A3 or >= 0xF900 and <= 0xFAFF
            or >= 0xFE30 and <= 0xFE4F or >= 0xFF00 and <= 0xFF60 or >= 0xFFE0 and <= 0xFFE6
            or >= 0x1F300 and <= 0x1F64F or >= 0x1F900 and <= 0x1F9FF or >= 0x20000 and <= 0x3FFFD) return 2;
        return 1;
    }

    private void Print(int rune)
    {
        int width = RuneWidth(rune);
        if (width == 0) return;
        _lastRune = rune;

        if (_pendingWrap)
        {
            if (_autoWrap)
            {
                _screen[CursorY].Wrapped = true;
                CursorX = 0;
                LineFeed();
            }
            _pendingWrap = false;
        }
        if (width == 2 && CursorX == Columns - 1)
        {
            if (!_autoWrap) return;
            _screen[CursorY].Wrapped = true;
            CursorX = 0;
            LineFeed();
        }

        var cells = _screen[CursorY].Cells;
        if (_insert) InsertBlanks(width);
        if (cells[CursorX].Rune == -1 && CursorX > 0) cells[CursorX - 1].Rune = 0;
        if (CursorX + width < Columns && cells[CursorX + width].Rune == -1) cells[CursorX + width].Rune = 0;

        cells[CursorX] = _pen with { Rune = rune };
        if (width == 2) cells[CursorX + 1] = _pen with { Rune = -1 };
        Mark(CursorY);

        CursorX += width;
        if (CursorX >= Columns)
        {
            CursorX = Columns - 1;
            _pendingWrap = _autoWrap;
        }
    }

    private Cell Blank => new() { Bg = _pen.Bg };

    private void LineFeed()
    {
        _pendingWrap = false;
        if (CursorY == _bottom) ScrollUp(1);
        else if (CursorY < Rows - 1) CursorY++;
        Mark(CursorY);
    }

    private void ReverseIndex()
    {
        _pendingWrap = false;
        if (CursorY == _top) ScrollDown(1);
        else if (CursorY > 0) CursorY--;
        Mark(CursorY);
    }

    private void ScrollUp(int n)
    {
        n = Math.Clamp(n, 1, _bottom - _top + 1);
        for (int i = 0; i < n; i++)
        {
            var gone = _screen[_top];
            if (_top == 0 && !AltScreen) PushScrollback(gone);
            for (int r = _top; r < _bottom; r++) _screen[r] = _screen[r + 1];
            _screen[_bottom] = new TerminalLine(Columns, Blank);
        }
        MarkRange(_top, _bottom);
    }

    private void ScrollDown(int n)
    {
        n = Math.Clamp(n, 1, _bottom - _top + 1);
        for (int i = 0; i < n; i++)
        {
            for (int r = _bottom; r > _top; r--) _screen[r] = _screen[r - 1];
            _screen[_top] = new TerminalLine(Columns, Blank);
        }
        MarkRange(_top, _bottom);
    }

    private void InsertBlanks(int n)
    {
        var cells = _screen[CursorY].Cells;
        n = Math.Min(n, Columns - CursorX);
        Array.Copy(cells, CursorX, cells, CursorX + n, Columns - CursorX - n);
        for (int i = 0; i < n; i++) cells[CursorX + i] = Blank;
        Mark(CursorY);
    }

    private void DeleteChars(int n)
    {
        var cells = _screen[CursorY].Cells;
        n = Math.Min(n, Columns - CursorX);
        Array.Copy(cells, CursorX + n, cells, CursorX, Columns - CursorX - n);
        for (int i = Columns - n; i < Columns; i++) cells[i] = Blank;
        Mark(CursorY);
    }

    private void EraseCells(int row, int from, int to)
    {
        var cells = _screen[row].Cells;
        var b = Blank;
        for (int i = Math.Max(0, from); i < Math.Min(Columns, to); i++) cells[i] = b;
        if (to >= Columns) _screen[row].Wrapped = false;
        Mark(row);
    }

    private void SaveCursor() => _saved = (CursorX, CursorY, _pen, _originMode);

    private void RestoreCursor()
    {
        CursorX = Math.Clamp(_saved.X, 0, Columns - 1);
        CursorY = Math.Clamp(_saved.Y, 0, Rows - 1);
        _pen = _saved.Pen;
        _originMode = _saved.Origin;
        _pendingWrap = false;
        Mark(CursorY);
    }

    private void FullReset()
    {
        _screen = _main;
        foreach (var l in _main) { Array.Clear(l.Cells); l.Wrapped = false; }
        _pen = default;
        CursorX = CursorY = 0;
        _top = 0;
        _bottom = Rows - 1;
        _autoWrap = true;
        _insert = _originMode = _pendingWrap = false;
        CursorVisible = true;
        AppCursorKeys = BracketedPaste = false;
        MarkAll();
    }


    private int P(int i, int def = 1) => i < _params.Count && _params[i] > 0 ? _params[i] : def;
    private int P0(int i) => i < _params.Count && _params[i] >= 0 ? _params[i] : 0;

    private void Csi(char final)
    {
        if (_private == '?') { PrivateMode(final); return; }
        if (_private != '\0' || _intermediate != '\0')
        {
            if (_private == '>' && final == 'c') Reply?.Invoke("\x1b[>0;10;1c");
            return;
        }

        switch (final)
        {
            case 'A': MoveTo(CursorX, Math.Max(CursorY - P(0), CursorY >= _top ? _top : 0)); break;
            case 'B': MoveTo(CursorX, Math.Min(CursorY + P(0), CursorY <= _bottom ? _bottom : Rows - 1)); break;
            case 'C': case 'a': MoveTo(CursorX + P(0), CursorY); break;
            case 'D': MoveTo(CursorX - P(0), CursorY); break;
            case 'E': MoveTo(0, Math.Min(CursorY + P(0), _bottom)); break;
            case 'F': MoveTo(0, Math.Max(CursorY - P(0), _top)); break;
            case 'G': case '`': MoveTo(P(0) - 1, CursorY); break;
            case 'd': MoveTo(CursorX, (_originMode ? _top : 0) + P(0) - 1); break;
            case 'H': case 'f': MoveTo(P(1) - 1, (_originMode ? _top : 0) + P(0) - 1); break;
            case 'J':
                switch (P0(0))
                {
                    case 0:
                        EraseCells(CursorY, CursorX, Columns);
                        for (int r = CursorY + 1; r < Rows; r++) EraseCells(r, 0, Columns);
                        break;
                    case 1:
                        for (int r = 0; r < CursorY; r++) EraseCells(r, 0, Columns);
                        EraseCells(CursorY, 0, CursorX + 1);
                        break;
                    case 2:
                        for (int r = 0; r < Rows; r++) EraseCells(r, 0, Columns);
                        break;
                    case 3:
                        ClearScrollback();
                        break;
                }
                break;
            case 'K':
                switch (P0(0))
                {
                    case 0: EraseCells(CursorY, CursorX, Columns); break;
                    case 1: EraseCells(CursorY, 0, CursorX + 1); break;
                    case 2: EraseCells(CursorY, 0, Columns); break;
                }
                break;
            case 'X': EraseCells(CursorY, CursorX, CursorX + P(0)); break;
            case '@': InsertBlanks(P(0)); break;
            case 'P': DeleteChars(P(0)); break;
            case 'L':
                if (CursorY >= _top && CursorY <= _bottom)
                {
                    int saveTop = _top;
                    _top = CursorY;
                    ScrollDown(P(0));
                    _top = saveTop;
                    CursorX = 0;
                }
                break;
            case 'M':
                if (CursorY >= _top && CursorY <= _bottom)
                {
                    int saveTop = _top;
                    _top = CursorY;
                    for (int i = 0; i < Math.Min(P(0), _bottom - CursorY + 1); i++)
                    {
                        for (int r = CursorY; r < _bottom; r++) _screen[r] = _screen[r + 1];
                        _screen[_bottom] = new TerminalLine(Columns, Blank);
                    }
                    MarkRange(CursorY, _bottom);
                    _top = saveTop;
                    CursorX = 0;
                }
                break;
            case 'S': ScrollUp(P(0)); break;
            case 'T': ScrollDown(P(0)); break;
            case 'b':
                for (int i = 0; i < Math.Min(P(0), Columns * Rows); i++) Print(_lastRune);
                break;
            case 'r':
            {
                int top = P(0) - 1, bottom = P(1, Rows) - 1;
                if (bottom > Rows - 1) bottom = Rows - 1;
                if (top < bottom)
                {
                    _top = top;
                    _bottom = bottom;
                    MoveTo(0, _originMode ? _top : 0);
                }
                break;
            }
            case 's': SaveCursor(); break;
            case 'u': RestoreCursor(); break;
            case 'm': Sgr(); break;
            case 'h': if (P0(0) == 4) _insert = true; break;
            case 'l': if (P0(0) == 4) _insert = false; break;
            case 'n':
                if (P0(0) == 5) Reply?.Invoke("\x1b[0n");
                else if (P0(0) == 6) Reply?.Invoke($"\x1b[{CursorY + 1};{CursorX + 1}R");
                break;
            case 'c':
                if (P0(0) == 0) Reply?.Invoke("\x1b[?1;2c");
                break;
        }
    }

    private void MoveTo(int x, int y)
    {
        _pendingWrap = false;
        Mark(CursorY);
        CursorX = Math.Clamp(x, 0, Columns - 1);
        CursorY = Math.Clamp(y, 0, Rows - 1);
        Mark(CursorY);
    }

    private void PrivateMode(char final)
    {
        if (final != 'h' && final != 'l') return;
        bool on = final == 'h';
        foreach (int mode in _params)
        {
            switch (mode)
            {
                case 1: AppCursorKeys = on; break;
                case 6: _originMode = on; MoveTo(0, on ? _top : 0); break;
                case 7: _autoWrap = on; break;
                case 25: CursorVisible = on; Version++; break;
                case 2004: BracketedPaste = on; break;
                case 47: case 1047: SwitchScreen(on, saveCursor: false); break;
                case 1049: SwitchScreen(on, saveCursor: true); break;
            }
        }
    }

    private void SwitchScreen(bool alt, bool saveCursor)
    {
        if (alt == AltScreen) return;
        if (alt)
        {
            if (saveCursor) _savedMain = (CursorX, CursorY, _pen, _originMode);
            _screen = _alt;
            foreach (var l in _alt) { Array.Clear(l.Cells); l.Wrapped = false; }
        }
        else
        {
            _screen = _main;
            if (saveCursor)
            {
                CursorX = Math.Clamp(_savedMain.X, 0, Columns - 1);
                CursorY = Math.Clamp(_savedMain.Y, 0, Rows - 1);
                _pen = _savedMain.Pen;
            }
        }
        _pendingWrap = false;
        MarkAll();
    }

    private void Sgr()
    {
        for (int i = 0; i < _params.Count; i++)
        {
            int p = _params[i] < 0 ? 0 : _params[i];
            switch (p)
            {
                case 0: _pen = default; break;
                case 1: _pen.Flags |= CellFlags.Bold; break;
                case 2: _pen.Flags |= CellFlags.Dim; break;
                case 3: _pen.Flags |= CellFlags.Italic; break;
                case 4: _pen.Flags |= CellFlags.Underline; break;
                case 7: _pen.Flags |= CellFlags.Inverse; break;
                case 8: _pen.Flags |= CellFlags.Hidden; break;
                case 9: _pen.Flags |= CellFlags.Strike; break;
                case 21: case 22: _pen.Flags &= ~(CellFlags.Bold | CellFlags.Dim); break;
                case 23: _pen.Flags &= ~CellFlags.Italic; break;
                case 24: _pen.Flags &= ~CellFlags.Underline; break;
                case 27: _pen.Flags &= ~CellFlags.Inverse; break;
                case 28: _pen.Flags &= ~CellFlags.Hidden; break;
                case 29: _pen.Flags &= ~CellFlags.Strike; break;
                case >= 30 and <= 37: _pen.Fg = 0x01000000u | (uint)(p - 30); break;
                case 39: _pen.Fg = 0; break;
                case >= 40 and <= 47: _pen.Bg = 0x01000000u | (uint)(p - 40); break;
                case 49: _pen.Bg = 0; break;
                case >= 90 and <= 97: _pen.Fg = 0x01000000u | (uint)(p - 90 + 8); break;
                case >= 100 and <= 107: _pen.Bg = 0x01000000u | (uint)(p - 100 + 8); break;
                case 38: case 48:
                {
                    uint color = 0;
                    int mode = i + 1 < _params.Count ? _params[i + 1] : -1;
                    if (mode == 5 && i + 2 < _params.Count)
                    {
                        color = 0x01000000u | (uint)Math.Clamp(_params[i + 2], 0, 255);
                        i += 2;
                    }
                    else if (mode == 2 && i + 4 < _params.Count)
                    {
                        int r = Math.Clamp(_params[i + 2], 0, 255), g = Math.Clamp(_params[i + 3], 0, 255), b = Math.Clamp(_params[i + 4], 0, 255);
                        color = 0x02000000u | (uint)(r << 16 | g << 8 | b);
                        i += 4;
                    }
                    else { i = _params.Count; break; }
                    if (p == 38) _pen.Fg = color; else _pen.Bg = color;
                    break;
                }
            }
        }
    }


    public string GetText(int line1, int col1, int line2, int col2)
    {
        if (line2 < line1 || (line2 == line1 && col2 < col1)) (line1, col1, line2, col2) = (line2, col2, line1, col1);
        var sb = new StringBuilder();
        for (int l = line1; l <= line2; l++)
        {
            var line = LineAt(l);
            if (line == null) break;
            int from = l == line1 ? col1 : 0;
            int to = l == line2 ? col2 + 1 : int.MaxValue;
            string t = line.Text(from, to);
            bool joins = line.Wrapped && l != line2;
            sb.Append(joins ? t : t.TrimEnd());
            if (!joins && l != line2) sb.Append(Environment.NewLine);
        }
        return sb.ToString();
    }

    public override string ToString() => string.Join("\n", Enumerable.Range(0, Rows).Select(r => _screen[r].Text().TrimEnd()));
}
