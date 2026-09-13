using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Presentation;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;

namespace Reconstructed4021.Tui2;

// CLSCOMM.PAS's CloseUpCom, ported from Reconstructed4021.Tui's CloseUpWindow/CloseUpContentView --
// see those two files' own doc comments for the field-layout (row/column) and per-field redaction
// citations transcribed verbatim below. Two real deviations from that port, both per the user's own
// explicit direction this time around:
//
//  - Real Pascal's shared display window title is dynamic per-turn text ("Anacreon: {Year} ({Age}th
//    year of your reign.)", DISPLAY.PAS:156-162) -- never the object's own name. Tui1 already dropped
//    that text from its own CloseUpWindow.Title to make room for name+tabs (that class's own doc
//    comment). This port goes one step further: name/coordinates sit in the frame's own title
//    position and the tab strip gets a separate region on the right of the same border row
//    (TabFrame), rather than concatenating both into one string.
//  - Only the Close Up tab exists here -- the Orders tab (fleet order-script editing) needs a
//    multi-line text editor this engine doesn't have yet; TabFrame itself doesn't special-case a
//    single tab, so adding Orders later is just one more AddTab-equivalent call.
//
// F2 rename and the D/C/T/J/A/R fleet-action shortcuns are still Tui1-only for now -- not wired here
// (this slice is examine-only); folding them in is cheap once ExamineCursor's own callers need them.
internal sealed class CloseUpOverlay : IOverlay
{
    private const int FrameWidth = 80;
    private const int FrameHeight = 21;

    private const ConsoleColor BorderFg = ConsoleColor.Gray; // SYSWBorder = 7 (LightGray on Black).
    private const ConsoleColor BorderBg = ConsoleColor.Black;
    private const ConsoleColor ContentFg = ConsoleColor.Gray; // SYSDispWind = 23 (LightGray on Blue).
    private const ConsoleColor ContentBg = ConsoleColor.DarkBlue; // DOS background attributes have no bright variant -- see FrameBuffer.AnsiCode's own note on this.

    // DisplayFleetInfo's FltTypeName (CLSCOMM.PAS:670-675), ordinal-aligned with FleetType.
    private static readonly string[] FleetTypeNames =
        ["Warpfleet", "Jumpfleet", "Hunter-Killer Fleet", "Stealth Fleet", "Fast-Warp Fleet"];

    private readonly ISectorObject _obj;
    private readonly Empire _viewer;
    private readonly Game _game;
    private readonly TabFrame _frame = new(["Close Up"]);

    public bool IsDismissed { get; private set; }

    public CloseUpOverlay(ISectorObject obj, Empire viewer, Game game)
    {
        _obj = obj;
        _viewer = viewer;
        _game = game;
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_frame.HandleKey(key))
        {
            return;
        }

