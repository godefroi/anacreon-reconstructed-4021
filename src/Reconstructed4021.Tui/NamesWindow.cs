using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// Worlds/Ministry menu bar's F9 shortcut (NMSWIND.PAS: NamesWindow), extended well past real
/// Pascal's read-only listing: a live search field (no Pascal equivalent -- that source has no
/// typed-command layer left in this port to filter against) plus F2/F3/F8 rename/add-bookmark/delete,
/// unifying what real Pascal split across this window (read-only) and the separate Worlds-menu
/// Add/Delete Name commands. One row per <see cref="NamesWindowReport.BuildRows"/> entry: the
/// player-given name, then either that object's own location (<c>GetCoordName</c>) or, for a fleet,
/// this port's own "Fleet" placeholder (<c>GetFleetName</c>'s real Pascal equivalent needs a
/// per-empire fleet slot number this port doesn't model, matching the same fallback already used by
/// Status/Fleet/News/Empire's own <see cref="CloseUpWindow.DescribeLocation"/>), or, for a bookmark,
/// its bare relative coordinate.
///
/// F2/F3/F8 are function keys rather than letters (real Pascal's own Alt-N mnemonic doesn't survive
/// here) because the search field needs every plain letter for typing a query -- 'N'/'A' as row
/// hotkeys would be swallowed as search text instead of firing an action. Attached directly to the
/// search field's own KeyDown to pre-empt TextField's internal key-binding table for exactly those
/// keys, the same trick already used for ListView's type-ahead search in GameShell's own object
/// picker. Home/End are dropped as row-jump bindings for the same reason -- they now move the text
/// cursor within the search field instead, standard search-box behavior; PageUp/PageDown/arrows
/// still page/step through rows regardless of where the search cursor sits.
/// </summary>
internal sealed class NamesWindow : Window
{
    private const int NoOfLines = 17; // NMSWIND.PAS: NoOfLines:=InitHeight-2, InitHeight=19.

