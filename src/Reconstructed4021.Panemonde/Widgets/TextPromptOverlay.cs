namespace Reconstructed4021.Panemonde.Widgets;

// A single-field text prompt popup (e.g. LaunchFleetCommand's "What name shall we use for this
// fleet?", or a save-game filename) -- Enter submits (trimmed, may be empty), Esc cancels with no
// callback at all. Colors are caller-supplied, matching every other widget here (BoxDrawing,
// TextInputField, TabFrame) -- IOverlay.Draw takes only the FrameBuffer, so they're constructor
// parameters instead of Draw parameters, with TextInputField's own contrasting default pair as the
// fallback for a caller with no reason to pick something else.
public sealed class TextPromptOverlay : IOverlay
{
    private readonly string _title;
    private readonly string _label;
    private readonly TextInputField _field;
    private readonly Action<string> _onSubmit;
    private readonly ConsoleColor _borderFg;
    private readonly ConsoleColor _borderBg;
    private readonly ConsoleColor _fieldFg;
    private readonly ConsoleColor _fieldBg;

    public bool IsDismissed { get; private set; }

    public TextPromptOverlay(string title, string label, string initialText, Action<string> onSubmit, int maxLength = 40,
        ConsoleColor? borderFg = null, ConsoleColor? borderBg = null, ConsoleColor? fieldFg = null, ConsoleColor? fieldBg = null)
    {
        _title = title;
        _label = label;
        _field = new TextInputField(initialText, maxLength);
        _onSubmit = onSubmit;
        _borderFg = borderFg ?? ConsoleColor.Gray;
        _borderBg = borderBg ?? ConsoleColor.Black;
        _fieldFg = fieldFg ?? TextInputField.DefaultFg;
        _fieldBg = fieldBg ?? TextInputField.DefaultBg;
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Enter:
                IsDismissed = true;
                _onSubmit(_field.Text.Trim());
                return;
            case ConsoleKey.Escape:
                IsDismissed = true;
                return;
        }

        _field.HandleKey(key);
    }

    public void Draw(FrameBuffer fb)
    {
        const int width = 50;
        const int height = 5;
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, _borderFg, _borderBg);
        var titleText = $" {_title} ";
        fb.DrawText(x + Math.Max(1, (width - titleText.Length) / 2), y, titleText, _borderFg, _borderBg);
        fb.DrawText(x + 1, y + 1, _label, _borderFg, _borderBg);
        _field.Draw(fb, x + 1, y + 2, width - 2, _fieldFg, _fieldBg);
        fb.DrawText(x + 1, y + height - 2, "Enter: confirm   Esc: cancel", _borderFg, _borderBg);
    }
}
