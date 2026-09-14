using System.Text;

namespace Reconstructed4021.Panemonde;

// The one thing this prototype exists to test: diff back-against-front, batch the whole frame's
// escape sequences into a single string, and issue exactly one write syscall to the raw stdout
// stream. Terminal.Gui's own OutputBase.Write does a separate write per dirty row instead (see the
// SingleFlushAnsiOutput comment in Reconstructed4021.Tui.csproj) -- that's the specific mechanism
// under test here, not output correctness in general.
public sealed class FrameBuffer
{
    private int _width;
    private int _height;
    private readonly Stream _stdout;
    private Cell[] _front;
    private Cell[] _back;

    public int Width => _width;
    public int Height => _height;

    public FrameBuffer(int width, int height, Stream stdout)
    {
        _width = width;
        _height = height;
        _stdout = stdout;
        _front = new Cell[width * height];
        _back = new Cell[width * height];
        Array.Fill(_front, Cell.Blank);
        Array.Fill(_back, Cell.Blank);
    }

    // ScreenHost's own resize detection (Console.WindowWidth/Height changed since the last frame) --
    // every screen already recomputes its own layout fresh from Width/Height on every Draw call (no
    // screen in this project caches a size once and reuses it, GalaxyMapScreen's initial viewport
    // centering aside), so reallocating here and letting the next Draw()/Present() cycle repaint from
    // scratch is the whole fix. Front and back both reset to blank rather than trying to preserve or
    // remap old content across a size change that no caller asked for.
    public void Resize(int width, int height)
    {
        _width = width;
        _height = height;
        _front = new Cell[width * height];
        _back = new Cell[width * height];
        Array.Fill(_front, Cell.Blank);
        Array.Fill(_back, Cell.Blank);
    }

    public void Set(int x, int y, Cell cell)
    {
        _back[(y * _width) + x] = cell;
    }

    public void Clear(Cell fill)
    {
        Array.Fill(_back, fill);
    }

    // Draws every character of text (including literal spaces -- callers that want spaces to show
    // whatever's already behind them, like AnacreonTitleWindow's title-over-stars, skip those cells
    // themselves and call Set directly instead of using this helper). Out-of-bounds columns/rows are
    // silently clipped, matching Terminal.Gui's own Label clipping at the view edge.
    //
    // maxWidth clips to a caller-owned logical width (e.g. the interior of a bordered box) rather than
    // the buffer's own absolute bounds -- without it, screens kept reinventing the same
    // "text.Length > w - x ? text[..(w - x)] : text" arithmetic themselves (WorldInfoOverlay,
    // CloseUpOverlay) to keep fixed-layout content from spilling past its own frame's border.
    public void DrawText(int x, int y, ReadOnlySpan<char> text, ConsoleColor fg, ConsoleColor bg, int? maxWidth = null)
    {
        if (y < 0 || y >= _height)
        {
            return;
        }

        var length = maxWidth is int w ? Math.Min(text.Length, Math.Max(0, w)) : text.Length;
        for (var i = 0; i < length; i++)
        {
            var col = x + i;
            if (col < 0 || col >= _width)
            {
                continue;
            }

            Set(col, y, new Cell(new Rune(text[i]), fg, bg));
        }
    }

