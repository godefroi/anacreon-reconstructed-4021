using Reconstructed4021.Core.Entities;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;

namespace Reconstructed4021.Tui2;

// F1 (HLPWIND.PAS: HelpWindow). Content is HelpPages (loaded from the embedded help.kdl resource --
// see that class's own doc comment for provenance/corrections). PageUp/PageDown turn a page at a
// time; End opens the topic index; '/' opens a port-only search prompt (no Pascal equivalent -- the
// content is small enough that a linear substring scan is the whole feature). Index/Search results
// are both HelpListOverlay<T>, pushed on top of this via the same overlay stack Attack's own
// AttackConfigPromptOverlay -> FleetGroupConfigurationOverlay handoff already uses.
internal sealed class HelpOverlay : IOverlay
{
    private const int NoOfLines = 19; // HLPWIND.PAS's own HelpPage array bound.

    // PageDown's own guard (HLPWIND.PAS:204-214) floors PageNo at 1, so it can never reach 0 --
    // HelpPages.Pages[0] ("Index", a cursor-instructions blurb) is real decoded content but
    // unreachable through any actual navigation path in the original game; this overlay matches that.
    private const int MinPageNumber = 2;
    private const int Width = 84;

    private readonly Action<IOverlay> _push;
    private int _pageNumber = MinPageNumber;

    public bool IsDismissed { get; private set; }

    public HelpOverlay(Action<IOverlay> push)
    {
        _push = push;
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.PageUp:
                _pageNumber = Math.Clamp(_pageNumber - 1, MinPageNumber, HelpPages.Pages.Count);
                break;
            case ConsoleKey.PageDown:
                _pageNumber = Math.Clamp(_pageNumber + 1, MinPageNumber, HelpPages.Pages.Count);
                break;
            case ConsoleKey.End:
                _push(new HelpListOverlay<HelpPages.IndexEntry>(HelpPages.Index, "Index", e => e.Label, e => e.PageNumber, GoToPage));
                break;
            case ConsoleKey.Escape or ConsoleKey.F1:
                IsDismissed = true;
                break;
            default:
                if (key.KeyChar == '/')
                {
                    OpenSearch();
                }

                break;
        }
    }

    private void GoToPage(int pageNumber) => _pageNumber = Math.Clamp(pageNumber, MinPageNumber, HelpPages.Pages.Count);

    private void OpenSearch() =>
        _push(new TextPromptOverlay("Search Help", "Search for:", string.Empty, query =>
        {
            if (query.Length == 0)
            {
                return;
            }

            var results = HelpPages.Search(query).ToList();
            var title = results.Count > 0 ? $"Search: \"{query}\" ({results.Count})" : $"Search: \"{query}\" -- no matches";
            _push(new HelpListOverlay<(int PageNumber, string Line)>(results, title, r => $"p{r.PageNumber,-3} {r.Line}", r => r.PageNumber, GoToPage));
        }));

    public void Draw(FrameBuffer fb)
    {
        var width = Math.Min(Width, fb.Width);
        var height = Math.Min(NoOfLines + 3, fb.Height - 2);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        var titleText = " ANACREON: Help ";
        fb.DrawText(x + Math.Max(1, (width - titleText.Length) / 2), y, titleText, ConsoleColor.White, ConsoleColor.Black);

        var lines = HelpPages.Pages[_pageNumber - 1];
        for (var i = 0; i < NoOfLines && i < height - 3; i++)
        {
            var text = i < lines.Count ? lines[i] : string.Empty;
            fb.DrawText(x + 1, y + 1 + i, text, ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
        }

        fb.DrawText(x + 1, y + height - 2, "PgUp/PgDn: page   End: index   /: search   Esc: close", ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
    }
}

// A pick-and-jump-to-page list, shared by Help's own Index (End) and Search (/) -- both are "list of
// (label, page number), Enter jumps there and closes, Esc just closes."
internal sealed class HelpListOverlay<T> : IOverlay
{
    private const int Width = 60;
    private readonly ListBox<T> _list;
    private readonly string _title;
    private readonly Func<T, int> _pageOf;
    private readonly Action<int> _onChosen;

    public bool IsDismissed { get; private set; }

    public HelpListOverlay(IReadOnlyList<T> items, string title, Func<T, string> format, Func<T, int> pageOf, Action<int> onChosen)
    {
        _list = new ListBox<T>(items, format);
        _title = title;
        _pageOf = pageOf;
        _onChosen = onChosen;
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_list.HandleKey(key))
        {
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.Enter when _list.SelectedItem is { } item:
                IsDismissed = true;
                _onChosen(_pageOf(item));
                break;
            case ConsoleKey.Escape:
                IsDismissed = true;
                break;
        }
    }

    public void Draw(FrameBuffer fb)
    {
        var width = Math.Min(Width, fb.Width);
        var height = Math.Min(Math.Max(_list.Items.Count, 1) + 2, fb.Height - 2);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        var titleText = $" {_title} ";
        fb.DrawText(x + Math.Max(1, (width - titleText.Length) / 2), y, titleText, ConsoleColor.White, ConsoleColor.Black);

        if (_list.Items.Count == 0)
        {
            fb.DrawText(x + 1, y + 1, "No matches.", ConsoleColor.Gray, ConsoleColor.Black);
            return;
        }

        _list.Draw(fb, x + 1, y + 1, width - 2, height - 2, ConsoleColor.Gray, ConsoleColor.Black, ConsoleColor.Black, ConsoleColor.Gray);
    }
}
