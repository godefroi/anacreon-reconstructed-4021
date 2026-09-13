namespace Reconstructed4021.Panemonde.Widgets;

// A bordered frame with a title on the left of the top border and tab labels on the right of it --
// tui1's TabbedWindow concatenated both into one Window.Title string; with full control over the
// border row here, they get their own regions instead (per the user's own explicit design for the
// Close Up port). Ctrl+PageUp/PageDown cycles tabs, matching TabbedWindow's own Step convention.
// Content is entirely the caller's concern -- this only draws the frame and tracks which tab is
// active; a single-tab frame (Close Up today) draws exactly the same way, no special-casing.
public sealed class TabFrame
{
    private readonly IReadOnlyList<string> _tabLabels;

    public int ActiveIndex { get; private set; }

    public TabFrame(IReadOnlyList<string> tabLabels)
    {
        if (tabLabels.Count == 0)
        {
            throw new ArgumentException("A TabFrame needs at least one tab.", nameof(tabLabels));
        }

        _tabLabels = tabLabels;
    }

    // Only meaningful with 2+ tabs -- returns false untouched otherwise, so a single-tab frame never
    // eats a Ctrl+PageUp/PageDown the caller might want for something else.
    public bool HandleKey(ConsoleKeyInfo key)
    {
        if (_tabLabels.Count < 2 || !key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            return false;
        }

        switch (key.Key)
        {
            case ConsoleKey.PageUp:
                ActiveIndex = (ActiveIndex - 1 + _tabLabels.Count) % _tabLabels.Count;
                return true;
            case ConsoleKey.PageDown:
                ActiveIndex = (ActiveIndex + 1) % _tabLabels.Count;
                return true;
            default:
                return false;
        }
    }

    // Draws the border plus the title/tabs row; the interior is left at the border's own fg/bg --
    // callers with a different content color (e.g. CloseUpOverlay's SYSDispWind Blue) fill their own
    // content rectangle afterward.
    public void Draw(FrameBuffer fb, int x, int y, int width, int height, string title,
        ConsoleColor fg, ConsoleColor bg, ConsoleColor activeFg, ConsoleColor activeBg)
    {
        BoxDrawing.DrawSingleLine(fb, x, y, width, height, fg, bg);

        var tabsText = string.Join(' ', _tabLabels.Select((label, i) => i == ActiveIndex ? $"[{label}]" : $" {label} "));
        var maxTitleWidth = Math.Max(0, width - tabsText.Length - 4);
        var titleText = $" {title} ";
        if (titleText.Length > maxTitleWidth)
        {
            titleText = maxTitleWidth <= 1 ? string.Empty : titleText[..(maxTitleWidth - 1)] + "…";
        }

        fb.DrawText(x + 1, y, titleText, fg, bg);

        var tabsX = x + width - 1 - tabsText.Length;
        var cursor = Math.Max(x + 1 + titleText.Length, tabsX);
        foreach (var (label, i) in _tabLabels.Select((label, i) => (label, i)))
        {
            var text = i == ActiveIndex ? $"[{label}]" : $" {label} ";
            fb.DrawText(cursor, y, text, i == ActiveIndex ? activeFg : fg, i == ActiveIndex ? activeBg : bg);
            cursor += text.Length + 1;
        }
    }
}
