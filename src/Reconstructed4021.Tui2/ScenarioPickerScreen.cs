using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Panemonde;

namespace Reconstructed4021.Tui2;

// New Game's scenario picker. Real Pascal has no equivalent -- NEWGAME.PAS's own ScenarioIntroduction
// just prompts for a hardcoded filename, no directory scan or title list -- this is a convenience over
// the same reference/scenarios/dos_131/*.SCN files Reconstructed4021.Tui's own picker uses.
public sealed class ScenarioPickerScreen : IScreen
{
    public sealed record ScenarioChoice(string Path, ScenarioLoader.ScenarioHeader Header)
    {
        public string Label => $"{Header.Title,-28} {Header.GalaxySize,3}x{Header.GalaxySize,-3} {Header.MinPlayers}-{Header.MaxPlayers} players";
    }

    private readonly ListBox<ScenarioChoice> _list;
    private readonly NewGameContext _context;
    private readonly Starfield _starfield = new();

    public IScreen? NextScreen { get; private set; }

    public ScenarioPickerScreen(IReadOnlyList<ScenarioChoice> scenarios, NewGameContext context)
    {
        _list = new ListBox<ScenarioChoice>(scenarios, choice => choice.Label);
        _context = context;
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_list.Items.Count == 0)
        {
            NextScreen = _context.MakeTitleScreen();
            return;
        }

        if (_list.HandleKey(key))
        {
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.Enter:
                NextScreen = BuildNext(_list.SelectedItem!);
                return;
            case ConsoleKey.Escape:
                NextScreen = _context.MakeTitleScreen();
                return;
        }
    }

    private IScreen BuildNext(ScenarioChoice choice)
    {
        var scenarioText = ScenarioLoader.ReadScenarioFile(choice.Path);
        var introPages = ScenarioLoader.ReadIntroPages(scenarioText);
        var flow = new NewGameFlow(choice.Header, scenarioText, _context);
        return introPages.Count > 0
            ? new IntroTextScreen(choice.Header.Title, introPages, flow)
            : flow.CreatePlayerCountOrFirstSetupScreen();
    }

    public void Update(TimeSpan elapsed) => _starfield.Update(elapsed);

    public void Draw(FrameBuffer fb)
    {
        var box = Chrome.Draw(fb, "New Game -- Choose a Scenario", _starfield);

        if (_list.Items.Count == 0)
        {
            fb.DrawText(box.X + 1, box.Y + 1, "No scenarios found.", Chrome.ContentFg, Chrome.ContentBg);
        }
        else
        {
            _list.Draw(fb, box.X + 1, box.Y + 1, box.Width - 2, box.Height - 3,
                Chrome.ContentFg, Chrome.ContentBg, ConsoleColor.Black, Chrome.ContentFg);
        }

        fb.DrawText(box.X + 1, box.Y + box.Height - 2, "Enter: choose   Esc: back to main menu", Chrome.ContentFg, Chrome.ContentBg);
    }
}
