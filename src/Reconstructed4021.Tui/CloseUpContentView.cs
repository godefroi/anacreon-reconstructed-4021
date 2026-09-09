using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// The "Close Up" tab inside <see cref="CloseUpWindow"/> -- extracted so it's one persistent tab view
/// managed by <see cref="TabbedWindow"/>, alongside the Orders tab (<see cref="FleetOrdersTabView"/>)
/// on one of the player's own fleets. Layout/redaction rules are exactly CloseUpWindow's own former
/// inline content -- see that class's own doc comment for the CLSCOMM.PAS field-layout citations.
/// </summary>
internal sealed class CloseUpContentView : View
{
    // COLORS.INC's ColorScrColor: SYSDispWind = 23 (LightGray on Blue) -- see CloseUpWindow's own
    // remarks; this tab's content area shares it, the border/title stays CloseUpWindow's own concern.
    private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue);

    public CloseUpContentView(ISectorObject obj, Empire viewer, Game game)
    {
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;
        SetScheme(new Scheme(DispWindAttribute));

        var name = obj.Names.GetValueOrDefault(viewer) ?? CloseUpWindow.DescribeLocation(obj, viewer);
        var fleet = obj as Fleet;

        // DisplayBasicInfo's own owner-name gate (Known) vs DisplayFleetInfo's (Scouted, CLSCOMM.PAS:
        // 685-690) genuinely differ -- a world only needs Known, which this window already requires
        // to open at all (Game.Visible), so its owner name and type are unconditional; an unscouted
        // fleet's type/owner are blank instead.
        string headerKind, headerOwner;
        if (fleet is not null) {
            var fleetScouted = Game.ScoutedOrOwned(viewer, fleet);
            headerKind = fleetScouted ? CloseUpWindow.FleetTypeNames[(int)fleet.Type] : "";
            headerOwner = fleetScouted ? obj.Owner.Name : "";
        } else {
            headerKind = CloseUpWindow.DescribeKind(obj);
            headerOwner = obj.Owner.Name;
        }

        // Row 1 (DisplayBasicInfo/DisplayFleetInfo): "Close Up: Name" at col 1, type name at col 36,
        // owner at col 60 -- one visual header line built from three separately-positioned Labels
        // rather than reproducing AdjustString's manual space-padding.
        AddAt(0, 0, $"Close Up: {name}");
        AddAt(35, 0, headerKind);
        AddAt(59, 0, headerOwner);

        if (fleet is not null) {
            LayoutFleet(fleet, viewer, game);
        } else if (obj is IEconomicWorld world) {
            LayoutWorld(world, viewer, game);
        }

        // No Pascal equivalent -- TUI-only shortcut legend for GameShell.ShowCloseUp's own D/C/T/J/A
        // handling (per the user's own explicit request). D deploys from whatever world is at obj's
        // own Location -- see GameShell.DeployFleet(Coordinate)'s own doc comment; C/T/J/A only ever
        // act on one of your own fleets. This window never opens on one of the player's own worlds at
        // all any more -- GameShell.ShowCloseUp routes that case to WorldInfoWindow instead -- so
        // obj is only ever an enemy/independent world or any fleet here. Gated the same way
        // GameShell.FleetActionHint's own D check is (obj itself owned, since Deploy's own source is
        // always obj's location, never the fleet at it) -- an unowned world or an enemy's fleet
        // offering "Deploy from here" only to have PickDeploySource reject it every time was the actual
        // bug a user reported ("I am offered the choice to 'deploy from here' ... for a world I don't
        // own").
        var fleetOwned = fleet is not null && ReferenceEquals(fleet.Owner, viewer);
        var worldOwned = obj is IEconomicWorld ownedWorld && ReferenceEquals(ownedWorld.Owner, viewer);
        if (fleetOwned) {
            // Split across two rows -- the full line runs past this window's own 78-column content
            // width (Width=80 minus the border), and Label doesn't wrap on its own. An owned fleet
            // always has an Orders tab (CloseUpWindow's own constructor), hence the Ctrl+PgUp/PgDn hint.
            AddAt(1, 17, "D:Deploy  C:Change Destination  T:Transfer  J:Abort/Join  A:Attack  R:Refuel");
            AddAt(1, 18, "Ctrl+PgUp/PgDn: Orders tab   (other key: close)");
        } else if (worldOwned) {
            AddAt(1, 17, "D:Deploy from here  (other key: close)");
        } else {
            AddAt(1, 17, "(any key: close)");
        }
    }

    /// <summary>
    /// DisplayBasicInfo (col 2/24) + DisplayCargoInfo (col 51-52) + DisplayMilitaryInfo (col 2-3) +
    /// DisplayBackground (col 2, row 10), CLSCOMM.PAS:500-634,804-805. Four separate redaction rules,
    /// not one applied three or four times: the Cls/Tech/Pop/Eff/Amb/Rev block needs
    /// <see cref="Game.ScoutedOrOwned"/> or it's left blank (labels only); the amb/che/met/sup/tri
    /// cargo line is gated on ownership alone, never shown for anyone else's world no matter how well
    /// scouted (DisplayCargoInfo has no Scouted branch at all); the ships/legions/defenses line falls
    /// back to <see cref="CloseUpWindow.YesNo"/>'s coarse magnitude bucket when Scouted-but-not-owned,
    /// or "????" when not even that; the scenario's own flavor text (<see cref="Game.FindWorldBackgroundText"/>)
    /// needs the same Scouted gate as the basic-info block, matching CloseUpCommand's own <c>IF
    /// Scouted(Player,Obj) THEN DisplayBackground(...)</c> — not shown at all for an owned-but-not-
    /// yet-Scouted world either, real Pascal's own condition, not an oversight.
    /// </summary>
    private void LayoutWorld(IEconomicWorld world, Empire viewer, Game game)
    {
        var owned = ReferenceEquals(world.Owner, viewer);
        var scouted = Game.ScoutedOrOwned(viewer, world);

        AddAt(1, 2, " Cls:"); AddAt(1, 3, "Tech:"); AddAt(1, 4, " Pop:");
        AddAt(23, 2, "Eff:"); AddAt(23, 3, "Amb:"); AddAt(23, 4, "Rev:");
        if (scouted) {
            AddAt(7, 2, world.EffectiveClass.ToString());
            AddAt(7, 3, world.TechLevel.ToString());
            AddAt(7, 4, world.Population.ToString());
            AddAt(28, 2, $"{world.Efficiency}%");
            AddAt(28, 3, world.IsAddictedToAmbrosia ? "yes" : "no");
            AddAt(28, 4, world.RevolutionIndex.ToString());
        }

        AddAt(51, 2, "amb  che  met  sup  tri");
        AddAt(49, 3, owned
            ? $"{world.Cargo.Ambrosia,5}{world.Cargo.Chemicals,5}{world.Cargo.Metals,5}{world.Cargo.Supplies,5}{world.Cargo.Trillum,5}"
            : "???? ???? ???? ???? ????");

        AddAt(2, 6, "fgt  hkr  jmp  jtn  pen  str  trn    men  nnj    LAM  def  GDM  ion");
        var s = world.Ships;
        var c = world.Cargo;
        var d = world.Defenses;
        string Level(int value) => owned ? value.ToString() : scouted ? CloseUpWindow.YesNo(value) : "????";
        AddAt(0, 7,
            $"{Level(s.Fighters),5}{Level(s.HunterKillers),5}{Level(s.Jumpships),5}{Level(s.Jumptransports),5}{Level(s.Penetrators),5}{Level(s.Starships),5}{Level(s.Transports),5}" +
            $"  {Level(c.Legions),5}{Level(c.NinjaLegions),5}" +
            $"  {Level(d.Lams),5}{Level(d.DefenseSatellites),5}{Level(d.Gdms),5}{Level(d.IonCannons),5}");

        if (scouted && Game.FindWorldBackgroundText(game, world, viewer, conquer: false) is { } background) {
            for (var i = 0; i < background.Count; i++)
                AddAt(1, 9 + i, background[i]);
        }
    }

    /// <summary>
    /// DisplayFleetInfo (col 1-2/14-15) + DisplayFleetComplement (col 2-3), CLSCOMM.PAS:654-768.
    /// Position is unconditional (real Pascal computes it the same way regardless of ownership); Range
    /// is the opposite -- always "(unknown)" for anyone but the owner, not even upgraded by Scouted.
    /// The complement line's ship types (fgt..trn) get <see cref="CloseUpWindow.YesNo"/> when
    /// Scouted-but-not-owned; everything else in that line (legions, amb/che/met/sup/tri) only ever
    /// shows for the owner, exactly like <see cref="LayoutWorld"/>'s own cargo line --
    /// DisplayFleetComplement's own <c>ResI IN [fgt..trn]</c> guard on its Scouted branch, not a
    /// restriction this port invented.
    /// </summary>
    private void LayoutFleet(Fleet fleet, Empire viewer, Game game)
    {
        var owned = ReferenceEquals(fleet.Owner, viewer);
        var scouted = Game.ScoutedOrOwned(viewer, fleet);
        var origin = viewer.Capital?.Location ?? new Coordinate(game.Galaxy.Size / 2, game.Galaxy.Size / 2);

        AddAt(1, 2, "   Position:");
        AddAt(14, 2, RelativeCoordinate.Format(fleet.Location, origin));

        AddAt(1, 3, "     Status:");
        AddAt(14, 3, CloseUpWindow.DescribeFleetStatus(fleet, viewer, game));

        AddAt(1, 4, "Destination:");
        AddAt(14, 4, CloseUpWindow.DescribeFleetDestination(fleet, viewer, origin));

        AddAt(1, 5, "      Range:");
        AddAt(14, 5, owned ? FleetLifecycle.EstimatedRange(fleet).ToString() : "(unknown)");

        AddAt(2, 7, "fgt  hkr  jmp  jtn  pen  str  trn  men  nnj  amb  che  met  sup  tri");
        var s = fleet.Ships;
        var c = fleet.Cargo;
        string ShipLevel(int value) => owned ? value.ToString() : scouted ? CloseUpWindow.YesNo(value) : "????";
        string CargoLevel(int value) => owned ? value.ToString() : "????";
        AddAt(0, 8,
            $"{ShipLevel(s.Fighters),5}{ShipLevel(s.HunterKillers),5}{ShipLevel(s.Jumpships),5}{ShipLevel(s.Jumptransports),5}{ShipLevel(s.Penetrators),5}{ShipLevel(s.Starships),5}{ShipLevel(s.Transports),5}" +
            $"{CargoLevel(c.Legions),5}{CargoLevel(c.NinjaLegions),5}{CargoLevel(c.Ambrosia),5}{CargoLevel(c.Chemicals),5}{CargoLevel(c.Metals),5}{CargoLevel(c.Supplies),5}{CargoLevel(c.Trillum),5}");
    }

    private void AddAt(int x, int y, string text) => Add(new Label { X = x, Y = y, Text = text });
}
