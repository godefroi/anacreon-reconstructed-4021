namespace Reconstructed4021.Panemonde;

// A vertically-scrolling, single-selection list -- the same shape needed by the scenario picker, the
// save-game picker, and the sector-object picker: arrow keys move a highlighted row, Enter (handled by
// the caller, not here -- what Enter/Esc do differs per screen) selects it. Scrolls only when there
// are more items than the drawn height allows.
public sealed class ListBox<T>
{
    private readonly Func<T, string> _format;

    public IReadOnlyList<T> Items { get; }
    public int SelectedIndex { get; private set; }
    public T? SelectedItem => Items.Count > 0 ? Items[SelectedIndex] : default;

    private int _scrollOffset;

    public ListBox(IReadOnlyList<T> items, Func<T, string> format)
    {
        Items = items;
        _format = format;
    }

    // Returns true if this key was consumed (Up/Down) -- false for anything else, which the caller
    // handles itself (Enter to select, Esc to cancel, ...).
    public bool HandleKey(ConsoleKeyInfo key)
    {
        if (Items.Count == 0)
        {
            return false;
        }

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                SelectedIndex = SelectedIndex == 0 ? Items.Count - 1 : SelectedIndex - 1;
                return true;
            case ConsoleKey.DownArrow:
                SelectedIndex = (SelectedIndex + 1) % Items.Count;
                return true;
            default:
                return false;
        }
    }

    public void Draw(FrameBuffer fb, int x, int y, int width, int height, ConsoleColor fg, ConsoleColor bg, ConsoleColor selectedFg, ConsoleColor selectedBg)
    {
        if (SelectedIndex < _scrollOffset)
        {
            _scrollOffset = SelectedIndex;
        }
        else if (SelectedIndex >= _scrollOffset + height)
        {
            _scrollOffset = SelectedIndex - height + 1;
        }

        for (var row = 0; row < height; row++)
        {
            var itemIndex = _scrollOffset + row;
            var isRealItem = itemIndex < Items.Count;
            var text = isRealItem ? _format(Items[itemIndex]) : string.Empty;
            var visible = text.Length > width ? text[..width] : text.PadRight(width);
            var selected = isRealItem && itemIndex == SelectedIndex;
            fb.DrawText(x, y + row, visible, selected ? selectedFg : fg, selected ? selectedBg : bg);
        }
    }
}
