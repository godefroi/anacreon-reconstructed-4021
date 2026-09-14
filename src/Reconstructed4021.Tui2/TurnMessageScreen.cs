using Reconstructed4021.Panemonde;

namespace Reconstructed4021.Tui2;

// Generic "read this, press any key" screen for every turn-loop transition (TurnStartGreetingWindow,
// EmpireStatusWindow, the Capital Fallen Report, Defeat, Victory -- see TurnLoop's own doc comment).
// Reuses Chrome's starfield-boxed shell -- already this project's generic full-screen content chrome
// for everything that isn't the galaxy map or title screen (SaveGamePickerScreen uses it too, not just
// the New Game wizard its own doc comment names) -- rather than reproducing tui1's literal Dim.Fill()
// solid-blue window as a one-off visual pattern used nowhere else in this port.
internal sealed class TurnMessageScreen : IScreen
{
    private readonly string _title;
    private readonly string[] _lines;
    private readonly Func<IScreen> _next;
    private readonly Starfield _starfield = new();

    public IScreen? NextScreen { get; private set; }

    public TurnMessageScreen(string title, string body, Func<IScreen> next)
    {
        _title = title;
        _lines = body.Split('\n');
        _next = next;
    }

    public void HandleKey(ConsoleKeyInfo key) => NextScreen = _next();

    public void Update(TimeSpan elapsed) => _starfield.Update(elapsed);

    public void Draw(FrameBuffer fb)
    {
        var box = Chrome.Draw(fb, _title, _starfield);
        for (var i = 0; i < _lines.Length && i + 2 < box.Height; i++)
        {
            fb.DrawText(box.X + 1, box.Y + 1 + i, _lines[i], Chrome.ContentFg, Chrome.ContentBg, maxWidth: box.Width - 2);
        }

        fb.DrawText(box.X + 1, box.Y + box.Height - 2, "Press any key to continue...", Chrome.ContentFg, Chrome.ContentBg, maxWidth: box.Width - 2);
    }
}
