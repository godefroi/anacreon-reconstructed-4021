using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// Worlds/Ministry menu bar's F5 shortcut (FLTWIND.PAS: FleetWindow). Same shape as
/// <see cref="StatusWindow"/> (its own doc comment covers the shared design: two nine-row panes
/// sharing one scroll position, plain-content header rows instead of <see cref="View.Title"/>,
/// Up/Down moving a highlighted-row marker with Enter jumping to Close Up) over
/// <see cref="FleetStatusReport.BuildRows"/>'s ordered fleet/starbase list instead.
///
/// Position/Destination show as capital-relative coordinates (<see cref="RelativeCoordinate"/>),
/// not real Pascal's own resolved place name (<c>GetName</c>) -- the same simplification
/// <see cref="CloseUpWindow"/>'s own fleet layout already made, reused here rather than building a
/// second, different redaction convention for the same two fields. Status/Destination redaction for
/// fleet rows reuses <see cref="CloseUpWindow.DescribeFleetStatus"/>/<see cref="CloseUpWindow.DescribeFleetDestination"/>
/// verbatim; starbase rows (Command Base/Fortress only, always the viewer's own here) need no
/// redaction at all, just the same InTransit-only EstimatedDateOfArrival guard those two methods
/// already get right (calling it unconditionally, the way real Pascal's own GetFleetPositionStatus
/// does, is exactly the bug already fixed once in <see cref="FleetLifecycle.RefuelFleet"/> -- Pascal's
/// own Destination is never null, this port's is once a fleet's arrived).
///
/// No "(orders)" suffix (FLTWIND.PAS's own FleetNextStatement check) -- this port has no scripted
/// fleet-orders queue to read one from.
/// </summary>
internal sealed class FleetWindow : Window
{
    private const int NoOfLines = 9; // FLTWIND.PAS: NoOfLines:=(InitHeight DIV 2)-1, InitHeight=21.
    private const int TopHeaderRow = 0;
    private const int PositionStatusStartRow = TopHeaderRow + 1;
    private const int DividingBarRow = PositionStatusStartRow + NoOfLines;
    private const int ShipCargoStartRow = DividingBarRow + 1;
    private const int TotalContentRows = ShipCargoStartRow + NoOfLines;

