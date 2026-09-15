using Reconstructed4021.Core.Entities;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;

namespace Reconstructed4021.Tui2.Overlays;

// Empire menu > Read Messages (DESIGN.PAS's ReadMessageCommand, :992-1055): PgUp/PgDn cycle when
// there's more than one, Esc closes. Only PgDn (or the initial display of the first message) marks a
// message read -- PgUp going back does not, matching ReadMessageCommand's own SetMessageRead call sites
// exactly.
internal sealed class ReadMessagesOverlay : IOverlay
{
    private const int Width = 82;
    private const int Height = 21;

    private readonly IReadOnlyList<Message> _messages;
    private readonly Empire _viewer;
    private readonly Action<Message> _onRead;
    private int _index;

    public bool IsDismissed { get; private set; }

    public ReadMessagesOverlay(IReadOnlyList<Message> messages, Empire viewer, Action<Message> onRead)
    {
        _messages = messages;
        _viewer = viewer;
        _onRead = onRead;
        onRead(messages[0]);
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.PageUp:
                if (_index > 0)
                {
                    _index--;
                }

                return;
            case ConsoleKey.PageDown:
                if (_index < _messages.Count - 1)
                {
                    _index++;
                    _onRead(_messages[_index]);
                }

                return;
            case ConsoleKey.Escape:
                IsDismissed = true;
                return;
        }
    }

    private string Header()
    {
        var message = _messages[_index];
        var prefix = _messages.Count > 1 ? $"({_index + 1} of {_messages.Count}) " : "";
        if (message.Intercepted)
        {
            return $"{prefix}Intercepted message from {message.Sender.Name}.";
        }

        return ReferenceEquals(message.Sender, _viewer)
            ? $"{prefix}Time capsule from the past."
            : $"{prefix}Message from {message.Sender.Name}.";
    }

    public void Draw(FrameBuffer fb)
    {
        var width = Math.Min(Width, fb.Width);
        var height = Math.Min(Height, fb.Height);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        fb.DrawText(x + 2, y, " Read Messages ", ConsoleColor.White, ConsoleColor.Black);
        fb.DrawText(x + 1, y + 1, Header(), ConsoleColor.White, ConsoleColor.Black, maxWidth: width - 2);

        var lines = _messages[_index].Lines;
        for (var i = 0; i < lines.Count && i < height - 4; i++)
        {
            fb.DrawText(x + 1, y + 3 + i, lines[i], ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
        }

        var hint = _messages.Count > 1 ? "PgUp/PgDn: switch message   Esc: close" : "Esc: close";
        fb.DrawText(x + 1, y + height - 2, hint, ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
    }
}
