using System.Text;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;


namespace Reconstructed4021.Tui2.Shared;


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
        BoxDrawing.DrawSingleLine(fb, box.X, box.Y, box.Width, box.Height, ContentFg, ContentBg);

        if (title.Length > 0 && box.Width > 4)
        {
            var titleText = $" {title} "[..Math.Min(title.Length + 2, box.Width - 2)];
            fb.DrawText(box.X + Math.Max(1, (box.Width - titleText.Length) / 2), box.Y, titleText, ContentFg, ContentBg);
        }

        return box;
    }
}
