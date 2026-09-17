using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Presentation;
using Reconstructed4021.Core.Types;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;


namespace Reconstructed4021.Tui.Overlays;


// F5 (FLTWIND.PAS: FleetWindow). Same two-pane-per-row shape as StatusOverlay (see its own doc
// comment) over FleetStatusReport's own fleet/starbase list instead. Each row's own text is colored
// by its owner (see _ownerColor) -- port-only addition, no Pascal precedent (real Pascal's own DOS
// display never colored fleet-window rows by owner at all), matching the same per-empire palette
// GalaxyMapScreen's own map glyphs already use, so a Kingdom fleet reads as the same color here as
// it does on the map.
internal sealed class FleetOverlay : IOverlay
{
    private const int NoOfLines = 9; // FLTWIND.PAS: NoOfLines:=(InitHeight DIV 2)-1, InitHeight=21.
    private const int Width = 80; // Must fit a classic 80x24 terminal -- see Height below.

    // Fixed at the window's own size, not shrink-wrapped to whatever terminal this happens to run in.
    // +1 over StatusOverlay's own Height for the ships/cargo toggle hint line; still 23 rows, one
    // short of the 24-row ceiling a classic terminal has to offer.
    private const int Height = NoOfLines * 2 + 5;

    // Real Pascal's own FLTWIND.PAS field was a fixed 8 characters. Fleets are nameable up to 40
    // characters (GalaxyMapScreen's own Deploy/Rename TextPromptOverlay, maxLength: 40), and 8 cut
    // nearly every real name down to an unreadable stub -- but this port's own box still has to fit
    // an actual 80-column terminal (the user's own explicit constraint), and there isn't room for both
    // a much wider name column and all 14 ship+cargo metrics side by side. Showing 7 at a time instead
    // of 14 (see _showCargo) frees enough width to widen this to 20 without growing the box past 80
    // columns; a name past that still gets a plain truncation, not a scrolling marquee -- simple,
    // and rare enough (most real fleet names are short) not to be worth the added state.
    private const int NameColumnWidth = 20;

    private const int PosWidth = 8;
    private const int DesWidth = 8;
    private const int StatusWidth = 16;
    private const int RangeWidth = 5;

    // Where the Range field starts within FormatPositionStatus's own row string -- everything before
    // it (name, a separating space, then each of Pos/Des/Status plus its own separating space).
    private const int RangeColumnStart = NameColumnWidth + 1 + PosWidth + 1 + DesWidth + 1 + StatusWidth + 1;

    private static readonly string PositionHeader = $"{"Fleet",-NameColumnWidth} {"Pos",-PosWidth} {"Des",-DesWidth} {"Status",-StatusWidth} {"Range",RangeWidth}";

    // Ship and cargo metrics (7 columns each, matching ShipType/CargoType's own real counts) no longer
    // share one row -- 14 side by side needed 70 columns on top of the name, more than an 80-wide box
    // has room for. Left/Right toggles which 7 are showing (see _showCargo); both header/value column
    // sets share the metrics' own 5-char field width.
    private static readonly string ShipMetricsHeader = $"{"Fleet",-NameColumnWidth} fgt  hkr  jmp  jtn  pen  str  trn ";
    private static readonly string CargoMetricsHeader = $"{"Fleet",-NameColumnWidth} men  nnj  amb  che  met  sup  tri ";

    private readonly Game _game;
    private readonly Empire _viewer;
    private readonly Coordinate _origin;
    private readonly Func<Empire, ConsoleColor> _ownerColor;
    private readonly ListBox<ISectorObject> _list;
    private readonly Action<ISectorObject> _onSelectRow;
    private bool _showCargo;

    public bool IsDismissed { get; private set; }

