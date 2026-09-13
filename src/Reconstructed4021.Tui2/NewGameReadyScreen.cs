using Reconstructed4021.Core;
using Reconstructed4021.Panemonde;

namespace Reconstructed4021.Tui2;

// Placeholder landing screen once ScenarioLoader.Load has actually produced a Game -- there's no
// galaxy map (GameShell's equivalent) in this project yet, so this just confirms the game was built
// and hands control back to the main menu. Replace with the real map screen once it exists; this is
// deliberately not meant to look finished.
internal sealed class NewGameReadyScreen : IScreen
{
    private readonly string _scenarioTitle;
    private readonly Game _game;
    private readonly NewGameContext _context;
    private readonly Starfield _starfield = new();

    public IScreen? NextScreen { get; private set; }

    public NewGameReadyScreen(string scenarioTitle, Game game, NewGameContext context)
    {
        _scenarioTitle = scenarioTitle;
        _game = game;
        _context = context;
    }

    public void HandleKey(ConsoleKeyInfo key) => NextScreen = _context.MakeTitleScreen();

    public void Update(TimeSpan elapsed) => _starfield.Update(elapsed);

    public void Draw(FrameBuffer fb)
    {
        var box = Chrome.Draw(fb, _scenarioTitle, _starfield);
        fb.DrawText(box.X + 1, box.Y + 1, "New game created:", Chrome.ContentFg, Chrome.ContentBg);
        fb.DrawText(box.X + 1, box.Y + 2, $"  Empires: {_game.Empires.Count}", Chrome.ContentFg, Chrome.ContentBg);
        fb.DrawText(box.X + 1, box.Y + 3, $"  Year: {_game.Year}", Chrome.ContentFg, Chrome.ContentBg);
        fb.DrawText(box.X + 1, box.Y + 5, "(No galaxy map screen yet -- this is a placeholder.)", Chrome.ContentFg, Chrome.ContentBg);
        fb.DrawText(box.X + 1, box.Y + box.Height - 2, "Press any key to return to the main menu.", Chrome.ContentFg, Chrome.ContentBg);
    }
}
