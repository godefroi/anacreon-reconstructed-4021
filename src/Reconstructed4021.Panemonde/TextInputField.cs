namespace Reconstructed4021.Panemonde;

// End-of-string editing only -- printable characters append, Backspace removes the last character, no
// mid-string cursor movement. A deliberate simplification (Terminal.Gui's TextField supports full
// cursor movement) matching what DOS-era InputString routines actually did; add real cursor movement
// only if a screen genuinely needs to edit in the middle of a string.
public sealed class TextInputField
{
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
    }
}
