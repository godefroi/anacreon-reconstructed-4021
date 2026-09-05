using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// CLSCOMM.PAS: CloseUpCom. Real Pascal draws this into DISPLAY.PAS's own shared "display window"
/// (<c>DrawScreen</c>'s <c>OpenWindow(1,4,80,21,ThinBRD,...,C.SYSDispWind,C.SYSWBorder,...)</c> --
/// the one panel every command shares, including the galaxy map itself in the original) -- this
/// window reproduces that exact size (80x21, ThinBRD single-line border, SYSDispWind color) but
/// centered over the map instead of pinned under the menu bar, since this port's galaxy map is the
/// permanent shell rather than one of several panels sharing that fixed slot
/// (docs/TUI_SURFACES_MAPPING.md's "Deliberate deviation" section). Added/removed directly as a
/// child of the running <see cref="GameShell"/> rather than its own <c>Application.Run</c> -- see
/// that class's own "Windows added/removed from the Toplevel" convention for overlays. No
/// "press any key" hint: real Pascal has none -- CloseUpCom just draws into the shared window and
/// returns control to the normal command loop, it isn't a modal dismiss the way this port's version
/// (any key removes the window) approximates it. The border title is that same shared window's own
/// persistent title (<c>DrawScreen</c>'s <c>Line</c>, "Anacreon: Year (Nth year of your reign.)"),
/// not anything specific to Close Up -- see <see cref="DisplayWindowTitle"/>.
///
/// Field layout (row/column positions) is transcribed directly from CLSCOMM.PAS's own
/// <c>DisplayBasicInfo</c>/<c>DisplayCargoInfo</c>/<c>DisplayMilitaryInfo</c> (non-fleet objects) and
/// <c>DisplayFleetInfo</c>/<c>DisplayFleetComplement</c> (fleets) -- every X/Y below is that source's
/// own 1-based <c>WriteString(...,x,y,...)</c> column/row minus 1. Field-level redaction is real too
/// (<see cref="LayoutWorld"/>/<see cref="LayoutFleet"/>'s own doc comments have the exact per-field
/// rules) -- this window can only be opened on something at least Known (<see cref="GameShell.ObjectsAt"/>'s
/// own <see cref="Game.Visible"/> gate), but Known alone still redacts most fields further down to
/// "????"/"(unknown)" until the viewer's own <see cref="Game.ScoutedOrOwned"/> tier is met.
/// </summary>
internal sealed class CloseUpWindow : Window
{
    // DisplayFleetInfo's FltTypeName (CLSCOMM.PAS:670-675) -- ordinal-aligned with FleetType (see
    // that enum's own doc comment: "Matches Pascal's FleetTypes / TypeOfFleet").
    private static readonly string[] FleetTypeNames =
        ["Warpfleet", "Jumpfleet", "Hunter-Killer Fleet", "Stealth Fleet", "Fast-Warp Fleet"];

    // DisplayFleetInfo's FltStatusName (CLSCOMM.PAS:664-668) -- ordinal-aligned with FleetStatus.
    // InTransit's real text is built specially below (EDA for your own fleet, "(?)" otherwise), so
    // this entry is never read as-is. Internal: FleetWindow reuses this for its own starbase rows
    // (Command Center/Fortress), which have no separate redaction path of their own to go through.
    internal static readonly string[] FleetStatusNames = ["at destination", "In transit", "out of trillum", "lost"];

