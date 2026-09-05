using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// Worlds/Ministry menu bar's F9 shortcut (NMSWIND.PAS: NamesWindow). Single-pane, no column header
/// (real Pascal's own window has none either -- just a bare name/location pair per row, narrow enough
/// that <see cref="View.Title"/> alone is fine). One row per <see cref="NamesWindowReport.BuildRows"/>
/// entry: the player-given name, then either that object's own location (<c>GetCoordName</c>) or, for
/// a fleet, this port's own "Fleet" placeholder (<c>GetFleetName</c>'s real Pascal equivalent needs a
/// per-empire fleet slot number this port doesn't model, matching the same fallback already used by
/// Status/Fleet/News/Empire's own <see cref="CloseUpWindow.DescribeLocation"/>).
///
/// Port-only addition, no Pascal equivalent: Up/Down move a highlighted-row marker (viewport follows
/// it) instead of just scrolling, and Enter opens the highlighted object's own Close Up via
/// <paramref name="onSelectObject"/> -- same addition already made to Status/Fleet/News/Empire.
/// </summary>
internal sealed class NamesWindow : Window
{
    private const int NoOfLines = 17; // NMSWIND.PAS: NoOfLines:=InitHeight-2, InitHeight=19.

    private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue);
    private static readonly TgAttribute BorderAttribute = new(StandardColor.LightGray, StandardColor.Black);
    private static readonly TgAttribute SelectedAttribute = new(StandardColor.Black, StandardColor.LightGray);

    private readonly IReadOnlyList<ISectorObject> _rows;
    private readonly Empire _viewer;
    private readonly Action<ISectorObject> _onSelectObject;
    private readonly Label[] _rowLabels = new Label[NoOfLines];
    private int _beginIndex;
    private int _selectedIndex;

    public NamesWindow(Game game, Empire viewer, Action<ISectorObject> onSelectObject)
    {
        _viewer = viewer;
        _onSelectObject = onSelectObject;
        _rows = NamesWindowReport.BuildRows(game.Galaxy, viewer);

        Title = "Names";
        Width = 30; // real Pascal's own window is already this narrow -- a name/location pair never needs more.
        Height = NoOfLines + 2;
        X = Pos.Center();
        Y = Pos.Center();
        BorderStyle = LineStyle.Single;
        CanFocus = true;
        SetScheme(new Scheme(DispWindAttribute));
        Border.View?.SetScheme(new Scheme(BorderAttribute));

        if (_rows.Count == 0) {
            Add(new Label { X = 1, Y = 1, Text = "No names have been" });
            Add(new Label { X = 1, Y = 2, Text = "defined." });
        }

        for (var i = 0; i < NoOfLines; i++) {
            _rowLabels[i] = new Label { X = 0, Y = i, Text = string.Empty };
            Add(_rowLabels[i]);
        }

        Redraw();

        KeyDown += (_, key) => {
            switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
                case KeyCode.CursorUp: MoveSelection(-1); key.Handled = true; break;
                case KeyCode.CursorDown: MoveSelection(1); key.Handled = true; break;
                case KeyCode.PageUp: MoveSelection(-NoOfLines); key.Handled = true; break;
                case KeyCode.PageDown: MoveSelection(NoOfLines); key.Handled = true; break;
                case KeyCode.Home: SetSelection(0); key.Handled = true; break;
                case KeyCode.End: SetSelection(_rows.Count - 1); key.Handled = true; break;
                case KeyCode.Enter when _rows.Count > 0:
                    _onSelectObject(_rows[_selectedIndex]);
                    key.Handled = true;
                    break;
            }
        };
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
        for (var i = 0; i < NoOfLines; i++) {
            var index = _beginIndex + i;
            var selected = index == _selectedIndex;
            _rowLabels[i].SetScheme(new Scheme(selected ? SelectedAttribute : DispWindAttribute));
            _rowLabels[i].Text = index < _rows.Count ? FormatRow(_rows[index]) : string.Empty;
        }
    }

    // GetNameLine (NMSWIND.PAS:93-113): name AdjustString'd to 8, then 5 spaces, then the
    // location/placeholder AdjustString'd to 12.
    private string FormatRow(ISectorObject obj)
    {
        var name = obj.Names.GetValueOrDefault(_viewer, string.Empty).PadRight(8)[..8];
        var location = CloseUpWindow.DescribeLocation(obj, _viewer).PadRight(12)[..12];
        return $"{name}     {location}";
    }
}
