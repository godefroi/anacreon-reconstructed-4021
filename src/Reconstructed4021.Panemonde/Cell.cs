using System.Text;

namespace Reconstructed4021.Panemonde;

// Overline is plain classic SGR (ECMA-48 codes 53 on/55 off, not a terminal-specific extension like
// UnderlineStyle's own colon sub-parameters), but support is still inconsistent across terminals --
// same "assumes a modern terminal" tradeoff as Underline and FrameBuffer's own DarkRed special case.
public readonly record struct Cell(Rune Glyph, ConsoleColor Fg, ConsoleColor Bg, UnderlineStyle Underline = UnderlineStyle.None, bool Overline = false)
{
    public static readonly Cell Blank = new(new Rune(' '), ConsoleColor.Gray, ConsoleColor.Black);
}