    // ownerColor: GalaxyMapScreen's own OwnerColor, passed in rather than recomputed here, so a
    // Kingdom fleet reads as exactly the same color in this window as its glyph does on the map --
    // not just a similar palette independently derived.
    public FleetOverlay(Game game, Empire viewer, Coordinate origin, Func<Empire, ConsoleColor> ownerColor, Action<ISectorObject> onSelectRow)
    {
        _game = game;
        _viewer = viewer;
        _origin = origin;
        _ownerColor = ownerColor;
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
            case ConsoleKey.LeftArrow or ConsoleKey.RightArrow:
                _showCargo = !_showCargo;
                break;
            case ConsoleKey.Enter when _list.SelectedItem is { } row:
                // Stacks Close Up on top instead of dismissing -- see StatusOverlay's own comment.
                _onSelectRow(row);
                break;
            case ConsoleKey.Escape or ConsoleKey.F5:
                IsDismissed = true;
                break;
        }
    }

    private static string FormatName(ISectorObject obj, Empire viewer) =>
        CloseUpOverlay.DisplayName(obj, viewer).PadRight(NameColumnWidth)[..NameColumnWidth];

    private string FormatPositionStatus(ISectorObject obj)
    {
        var name = FormatName(obj, _viewer);
        var pos = RelativeCoordinate.Format(obj.Location, _origin);
        var des = FormatDestination(obj);
        var status = FormatStatus(obj);
        var owned = ReferenceEquals(obj.Owner, _viewer);
        var range = owned ? FleetLifecycle.EstimatedRange(obj).ToString() : "(unknown)";

        return $"{name} {pos,-PosWidth} {des,-DesWidth} {status,-StatusWidth} {range,RangeWidth}";
    }

    // Whether obj's own remaining range (INTRFACE.PAS's EstimatedRange -- years of fuel/trillum left)
    // falls short of what its own current journey needs (EstimatedDateOfArrival). Real Pascal never
    // flagged this in the Fleet window itself -- a fleet just ran dry mid-flight and a FleetOutOfFuel
    // news item showed up after the fact -- but catching it here, before the news does, is the whole
    // point of a status column at all.
    private bool RangeInsufficient(ISectorObject obj)
    {
        if (!ReferenceEquals(obj.Owner, _viewer))
        {
            return false;
        }

        var destination = obj switch
        {
            Fleet fleet => fleet.Destination,
            Starbase starbase => starbase.Destination,
            _ => null,
        };

        if (destination is null || destination == obj.Location)
        {
            return false;
        }

        return FleetLifecycle.EstimatedRange(obj) < FleetLifecycle.EstimatedDateOfArrival(obj, _game);
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

    private string FormatMetrics(ISectorObject obj)
    {
        var name = FormatName(obj, _viewer);
        var owned = ReferenceEquals(obj.Owner, _viewer);
        var scouted = obj is Fleet fleet && Game.Scouted(_viewer, fleet);

        if (!owned && !scouted)
        {
            return $"{name} (out of range)";
        }

        var holder = (IShipCargoHolder)obj;

        if (_showCargo)
        {
            var c = holder.Cargo;
            string CargoLevel(int value) => owned ? $"{value,5}" : "   --";
            return $"{name} {CargoLevel(c.Legions)}{CargoLevel(c.NinjaLegions)}{CargoLevel(c.Ambrosia)}{CargoLevel(c.Chemicals)}{CargoLevel(c.Metals)}{CargoLevel(c.Supplies)}{CargoLevel(c.Trillum)}";
        }

        var s = holder.Ships;
        string ShipLevel(int value) => owned ? $"{value,5}" : $"{CloseUpWindowText.YesNo(value),5}";
        return $"{name} {ShipLevel(s.Fighters)}{ShipLevel(s.HunterKillers)}{ShipLevel(s.Jumpships)}{ShipLevel(s.Jumptransports)}{ShipLevel(s.Penetrators)}{ShipLevel(s.Starships)}{ShipLevel(s.Transports)}";
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

        // Dotted underline (no overline -- this one only separates the header from its own column
        // data below, not two panels from each other) same reasoning as the metrics divider below:
        // distinct from the selection highlight with no color collision, padded to the full interior
        // width so the line reads as one continuous rule under the whole header, not just under
        // however far the header text itself happens to reach.
        fb.DrawText(x + 1, y + 1, PositionHeader.PadRight(width - 2), ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2,
            underline: UnderlineStyle.Dotted);
        _list.Draw(fb, x + 1, y + 2, width - 2, NoOfLines, ConsoleColor.Gray, ConsoleColor.Black, ConsoleColor.Black, ConsoleColor.Gray, obj => _ownerColor(obj.Owner));

        var offset = _list.ScrollOffset;

        // Range field redrawn in red, right over the same cells _list.Draw just wrote, wherever a
        // fleet/starbase's own remaining range falls short of its current journey -- see
        // RangeInsufficient's own doc comment for why. Selection's own black-on-gray still shows
        // through as the background; only the digits themselves turn red.
        for (var row = 0; row < NoOfLines; row++)
        {
            var index = offset + row;
            if (index >= _list.Items.Count || !RangeInsufficient(_list.Items[index]))
            {
                continue;
            }

            var range = FleetLifecycle.EstimatedRange(_list.Items[index]).ToString().PadLeft(RangeWidth);
            var selected = index == _list.SelectedIndex;
            fb.DrawText(x + 1 + RangeColumnStart, y + 2 + row, range, ConsoleColor.Red, selected ? ConsoleColor.Gray : ConsoleColor.Black, maxWidth: RangeWidth);
        }

        // FLTWIND.PAS's own DividingBar (:58,161) is drawn inverted (C.SYSTBorder) to separate the two
        // synchronized panels -- not blank whitespace. An inverted Black-on-Gray bar here would be
        // indistinguishable from this same overlay's own selected-row highlight (identical colors), so
        // the divider is framed with dotted underline+overline instead, in the ordinary Gray-on-Black
        // every other header already uses -- distinct from both plain rows (no lines) and the
        // selection highlight (a color swap, no lines), with no color collision either way.
        var metricsHeaderRow = y + 2 + NoOfLines;
        var metricsHeaderText = (_showCargo ? CargoMetricsHeader : ShipMetricsHeader).PadRight(width - 2);
        fb.DrawText(x + 1, metricsHeaderRow, metricsHeaderText, ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2,
            underline: UnderlineStyle.Dotted, overline: true);

        for (var row = 0; row < NoOfLines; row++)
        {
            var index = offset + row;
            var selected = index == _list.SelectedIndex;
            var text = index < _list.Items.Count ? FormatMetrics(_list.Items[index]) : string.Empty;
            var visible = text.Length > width - 2 ? text[..(width - 2)] : text.PadRight(width - 2);
            var rowFg = !selected && index < _list.Items.Count ? _ownerColor(_list.Items[index].Owner) : ConsoleColor.Gray;
            fb.DrawText(x + 1, metricsHeaderRow + 1 + row, visible, selected ? ConsoleColor.Black : rowFg, selected ? ConsoleColor.Gray : ConsoleColor.Black);
        }

        fb.DrawText(x + 1, y + height - 2, "<-/-> ships/cargo   Enter: examine   Esc: close", ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
    }
}
