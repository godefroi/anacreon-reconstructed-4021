using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;

namespace Reconstructed4021.Tui2;

// AttackCommand's own "Standard battle configuration (Y/n)?" fork (ATTCOMM.PAS:1608-1616), plus the
// port-only 'A' (Auto) shortcut GameShell.BeginAttack added on top per the user's own explicit
// request -- jumps straight to the same single Auto Attack confirmation Ministry of War's own menu
// item shows, rather than a second confirmation layered on this one. A dedicated overlay rather than
// a third button on the shared ConfirmOverlay: 'A' must not become an option on every other Yes/No/Esc
// confirm in the app, only this one attack-configuration prompt.
internal sealed class AttackConfigPromptOverlay : IOverlay
{
    private readonly Action<char> _onChoice;

    public bool IsDismissed { get; private set; }

    public AttackConfigPromptOverlay(Action<char> onChoice)
    {
        _onChoice = onChoice;
    }

    private const string Message = "Standard battle configuration?";
    private const string Hint = "(Y)es / (N)o / (A)uto   Esc: cancel";

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)
        {
            IsDismissed = true;
            return;
        }

        var letter = char.ToUpperInvariant(key.KeyChar);
        if (key.Key == ConsoleKey.Enter)
        {
            letter = 'Y';
        }

        if (letter is 'Y' or 'N' or 'A')
        {
            IsDismissed = true;
            _onChoice(letter);
        }
    }

    public void Draw(FrameBuffer fb)
    {
        var width = Math.Min(Math.Max(Message.Length, Hint.Length) + 4, fb.Width);
        var height = Math.Min(5, fb.Height);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        var titleText = " Attack ";
        fb.DrawText(x + Math.Max(1, (width - titleText.Length) / 2), y, titleText, ConsoleColor.White, ConsoleColor.Black);
        fb.DrawText(x + 2, y + 1, Message, ConsoleColor.White, ConsoleColor.Black, maxWidth: width - 3);
        fb.DrawText(x + 2, y + height - 2, Hint, ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 3);
    }
}
