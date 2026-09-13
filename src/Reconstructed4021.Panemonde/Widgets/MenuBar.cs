namespace Reconstructed4021.Panemonde.Widgets;

// A pull-down menu bar: top-level items across the top row, each opening a dropdown of leaf items
// below it. Matches PULLDOWN.PAS's own two supported input styles -- arrow-key navigation, or pressing
// a hotkey letter directly, including jumping straight from one open menu to a different top-level one
// by its own hotkey ("Once in a menu, pressing the first letter for a particular item will select it
// ... Pressing the first letter of any of the menus will pull down that menu"). Real Pascal has no
// visual hotkey highlight at all (COLORS.INC's SYSMenuBar/SYSMenu/SYSDispSelect are the only three menu
// colors it defines), but showing one is cheap and meaningfully more usable, so this widget adds it as
// a deliberate departure -- Draw takes a distinct hotColor for exactly that.
//
// Hotkeys use an underscore immediately before the letter ("_Game", "Sa_ve", "E_xit to OS"), the same
// convention Terminal.Gui's own MenuItem uses -- so menu text ported from an existing Terminal.Gui
// MenuBarItem/MenuItem list can be reused verbatim, hotkeys included, with no re-authoring.
public sealed class MenuBar
{
    public sealed record Item(string Label, Action Activate);
    public sealed record TopItem(string Label, IReadOnlyList<Item> Leaves);

    private readonly IReadOnlyList<TopItem> _items;
    private int _activeIndex;
    private int _selectedLeafIndex;

    public bool IsOpen { get; private set; }

    public MenuBar(IReadOnlyList<TopItem> items)
    {
        _items = items;
    }

    public void Open(int index)
    {
        IsOpen = true;
        _activeIndex = Math.Clamp(index, 0, _items.Count - 1);
        _selectedLeafIndex = 0;
    }

    public void Close() => IsOpen = false;

    private IReadOnlyList<Item> ActiveLeaves => _items[_activeIndex].Leaves;

    // Returns true if this key was consumed. Route Alt+<letter> here first (opens the matching
    // top-level menu from anywhere, TUI-only convenience on top of the two styles above); once IsOpen,
    // route every key here until it closes.
    public bool HandleKey(ConsoleKeyInfo key)
    {
        if (!IsOpen)
        {
            if (key.Modifiers.HasFlag(ConsoleModifiers.Alt) && TryFindTopIndex(key.KeyChar, out var altIndex))
            {
                Open(altIndex);
                return true;
            }

            return false;
        }

        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                _activeIndex = (_activeIndex + _items.Count - 1) % _items.Count;
                _selectedLeafIndex = 0;
                return true;
            case ConsoleKey.RightArrow:
                _activeIndex = (_activeIndex + 1) % _items.Count;
                _selectedLeafIndex = 0;
                return true;
            case ConsoleKey.UpArrow:
                _selectedLeafIndex = (_selectedLeafIndex + ActiveLeaves.Count - 1) % ActiveLeaves.Count;
                return true;
            case ConsoleKey.DownArrow:
                _selectedLeafIndex = (_selectedLeafIndex + 1) % ActiveLeaves.Count;
                return true;
            case ConsoleKey.Enter:
                Activate(ActiveLeaves[_selectedLeafIndex]);
                return true;
            case ConsoleKey.Escape:
                Close();
                return true;
        }

        // A bare letter: jump straight to a different top-level menu, or select a leaf in the one
        // that's already open. Top-level hotkeys are tried first -- PULLDOWN.PAS's own doc comment
        // guarantees an item's hotkey never collides with a *menu's* hotkey (only with another item's,
        // across different menus), so this order is unambiguous.
        if (TryFindTopIndex(key.KeyChar, out var topIndex) && topIndex != _activeIndex)
        {
            Open(topIndex);
            return true;
        }

        if (TryFindLeafIndex(ActiveLeaves, key.KeyChar, out var leafIndex))
        {
            Activate(ActiveLeaves[leafIndex]);
            return true;
        }

