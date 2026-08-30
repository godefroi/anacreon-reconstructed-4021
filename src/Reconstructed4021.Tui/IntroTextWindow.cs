using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Reconstructed4021.Tui;

/// <summary>
/// NEWGAME.PAS:1491-1505 (ScenarioIntroduction's own ReadPage/PressAnyKey loop) -- the scenario's intro
/// narrative text, inside the same 80x24 NewGameWindow box the whole New Game flow runs in (see its own
/// doc comment). Pagination itself (splitting on the scenario's own real NEWPAGE markers, trimming,
/// dropping empty pages) is a scenario-file-format concern, not a UI one, so it lives in
/// ScenarioLoader.ReadIntroPages and is directly testable there with no Terminal.Gui involved -- this
/// class only renders whatever pages it's handed and advances through them. Not a real scrolling widget
/// (TextView is obsolete, its replacement lives in a separate Editor package not worth adding just to
/// show static text; a plain Label can't scroll at all) -- just our own "any key advances" KeyDown, the
/// same reliable pattern used everywhere else in this flow.
/// </summary>
internal sealed class IntroTextWindow : NewGameWindow
{
    public bool Cancelled { get; private set; }

    public IntroTextWindow(string scenarioTitle, IReadOnlyList<string> pages) : base(scenarioTitle)
    {
        var pageIndex = 0;

        // No interactive control on this screen at all, so a plain Window-level KeyDown is all it needs
        // -- no focused child around to swallow a key first, the same class of surprise that bit
        // PlayerSetupWindow's gender selector. Y=0, not 1: NEWGAME.PAS:1502's own
        // WriteString(Line[i],1,i,C.SYSDispWind) starts the very first line flush at the window's own
        // first content row, no blank margin above it -- confirmed load-bearing, not cosmetic, by
        // checking every committed *.SCN's real ReadIntroPages output: GAUNTLET.SCN has a genuine 22-line
        // page, exactly this box's full interior height, so reserving an extra unearned margin row here
        // would silently clip that page's last line.
        var textLabel = new Label { X = 1, Y = 0, Text = pages[0], CanFocus = false };

        // Real Pascal's own PressAnyKey(40,22,'Press any key to continue...') -- column 40, one content
        // row above the bottommost, confirmed against a real DOSBox run. X=40 is literal, not
        // percentage-scaled, since Content is a fixed 80-wide box now (not Dim.Fill()). NEWGAME.PAS:1502's
        // WriteString loop has no length check against row 22 either, so on the rare page long enough to
        // reach it (see the textLabel comment above), the real DOS game would visibly overwrite that
        // page's own text with this prompt -- an authentic quirk, not something to engineer around.
        var prompt = new Label { X = 40, Y = Pos.AnchorEnd(2), Text = PromptText(0, pages.Count) };
        Content.Add(textLabel);
        Content.Add(prompt);

        KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode == KeyCode.Esc) {
                Cancelled = true;
                Dismiss();
                key.Handled = true;
                return;
            }

            pageIndex++;
            if (pageIndex >= pages.Count) {
                Dismiss();
            } else {
                textLabel.Text = pages[pageIndex];
                prompt.Text = PromptText(pageIndex, pages.Count);
            }

            key.Handled = true;
        };
    }

    // The "(2/3)" page-count suffix has no Pascal equivalent (real PressAnyKey's text is always the
    // literal same string) -- a low-risk usability addition, kept only when there's more than one page.
    private static string PromptText(int pageIndex, int pageCount) => pageCount > 1
        ? $"Press any key to continue... ({pageIndex + 1}/{pageCount})"
        : "Press any key to continue...";
}