    // Diffs _back against _front, writes one batched frame to stdout, swaps the buffers so _back
    // (now the stale copy) is ready to be overwritten by the next frame's Set() calls. Returns the
    // byte count actually written, so callers can report bytes/frame without re-encoding.
    public int Present()
    {
        var sb = new StringBuilder();
        var lastFg = (ConsoleColor?)null;
        var lastBg = (ConsoleColor?)null;
        var cursorRow = -1;
        var cursorCol = -1;

        for (var y = 0; y < _height; y++)
        {
            var x = 0;
            while (x < _width)
            {
                var idx = (y * _width) + x;
                if (_back[idx].Equals(_front[idx]))
                {
                    x++;
                    continue;
                }

                if (cursorRow != y || cursorCol != x)
                {
                    sb.Append("\x1b[").Append(y + 1).Append(';').Append(x + 1).Append('H');
                }

                while (x < _width)
                {
                    idx = (y * _width) + x;
                    if (_back[idx].Equals(_front[idx]))
                    {
                        break;
                    }

                    var cell = _back[idx];
                    if (cell.Fg != lastFg || cell.Bg != lastBg)
                    {
                        AppendSgr(sb, cell.Fg, cell.Bg);
                        lastFg = cell.Fg;
                        lastBg = cell.Bg;
                    }

                    sb.Append(cell.Glyph);
                    x++;
                }

                cursorRow = y;
                cursorCol = x;
            }
        }

        if (sb.Length == 0)
        {
            (_front, _back) = (_back, _front);
            return 0;
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        _stdout.Write(bytes, 0, bytes.Length);
        _stdout.Flush();

        (_front, _back) = (_back, _front);
        return bytes.Length;
    }

    // 16-color ANSI (30-37/90-97 fg, 40-47/100-107 bg) for everything except DarkRed: DOS's CGA red
    // (COLORS.INC value 4, used for the menu bar, help line, unscouted-sector color, and title/logo
    // text) renders noticeably brighter/more orange as ANSI 31 than the real DOSBox color -- tui1's
    // own DosColors.cs made the same fix, measured directly off a screenshot at r=154 g=0 b=0. Special
    // -cased here rather than widening Cell/DrawText to a general truecolor type, since DarkRed is the
    // only color this project needs corrected.
    private static void AppendSgr(StringBuilder sb, ConsoleColor fg, ConsoleColor bg)
    {
        sb.Append("\x1b[");
        AppendColorCode(sb, fg, isBackground: false);
        sb.Append(';');
        AppendColorCode(sb, bg, isBackground: true);
        sb.Append('m');
    }

    private static void AppendColorCode(StringBuilder sb, ConsoleColor color, bool isBackground)
    {
        if (color == ConsoleColor.DarkRed)
        {
            sb.Append(isBackground ? "48;2;154;0;0" : "38;2;154;0;0");
            return;
        }

        sb.Append(AnsiCode(color, isBackground));
    }

    private static int AnsiCode(ConsoleColor color, bool isBackground)
    {
        // ConsoleColor's own numeric values aren't in ANSI order: bit 3 is intensity in both, but the
        // low 3 bits are Windows console's BGR bit order (Blue=1, Green=2, Red=4 -- the Win32 console
        // API's FOREGROUND_BLUE/GREEN/RED flag values), not ANSI's RGB order (Red=1, Green=2, Blue=4).
        // Using the raw low 3 bits directly swaps every red and blue (confirmed against a real
        // terminal: DarkRed rendered as blue) -- bit 0 and bit 2 have to be swapped, bit 1 (green)
        // stays put.
        var bright = ((int)color & 8) != 0;
        var raw = (int)color & 7;
        var baseIndex = (raw & 0b010) | ((raw & 0b100) >> 2) | ((raw & 0b001) << 2);
        var ansiBase = isBackground ? 40 : 30;
        return bright ? ansiBase + 60 + baseIndex : ansiBase + baseIndex;
    }

    // Used only by the --bench self-check: confirms Present() actually made _front match what was
    // requested, so a diff/swap bug fails loudly instead of silently under-rendering.
    public bool FrontMatches(Cell[] expectedBack)
    {
        return _front.AsSpan().SequenceEqual(expectedBack);
    }

    public Cell[] SnapshotBack()
    {
        return (Cell[])_back.Clone();
    }

    // Plain-text render of whatever's currently on the front buffer (i.e. the last frame Present()
    // wrote), one string per row with trailing spaces trimmed -- for PanemondeDriver's DUMP script
    // instruction. No color/attribute info, same tradeoff TuiDriver's own screen dump makes.
    public IReadOnlyList<string> RenderText()
    {
        var lines = new string[_height];
        for (var y = 0; y < _height; y++)
        {
            var chars = new char[_width];
            for (var x = 0; x < _width; x++)
            {
                var rune = _front[(y * _width) + x].Glyph;
                chars[x] = rune.IsBmp ? (char)rune.Value : '?';
            }

            lines[y] = new string(chars).TrimEnd();
        }

        return lines;
    }
}
