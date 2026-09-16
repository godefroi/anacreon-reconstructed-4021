using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Presentation;
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
/// that class's own "Windows added/removed from the Toplevel" convention for overlays.
///
/// Field layout (row/column positions) for the Close Up tab (<see cref="CloseUpContentView"/>) is
/// transcribed directly from CLSCOMM.PAS's own <c>DisplayBasicInfo</c>/<c>DisplayCargoInfo</c>/
/// <c>DisplayMilitaryInfo</c> (non-fleet objects) and <c>DisplayFleetInfo</c>/<c>DisplayFleetComplement</c>
/// (fleets) -- every X/Y there is that source's own 1-based <c>WriteString(...,x,y,...)</c>
/// column/row minus 1. Field-level redaction is real too (that class's own doc comments have the
/// exact per-field rules) -- this window can only be opened on something at least Known
/// (<see cref="GameShell.ObjectsAt"/>'s own <see cref="Game.Visible"/> gate), but Known alone still
/// redacts most fields further down to "????"/"(unknown)" until the viewer's own
/// <see cref="Game.ScoutedOrOwned"/> tier is met.
///
/// One of the player's own fleets additionally gets a second, always-present "Orders" tab
/// (<see cref="FleetOrdersTabView"/>) -- FLTCOMM.PAS's own <c>FleetOrdersCommand</c> mini scripting
/// language, editable in place, superseding the old standalone Fleet menu > Orders window. Tab
/// switching/title display (<see cref="TabbedWindow"/>) is shared with <see cref="WorldInfoWindow"/>
/// -- no Pascal screen to match either way, so no reason left to keep two separate mechanisms; this
/// window's Title no longer carries the shared display window's persistent "Anacreon: Year (...)"
/// text (<see cref="DisplayWindowTitle"/> is still used elsewhere, e.g. the Attack display window --
/// just not for this window's own tab-bearing Title any more).
/// </summary>
internal sealed class CloseUpWindow : Window
{
    // DisplayFleetInfo's FltTypeName (CLSCOMM.PAS:670-675) -- ordinal-aligned with FleetType (see
    // that enum's own doc comment: "Matches Pascal's FleetTypes / TypeOfFleet"). Internal: CloseUpContentView reuses this.
    internal static readonly string[] FleetTypeNames =
        ["Warpfleet", "Jumpfleet", "Hunter-Killer Fleet", "Stealth Fleet", "Fast-Warp Fleet"];

    // COLORS.INC's ColorScrColor: SYSWBorder = 7 (LightGray on Black) -- the border/title color; the
    // content area's own SYSDispWind background now lives on CloseUpContentView/FleetOrdersTabView.
    private static readonly TgAttribute BorderAttribute = new(StandardColor.LightGray, StandardColor.Black);

    private readonly TabbedWindow tabs;

    /// <summary>
    /// Fires exactly once, when the Orders tab is ready for this whole window to close (mirrors the
    /// old <c>FleetOrdersWindow.Closed</c> event this superseded, via <see cref="FleetOrdersTabView.Committed"/>).
    /// A non-null message is the real completion/cancellation report for the caller to show via
    /// <c>ShowInfo</c>; null means the compile was aborted and nothing changed. Never raised for
    /// anything but a successful/discarded Orders-tab commit -- Esc/any-key on the Close Up tab is
    /// still handled entirely by <see cref="GameShell.ShowCloseUp"/>'s own external <c>KeyDown</c>
    /// wiring, unchanged.
    /// </summary>
    public event EventHandler<string?>? OrdersCommitted;

    public CloseUpWindow(ISectorObject obj, Empire viewer, Game game, string initialTab = "Close Up")
    {
        Width = 80;
        Height = 21;
        X = Pos.Center();
        Y = Pos.Center();
        BorderStyle = LineStyle.Single; // ThinBRD
        CanFocus = true;
        Border.View?.SetScheme(new Scheme(BorderAttribute));

        var name = obj.Names.GetValueOrDefault(viewer) ?? DescribeLocation(obj, viewer);
        tabs = new TabbedWindow(this, name);
        tabs.AddTab("Close Up", new CloseUpContentView(obj, viewer, game));

        if (obj is Fleet ownFleet && ReferenceEquals(ownFleet.Owner, viewer)) {
            var ordersTab = new FleetOrdersTabView(ownFleet, game, viewer);
            ordersTab.Committed += (_, message) => OrdersCommitted?.Invoke(this, message);
            tabs.AddTab("Orders", ordersTab);
        }

        tabs.Show(initialTab);
        Initialized += (_, _) => tabs.FocusCurrentTab();
    }

    public bool ShowingOrders => tabs.IsShowing("Orders");

    /// <summary>Updates the title after a rename (F2, GameShell's own RenameObject) -- the name shown here was only ever a snapshot taken at construction (see the constructor's own <c>name</c> local).</summary>
    public void RefreshTitle(string newName) => tabs.Rename(newName);

    /// <summary>
    /// DrawScreen's own Line (DISPLAY.PAS:156-162): "Anacreon: {Year} ({Age}{ordinal suffix} year
    /// of your reign.)", Age = EmpireAge(Player)+1 = Game.Year - FoundingYear + 1
    /// (PRIMINTR.PAS:977-980). The ordinal suffix formula is OrdinalString (STRG.PAS:48-65) --
    /// standard English ordinals, with the 11th-13th "always th" exception baked into its own
    /// Penultim=1 check. No longer this window's own Title (see this class's own doc comment) --
    /// still used by other real consumers of the same shared-window text, e.g. the Attack display.
    /// </summary>
    internal static string DisplayWindowTitle(Game game, Empire viewer)
    {
        var age = game.Year - viewer.FoundingYear + 1;
        return $"Anacreon: {game.Year} ({age}{DisplayText.OrdinalSuffix(age)} year of your reign.)";
    }

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

}
