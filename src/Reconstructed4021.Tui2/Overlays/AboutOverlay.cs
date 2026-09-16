using Reconstructed4021.Core.Entities;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;

namespace Reconstructed4021.Tui2.Overlays;

// Home menu > About Anacreon (TMA.PAS: AboutAnacreon, :82-143). Content is AboutPages (loaded from
// the embedded about.kdl resource -- see that class's own doc comment, and about.kdl's own header
// comment, for provenance: pages 1-2 are the real 1.31 release's own text verbatim, page 3 covers
// this port and has no Pascal equivalent).
internal sealed class AboutOverlay : IOverlay
{
    private const int Width = 80;
    private const int Height = 23;

    private static IReadOnlyList<IReadOnlyList<string>> Pages => AboutPages.Pages;

    private int _pageIndex;

    public bool IsDismissed { get; private set; }

    public void HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.PageUp or ConsoleKey.LeftArrow:
                _pageIndex = Math.Max(0, _pageIndex - 1);
                break;
            case ConsoleKey.PageDown or ConsoleKey.RightArrow:
                _pageIndex = Math.Min(Pages.Count - 1, _pageIndex + 1);
                break;
            case ConsoleKey.Escape:
                IsDismissed = true;
                break;
        }
    }

    public void Draw(FrameBuffer fb)
    {
        var width = Math.Min(Width, fb.Width);
        var height = Math.Min(Height, fb.Height);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        var titleText = " About Anacreon ";
        fb.DrawText(x + Math.Max(1, (width - titleText.Length) / 2), y, titleText, ConsoleColor.White, ConsoleColor.Black);

        var lines = Pages[_pageIndex];
        for (var i = 0; i < lines.Count && i < height - 3; i++)
        {
            fb.DrawText(x + 1, y + 1 + i, lines[i], ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
        }

        var hint = $"Page {_pageIndex + 1} of {Pages.Count}   PgUp/PgDn: page   Esc: close";
        fb.DrawText(x + 1, y + height - 2, hint, ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
    }
}
