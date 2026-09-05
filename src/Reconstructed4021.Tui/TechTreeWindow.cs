using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core.Entities;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// Empire menu's Tech Tree screen -- no Pascal equivalent (see <see cref="TechTreeReport"/>'s own doc
/// comment for why: tech advancement there is a per-turn random roll, not a player-directed research
/// queue). Read-only, scroll-only list -- <see cref="TechTreeReport.BuildRows"/>'s rows grouped by
/// <c>TechLevel</c>, each level's own header line marked when it's the viewer's current level, each
/// item checked off if <see cref="TechTreeReport.Row.Owned"/>.
/// </summary>
internal sealed class TechTreeWindow : Window
{
    private const int NoOfLines = 20;

    private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue);
    private static readonly TgAttribute BorderAttribute = new(StandardColor.LightGray, StandardColor.Black);
    private static readonly TgAttribute HeaderAttribute = new(StandardColor.White, StandardColor.Blue);

    // DATACNST.PAS's TechN (:152-163) -- full tech-level names, ordinal-aligned with TechLevel.
    private static readonly string[] TechLevelNames = [
        "pre-tech", "primitive", "pre-atomic", "atomic", "pre-warp", "warp", "jump", "bio-tech",
        "starship", "pre-gate", "gate",
    ];

    private readonly record struct Line(bool IsHeader, string Text);

    private readonly IReadOnlyList<Line> _lines;
    private readonly Label[] _lineLabels = new Label[NoOfLines];
    private int _beginIndex;

    public TechTreeWindow(Empire viewer)
    {
        _lines = BuildLines(viewer);

        Title = "Technology Tree";
        Width = 40;
        Height = NoOfLines + 2;
        X = Pos.Center();
        Y = Pos.Center();
        BorderStyle = LineStyle.Single;
        CanFocus = true;
        SetScheme(new Scheme(DispWindAttribute));
        Border.View?.SetScheme(new Scheme(BorderAttribute));

        for (var i = 0; i < NoOfLines; i++) {
            _lineLabels[i] = new Label { X = 0, Y = i, Text = string.Empty };
            Add(_lineLabels[i]);
        }

        Redraw();

        KeyDown += (_, key) => {
            switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
                case KeyCode.CursorUp: Scroll(-1); key.Handled = true; break;
                case KeyCode.CursorDown: Scroll(1); key.Handled = true; break;
                case KeyCode.PageUp: Scroll(-NoOfLines); key.Handled = true; break;
                case KeyCode.PageDown: Scroll(NoOfLines); key.Handled = true; break;
                case KeyCode.Home: Scroll(int.MinValue); key.Handled = true; break;
                case KeyCode.End: Scroll(int.MaxValue); key.Handled = true; break;
            }
        };
    }

    private static List<Line> BuildLines(Empire viewer)
    {
        var lines = new List<Line>();
        var rows = TechTreeReport.BuildRows(viewer);

        foreach (var group in rows.GroupBy(r => r.Level)) {
            var current = group.Key == viewer.TechnologyLevel ? "  (current)" : "";
            lines.Add(new Line(true, $"{TechLevelNames[(int)group.Key]}{current}"));
            foreach (var row in group) {
                var mark = row.Owned ? 'x' : ' ';
                lines.Add(new Line(false, $"  [{mark}] {TechCatalog.DisplayName(row.Identity)}"));
            }
        }

        return lines;
    }

    private void Scroll(int delta)
    {
        var maxBegin = Math.Max(0, _lines.Count - NoOfLines);
        _beginIndex = delta switch {
            int.MinValue => 0,
            int.MaxValue => maxBegin,
            _ => Math.Clamp(_beginIndex + delta, 0, maxBegin),
        };
        Redraw();
    }

    private void Redraw()
    {
        for (var i = 0; i < NoOfLines; i++) {
            var index = _beginIndex + i;
            if (index >= _lines.Count) {
                _lineLabels[i].Text = string.Empty;
                continue;
            }

            var line = _lines[index];
            _lineLabels[i].SetScheme(new Scheme(line.IsHeader ? HeaderAttribute : DispWindAttribute));
            _lineLabels[i].Text = line.Text;
        }
    }
}