        return false;
    }

    private void Activate(Item item)
    {
        Close();
        item.Activate();
    }

    private bool TryFindTopIndex(char ch, out int index)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (HotKey(_items[i].Label) == char.ToUpperInvariant(ch))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static bool TryFindLeafIndex(IReadOnlyList<Item> leaves, char ch, out int index)
    {
        for (var i = 0; i < leaves.Count; i++)
        {
            if (HotKey(leaves[i].Label) == char.ToUpperInvariant(ch))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    // "_Game" -> 'G', "Sa_ve" -> 'V', "E_xit to OS" -> 'X'. '\0' (matches nothing) if there's no
    // underscore -- a menu with no letter hotkey at all (e.g. an icon-only entry).
    private static char HotKey(string label)
    {
        var i = label.IndexOf('_');
        return i >= 0 && i + 1 < label.Length ? char.ToUpperInvariant(label[i + 1]) : '\0';
    }

    public void Draw(FrameBuffer fb, int barRow, ConsoleColor barFg, ConsoleColor barBg, ConsoleColor hotColor,
        ConsoleColor dropdownFg, ConsoleColor dropdownBg, ConsoleColor selectedFg, ConsoleColor selectedBg)
    {
        fb.DrawText(0, barRow, new string(' ', fb.Width), barFg, barBg);

        var col = 1;
        var activeCol = col;
        for (var i = 0; i < _items.Count; i++)
        {
            var label = $" {_items[i].Label} ";
            if (IsOpen && i == _activeIndex)
            {
                activeCol = col;
                fb.DrawText(col, barRow, Display(label), selectedFg, selectedBg); // whole item inverts, matching SYSDispSelect -- no separate hotkey color while inverted.
            }
            else
            {
                DrawWithHotkey(fb, col, barRow, label, barFg, hotColor, barBg);
            }

            col += label.Length + 1;
        }

        if (IsOpen)
        {
            DrawDropdown(fb, barRow, activeCol, dropdownFg, dropdownBg, hotColor, selectedFg, selectedBg);
        }
    }

    private void DrawDropdown(FrameBuffer fb, int barRow, int activeCol, ConsoleColor fg, ConsoleColor bg, ConsoleColor hotColor, ConsoleColor selectedFg, ConsoleColor selectedBg)
    {
        var leaves = ActiveLeaves;
        var width = leaves.Max(l => Display(l.Label).Length) + 2;
        var height = leaves.Count + 2;
        var x = Math.Min(activeCol, Math.Max(0, fb.Width - width));
        var y = barRow + 1;

        // DrawBox already filled the whole interior with (fg, bg) blanks, so a selected row just needs
        // its own background swapped across the full interior width before the label draws on top --
        // an unselected row needs no padding at all, the fill underneath already covers it.
        DrawBox(fb, x, y, width, height, fg, bg);
        for (var i = 0; i < leaves.Count; i++)
        {
            if (i == _selectedLeafIndex)
            {
                fb.DrawText(x + 1, y + 1 + i, new string(' ', width - 2), selectedFg, selectedBg);
                fb.DrawText(x + 1, y + 1 + i, Display(leaves[i].Label), selectedFg, selectedBg);
            }
            else
            {
                DrawWithHotkey(fb, x + 1, y + 1 + i, leaves[i].Label, fg, hotColor, bg);
            }
        }
    }

    private static string Display(string label) => label.Replace("_", "");

    // Draws label with its underscore-marked hotkey letter in hotColor and everything else (including
    // the underscore itself, which is never drawn) in normalColor.
    private static void DrawWithHotkey(FrameBuffer fb, int x, int y, string label, ConsoleColor normalColor, ConsoleColor hotColor, ConsoleColor bg)
    {
        var col = x;
        for (var i = 0; i < label.Length; i++)
        {
            if (label[i] == '_')
            {
                continue;
            }

            var isHotkey = i > 0 && label[i - 1] == '_';
            fb.Set(col, y, new Cell(new System.Text.Rune(label[i]), isHotkey ? hotColor : normalColor, bg));
            col++;
        }
    }

    private static void DrawBox(FrameBuffer fb, int x, int y, int width, int height, ConsoleColor fg, ConsoleColor bg)
    {
        for (var row = 0; row < height; row++)
        {
            for (var col = 0; col < width; col++)
            {
                fb.Set(x + col, y + row, new Cell(new System.Text.Rune(' '), fg, bg));
            }
        }

        fb.Set(x, y, new Cell(new System.Text.Rune('┌'), fg, bg));
        fb.Set(x + width - 1, y, new Cell(new System.Text.Rune('┐'), fg, bg));
        fb.Set(x, y + height - 1, new Cell(new System.Text.Rune('└'), fg, bg));
        fb.Set(x + width - 1, y + height - 1, new Cell(new System.Text.Rune('┘'), fg, bg));
        for (var i = 1; i < width - 1; i++)
        {
            fb.Set(x + i, y, new Cell(new System.Text.Rune('─'), fg, bg));
            fb.Set(x + i, y + height - 1, new Cell(new System.Text.Rune('─'), fg, bg));
        }

        for (var i = 1; i < height - 1; i++)
        {
            fb.Set(x, y + i, new Cell(new System.Text.Rune('│'), fg, bg));
            fb.Set(x + width - 1, y + i, new Cell(new System.Text.Rune('│'), fg, bg));
        }
    }
}
