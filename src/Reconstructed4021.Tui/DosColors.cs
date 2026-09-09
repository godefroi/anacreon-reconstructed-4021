using Terminal.Gui.Drawing;

namespace Reconstructed4021.Tui;

/// <summary>
/// Color corrections confirmed against a real DOSBox run of the original, not Terminal.Gui's own
/// named-color guesses. <see cref="StandardColor.Red"/> renders noticeably brighter/more orange
/// than the real CGA red DOSBox shows -- measured directly off a screenshot at r=154 g=0 b=0, used
/// everywhere this port's own COLORS.INC-derived palette calls for Red (SYSMenuBar/SYSHelpLine/the
/// title screen's own red text).
/// </summary>
internal static class DosColors
{
    public static readonly Color Red = new(154, 0, 0);

    /// <summary>
    /// Per-empire galaxy-view colors (issue #3): the original never gave individual empires distinct
    /// colors (GalaxyView's own doc comment), so this is a new addition, not a port. One slot per
    /// non-player empire slot below <see cref="Reconstructed4021.Core.Entities.Empire"/>'s hard cap of
    /// 8 total (TYPES.PAS:56's <c>MaxNoOfEmpires: Empire = Empire8</c>) -- 7 colors, no cycling needed.
    /// Picked from the CGA 16 (<see cref="StandardColor"/> already mirrors that exact palette) avoiding
    /// every color already meaningful in <see cref="Reconstructed4021.Tui.GalaxyView"/>: White (player),
    /// LightGray (other-empire fallback/grid), DarkGray (independent), Magenta (nebula), Red/BrightRed
    /// and BrightMagenta (too close to nebula/unscouted to tell apart at a glance) are all excluded.
    /// </summary>
    public static readonly IReadOnlyList<StandardColor> EmpirePalette = [
        StandardColor.Blue,
        StandardColor.Green,
        StandardColor.Cyan,
        StandardColor.Brown,
        StandardColor.BrightBlue,
        StandardColor.BrightCyan,
        StandardColor.BrightYellow,
    ];
}