        // Any other key closes -- CloseUpWindow's own doc comment on this already being a deviation
        // from real Pascal's non-modal CloseUpCom.
        IsDismissed = true;
    }

    public void Draw(FrameBuffer fb)
    {
        var width = Math.Min(FrameWidth, fb.Width);
        var height = Math.Min(FrameHeight, fb.Height);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        var name = _obj.Names.GetValueOrDefault(_viewer) ?? DescribeLocation(_obj, _viewer);
        _frame.Draw(fb, x, y, width, height, name, BorderFg, BorderBg, ConsoleColor.White, BorderBg);

        var contentX = x + 1;
        var contentY = y + 1;
        var contentWidth = width - 2;
        var contentHeight = height - 2;
        for (var row = 0; row < contentHeight; row++)
        {
            fb.DrawText(contentX, contentY + row, new string(' ', contentWidth), ContentFg, ContentBg);
        }

        DrawContent(fb, contentX, contentY, contentWidth);
    }

    private void DrawContent(FrameBuffer fb, int cx, int cy, int cw)
    {
        void At(int x, int y, string text)
        {
            if (x < cw)
            {
                fb.DrawText(cx + x, cy + y, text.Length > cw - x ? text[..(cw - x)] : text, ContentFg, ContentBg);
            }
        }

        var fleet = _obj as Fleet;
        string headerKind, headerOwner;
        if (fleet is not null)
        {
            var fleetScouted = Game.ScoutedOrOwned(_viewer, fleet);
            headerKind = fleetScouted ? FleetTypeNames[(int)fleet.Type] : "";
            headerOwner = fleetScouted ? _obj.Owner.Name : "";
        }
        else
        {
            headerKind = DescribeKind(_obj);
            headerOwner = _obj.Owner.Name;
        }

        var name = _obj.Names.GetValueOrDefault(_viewer) ?? DescribeLocation(_obj, _viewer);
        At(0, 0, $"Close Up: {name}");
        At(35, 0, headerKind);
        At(59, 0, headerOwner);

        if (fleet is not null)
        {
            LayoutFleet(fleet, At);
        }
        else if (_obj is IEconomicWorld world)
        {
            LayoutWorld(world, At);
        }

        var fleetOwned = fleet is not null && ReferenceEquals(fleet.Owner, _viewer);
        var worldOwned = _obj is IEconomicWorld ownedWorld && ReferenceEquals(ownedWorld.Owner, _viewer);
        At(1, 17, fleetOwned || worldOwned
            ? "(fleet/world actions not wired up yet -- any key closes)"
            : "(any key closes)");
    }

    private void LayoutWorld(IEconomicWorld world, Action<int, int, string> at)
    {
        var owned = ReferenceEquals(world.Owner, _viewer);
        var scouted = Game.ScoutedOrOwned(_viewer, world);

        at(1, 2, " Cls:"); at(1, 3, "Tech:"); at(1, 4, " Pop:");
        at(23, 2, "Eff:"); at(23, 3, "Amb:"); at(23, 4, "Rev:");
        if (scouted)
        {
            at(7, 2, world.EffectiveClass.ToString());
            at(7, 3, world.TechLevel.ToString());
            at(7, 4, world.Population.ToString());
            at(28, 2, $"{world.Efficiency}%");
            at(28, 3, world.IsAddictedToAmbrosia ? "yes" : "no");
            at(28, 4, world.RevolutionIndex.ToString());
        }

        at(51, 2, "amb  che  met  sup  tri");
        at(49, 3, owned
            ? $"{world.Cargo.Ambrosia,5}{world.Cargo.Chemicals,5}{world.Cargo.Metals,5}{world.Cargo.Supplies,5}{world.Cargo.Trillum,5}"
            : "???? ???? ???? ???? ????");

        at(2, 6, "fgt  hkr  jmp  jtn  pen  str  trn    men  nnj    LAM  def  GDM  ion");
        var s = world.Ships;
        var c = world.Cargo;
        var d = world.Defenses;
        string Level(int value) => owned ? value.ToString() : scouted ? CloseUpWindowText.YesNo(value) : "????";
        at(0, 7,
            $"{Level(s.Fighters),5}{Level(s.HunterKillers),5}{Level(s.Jumpships),5}{Level(s.Jumptransports),5}{Level(s.Penetrators),5}{Level(s.Starships),5}{Level(s.Transports),5}" +
            $"  {Level(c.Legions),5}{Level(c.NinjaLegions),5}" +
            $"  {Level(d.Lams),5}{Level(d.DefenseSatellites),5}{Level(d.Gdms),5}{Level(d.IonCannons),5}");

        if (scouted && Game.FindWorldBackgroundText(_game, world, _viewer, conquer: false) is { } background)
        {
            for (var i = 0; i < background.Count; i++)
            {
                at(1, 9 + i, background[i]);
            }
        }
    }

    private void LayoutFleet(Fleet fleet, Action<int, int, string> at)
    {
        var owned = ReferenceEquals(fleet.Owner, _viewer);
        var scouted = Game.ScoutedOrOwned(_viewer, fleet);
        var origin = _viewer.Capital?.Location ?? new Coordinate(_game.Galaxy.Size / 2, _game.Galaxy.Size / 2);

        at(1, 2, "   Position:");
        at(14, 2, RelativeCoordinate.Format(fleet.Location, origin));

        at(1, 3, "     Status:");
        at(14, 3, CloseUpWindowText.DescribeFleetStatus(fleet, _viewer, _game));

        at(1, 4, "Destination:");
        at(14, 4, CloseUpWindowText.DescribeFleetDestination(fleet, _viewer, origin));

        at(1, 5, "      Range:");
        at(14, 5, owned ? FleetLifecycle.EstimatedRange(fleet).ToString() : "(unknown)");

        at(2, 7, "fgt  hkr  jmp  jtn  pen  str  trn  men  nnj  amb  che  met  sup  tri");
        var s = fleet.Ships;
        var c = fleet.Cargo;
        string ShipLevel(int value) => owned ? value.ToString() : scouted ? CloseUpWindowText.YesNo(value) : "????";
        string CargoLevel(int value) => owned ? value.ToString() : "????";
        at(0, 8,
            $"{ShipLevel(s.Fighters),5}{ShipLevel(s.HunterKillers),5}{ShipLevel(s.Jumpships),5}{ShipLevel(s.Jumptransports),5}{ShipLevel(s.Penetrators),5}{ShipLevel(s.Starships),5}{ShipLevel(s.Transports),5}" +
            $"{CargoLevel(c.Legions),5}{CargoLevel(c.NinjaLegions),5}{CargoLevel(c.Ambrosia),5}{CargoLevel(c.Chemicals),5}{CargoLevel(c.Metals),5}{CargoLevel(c.Supplies),5}{CargoLevel(c.Trillum),5}");
    }

    internal static string DescribeKind(ISectorObject obj) => obj switch
    {
        Planet p => p.Type.ToString(),
        Starbase s => s.Kind.ToString(),
        Fleet => "Fleet",
        Stargate g => g.Kind.ToString(),
        ConstructionSite c => $"{c.Building} site",
        _ => "Unknown",
    };

    internal static string DescribeLocation(ISectorObject obj, Empire viewer) => obj switch
    {
        Fleet => "Fleet",
        _ => RelativeCoordinate.Format(obj.Location, viewer.Capital?.Location ?? new Coordinate(0, 0)),
    };
}
