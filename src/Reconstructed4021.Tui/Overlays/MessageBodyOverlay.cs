using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;

namespace Reconstructed4021.Tui.Overlays;

// SendMessageCommand's own message-body editor (DESIGN.PAS:948-954: InitializeEdit/EditText loop,
// Esc ends it) -- a thin host around TextEditor's own word-wrap mode (RMargin=77, TEXTSTRC.PAS:14)
// rather than a second from-scratch editor; see TextEditor's own doc comment for the wrap algorithm.
internal sealed class MessageBodyOverlay : IOverlay
{
    private const int RMargin = 77;
    private const int Width = RMargin + 4;
    private const int Height = 21;

    private readonly TextEditor _editor = new("", wrapMargin: RMargin);
    private readonly Action<IReadOnlyList<string>> _onSubmit;

    public bool IsDismissed { get; private set; }

    public MessageBodyOverlay(Action<IReadOnlyList<string>> onSubmit)
    {
        _onSubmit = onSubmit;
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)
        {
            IsDismissed = true;
            _onSubmit(_editor.Lines);
            return;
        }

        _editor.HandleKey(key);
    }

    public void Draw(FrameBuffer fb)
    {
        var width = Math.Min(Width, fb.Width);
        var height = Math.Min(Height, fb.Height);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        fb.DrawText(x + 2, y, " Send Message: type your message, Esc when done ", ConsoleColor.White, ConsoleColor.Black, maxWidth: width - 4);
        _editor.Draw(fb, x + 1, y + 1, width - 2, height - 2, ConsoleColor.Gray, ConsoleColor.Black, ConsoleColor.Gray, ConsoleColor.Black, ConsoleColor.Gray);
    }
}
