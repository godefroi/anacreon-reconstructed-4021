using System.Text;
using Reconstructed4021.Panemonde;

namespace Reconstructed4021.Tui2;

// Shared look for every New Game screen: a starfield backdrop behind a centered, single-line-bordered
// box with a title -- the same "80x24 box over a black backdrop" chrome Reconstructed4021.Tui's
// NewGameWindow used (COLORS.INC's SYSDispWind: light gray on blue), composed per-screen here rather
// than through a shared base class.
internal static class Chrome
{
    private const int BoxWidth = 80;
    private const int BoxHeight = 24;
    public static readonly ConsoleColor ContentFg = ConsoleColor.Gray;
    public static readonly ConsoleColor ContentBg = ConsoleColor.DarkBlue; // DOS text-mode background attributes have no bright variant.

    public readonly record struct Box(int X, int Y, int Width, int Height);

    // Clamped to the real frame buffer size: ScreenHost sizes it off the actual console window, which
    // can be narrower or shorter than this box's own 80x24, and FrameBuffer.Set indexes its backing
    // array directly with no bounds check of its own -- an unclamped box origin here would throw
    // IndexOutOfRangeException on a small window instead of just drawing an imperfect frame.
    public static Box GetBox(FrameBuffer fb)
    {
        var width = Math.Min(BoxWidth, fb.Width);
        var height = Math.Min(BoxHeight, fb.Height);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);
        return new Box(x, y, width, height);
    }

    public static Box Draw(FrameBuffer fb, string title, Starfield starfield)
    {
        fb.Clear(new Cell(new Rune(' '), ConsoleColor.Gray, ConsoleColor.Black));
        starfield.Draw(fb);

        var box = GetBox(fb);
        DrawBorder(fb, box);

        // Content draws over whatever stars fall inside the box's own rect during the fill below,
        // same as NewGameWindow's own opaque child View did.
        for (var row = 1; row < box.Height - 1; row++)
        {
            for (var col = 1; col < box.Width - 1; col++)
            {
                fb.Set(box.X + col, box.Y + row, new Cell(new Rune(' '), ContentFg, ContentBg));
            }
        }

        if (title.Length > 0 && box.Width > 4)
        {
            var titleText = $" {title} "[..Math.Min(title.Length + 2, box.Width - 2)];
            fb.DrawText(box.X + Math.Max(1, (box.Width - titleText.Length) / 2), box.Y, titleText, ContentFg, ContentBg);
        }

        return box;
    }

    private static void DrawBorder(FrameBuffer fb, Box box)
    {
        fb.Set(box.X, box.Y, new Cell(new Rune('┌'), ContentFg, ContentBg));
        fb.Set(box.X + box.Width - 1, box.Y, new Cell(new Rune('┐'), ContentFg, ContentBg));
        fb.Set(box.X, box.Y + box.Height - 1, new Cell(new Rune('└'), ContentFg, ContentBg));
        fb.Set(box.X + box.Width - 1, box.Y + box.Height - 1, new Cell(new Rune('┘'), ContentFg, ContentBg));

        for (var i = 1; i < box.Width - 1; i++)
        {
            fb.Set(box.X + i, box.Y, new Cell(new Rune('─'), ContentFg, ContentBg));
            fb.Set(box.X + i, box.Y + box.Height - 1, new Cell(new Rune('─'), ContentFg, ContentBg));
        }

        for (var i = 1; i < box.Height - 1; i++)
        {
            fb.Set(box.X, box.Y + i, new Cell(new Rune('│'), ContentFg, ContentBg));
            fb.Set(box.X + box.Width - 1, box.Y + i, new Cell(new Rune('│'), ContentFg, ContentBg));
        }
    }
}
