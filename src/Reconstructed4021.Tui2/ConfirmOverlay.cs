using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;

namespace Reconstructed4021.Tui2;

// GameShell.ShowConfirm/DosDialogWindow's own Yes/No shape -- Enter confirms, Esc/anything else
// declines. Pushed on top of whatever overlay asked for the confirmation (e.g. WorldInfoOverlay's own
// Designate risk warnings); the overlay stack's own top-only routing means the overlay underneath
// never sees a key while this is up.
internal sealed class ConfirmOverlay : IOverlay
{
    private readonly string _title;
    private readonly string[] _lines;
    private readonly Action<bool> _onAnswered;

    public bool IsDismissed { get; private set; }

    public ConfirmOverlay(string title, string message, Action<bool> onAnswered)
    {
        _title = title;
        _lines = message.Split('\n');
        _onAnswered = onAnswered;
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        IsDismissed = true;
        _onAnswered(key.Key == ConsoleKey.Enter);
    }

    private const string Hint = "Enter: yes   any other key: no";

    public void Draw(FrameBuffer fb)
    {
        // Hint's own length has to factor into width, and its clip has to account for the extra column
        // of left padding (text starts at x+2, one deeper than the box's x+1 interior) -- see
        // GalaxyMapScreen.DrawInfoPopup's own fix for the same overflow.
        var width = Math.Min(Math.Max(Math.Max(_lines.Max(l => l.Length), _title.Length + 2), Hint.Length) + 4, fb.Width);
        var height = Math.Min(_lines.Length + 4, fb.Height);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        var titleText = $" {_title} ";
        fb.DrawText(x + Math.Max(1, (width - titleText.Length) / 2), y, titleText, ConsoleColor.White, ConsoleColor.Black);
        for (var i = 0; i < _lines.Length; i++)
        {
            fb.DrawText(x + 2, y + 1 + i, _lines[i], ConsoleColor.White, ConsoleColor.Black, maxWidth: width - 3);
        }

        fb.DrawText(x + 2, y + height - 2, Hint, ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 3);
    }
}
