using System.Text;
using Reconstructed4021.Panemonde;

namespace Reconstructed4021.Panemonde.Widgets;

// End-of-string editing only -- printable characters append, Backspace removes the last character, no
// mid-string cursor movement. A deliberate simplification (Terminal.Gui's TextField supports full
// cursor movement) matching what DOS-era InputString routines actually did; add real cursor movement
// only if a screen genuinely needs to edit in the middle of a string.
//
// Colors are the caller's own choice, not fixed here: this same field gets reused against very
// different backdrops (a popup over New Game's blue "paper", a black GameShell background for
// something like a fleet-rename prompt), so baking in one palette would just trade today's "field is
// indistinguishable from the background" bug for a different mismatch tomorrow. DefaultFg/DefaultBg
// below is a ready-made contrasting pair for a caller that doesn't have its own reason to pick
// something else -- not a hardcoded requirement.
public sealed class TextInputField
{
    public static readonly ConsoleColor DefaultFg = ConsoleColor.Black;
    public static readonly ConsoleColor DefaultBg = ConsoleColor.Gray;

    private readonly int _maxLength;

    public string Text { get; private set; }

    public TextInputField(string initialText, int maxLength)
    {
        Text = initialText;
        _maxLength = maxLength;
    }

    // Returns true if this key was consumed as a text edit -- false for anything else (arrows, Enter,
    // Esc, ...), which callers handle themselves.
    public bool HandleKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Backspace)
        {
            if (Text.Length > 0)
            {
                Text = Text[..^1];
            }

            return true;
        }

        // ConsoleKeyInfo.KeyChar is '\0' for arrows/function keys/etc. -- checking IsControl alone
        // would let those through as (invisible) appended characters.
        if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar) && Text.Length < _maxLength)
        {
            Text += key.KeyChar;
            return true;
        }

        return false;
    }

    public void Draw(FrameBuffer fb, int x, int y, int width, ConsoleColor fg, ConsoleColor bg)
    {
        var visible = Text.Length > width ? Text[^width..] : Text.PadRight(width);
        fb.DrawText(x, y, visible, fg, bg);

        // End-of-string editing only, so the insertion point is always exactly after the last
        // character -- shows where typing continues, distinct from a field that just has no cursor at
        // all once its background already matches the color fix above.
        if (Text.Length < width)
        {
            fb.Set(x + Text.Length, y, new Cell(new Rune('_'), fg, bg));
        }
    }
}
