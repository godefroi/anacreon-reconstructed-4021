using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace ThreeLn.Reconstruction4021.Tui;

/// <summary>
/// NEWGAME.PAS:1491-1505 (ScenarioIntroduction's own ReadPage/PressAnyKey loop) -- the scenario's intro
/// narrative text, inside the same full-screen blue backdrop PlayerCountWindow opens (see its own doc
/// comment). Paginated primarily on the scenario's own real NEWPAGE markers (ScenarioLoader.ReadIntroText
/// joins them with '\f'; AFTERMAT.SCN uses three, one after each of its three decorative boxes -- an
/// earlier version of this ignored them and chunked by a blind fixed line count instead, which split a
/// box's own border across two screens, confirmed from real testing), falling back to a fixed-size
/// chunker only for the rare page that's still too long for one screen on its own. Not a real scrolling
/// widget (TextView is obsolete, its replacement lives in a separate Editor package not worth adding just
/// to show static text; a plain Label can't scroll at all) -- just our own "any key advances" KeyDown,
/// the same reliable pattern used everywhere else in this flow.
/// </summary>
internal sealed class IntroTextWindow : Window
{
    private const int LinesPerPage = 18;

    // COLORS.INC's ColorScrColor: SYSDispWind=23 -> LightGray on Blue.
    private static readonly TgAttribute SysDispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue);

    public bool Cancelled { get; private set; }

    public IntroTextWindow(string scenarioTitle, string introText)
    {
        Title = scenarioTitle;
        Width = Dim.Fill();
        Height = Dim.Fill();
        SetScheme(new Scheme(SysDispWindAttribute));

        var pages = Paginate(introText);
        var pageIndex = 0;

        // No interactive control on this screen at all, so a plain Window-level KeyDown is all it needs
        // -- no focused child around to swallow a key first, the same class of surprise that bit
        // PlayerSetupWindow's gender selector.
        var textLabel = new Label { X = 1, Y = 1, Text = pages[0], CanFocus = false };
        var footer = new Label { X = 1, Y = Pos.AnchorEnd(1), Text = FooterText(0, pages.Count) };
        Add(textLabel);
        Add(footer);

        KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode == KeyCode.Esc) {
                Cancelled = true;
                App?.RequestStop();
                key.Handled = true;
                return;
            }

            pageIndex++;
            if (pageIndex >= pages.Count) {
                App?.RequestStop();
            } else {
                textLabel.Text = pages[pageIndex];
                footer.Text = FooterText(pageIndex, pages.Count);
            }

            key.Handled = true;
        };
    }

    private static string FooterText(int pageIndex, int pageCount) => pageCount > 1
        ? $"Press any key to continue... ({pageIndex + 1}/{pageCount})   Esc: back to main menu"
        : "Press any key to continue...   Esc: back to main menu";

    /// <summary>
    /// Splits on ReadIntroText's '\f' page separators first, so a real NEWPAGE-delimited page (e.g. one
    /// of AFTERMAT's three boxes) always stays on one screen regardless of its own line count. Only a
    /// single real page still over LinesPerPage falls back to fixed-size chunking. FENCES.SCN's trailing
    /// NEWPAGE immediately before ENDTEXT produces a genuinely empty final page (see
    /// ScenarioLoader.CollectIntroTextPages's own doc comment on why that's correct, not a bug) -- dropped
    /// here after trimming trailing blank lines, along with any other page that ends up entirely blank.
    /// </summary>
    private static List<string> Paginate(string introText)
    {
        var pages = new List<string>();
        foreach (var rawPage in introText.Split('\f')) {
            var lines = rawPage.Split('\n').ToList();
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1])) {
                lines.RemoveAt(lines.Count - 1);
            }

            if (lines.Count == 0) {
                continue;
            }

            for (var i = 0; i < lines.Count; i += LinesPerPage) {
                pages.Add(string.Join('\n', lines.Skip(i).Take(LinesPerPage)));
            }
        }

        return pages.Count > 0 ? pages : [""];
    }
}
