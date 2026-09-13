using System.Text;

namespace Reconstructed4021.Panemonde;

internal readonly record struct Cell(Rune Glyph, ConsoleColor Fg, ConsoleColor Bg)
{
    public static readonly Cell Blank = new(new Rune(' '), ConsoleColor.Gray, ConsoleColor.Black);
}
