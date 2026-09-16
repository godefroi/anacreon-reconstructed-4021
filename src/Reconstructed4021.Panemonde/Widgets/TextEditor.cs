using System.Text;

namespace Reconstructed4021.Panemonde.Widgets;

// A multi-line editable text buffer with a real 2D cursor -- TextInputField's end-of-string-only
// editing doesn't fit a script with multiple lines (fleet orders; Send Message's own body, the second
// real need this earned an optional word-wrap mode on). Deliberately plain otherwise: no undo, no
// selection/clipboard -- add those on the same "second real demonstrated need" basis everything else
// in this project earns an abstraction, not preemptively.
//
// A 1-column gutter can mark one line with a glyph (MarkedLine, 1-based -- matching
// FleetOrderCompiler's own line-numbering convention) instead of inverting that whole line's colors:
// a gutter mark doesn't fight with anything else that might want to color a row later (a cursor-row
// highlight, a future error indicator), the way a full-line color swap would. Terminal.Gui's own
// Editor already draws exactly this kind of gutter; this is that same idea, not a new one.
public sealed class TextEditor
{
    // One entry per on-screen row. EndsParagraph marks the LAST line of a paragraph -- with no
    // wrapMargin (fleet orders' own use), every line is its own one-line paragraph and this is always
    // true, matching plain line-based editing exactly; with a wrapMargin, a real Enter sets it true on
    // the line it split, and word-wrap's own line breaks leave it false on every line but the last.
    private sealed class Line(string text, bool endsParagraph)
    {
        public string Text = text;
        public bool EndsParagraph = endsParagraph;
    }

    private readonly List<Line> _lines;
    private readonly int? _wrapMargin;
    private int _scrollRow;
    private int _scrollCol;

    public int CursorRow { get; private set; }
    public int CursorCol { get; private set; }

    /// <summary>1-based line number to mark in the gutter, or null for no mark.</summary>
    public int? MarkedLine { get; set; }
    public char MarkedLineGlyph { get; set; } = '►';

    public IReadOnlyList<string> Lines => [.. _lines.Select(l => l.Text)];
    public string Text => string.Join('\n', Lines);

    /// <param name="wrapMargin">
    /// Null (the default): plain line-based editing, byte-identical to this class's own pre-word-wrap
    /// behavior -- Enter always makes a new line, Backspace/Delete never reflow anything. A value turns
    /// on EDIT.PAS-style word-wrap (RMargin=77 there; Send Message is the one caller that passes it) --
    /// see <see cref="RewrapParagraphAt"/>'s own doc comment for how that's implemented.
    /// </param>
    public TextEditor(string initialText, int? wrapMargin = null)
    {
        _wrapMargin = wrapMargin;
        var rawLines = initialText.Split('\n');
        _lines = [];
        foreach (var line in rawLines)
        {
            _lines.Add(new Line(line, endsParagraph: true));
        }

        if (_lines.Count == 0)
        {
            _lines.Add(new Line("", endsParagraph: true));
        }

        if (wrapMargin is { } margin)
        {
            // Each seed line starts out as its own one-line paragraph; re-wrapping one that's already
            // short is a no-op (WrapParagraph returns it unchanged), and one that's long expands in
            // place, so a plain left-to-right pass over the (possibly growing) list is enough --
            // re-wrapping an already-correctly-wrapped continuation line a second time is still a
            // no-op, not a source of drift.
            for (var i = 0; i < _lines.Count; i++)
            {
                RewrapParagraphAt(i, margin);
            }
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
                    CursorCol = _lines[CursorRow].Text.Length;
                }

                return true;
            case ConsoleKey.RightArrow:
                if (CursorCol < _lines[CursorRow].Text.Length)
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
                    CursorCol = Math.Min(CursorCol, _lines[CursorRow].Text.Length);
                }

                return true;
            case ConsoleKey.DownArrow:
                if (CursorRow < _lines.Count - 1)
                {
                    CursorRow++;
                    CursorCol = Math.Min(CursorCol, _lines[CursorRow].Text.Length);
                }

                return true;
            case ConsoleKey.Home:
                CursorCol = 0;
                return true;
            case ConsoleKey.End:
                CursorCol = _lines[CursorRow].Text.Length;
                return true;
            case ConsoleKey.Backspace:
                if (_wrapMargin is { } backspaceMargin)
                {
                    WrappedBackspace(backspaceMargin);
                    return true;
                }

