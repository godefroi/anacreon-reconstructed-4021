using Reconstructed4021.Panemonde;
using Reconstructed4021.Tui2.NewGame;
using Reconstructed4021.Tui2.Shared;


namespace Reconstructed4021.Tui2.Screens;


// NEWGAME.PAS:1491-1505 (ScenarioIntroduction's own ReadPage/PressAnyKey loop) -- the scenario's intro
// narrative text. Pagination itself (splitting on the scenario's own NEWPAGE markers) is a
// scenario-file-format concern handled by ScenarioLoader.ReadIntroPages, not a UI one -- this screen
// only renders whatever pages it's handed and advances through them on any key.
internal sealed class IntroTextScreen : IScreen
{
    private readonly string _title;
    private readonly IReadOnlyList<string> _pages;
    private readonly NewGameFlow _flow;
    private readonly Starfield _starfield = new();
    private int _pageIndex;

    public IScreen? NextScreen { get; private set; }

    public IntroTextScreen(string title, IReadOnlyList<string> pages, NewGameFlow flow)
    {
        _title = title;
        _pages = pages;
        _flow = flow;
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)
        {
            NextScreen = _flow.Context.MakeTitleScreen();
            return;
        }

        _pageIndex++;
        if (_pageIndex >= _pages.Count)
        {
            NextScreen = _flow.CreatePlayerCountOrFirstSetupScreen();
        }
    }

    public void Update(TimeSpan elapsed) => _starfield.Update(elapsed);

    public void Draw(FrameBuffer fb)
    {
        var box = Chrome.Draw(fb, _title, _starfield);
        var lines = _pages[_pageIndex].Split('\n');
        for (var i = 0; i < lines.Length && i < box.Height - 2; i++)
        {
            fb.DrawText(box.X + 1, box.Y + 1 + i, lines[i], Chrome.ContentFg, Chrome.ContentBg);
        }

        var prompt = _pages.Count > 1
            ? $"Press any key to continue... ({_pageIndex + 1}/{_pages.Count})"
            : "Press any key to continue...";
        fb.DrawText(box.X + 1, box.Y + box.Height - 2, prompt, Chrome.ContentFg, Chrome.ContentBg);
    }
}