    // COLORS.INC's ColorScrColor: SYSDispWind = 23 (LightGray on Blue, the content area) vs
    // SYSWBorder = 7 (LightGray on Black, the border/title) -- real Pascal genuinely uses two
    // different backgrounds here, confirmed by re-reading COLORS.INC rather than assumed.
    private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue);
    private static readonly TgAttribute BorderAttribute = new(StandardColor.LightGray, StandardColor.Black);

    public CloseUpWindow(ISectorObject obj, Empire viewer, Game game)
    {
        Title = DisplayWindowTitle(game, viewer); // DrawScreen's own Line (DISPLAY.PAS:156-162) -- the shared window border every command (this one included) draws inside.
        Width = 80;
        Height = 21;
        X = Pos.Center();
        Y = Pos.Center();
        BorderStyle = LineStyle.Single; // ThinBRD
        CanFocus = true;
        SetScheme(new Scheme(DispWindAttribute));
        Border.View?.SetScheme(new Scheme(BorderAttribute));

        var name = obj.Names.GetValueOrDefault(viewer) ?? DescribeLocation(obj, viewer);
        var fleet = obj as Fleet;

        // DisplayBasicInfo's own owner-name gate (Known) vs DisplayFleetInfo's (Scouted, CLSCOMM.PAS:
        // 685-690) genuinely differ -- a world only needs Known, which this window already requires
        // to open at all (Game.Visible), so its owner name and type are unconditional; an unscouted
        // fleet's type/owner are blank instead.
        string headerKind, headerOwner;
        if (fleet is not null) {
            var fleetScouted = Game.ScoutedOrOwned(viewer, fleet);
            headerKind = fleetScouted ? FleetTypeNames[(int)fleet.Type] : "";
            headerOwner = fleetScouted ? obj.Owner.Name : "";
        } else {
            headerKind = DescribeKind(obj);
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
        // own Location and is offered regardless of obj's type -- see
        // GameShell.DeployFleet(Coordinate)'s own doc comment; C/T/J/A only ever act on one of your
        // own fleets. This window never opens on one of the player's own worlds at all any more --
        // GameShell.ShowCloseUp routes that case to WorldInfoWindow instead -- so there's no
        // world-owned branch here to show.
        var fleetOwned = fleet is not null && ReferenceEquals(fleet.Owner, viewer);
        if (fleetOwned) {
            // Split across two rows -- the full line runs to 88 columns, past this window's own
            // 78-column content width (Width=80 minus the border), and Label doesn't wrap on its own.
            AddAt(1, 17, "D:Deploy  C:Change Destination  T:Transfer  J:Abort/Join  A:Attack");
            AddAt(1, 18, "(other key: close)");
        } else {
            AddAt(1, 17, "D:Deploy from here  (other key: close)");
        }
    }

    /// <summary>
    /// DrawScreen's own Line (DISPLAY.PAS:156-162): "Anacreon: {Year} ({Age}{ordinal suffix} year
    /// of your reign.)", Age = EmpireAge(Player)+1 = Game.Year - FoundingYear + 1
    /// (PRIMINTR.PAS:977-980). The ordinal suffix formula is OrdinalString (STRG.PAS:48-65) --
    /// standard English ordinals, with the 11th-13th "always th" exception baked into its own
    /// Penultim=1 check.
    /// </summary>
    internal static string DisplayWindowTitle(Game game, Empire viewer)
    {
        var age = game.Year - viewer.FoundingYear + 1;
        return $"Anacreon: {game.Year} ({age}{DisplayText.OrdinalSuffix(age)} year of your reign.)";
    }

    /// <summary>"Kind" label shown alongside a name (this window's own header "type" column) -- not a
    /// name-fallback itself. See <see cref="DescribeLocation"/> for that job.</summary>
    public static string DescribeKind(ISectorObject obj) => obj switch {
        Planet p => p.Type.ToString(),
        Starbase s => s.Kind.ToString(),
        Fleet => "Fleet",
        Stargate g => g.Kind.ToString(),
        ConstructionSite c => $"{c.Building} site",
        _ => "Unknown",
    };

    /// <summary>
    /// GetName's own ShortFormat fallback (PRIMINTR.PAS:1467-1528) for an object with no player-given
    /// name: a plain relative coordinate, never the object's own type/designation -- confirmed by
    /// reading real Pascal's GetName after a user report that an unnamed Independent-designated world
    /// showed literally "Independent" wherever a name was expected, reading like an empire name rather
    /// than a placeholder. <see cref="DescribeKind"/> is genuinely a different job (a "kind" label
    /// shown *alongside* a name); it was being reused here too, which was the bug.
    /// </summary>
    public static string DescribeLocation(ISectorObject obj, Empire viewer) => obj switch {
        Fleet => "Fleet", // GetFleetName's own "Fleet<N>"/"Enemy<N>" -- no slot index in this port to mirror exactly
        _ => RelativeCoordinate.Format(obj.Location, viewer.Capital?.Location ?? new Coordinate(0, 0)),
    };

    /// <summary>
    /// GetName's own LongFormat fallback (same source): the coarse object kind plus coordinate, e.g.
    /// "planet at 5,10" -- real Pascal's own literal example in GetName's doc comment. For contexts
    /// where a full sentence reads better with a kind word (<see cref="NewsWindow"/>) than
    /// <see cref="DescribeLocation"/>'s bare coordinate.
    /// </summary>
    public static string DescribeLocationLong(ISectorObject obj, Empire viewer) => obj switch {
        Fleet => "Fleet",
        _ => $"{LongKindWord(obj)} at {RelativeCoordinate.Format(obj.Location, viewer.Capital?.Location ?? new Coordinate(0, 0))}",
    };

    private static string LongKindWord(ISectorObject obj) => obj switch {
        Planet => "planet",
        Starbase => "starbase",
        Stargate => "gate",
        ConstructionSite => "construction site",
        _ => "object",
    };

    /// <summary>
    /// DisplayBasicInfo (col 2/24) + DisplayCargoInfo (col 51-52) + DisplayMilitaryInfo (col 2-3) +
    /// DisplayBackground (col 2, row 10), CLSCOMM.PAS:500-634,804-805. Four separate redaction rules,
    /// not one applied three or four times: the Cls/Tech/Pop/Eff/Amb/Rev block needs
    /// <see cref="Game.ScoutedOrOwned"/> or it's left blank (labels only); the amb/che/met/sup/tri
    /// cargo line is gated on ownership alone, never shown for anyone else's world no matter how well
    /// scouted (DisplayCargoInfo has no Scouted branch at all); the ships/legions/defenses line falls
    /// back to <see cref="YesNo"/>'s coarse magnitude bucket when Scouted-but-not-owned, or "????"
    /// when not even that; the scenario's own flavor text (<see cref="Game.FindWorldBackgroundText"/>)
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
        string Level(int value) => owned ? value.ToString() : scouted ? YesNo(value) : "????";
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
    /// The complement line's ship types (fgt..trn) get <see cref="YesNo"/> when Scouted-but-not-owned;
    /// everything else in that line (legions, amb/che/met/sup/tri) only ever shows for the owner,
    /// exactly like <see cref="LayoutWorld"/>'s own cargo line -- DisplayFleetComplement's own
    /// <c>ResI IN [fgt..trn]</c> guard on its Scouted branch, not a restriction this port invented.
    /// </summary>
    private void LayoutFleet(Fleet fleet, Empire viewer, Game game)
    {
        var owned = ReferenceEquals(fleet.Owner, viewer);
        var scouted = Game.ScoutedOrOwned(viewer, fleet);
        var origin = viewer.Capital?.Location ?? new Coordinate(game.Galaxy.Size / 2, game.Galaxy.Size / 2);

        AddAt(1, 2, "   Position:");
        AddAt(14, 2, RelativeCoordinate.Format(fleet.Location, origin));

        AddAt(1, 3, "     Status:");
        AddAt(14, 3, DescribeFleetStatus(fleet, viewer, game));

        AddAt(1, 4, "Destination:");
        AddAt(14, 4, DescribeFleetDestination(fleet, viewer, origin));

        AddAt(1, 5, "      Range:");
        AddAt(14, 5, owned ? FleetLifecycle.EstimatedRange(fleet).ToString() : "(unknown)");

        AddAt(2, 7, "fgt  hkr  jmp  jtn  pen  str  trn  men  nnj  amb  che  met  sup  tri");
        var s = fleet.Ships;
        var c = fleet.Cargo;
        string ShipLevel(int value) => owned ? value.ToString() : scouted ? YesNo(value) : "????";
        string CargoLevel(int value) => owned ? value.ToString() : "????";
        AddAt(0, 8,
            $"{ShipLevel(s.Fighters),5}{ShipLevel(s.HunterKillers),5}{ShipLevel(s.Jumpships),5}{ShipLevel(s.Jumptransports),5}{ShipLevel(s.Penetrators),5}{ShipLevel(s.Starships),5}{ShipLevel(s.Transports),5}" +
            $"{CargoLevel(c.Legions),5}{CargoLevel(c.NinjaLegions),5}{CargoLevel(c.Ambrosia),5}{CargoLevel(c.Chemicals),5}{CargoLevel(c.Metals),5}{CargoLevel(c.Supplies),5}{CargoLevel(c.Trillum),5}");
    }

    /// <summary>MISC.PAS's YesNo (:78-96) -- a coarse magnitude bucket for a Scouted-but-not-owned count, not a real number. Width padding comes from each call site's own <c>{,5}</c> format, matching Pascal's own pre-padded 5-char literals. Internal: <see cref="StatusWindow"/> reuses this same bucket table rather than duplicating it.</summary>
    internal static string YesNo(int level) => level switch {
        0 => "no",
        >= 1 and <= 500 => "yes-",
        >= 501 and <= 1500 => "yes1",
        >= 1501 and <= 2500 => "yes2",
        >= 2501 and <= 3500 => "yes3",
        >= 3501 and <= 4500 => "yes4",
        >= 4501 and <= 5500 => "yes5",
        >= 5501 and <= 6500 => "yes6",
        >= 6501 and <= 7500 => "yes7",
        >= 7501 and <= 8500 => "yes8",
        >= 8501 and <= 9500 => "yes9",
        >= 9501 and <= 9999 => "yes+",
        _ => "----",
    };

    /// <summary>
    /// DisplayFleetInfo's own destination gate (CLSCOMM.PAS:720-732): even Scouted, a non-owned
    /// fleet's destination only shows while that fleet is <see cref="FleetStatus.Ready"/> -- while
    /// it's still in transit, where it's headed stays hidden regardless. Internal: <see cref="FleetWindow"/>
    /// reuses this rather than re-deriving the same redaction rule.
    /// </summary>
    internal static string DescribeFleetDestination(Fleet fleet, Empire viewer, Coordinate origin)
    {
        var visible = ReferenceEquals(fleet.Owner, viewer) ||
            (fleet.Status == FleetStatus.Ready && Game.Scouted(viewer, fleet));

        if (!visible) {
            return "(unknown)";
        }

        return fleet.Destination is { } dest ? RelativeCoordinate.Format(dest, origin) : "(none)";
    }

    /// <summary>
    /// DisplayFleetInfo's own Emp=Player branch (CLSCOMM.PAS:700-716): an in-transit fleet you own
    /// shows its real ETA (EstimatedDateOfArrival); a Scouted-but-not-owned one shows the literal
    /// "(?)" placeholder baked into Pascal's own <c>FltStatusName[FInTrans]</c> ('In transit (?)'),
    /// since nothing here computes another empire's ETA; anything less than Scouted is "(unknown)".
    /// Internal: <see cref="FleetWindow"/> reuses this rather than re-deriving the same redaction
    /// rule (and the same EstimatedDateOfArrival-needs-a-real-Destination guard this method already
    /// gets right by only calling it inside the InTransit branch).
    /// </summary>
    internal static string DescribeFleetStatus(Fleet fleet, Empire viewer, Game game)
    {
        var owned = ReferenceEquals(fleet.Owner, viewer);
        if (!owned && !Game.Scouted(viewer, fleet)) {
            return "(unknown)";
        }

        if (fleet.Status != FleetStatus.InTransit) {
            return FleetStatusNames[(int)fleet.Status];
        }

        return owned
            ? $"In transit ({FleetLifecycle.EstimatedDateOfArrival(fleet, game)})"
            : "In transit (?)";
    }

    private void AddAt(int x, int y, string text) => Add(new Label { X = x, Y = y, Text = text });
}
