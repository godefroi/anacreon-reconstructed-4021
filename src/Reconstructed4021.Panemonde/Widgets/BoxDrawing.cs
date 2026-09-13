using System.Text;

namespace Reconstructed4021.Panemonde.Widgets;

// A bordered box, interior filled solid: the one popup/frame style this project actually uses --
// Chrome's outer New Game box, MenuBar's dropdown, and every other bordered popup (e.g. the gender
// pick) all draw the same shape, just with their own colors and border weight. Single-line matches
// ThinBRD, the border style every bordered popup in the Pascal source actually uses (e.g.
// PlayerSetupWindow's own OpenWindow(20,12,50,7,ThinBRD,...) for its gender prompt); double-line has
// no Pascal precedent (AnacreonTitleWindow's own menu buttons, a TUI-only widget to begin with) but is
// kept as a second option since one already exists.
public static class BoxDrawing
{
    public static void DrawSingleLine(FrameBuffer fb, int x, int y, int width, int height, ConsoleColor fg, ConsoleColor bg) =>
        Draw(fb, x, y, width, height, fg, bg, '┌', '┐', '└', '┘', '─', '│');

    public static void DrawDoubleLine(FrameBuffer fb, int x, int y, int width, int height, ConsoleColor fg, ConsoleColor bg) =>
        Draw(fb, x, y, width, height, fg, bg, '╔', '╗', '╚', '╝', '═', '║');

    private static void Draw(FrameBuffer fb, int x, int y, int width, int height, ConsoleColor fg, ConsoleColor bg,
        char topLeft, char topRight, char bottomLeft, char bottomRight, char horizontal, char vertical)
    {
        for (var row = 0; row < height; row++)
        {
            for (var col = 0; col < width; col++)
            {
                fb.Set(x + col, y + row, new Cell(new Rune(' '), fg, bg));
            }
        }

        fb.Set(x, y, new Cell(new Rune(topLeft), fg, bg));
        fb.Set(x + width - 1, y, new Cell(new Rune(topRight), fg, bg));
        fb.Set(x, y + height - 1, new Cell(new Rune(bottomLeft), fg, bg));
        fb.Set(x + width - 1, y + height - 1, new Cell(new Rune(bottomRight), fg, bg));

        for (var i = 1; i < width - 1; i++)
        {
            fb.Set(x + i, y, new Cell(new Rune(horizontal), fg, bg));
            fb.Set(x + i, y + height - 1, new Cell(new Rune(horizontal), fg, bg));
        }

        for (var i = 1; i < height - 1; i++)
        {
            fb.Set(x, y + i, new Cell(new Rune(vertical), fg, bg));
            fb.Set(x + width - 1, y + i, new Cell(new Rune(vertical), fg, bg));
        }
    }
}
