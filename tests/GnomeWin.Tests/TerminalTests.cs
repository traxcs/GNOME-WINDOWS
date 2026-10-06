using System.Windows.Input;
using GnomeWin.Terminal;
using Xunit;

namespace GnomeWin.Tests;

public class TerminalTests
{
    private static TerminalBuffer Feed(int cols, int rows, string text)
    {
        var b = new TerminalBuffer(cols, rows);
        b.Feed(text);
        return b;
    }

    [Fact]
    public void Long_lines_soft_wrap_and_copy_as_one_line()
    {
        var b = Feed(5, 3, "abcdefg");
        Assert.Equal("abcde", b.ScreenLine(0).Text());
        Assert.Equal("fg", b.ScreenLine(1).Text().TrimEnd());
        Assert.True(b.ScreenLine(0).Wrapped);
        Assert.Equal("abcdefg", b.GetText(0, 0, 1, 4));
    }

    [Fact]
    public void Cursor_position_and_overwrite()
    {
        var b = Feed(10, 2, "hello\x1b[1;3HX");
        Assert.Equal("heXlo", b.ScreenLine(0).Text().TrimEnd());
        Assert.Equal(3, b.CursorX);
    }

    [Fact]
    public void Sgr_palette_and_true_colour()
    {
        var b = Feed(10, 1, "\x1b[1;31mA\x1b[0mB\x1b[38;2;10;20;30mC\x1b[48;5;200mD");
        var c = b.ScreenLine(0).Cells;
        Assert.Equal(0x01000001u, c[0].Fg);
        Assert.True(c[0].Flags.HasFlag(CellFlags.Bold));
        Assert.Equal(0u, c[1].Fg);
        Assert.Equal(CellFlags.None, c[1].Flags);
        Assert.Equal(0x02000000u | 0x0A141Eu, c[2].Fg);
        Assert.Equal(0x01000000u | 200u, c[3].Bg);
    }

    [Fact]
    public void Lines_scrolled_off_go_to_scrollback()
    {
        var b = Feed(10, 2, "1\r\n2\r\n3");
        Assert.Single(b.Scrollback);
        Assert.Equal("1", b.Scrollback[0].Text());
        Assert.Equal("2", b.ScreenLine(0).Text().TrimEnd());
        Assert.Equal("3", b.ScreenLine(1).Text().TrimEnd());
    }

    [Fact]
    public void Alternate_screen_leaves_the_main_screen_and_history_untouched()
    {
        var b = Feed(10, 2, "main\x1b[?1049hfull\r\nscreen\r\nmore\x1b[?1049l");
        Assert.False(b.AltScreen);
        Assert.Equal("main", b.ScreenLine(0).Text().TrimEnd());
        Assert.Empty(b.Scrollback);
    }

    [Fact]
    public void Cursor_position_report_is_answered()
    {
        var b = new TerminalBuffer(10, 5);
        string? reply = null;
        b.Reply += r => reply = r;
        b.Feed("\x1b[2;3H\x1b[6n");
        Assert.Equal("\x1b[2;3R", reply);
    }

    [Fact]
    public void Osc_sets_the_title_and_is_not_printed()
    {
        var b = Feed(20, 1, "\x1b]0;user@host: ~/src\x07$ ");
        Assert.Equal("user@host: ~/src", b.Title);
        Assert.Equal("$", b.ScreenLine(0).Text().TrimEnd());
    }

    [Fact]
    public void Wide_characters_take_two_cells()
    {
        var b = Feed(10, 1, "日a");
        var c = b.ScreenLine(0).Cells;
        Assert.Equal('日', c[0].Rune);
        Assert.Equal(-1, c[1].Rune);
        Assert.Equal('a', c[2].Rune);
        Assert.Equal("日a", b.ScreenLine(0).Text().TrimEnd());
    }

    [Fact]
    public void Erase_and_delete_characters()
    {
        var b = Feed(10, 2, "abcdef\x1b[1;3H\x1b[2P");
        Assert.Equal("abef", b.ScreenLine(0).Text().TrimEnd());
        b.Feed("\x1b[2K");
        Assert.Equal(string.Empty, b.ScreenLine(0).Text().Trim());
    }

    [Fact]
    public void Shrinking_keeps_the_cursor_line_visible()
    {
        var b = Feed(10, 5, "1\r\n2\r\n3\r\n4\r\n5");
        b.Resize(10, 2);
        Assert.Equal(1, b.CursorY);
        Assert.Equal("5", b.ScreenLine(1).Text().TrimEnd());
        Assert.Equal(3, b.Scrollback.Count);
    }

    [Fact]
    public void Scroll_region_scrolls_only_inside()
    {
        var b = Feed(10, 4, "top\r\n\x1b[2;3r\x1b[2;1Ha\r\nb\r\nc");
        Assert.Equal("top", b.ScreenLine(0).Text().TrimEnd());
        Assert.Equal("b", b.ScreenLine(1).Text().TrimEnd());
        Assert.Equal("c", b.ScreenLine(2).Text().TrimEnd());
        Assert.Empty(b.Scrollback);
    }

    [Fact]
    public void Keys_are_encoded_like_xterm()
    {
        Assert.Equal("\x1b[A", TerminalView.KeySequence(Key.Up, false, false, false, appCursor: false));
        Assert.Equal("\x1bOA", TerminalView.KeySequence(Key.Up, false, false, false, appCursor: true));
        Assert.Equal("\x1b[1;5D", TerminalView.KeySequence(Key.Left, ctrl: true, alt: false, shift: false, appCursor: false));
        Assert.Equal("\x03", TerminalView.KeySequence(Key.C, ctrl: true, alt: false, shift: false, appCursor: false));
        Assert.Equal("\x7f", TerminalView.KeySequence(Key.Back, false, false, false, false));
        Assert.Equal("\x1b[3~", TerminalView.KeySequence(Key.Delete, false, false, false, false));
        Assert.Null(TerminalView.KeySequence(Key.E, ctrl: true, alt: true, shift: false, appCursor: false));
        Assert.Null(TerminalView.KeySequence(Key.A, false, false, false, false));
    }
}