                if (CursorCol > 0)
                {
                    _lines[CursorRow].Text = _lines[CursorRow].Text.Remove(CursorCol - 1, 1);
                    CursorCol--;
                }
                else if (CursorRow > 0)
                {
                    var joinCol = _lines[CursorRow - 1].Text.Length;
                    _lines[CursorRow - 1].Text += _lines[CursorRow].Text;
                    _lines.RemoveAt(CursorRow);
                    CursorRow--;
                    CursorCol = joinCol;
                }

                return true;
            case ConsoleKey.Delete:
                if (_wrapMargin is { } deleteMargin)
                {
                    WrappedDelete(deleteMargin);
                    return true;
                }

                if (CursorCol < _lines[CursorRow].Text.Length)
                {
                    _lines[CursorRow].Text = _lines[CursorRow].Text.Remove(CursorCol, 1);
                }
                else if (CursorRow < _lines.Count - 1)
                {
                    _lines[CursorRow].Text += _lines[CursorRow + 1].Text;
                    _lines.RemoveAt(CursorRow + 1);
                }

                return true;
            case ConsoleKey.Enter:
                if (_wrapMargin is { } enterMargin)
                {
                    WrappedEnter(enterMargin);
                    return true;
                }

                var remainder = _lines[CursorRow].Text[CursorCol..];
                _lines[CursorRow].Text = _lines[CursorRow].Text[..CursorCol];
                _lines.Insert(CursorRow + 1, new Line(remainder, endsParagraph: true));
                CursorRow++;
                CursorCol = 0;
                return true;
        }

        if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
        {
            _lines[CursorRow].Text = _lines[CursorRow].Text.Insert(CursorCol, key.KeyChar.ToString());
            CursorCol++;
            if (_wrapMargin is { } insertMargin)
            {
                RewrapParagraphAt(CursorRow, insertMargin);
            }

            return true;
        }

        return false;
    }

    // InsertPageBreak (EDIT.PAS:398-431): splits the current line at the cursor. The first half always
    // ends a paragraph now (a real Enter happened right there); the new second half inherits whatever
    // the pre-split line's own EndsParagraph was (still mid-paragraph if the split happened partway
    // through a longer wrapped paragraph, itself paragraph-ending if the split was on that paragraph's
    // last line). Re-wrapping the new half afterward is BackwardWordWrap's own follow-up call: pulls
    // trailing wrapped content back up into it if it's now short enough to take more.
    private void WrappedEnter(int margin)
    {
        var line = _lines[CursorRow];
        var remainder = line.Text[CursorCol..];
        var wasEndsParagraph = line.EndsParagraph;
        line.Text = line.Text[..CursorCol];
        line.EndsParagraph = true;
        _lines.Insert(CursorRow + 1, new Line(remainder, wasEndsParagraph));
        CursorRow++;
        CursorCol = 0;
        RewrapParagraphAt(CursorRow, margin);
    }

    // DeleteChar's TmpX=1 branch (EDIT.PAS:304-396), collapsed into one case instead of transliterating
    // its own "join two paragraphs" vs. "reflow within one paragraph" split: whether the previous line
    // currently ends its own paragraph decides which one this is. Ending it: un-hard-break it (the two
    // paragraphs become one, no character lost -- only the paragraph gap disappears), matching Pascal's
    // own PrevLine^.Para:=False with no text change. Not ending it: the two lines were already the same
    // paragraph, so backspacing at this (purely visual) wrap point deletes the character right before
    // it, same as backspacing mid-paragraph anywhere else. Either way, RewrapParagraphAt re-derives the
    // paragraph's own line breaks from scratch afterward instead of reproducing ForwardWordWrap/
    // BackwardWordWrap's own incremental cascade -- see that method's own doc comment for why a
    // from-scratch re-wrap converges to the exact same result.
    private void WrappedBackspace(int margin)
    {
        if (CursorCol > 0)
        {
            _lines[CursorRow].Text = _lines[CursorRow].Text.Remove(CursorCol - 1, 1);
            CursorCol--;
            RewrapParagraphAt(CursorRow, margin);
            return;
        }

        if (CursorRow == 0)
        {
            return;
        }

        var prev = _lines[CursorRow - 1];
        if (prev.EndsParagraph)
        {
            prev.EndsParagraph = false;
        }
        else if (prev.Text.Length > 0)
        {
            prev.Text = prev.Text[..^1];
        }

        RewrapParagraphAt(CursorRow - 1, margin);
    }

    // DelKey (EDIT.PAS:448-452): only ever acts on a character strictly before the line's own end
    // (CurX<=CurLen) -- real Pascal's Delete key never joins the current line onto the next one at all
    // (only Backspace can join lines), so this deliberately does NOT mirror WrappedDelete's own
    // no-wrap-mode sibling above, which does cross into the next line.
    private void WrappedDelete(int margin)
    {
        if (CursorCol >= _lines[CursorRow].Text.Length)
        {
            return;
        }

        CursorCol++;
        WrappedBackspace(margin);
    }

    // ForwardWordWrap/BackwardWordWrap (EDIT.PAS:138-227), replaced by one from-scratch primitive: the
    // paragraph containing lineIndex is reconstructed into its full raw text (safe because a wrap cut
    // always keeps its separator character attached to the EARLIER piece -- see WrapParagraph's own doc
    // comment -- so plain concatenation exactly reverses it) and re-wrapped via WrapParagraph. A greedy
    // word-wrap of one fixed string has exactly one right answer, so re-deriving it from scratch here
    // converges to the identical line breaks EDIT.PAS's own incremental cascades converge to for that
    // same final text -- this just gets there without reproducing five separately-transliterated
    // insert/delete-time special cases. The one real behavioral difference from a verbatim port: a
    // forward cut can leave a line at margin+1 characters when the separator lands exactly there (see
    // WrapParagraph), while EDIT.PAS's own BackwardWordWrap only ever pulls a word back when the result
    // stays at or under margin -- so a line produced by backspacing near a wrap point can differ from
    // EDIT.PAS's own result by that one trailing character. Never visible more than one line at a time,
    // and gone the next time that paragraph gets edited (or the message is sent) since the whole
    // paragraph reflows again from scratch.
    private void RewrapParagraphAt(int lineIndex, int margin)
    {
        var start = lineIndex;
        while (start > 0 && !_lines[start - 1].EndsParagraph)
        {
            start--;
        }

        var end = lineIndex;
        while (!_lines[end].EndsParagraph)
        {
            end++;
        }

        var cursorInParagraph = CursorRow >= start && CursorRow <= end;
        var cursorOffset = 0;
        if (cursorInParagraph)
        {
            for (var i = start; i < CursorRow; i++)
            {
                cursorOffset += _lines[i].Text.Length;
            }

            cursorOffset += CursorCol;
        }

        var text = string.Concat(Enumerable.Range(start, end - start + 1).Select(i => _lines[i].Text));
        var ranges = WrapParagraph(text, margin);

        _lines.RemoveRange(start, end - start + 1);
        for (var i = 0; i < ranges.Count; i++)
        {
            var (s, e) = ranges[i];
            _lines.Insert(start + i, new Line(text[s..e], endsParagraph: i == ranges.Count - 1));
        }

        if (cursorInParagraph)
        {
            for (var i = 0; i < ranges.Count; i++)
            {
                if (cursorOffset <= ranges[i].End || i == ranges.Count - 1)
                {
                    CursorRow = start + i;
                    CursorCol = Math.Clamp(cursorOffset - ranges[i].Start, 0, ranges[i].End - ranges[i].Start);
                    break;
                }
            }
        }
    }

    // Greedy word-wrap of one paragraph's raw text (FormatLine, EDIT.PAS:118-136): keep the running
    // line under margin, cutting at the last space/hyphen at or before column margin+1 -- inclusive of
    // that separator, which is why plain concatenation of the resulting pieces exactly reconstructs the
    // original text (RewrapParagraphAt's own doc comment relies on this). A single "word" longer than
    // margin with no separator at all hard-breaks at exactly margin characters (Pascal's own PosX=1
    // fallback). Returns [start,end) ranges into `text` rather than the substrings themselves so a
    // caller can also use them to re-derive a cursor's own line/column after the splice.
    private static List<(int Start, int End)> WrapParagraph(string text, int margin)
    {
        var ranges = new List<(int, int)>();
        var start = 0;
        while (true)
        {
            if (text.Length - start <= margin)
            {
                ranges.Add((start, text.Length));
                return ranges;
            }

            var scan = start + margin; // 0-based index of the (margin+1)th character from start.
            while (scan > start && text[scan] != ' ' && text[scan] != '-')
            {
                scan--;
            }

            var cut = scan == start ? start + margin - 1 : scan;
            ranges.Add((start, cut + 1));
            start = cut + 1;
        }
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

            var text = isRealLine ? _lines[lineIndex].Text : string.Empty;
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
