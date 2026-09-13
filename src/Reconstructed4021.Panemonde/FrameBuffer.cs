using System.Text;

namespace Reconstructed4021.Panemonde;

// The one thing this prototype exists to test: diff back-against-front, batch the whole frame's
// escape sequences into a single string, and issue exactly one write syscall to the raw stdout
// stream. Terminal.Gui's own OutputBase.Write does a separate write per dirty row instead (see the
// SingleFlushAnsiOutput comment in Reconstructed4021.Tui.csproj) -- that's the specific mechanism
// under test here, not output correctness in general.
internal sealed class FrameBuffer
{
    private readonly int _width;
    private readonly int _height;
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

    public void Set(int x, int y, Cell cell)
    {
        _back[(y * _width) + x] = cell;
    }

    public void Clear(Cell fill)
    {
        Array.Fill(_back, fill);
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

    // ponytail: 16-color ANSI only (30-37/90-97 fg, 40-47/100-107 bg), no truecolor -- matches what
    // the synthetic scene actually uses (ConsoleColor), add 24-bit SGR only if a real palette needs it.
    private static void AppendSgr(StringBuilder sb, ConsoleColor fg, ConsoleColor bg)
    {
        sb.Append("\x1b[").Append(AnsiCode(fg, isBackground: false))
          .Append(';').Append(AnsiCode(bg, isBackground: true)).Append('m');
    }

    private static int AnsiCode(ConsoleColor color, bool isBackground)
    {
        // ConsoleColor's own numeric values aren't in ANSI order (e.g. DarkYellow=6 isn't ANSI
        // yellow), so this table is a real translation, not a formatting nicety.
        var bright = ((int)color & 8) != 0;
        var baseIndex = (int)color & 7;
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
}