    private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue);
    private static readonly TgAttribute BorderAttribute = new(StandardColor.LightGray, StandardColor.Black);
    private static readonly TgAttribute SelectedAttribute = new(StandardColor.Black, StandardColor.LightGray);

    private readonly Game _game;
    private readonly Empire _viewer;
    private readonly Action<ISectorObject> _onSelectObject;
    private readonly Action<Coordinate> _onJumpToLocation;
    private readonly Action<NameEntry> _onRename;
    private readonly Action<NameEntry> _onDelete;
    private readonly Action _onAddBookmark;
    private readonly TextField _searchField;
    private readonly Label[] _rowLabels = new Label[NoOfLines];
    private List<NameEntry> _allRows = [];
    private IReadOnlyList<NameEntry> _rows = [];
    private int _beginIndex;
    private int _selectedIndex;

    public NamesWindow(
        Game game, Empire viewer,
        Action<ISectorObject> onSelectObject, Action<Coordinate> onJumpToLocation,
        Action<NameEntry> onRename, Action<NameEntry> onDelete, Action onAddBookmark)
    {
        _game = game;
        _viewer = viewer;
        _onSelectObject = onSelectObject;
        _onJumpToLocation = onJumpToLocation;
        _onRename = onRename;
        _onDelete = onDelete;
        _onAddBookmark = onAddBookmark;

        Title = "Names";
        Width = 40; // wider than real Pascal's own 30 -- room for the F2/F3/F8 hint line below.
        Height = NoOfLines + 4; // +2 for border, +1 search field, +1 hint line (real Pascal has neither).
        X = Pos.Center();
        Y = Pos.Center();
        BorderStyle = LineStyle.Single;
        CanFocus = true;
        SetScheme(new Scheme(DispWindAttribute));
        Border.View?.SetScheme(new Scheme(BorderAttribute));

        _searchField = new TextField { X = 0, Y = 0, Width = Dim.Fill() };
        Add(_searchField);

        for (var i = 0; i < NoOfLines; i++) {
            _rowLabels[i] = new Label { X = 0, Y = i + 1, Text = string.Empty };
            Add(_rowLabels[i]);
        }

        Add(new Label { X = 0, Y = Pos.AnchorEnd(1), Text = "F2:rename  F3:add  F8:delete" });

        Initialized += (_, _) => _searchField.SetFocus();

        _searchField.TextChanged += (_, _) => ApplyFilter();

        _searchField.KeyDown += (_, key) => {
            switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
                case KeyCode.CursorUp: MoveSelection(-1); key.Handled = true; break;
                case KeyCode.CursorDown: MoveSelection(1); key.Handled = true; break;
                case KeyCode.PageUp: MoveSelection(-NoOfLines); key.Handled = true; break;
                case KeyCode.PageDown: MoveSelection(NoOfLines); key.Handled = true; break;
                case KeyCode.Enter when _rows.Count > 0: Choose(_rows[_selectedIndex]); key.Handled = true; break;
                case KeyCode.F2 when _rows.Count > 0: _onRename(_rows[_selectedIndex]); key.Handled = true; break;
                case KeyCode.F3: _onAddBookmark(); key.Handled = true; break;
                case KeyCode.F8 when _rows.Count > 0: _onDelete(_rows[_selectedIndex]); key.Handled = true; break;
            }
        };

        RefreshRows();
    }

    /// <summary>Recomputes rows from the live galaxy/bookmarks, reapplying the current search text -- call after any add/rename/delete so a stacked rename dialog's result shows immediately without closing this window.</summary>
    public void RefreshRows()
    {
        _allRows = NamesWindowReport.BuildRows(_game.Galaxy, _viewer);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = _searchField.Text?.Trim() ?? "";
        _rows = query.Length == 0
            ? _allRows
            : _allRows.Where(e => DisplayNameOf(e).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        _selectedIndex = 0;
        _beginIndex = 0;
        Redraw();
    }

    private void Choose(NameEntry entry)
    {
        if (entry.Object is { } obj) {
            _onSelectObject(obj);
        } else {
            _onJumpToLocation(entry.Bookmark!.Location);
        }
    }

    private int MaxBeginIndex() => Math.Max(0, _rows.Count - NoOfLines);

    private void MoveSelection(int delta) => SetSelection(_selectedIndex + delta);

    private void SetSelection(int index)
    {
        if (_rows.Count == 0) {
            return;
        }

        _selectedIndex = Math.Clamp(index, 0, _rows.Count - 1);
        if (_selectedIndex < _beginIndex) {
            _beginIndex = _selectedIndex;
        } else if (_selectedIndex >= _beginIndex + NoOfLines) {
            _beginIndex = _selectedIndex - NoOfLines + 1;
        }
        _beginIndex = Math.Clamp(_beginIndex, 0, MaxBeginIndex());
        Redraw();
    }

    private void Redraw()
    {
        if (_rows.Count == 0) {
            _rowLabels[0].SetScheme(new Scheme(DispWindAttribute));
            _rowLabels[0].Text = _allRows.Count == 0 ? "No names have been defined." : "No matches.";
            for (var i = 1; i < NoOfLines; i++) {
                _rowLabels[i].Text = string.Empty;
            }
            return;
        }

        for (var i = 0; i < NoOfLines; i++) {
            var index = _beginIndex + i;
            var selected = index == _selectedIndex;
            _rowLabels[i].SetScheme(new Scheme(selected ? SelectedAttribute : DispWindAttribute));
            _rowLabels[i].Text = index < _rows.Count ? FormatRow(_rows[index]) : string.Empty;
        }
    }

    private string DisplayNameOf(NameEntry entry) => entry.Object?.Names.GetValueOrDefault(_viewer, string.Empty) ?? entry.Bookmark!.Name;

    private string LocationOf(NameEntry entry) => entry.Object is { } obj
        ? CloseUpWindow.DescribeLocation(obj, _viewer)
        : RelativeCoordinate.Format(entry.Bookmark!.Location, _viewer.Capital?.Location ?? new Coordinate(0, 0));

    // GetNameLine (NMSWIND.PAS:93-113): name AdjustString'd to 8, then 5 spaces, then the
    // location/placeholder AdjustString'd to 12.
    private string FormatRow(NameEntry entry)
    {
        var name = DisplayNameOf(entry).PadRight(8)[..8];
        var location = LocationOf(entry).PadRight(12)[..12];
        return $"{name}     {location}";
    }
}
