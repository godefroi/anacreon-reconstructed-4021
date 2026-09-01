using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
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
/// own 1-based <c>WriteString(...,x,y,...)</c> column/row minus 1. Real Pascal redacts most fields
/// behind <c>Known</c>/<c>Scouted</c> checks for anything not the player's own -- not reproduced
/// here, matching this branch's other already-accepted simplification that <see cref="GalaxyView"/>
/// itself draws the whole map with no fog-of-war (docs/OPEN_GAPS.md); values are always shown in
/// full regardless of who's looking. The one owner-gated field kept (not a fog-of-war matter, a
/// genuine "only your own fleet's instruments tell you this" mechanic even in real Pascal) is a
/// fleet's own in-transit ETA, versus a bare "(?)" for anyone else's.
/// </summary>
internal sealed class CloseUpWindow : Window
{
    // DisplayFleetInfo's FltTypeName (CLSCOMM.PAS:670-675) -- ordinal-aligned with FleetType (see
    // that enum's own doc comment: "Matches Pascal's FleetTypes / TypeOfFleet").
    private static readonly string[] FleetTypeNames =
        ["Warpfleet", "Jumpfleet", "Hunter-Killer Fleet", "Stealth Fleet", "Fast-Warp Fleet"];

    // DisplayFleetInfo's FltStatusName (CLSCOMM.PAS:664-668) -- ordinal-aligned with FleetStatus.
    // InTransit's real text is built specially below (EDA for your own fleet, "(?)" otherwise), so
    // this entry is never read as-is.
    private static readonly string[] FleetStatusNames = ["at destination", "In transit", "out of trillum", "lost"];

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

        var name = obj.Names.GetValueOrDefault(viewer) ?? DescribeKind(obj);
        var headerKind = obj is Fleet headerFleet ? FleetTypeNames[(int)headerFleet.Type] : DescribeKind(obj);

        // Row 1 (DisplayBasicInfo/DisplayFleetInfo): "Close Up: Name" at col 1, type name at col 36,
        // owner at col 60 -- one visual header line built from three separately-positioned Labels
        // rather than reproducing AdjustString's manual space-padding.
        AddAt(0, 0, $"Close Up: {name}");
        AddAt(35, 0, headerKind);
        AddAt(59, 0, obj.Owner.Name);

