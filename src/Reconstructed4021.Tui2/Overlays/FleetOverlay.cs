using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Presentation;
using Reconstructed4021.Core.Types;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;
using Reconstructed4021.Tui2.Shared;


namespace Reconstructed4021.Tui2.Overlays;


// F5 (FLTWIND.PAS: FleetWindow). Same two-pane-per-row shape as StatusOverlay (see its own doc
// comment) over FleetStatusReport's own fleet/starbase list instead.
internal sealed class FleetOverlay : IOverlay
{
    private const int NoOfLines = 9; // FLTWIND.PAS: NoOfLines:=(InitHeight DIV 2)-1, InitHeight=21.
    private const int Width = 86;

    // Fixed at the window's own size at 80x25 (see StatusOverlay's identical Height comment), not
    // shrink-wrapped to whatever terminal this happens to run in.
    private const int Height = NoOfLines * 2 + 4;

    private static readonly string PositionHeader = $"{"Fleet",-8} {"Pos",-8} {"Des",-8} {"Status",-16} {"Range",5}";
    private const string ShipCargoHeader = "Fleet     fgt  hkr  jmp  jtn  pen  str  trn  men  nnj  amb  che  met  sup  tri ";

    private readonly Game _game;
    private readonly Empire _viewer;
    private readonly Coordinate _origin;
    private readonly ListBox<ISectorObject> _list;
    private readonly Action<ISectorObject> _onSelectRow;

    public bool IsDismissed { get; private set; }

    public FleetOverlay(Game game, Empire viewer, Coordinate origin, Action<ISectorObject> onSelectRow)
    {
        _game = game;
        _viewer = viewer;
        _origin = origin;
        _onSelectRow = onSelectRow;
        _list = new ListBox<ISectorObject>(FleetStatusReport.BuildRows(game.Galaxy, viewer), FormatPositionStatus);
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_list.HandleKey(key))
        {
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.Enter when _list.SelectedItem is { } row:
                // Stacks Close Up on top instead of dismissing -- see StatusOverlay's own comment.
                _onSelectRow(row);
                break;
            case ConsoleKey.Escape or ConsoleKey.F5:
                IsDismissed = true;
                break;
        }
    }

    private string FormatPositionStatus(ISectorObject obj)
    {
        var name = CloseUpOverlay.DisplayName(obj, _viewer).PadRight(8)[..8];
        var pos = RelativeCoordinate.Format(obj.Location, _origin);
        var des = FormatDestination(obj);
        var status = FormatStatus(obj);
        var owned = ReferenceEquals(obj.Owner, _viewer);
        var range = owned ? FleetLifecycle.EstimatedRange(obj).ToString() : "(unknown)";

        return $"{name} {pos,-8} {des,-8} {status,-16} {range,5}";
    }

    private string FormatDestination(ISectorObject obj) => obj switch
    {
        Fleet fleet => CloseUpWindowText.DescribeFleetDestination(fleet, _viewer, _origin),
        Starbase { Destination: { } dest } => RelativeCoordinate.Format(dest, _origin),
        Starbase => "(none)",
        _ => "",
    };

    private string FormatStatus(ISectorObject obj) => obj switch
    {
        Fleet fleet => CloseUpWindowText.DescribeFleetStatus(fleet, _viewer, _game),
        Starbase { Status: FleetStatus.InTransit } starbase => $"In transit ({FleetLifecycle.EstimatedDateOfArrival(starbase, _game)})",
        Starbase starbase => CloseUpWindowText.FleetStatusNames[(int)starbase.Status],
        _ => "",
    };

    private string FormatShipCargo(ISectorObject obj)
    {
        var name = CloseUpOverlay.DisplayName(obj, _viewer).PadRight(8)[..8];
        var owned = ReferenceEquals(obj.Owner, _viewer);
        var scouted = obj is Fleet fleet && Game.Scouted(_viewer, fleet);

        if (!owned && !scouted)
        {
            return $"{name} (out of range)";
        }

        var holder = (IShipCargoHolder)obj;
        var s = holder.Ships;
        var c = holder.Cargo;
        string ShipLevel(int value) => owned ? $"{value,5}" : $"{CloseUpWindowText.YesNo(value),5}";
        string CargoLevel(int value) => owned ? $"{value,5}" : "   --";

        return $"{name} " +
               $"{ShipLevel(s.Fighters)}{ShipLevel(s.HunterKillers)}{ShipLevel(s.Jumpships)}{ShipLevel(s.Jumptransports)}{ShipLevel(s.Penetrators)}{ShipLevel(s.Starships)}{ShipLevel(s.Transports)}" +
               $"{CargoLevel(c.Legions)}{CargoLevel(c.NinjaLegions)}{CargoLevel(c.Ambrosia)}{CargoLevel(c.Chemicals)}{CargoLevel(c.Metals)}{CargoLevel(c.Supplies)}{CargoLevel(c.Trillum)}";
    }

    public void Draw(FrameBuffer fb)
    {
        var width = Math.Min(Width, fb.Width);
        var height = Math.Min(Height, fb.Height);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        fb.DrawText(x + Math.Max(1, (width - 7) / 2), y, " Fleet ", ConsoleColor.White, ConsoleColor.Black);

        if (_list.Items.Count == 0)
        {
            fb.DrawText(x + 1, y + 1, "No fleets have been deployed.", ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
            return;
        }

        fb.DrawText(x + 1, y + 1, PositionHeader, ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
        _list.Draw(fb, x + 1, y + 2, width - 2, NoOfLines, ConsoleColor.Gray, ConsoleColor.Black, ConsoleColor.Black, ConsoleColor.Gray);

        var cargoHeaderRow = y + 2 + NoOfLines;
        fb.DrawText(x + 1, cargoHeaderRow, ShipCargoHeader, ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);

        var offset = _list.ScrollOffset;
        for (var row = 0; row < NoOfLines; row++)
        {
            var index = offset + row;
            var selected = index == _list.SelectedIndex;
            var text = index < _list.Items.Count ? FormatShipCargo(_list.Items[index]) : string.Empty;
            var visible = text.Length > width - 2 ? text[..(width - 2)] : text.PadRight(width - 2);
            fb.DrawText(x + 1, cargoHeaderRow + 1 + row, visible, selected ? ConsoleColor.Black : ConsoleColor.Gray, selected ? ConsoleColor.Gray : ConsoleColor.Black);
        }
    }
}
