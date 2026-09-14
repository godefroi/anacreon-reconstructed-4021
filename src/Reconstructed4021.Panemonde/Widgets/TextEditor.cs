using System.Text;

namespace Reconstructed4021.Panemonde.Widgets;

// A multi-line editable text buffer with a real 2D cursor -- TextInputField's end-of-string-only
// editing doesn't fit a script with multiple lines (fleet orders today; anything else that needs a
// small in-place text editor later). Deliberately plain: no undo, no selection/clipboard, no
// word-wrap -- add those on the same "second real demonstrated need" basis everything else in this
// project earns an abstraction, not preemptively.
//
// A 1-column gutter can mark one line with a glyph (MarkedLine, 1-based -- matching
// FleetOrderCompiler's own line-numbering convention) instead of inverting that whole line's colors:
// a gutter mark doesn't fight with anything else that might want to color a row later (a cursor-row
// highlight, a future error indicator), the way a full-line color swap would. Terminal.Gui's own
// Editor already draws exactly this kind of gutter; this is that same idea, not a new one.
public sealed class TextEditor
{
    private readonly List<string> _lines;
    private int _scrollRow;
    private int _scrollCol;

    public int CursorRow { get; private set; }
    public int CursorCol { get; private set; }

    /// <summary>1-based line number to mark in the gutter, or null for no mark.</summary>
    public int? MarkedLine { get; set; }
    public char MarkedLineGlyph { get; set; } = '►';

    public IReadOnlyList<string> Lines => _lines;
    public string Text => string.Join('\n', _lines);

    public TextEditor(string initialText)
    {
        _lines = [.. initialText.Split('\n')];
        if (_lines.Count == 0)
        {
            _lines.Add(string.Empty);
        }
    }

    // Returns true if this key was consumed as an edit/navigation -- false for anything a caller might
    // want to handle itself (e.g. Esc to compile-and-close, Ctrl+N to mark the current line).
    public bool HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                if (CursorCol > 0)
                {
                    CursorCol--;
                }
                else if (CursorRow > 0)
                {
                    CursorRow--;
                    CursorCol = _lines[CursorRow].Length;
                }

                return true;
            case ConsoleKey.RightArrow:
                if (CursorCol < _lines[CursorRow].Length)
                {
                    CursorCol++;
                }
                else if (CursorRow < _lines.Count - 1)
                {
                    CursorRow++;
                    CursorCol = 0;
                }

                return true;
            case ConsoleKey.UpArrow:
                if (CursorRow > 0)
                {
                    CursorRow--;
                    CursorCol = Math.Min(CursorCol, _lines[CursorRow].Length);
                }

                return true;
            case ConsoleKey.DownArrow:
                if (CursorRow < _lines.Count - 1)
                {
                    CursorRow++;
                    CursorCol = Math.Min(CursorCol, _lines[CursorRow].Length);
                }

                return true;
            case ConsoleKey.Home:
                CursorCol = 0;
                return true;
            case ConsoleKey.End:
                CursorCol = _lines[CursorRow].Length;
                return true;
            case ConsoleKey.Backspace:
                if (CursorCol > 0)
                {
                    _lines[CursorRow] = _lines[CursorRow].Remove(CursorCol - 1, 1);
                    CursorCol--;
                }
                else if (CursorRow > 0)
                {
                    var joinCol = _lines[CursorRow - 1].Length;
                    _lines[CursorRow - 1] += _lines[CursorRow];
                    _lines.RemoveAt(CursorRow);
                    CursorRow--;
                    CursorCol = joinCol;
                }

                return true;
            case ConsoleKey.Delete:
                if (CursorCol < _lines[CursorRow].Length)
                {
                    _lines[CursorRow] = _lines[CursorRow].Remove(CursorCol, 1);
                }
                else if (CursorRow < _lines.Count - 1)
                {
                    _lines[CursorRow] += _lines[CursorRow + 1];
                    _lines.RemoveAt(CursorRow + 1);
                }

                return true;
            case ConsoleKey.Enter:
            {
                var remainder = _lines[CursorRow][CursorCol..];
                _lines[CursorRow] = _lines[CursorRow][..CursorCol];
                _lines.Insert(CursorRow + 1, remainder);
                CursorRow++;
                CursorCol = 0;
                return true;
            }
        }

        if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
        {
            _lines[CursorRow] = _lines[CursorRow].Insert(CursorCol, key.KeyChar.ToString());
            CursorCol++;
            return true;
        }

        return false;
    }

    public void Draw(FrameBuffer fb, int x, int y, int width, int height, ConsoleColor fg, ConsoleColor bg, ConsoleColor gutterFg, ConsoleColor cursorFg, ConsoleColor cursorBg)
    {
        const int gutterWidth = 1;
        var textX = x + gutterWidth;
        var textWidth = Math.Max(0, width - gutterWidth);

        EnsureCursorVisible(height, textWidth);

        for (var row = 0; row < height; row++)
        {
            var lineIndex = _scrollRow + row;
            var isRealLine = lineIndex < _lines.Count;

            var gutterChar = isRealLine && lineIndex + 1 == MarkedLine ? MarkedLineGlyph : ' ';
            fb.Set(x, y + row, new Cell(new Rune(gutterChar), gutterFg, bg));

            var text = isRealLine ? _lines[lineIndex] : string.Empty;
            var visible = _scrollCol < text.Length ? text[_scrollCol..] : string.Empty;
            var padded = visible.Length > textWidth ? visible[..textWidth] : visible.PadRight(textWidth);
            fb.DrawText(textX, y + row, padded, fg, bg);

            if (isRealLine && lineIndex == CursorRow)
            {
                var cursorScreenCol = CursorCol - _scrollCol;
                if (cursorScreenCol >= 0 && cursorScreenCol < textWidth)
                {
                    var cursorChar = CursorCol < text.Length ? text[CursorCol] : ' ';
                    fb.Set(textX + cursorScreenCol, y + row, new Cell(new Rune(cursorChar), cursorFg, cursorBg));
                }
            }
        }
    }

    private void EnsureCursorVisible(int height, int textWidth)
    {
        if (CursorRow < _scrollRow)
        {
            _scrollRow = CursorRow;
        }
        else if (CursorRow >= _scrollRow + height)
        {
            _scrollRow = CursorRow - height + 1;
        }

        if (CursorCol < _scrollCol)
        {
            _scrollCol = CursorCol;
        }
        else if (textWidth > 0 && CursorCol >= _scrollCol + textWidth)
        {
            _scrollCol = CursorCol - textWidth + 1;
        }
    }
}
