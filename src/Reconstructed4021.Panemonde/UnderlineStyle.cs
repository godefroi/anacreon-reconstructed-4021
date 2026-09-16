namespace Reconstructed4021.Panemonde;

/// <summary>
/// Terminal underline styles, per the "extended underline" convention originated by Kitty
/// (https://sw.kovidgoyal.net/kitty/underlines/) and since adopted by iTerm2, WezTerm, Windows
/// Terminal, and most VTE-based Linux terminals: <c>CSI 4:&lt;n&gt; m</c> instead of the classic
/// <c>CSI 4 m</c>, where the colon introduces a sub-parameter selecting the style. Not universally
/// supported (a legacy terminal that only understands classic SGR may ignore the whole sequence, or
/// worse, stop parsing at the colon and misread the rest of the line) -- same "assumes a modern
/// terminal" tradeoff <see cref="FrameBuffer"/>'s own DarkRed-as-truecolor special case already makes.
/// </summary>
public enum UnderlineStyle
{
    None,
    Single,
    Dotted,
}
