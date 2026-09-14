using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Presentation;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;

namespace Reconstructed4021.Tui2;

// F9 (NMSWIND.PAS: NamesWindow), extended past real Pascal's read-only listing the same way Tui's own
// NamesWindow already was: a live search filter plus F2/F3/F8 rename/add-bookmark/delete, unifying
// what real Pascal split across this window and the separate Worlds-menu Add/Delete Name commands.
// F2/F8 mutate a row in place and just refresh -- no need to close/reopen. F3 (add a bookmark) is the
// one action that genuinely needs the map cursor interactive, so it's the only one that dismisses
// this overlay first (via onAddBookmark, which BeginPicks a location then reopens this).
internal sealed class NamesOverlay : IOverlay
{
    private const int NoOfLines = 17; // NMSWIND.PAS: NoOfLines:=InitHeight-2, InitHeight=19.
    private const int Width = 44;

    private readonly Game _game;
    private readonly Empire _viewer;
    private readonly Action<ISectorObject> _onSelectObject;
    private readonly Action<Coordinate> _onJumpToLocation;
    private readonly Action<IOverlay> _push;
    private readonly Action _onAddBookmark;
    private readonly TextInputField _searchField = new(string.Empty, 30);
    private List<NameEntry> _allRows = [];
    private ListBox<NameEntry> _list = null!;

    public bool IsDismissed { get; private set; }

    public NamesOverlay(Game game, Empire viewer, Action<ISectorObject> onSelectObject, Action<Coordinate> onJumpToLocation, Action<IOverlay> push, Action onAddBookmark)
    {
        _game = game;
        _viewer = viewer;
        _onSelectObject = onSelectObject;
        _onJumpToLocation = onJumpToLocation;
        _push = push;
        _onAddBookmark = onAddBookmark;
        RefreshRows();
    }

    // Call after any add/rename/delete so the result shows immediately without closing this overlay.
    private void RefreshRows()
    {
        _allRows = NamesWindowReport.BuildRows(_game.Galaxy, _viewer);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = _searchField.Text.Trim();
        var filtered = query.Length == 0
            ? _allRows
            : _allRows.Where(e => DisplayNameOf(e).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        _list = new ListBox<NameEntry>(filtered, FormatRow);
    }

    private string DisplayNameOf(NameEntry entry) => entry.Object?.Names.GetValueOrDefault(_viewer, string.Empty) ?? entry.Bookmark!.Name;

    private string LocationOf(NameEntry entry) => entry.Object is { } obj
        ? CloseUpOverlay.DescribeLocation(obj, _viewer)
        : RelativeCoordinate.Format(entry.Bookmark!.Location, _viewer.Capital?.Location ?? new Coordinate(0, 0));

    // GetNameLine (NMSWIND.PAS:93-113): name padded to 8, then 5 spaces, then location padded to 12.
    private string FormatRow(NameEntry entry) => $"{DisplayNameOf(entry).PadRight(8)[..8]}     {LocationOf(entry).PadRight(12)[..12]}";

    private void Choose(NameEntry entry)
    {
        if (entry.Object is { } obj)
        {
            // Stacks Close Up on top instead of dismissing -- see StatusOverlay's own comment.
            _onSelectObject(obj);
            return;
        }

        // A bookmark has no Close Up to stack -- jumping the map cursor needs this overlay gone
        // first, same as AddBookmark's own BeginPick precondition.
        IsDismissed = true;
        _onJumpToLocation(entry.Bookmark!.Location);
    }

    private void Rename(NameEntry entry)
    {
        if (entry.Object is { } obj)
        {
            _push(new TextPromptOverlay("Name", "New name (blank to clear):", obj.Names.GetValueOrDefault(_viewer, string.Empty), newName =>
            {
                if (newName.Length == 0)
                {
                    obj.Names.Remove(_viewer);
                }
                else
                {
                    obj.Names[_viewer] = newName;
                }

                RefreshRows();
            }));
            return;
        }

        var bookmark = entry.Bookmark!;
        _push(new TextPromptOverlay("Name Location", "New name (blank to delete):", bookmark.Name, newName =>
        {
            // A bookmark has nothing sensible to fall back to once blanked -- unlike an object, which
            // still has its coordinate/kind to show -- so blank here just deletes it too.
            if (newName.Length == 0)
            {
                _viewer.Bookmarks.Remove(bookmark);
            }
            else
            {
                bookmark.Name = newName;
            }

            RefreshRows();
        }));
    }

    private void Delete(NameEntry entry)
    {
        if (entry.Object is { } obj)
        {
            obj.Names.Remove(_viewer);
        }
        else
        {
            _viewer.Bookmarks.Remove(entry.Bookmark!);
        }

        RefreshRows();
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_list.HandleKey(key))
        {
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.Enter when _list.SelectedItem is { } entry:
                Choose(entry);
                return;
            case ConsoleKey.Escape or ConsoleKey.F9:
                IsDismissed = true;
                return;
            case ConsoleKey.F2 when _list.SelectedItem is { } entry:
                Rename(entry);
                return;
            case ConsoleKey.F3:
                IsDismissed = true;
                _onAddBookmark();
                return;
            case ConsoleKey.F8 when _list.SelectedItem is { } entry:
                Delete(entry);
                return;
        }

        if (_searchField.HandleKey(key))
        {
            ApplyFilter();
        }
    }

    public void Draw(FrameBuffer fb)
    {
        var width = Math.Min(Width, fb.Width);
        var height = Math.Min(NoOfLines + 4, fb.Height - 2);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        fb.DrawText(x + Math.Max(1, (width - 7) / 2), y, " Names ", ConsoleColor.White, ConsoleColor.Black);

        _searchField.Draw(fb, x + 1, y + 1, width - 2, ConsoleColor.Black, ConsoleColor.Gray);

        var listHeight = height - 4;
        if (_list.Items.Count == 0)
        {
            var message = _allRows.Count == 0 ? "No names have been defined." : "No matches.";
            fb.DrawText(x + 1, y + 2, message, ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
        }
        else
        {
            _list.Draw(fb, x + 1, y + 2, width - 2, listHeight, ConsoleColor.Gray, ConsoleColor.Black, ConsoleColor.Black, ConsoleColor.Gray);
        }

        fb.DrawText(x + 1, y + height - 2, "F2:rename  F3:add  F8:delete", ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
    }
}