    private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue);
    private static readonly TgAttribute BorderAttribute = new(StandardColor.LightGray, StandardColor.Black);
    private static readonly TgAttribute SelectedAttribute = new(StandardColor.Black, StandardColor.LightGray);

    private readonly Game _game;
    private readonly Empire _viewer;
    private readonly Coordinate _origin;
    private readonly Action<ISectorObject> _onSelectRow;
    private readonly IReadOnlyList<ISectorObject> _rows;
    private readonly Label[] _positionStatusLabels = new Label[NoOfLines];
    private readonly Label[] _shipCargoLabels = new Label[NoOfLines];
    private int _beginIndex;
    private int _selectedIndex;

    public FleetWindow(Game game, Empire viewer, Action<ISectorObject> onSelectRow)
    {
        _game = game;
        _viewer = viewer;
        _origin = viewer.Capital?.Location ?? new Coordinate(game.Galaxy.Size / 2, game.Galaxy.Size / 2);
        _onSelectRow = onSelectRow;
        _rows = FleetStatusReport.BuildRows(game.Galaxy, viewer);

        Title = "Fleet";
        Width = 86;
        Height = TotalContentRows + 2;
        X = Pos.Center();
        Y = Pos.Center();
        BorderStyle = LineStyle.Single;
        CanFocus = true;
        SetScheme(new Scheme(DispWindAttribute));
        Border.View?.SetScheme(new Scheme(BorderAttribute));

        var positionHeader = new Label { X = 0, Y = TopHeaderRow, Text = $"{"Fleet",-8} {"Pos",-8} {"Des",-8} {"Status",-16} {"Range",5}" };
        positionHeader.SetScheme(new Scheme(BorderAttribute));
        Add(positionHeader);

        for (var i = 0; i < NoOfLines; i++) {
            _positionStatusLabels[i] = new Label { X = 0, Y = PositionStatusStartRow + i, Text = string.Empty };
            Add(_positionStatusLabels[i]);
        }

        var shipCargoHeader = new Label {
            X = 0, Y = DividingBarRow,
            Text = "Fleet     fgt  hkr  jmp  jtn  pen  str  trn  men  nnj  amb  che  met  sup  tri ",
        };
        shipCargoHeader.SetScheme(new Scheme(BorderAttribute));
        Add(shipCargoHeader);

        if (_rows.Count == 0) {
            _positionStatusLabels[0].Text = "No fleets have been deployed.";
            Add(new Label { X = 0, Y = ShipCargoStartRow, Text = "No fleets have been deployed." });
        }

        for (var i = 0; i < NoOfLines; i++) {
            _shipCargoLabels[i] = new Label { X = 0, Y = ShipCargoStartRow + i, Text = string.Empty };
            Add(_shipCargoLabels[i]);
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
                    _onSelectRow(_rows[_selectedIndex]);
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
            var attribute = selected ? SelectedAttribute : DispWindAttribute;
            _positionStatusLabels[i].SetScheme(new Scheme(attribute));
            _shipCargoLabels[i].SetScheme(new Scheme(attribute));

            if (index >= _rows.Count) {
                _positionStatusLabels[i].Text = string.Empty;
                _shipCargoLabels[i].Text = string.Empty;
                continue;
            }

            var obj = _rows[index];
            _positionStatusLabels[i].Text = FormatPositionStatus(obj);
            _shipCargoLabels[i].Text = FormatShipCargo(obj);
        }
    }

    private string FormatPositionStatus(ISectorObject obj)
    {
        var name = (obj.Names.GetValueOrDefault(_viewer) ?? CloseUpWindow.DescribeLocation(obj, _viewer)).PadRight(8)[..8];
        var pos = RelativeCoordinate.Format(obj.Location, _origin);
        var des = FormatDestination(obj);
        var status = FormatStatus(obj);
        var owned = ReferenceEquals(obj.Owner, _viewer);
        var range = owned ? FleetLifecycle.EstimatedRange(obj).ToString() : "(unknown)";

        return $"{name} {pos,-8} {des,-8} {status,-16} {range,5}";
    }

    private string FormatDestination(ISectorObject obj) => obj switch {
        Fleet fleet => CloseUpWindow.DescribeFleetDestination(fleet, _viewer, _origin),
        Starbase { Destination: { } dest } => RelativeCoordinate.Format(dest, _origin),
        Starbase => "(none)",
        _ => "",
    };

    private string FormatStatus(ISectorObject obj) => obj switch {
        Fleet fleet => CloseUpWindow.DescribeFleetStatus(fleet, _viewer, _game),
        Starbase { Status: FleetStatus.InTransit } starbase => $"In transit ({FleetLifecycle.EstimatedDateOfArrival(starbase, _game)})",
        Starbase starbase => CloseUpWindow.FleetStatusNames[(int)starbase.Status],
        _ => "",
    };

    private string FormatShipCargo(ISectorObject obj)
    {
        var name = (obj.Names.GetValueOrDefault(_viewer) ?? CloseUpWindow.DescribeLocation(obj, _viewer)).PadRight(8)[..8];
        var owned = ReferenceEquals(obj.Owner, _viewer);
        var scouted = obj is Fleet fleet && Game.Scouted(_viewer, fleet);

        if (!owned && !scouted) {
            return $"{name} (out of range)";
        }

        var holder = (IShipCargoHolder)obj;
        var s = holder.Ships;
        var c = holder.Cargo;
        string ShipLevel(int value) => owned ? $"{value,5}" : $"{CloseUpWindow.YesNo(value),5}";
        string CargoLevel(int value) => owned ? $"{value,5}" : "   --";

        return $"{name} " +
               $"{ShipLevel(s.Fighters)}{ShipLevel(s.HunterKillers)}{ShipLevel(s.Jumpships)}{ShipLevel(s.Jumptransports)}{ShipLevel(s.Penetrators)}{ShipLevel(s.Starships)}{ShipLevel(s.Transports)}" +
               $"{CargoLevel(c.Legions)}{CargoLevel(c.NinjaLegions)}{CargoLevel(c.Ambrosia)}{CargoLevel(c.Chemicals)}{CargoLevel(c.Metals)}{CargoLevel(c.Supplies)}{CargoLevel(c.Trillum)}";
    }
}
