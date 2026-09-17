using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;


namespace Reconstructed4021.Tui.Overlays;


// F1 (HLPWIND.PAS: HelpWindow). Content is HelpPages (loaded from the embedded help.kdl resource --
// see that class's own doc comment for provenance/corrections). PageUp/PageDown turn a page at a
// time; End opens the topic index; '/' opens a port-only search prompt (no Pascal equivalent -- the
// content is small enough that a linear substring scan is the whole feature). Index/Search results
// are both HelpListOverlay<T>, pushed on top of this via the same overlay stack Attack's own
// AttackConfigPromptOverlay -> FleetGroupConfigurationOverlay handoff already uses.

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