        if (obj is Fleet fleet) {
            LayoutFleet(fleet, viewer, game);
        } else if (obj is IEconomicWorld world) {
            LayoutWorld(world);
        }
    }

    /// <summary>
    /// DrawScreen's own Line (DISPLAY.PAS:156-162): "Anacreon: {Year} ({Age}{ordinal suffix} year
    /// of your reign.)", Age = EmpireAge(Player)+1 = Game.Year - FoundingYear + 1
    /// (PRIMINTR.PAS:977-980). The ordinal suffix formula is OrdinalString (STRG.PAS:48-65) --
    /// standard English ordinals, with the 11th-13th "always th" exception baked into its own
    /// Penultim=1 check.
    /// </summary>
    private static string DisplayWindowTitle(Game game, Empire viewer)
    {
        var age = game.Year - viewer.FoundingYear + 1;
        return $"Anacreon: {game.Year} ({age}{OrdinalSuffix(age)} year of your reign.)";
    }

    private static string OrdinalSuffix(int num)
    {
        var ultim = num % 10;
        var penultim = num % 100 / 10;

        if (penultim == 1 || ultim > 3 || ultim == 0) {
            return "th";
        }

        return ultim switch { 3 => "rd", 2 => "nd", 1 => "st", _ => "th" };
    }

    /// <summary>Fallback label for an object with no player-given name -- shared with <see cref="GameShell"/>'s sector picker overlay. For a Fleet this is a generic placeholder, not its real type -- see the header's own FleetTypeNames lookup for that.</summary>
    public static string DescribeKind(ISectorObject obj) => obj switch {
        Planet p => p.Type.ToString(),
        Starbase s => s.Kind.ToString(),
        Fleet => "Fleet",
        Stargate g => g.Kind.ToString(),
        ConstructionSite c => $"{c.Building} site",
        _ => "Unknown",
    };

    /// <summary>DisplayBasicInfo (col 2/24) + DisplayCargoInfo (col 51-52) + DisplayMilitaryInfo (col 2-3), CLSCOMM.PAS:500-634.</summary>
    private void LayoutWorld(IEconomicWorld world)
    {
        AddAt(1, 2, " Cls:"); AddAt(7, 2, world.EffectiveClass.ToString());
        AddAt(1, 3, "Tech:"); AddAt(7, 3, world.TechLevel.ToString());
        AddAt(1, 4, " Pop:"); AddAt(7, 4, world.Population.ToString());

        AddAt(23, 2, "Eff:"); AddAt(28, 2, $"{world.Efficiency}%");
        AddAt(23, 3, "Amb:"); AddAt(28, 3, world.IsAddictedToAmbrosia ? "yes" : "no");
        AddAt(23, 4, "Rev:"); AddAt(28, 4, world.RevolutionIndex.ToString());

        AddAt(51, 2, "amb  che  met  sup  tri");
        AddAt(49, 3, $"{world.Cargo.Ambrosia,5}{world.Cargo.Chemicals,5}{world.Cargo.Metals,5}{world.Cargo.Supplies,5}{world.Cargo.Trillum,5}");

        AddAt(2, 6, "fgt  hkr  jmp  jtn  pen  str  trn    men  nnj    LAM  def  GDM  ion");
        var s = world.Ships;
        var c = world.Cargo;
        var d = world.Defenses;
        AddAt(0, 7,
            $"{s.Fighters,5}{s.HunterKillers,5}{s.Jumpships,5}{s.Jumptransports,5}{s.Penetrators,5}{s.Starships,5}{s.Transports,5}" +
            $"  {c.Legions,5}{c.NinjaLegions,5}" +
            $"  {d.Lams,5}{d.DefenseSatellites,5}{d.Gdms,5}{d.IonCannons,5}");
    }

    /// <summary>DisplayFleetInfo (col 1-2/14-15) + DisplayFleetComplement (col 2-3), CLSCOMM.PAS:654-768.</summary>
    private void LayoutFleet(Fleet fleet, Empire viewer, Game game)
    {
        AddAt(1, 2, "   Position:");
        AddAt(14, 2, $"{fleet.Location.X},{fleet.Location.Y}");

        AddAt(1, 3, "     Status:");
        AddAt(14, 3, DescribeFleetStatus(fleet, viewer, game));

        AddAt(1, 4, "Destination:");
        AddAt(14, 4, fleet.Destination is { } dest ? $"{dest.X},{dest.Y}" : "(none)");

        AddAt(1, 5, "      Range:");
        AddAt(14, 5, FleetLifecycle.EstimatedRange(fleet).ToString());

        AddAt(2, 7, "fgt  hkr  jmp  jtn  pen  str  trn  men  nnj  amb  che  met  sup  tri");
        var s = fleet.Ships;
        var c = fleet.Cargo;
        AddAt(0, 8,
            $"{s.Fighters,5}{s.HunterKillers,5}{s.Jumpships,5}{s.Jumptransports,5}{s.Penetrators,5}{s.Starships,5}{s.Transports,5}" +
            $"{c.Legions,5}{c.NinjaLegions,5}{c.Ambrosia,5}{c.Chemicals,5}{c.Metals,5}{c.Supplies,5}{c.Trillum,5}");
    }

    /// <summary>
    /// DisplayFleetInfo's own Emp=Player branch (CLSCOMM.PAS:700-716): an in-transit fleet you own
    /// shows its real ETA (EstimatedDateOfArrival); anyone else's (even scouted) shows "(?)" -- a
    /// genuine "only your own fleet's instruments know this" mechanic, not the fog-of-war redaction
    /// this window otherwise skips.
    /// </summary>
    private static string DescribeFleetStatus(Fleet fleet, Empire viewer, Game game)
    {
        if (fleet.Status != FleetStatus.InTransit) {
            return FleetStatusNames[(int)fleet.Status];
        }

        return ReferenceEquals(fleet.Owner, viewer)
            ? $"In transit ({FleetLifecycle.EstimatedDateOfArrival(fleet, game)})"
            : "In transit (?)";
    }

    private void AddAt(int x, int y, string text) => Add(new Label { X = x, Y = y, Text = text });
}
