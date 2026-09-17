using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Presentation;
using Reconstructed4021.Core.Types;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;


namespace Reconstructed4021.Tui.Overlays;


// Empire menu > Tech Tree, ported from Reconstructed4021.Tui's own TechTreeWindow. Read-only,
// scroll-only list -- TechTreeReport.BuildRows's rows grouped by TechLevel, each level's own header
// line marked when it's the viewer's current level, each item checked off if Row.Owned.
internal sealed class TechTreeOverlay : IOverlay
{
    private const int Width = 40;
    private const int Height = 22;

    private readonly ListBox<string> _list;

    public bool IsDismissed { get; private set; }

    public TechTreeOverlay(Empire viewer)
    {
        _list = new ListBox<string>(BuildLines(viewer), s => s);
    }

    private static List<string> BuildLines(Empire viewer)
    {
        var lines = new List<string>();
        foreach (var group in TechTreeReport.BuildRows(viewer).GroupBy(r => r.Level))
        {
            var current = group.Key == viewer.TechnologyLevel ? "  (current)" : "";
            lines.Add($"{CloseUpWindowText.TechLevelNames[group.Key]}{current}");
            foreach (var row in group)
            {
                var mark = row.Owned ? 'x' : ' ';
                lines.Add($"  [{mark}] {TechCatalog.DisplayName(row.Identity)}");
            }
        }

        return lines;
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_list.HandleKey(key))
        {
            return;
        }

        if (key.Key == ConsoleKey.Escape)
        {
            IsDismissed = true;
        }
    }

    public void Draw(FrameBuffer fb)
    {
        var width = Math.Min(Width, fb.Width);
        var height = Math.Min(Height, fb.Height);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        fb.DrawText(x + Math.Max(1, (width - 16) / 2), y, " Technology Tree ", ConsoleColor.White, ConsoleColor.Black);
        _list.Draw(fb, x + 1, y + 1, width - 2, height - 2, ConsoleColor.Gray, ConsoleColor.Black, ConsoleColor.Black, ConsoleColor.Gray);
    }
}
