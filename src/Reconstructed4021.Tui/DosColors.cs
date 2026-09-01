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
}
