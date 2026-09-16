using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;


namespace Reconstructed4021.Tui2.Overlays;


// F8 (EMPWIND.PAS: EmpireWindow). Single-pane, at most 8 rows (one viewer plus up to 7 other
// active empires) -- never scrolls, matching real Pascal's own fixed 8-slot roster.
internal sealed class EmpireOverlay : IOverlay
{
    private const int Width = 82;

    // DATACNST.PAS's TechStr, ordinal-aligned with TechLevel -- same table Tui's EmpireWindow/
    // StatusWindow each keep their own copy of; not shared, matching that existing precedent.
    private static readonly string[] TechCodes = ["pt", " p", "pa", " a", "pw", " w", " j", " b", " s", "pg", " g"];
    private static readonly ShipType[] ShipColumns = [ShipType.Fighter, ShipType.HunterKiller, ShipType.Jumpship, ShipType.Jumptransport, ShipType.Penetrator, ShipType.Starship, ShipType.Transport];
    private const string Header = "Empire       Tl Pln SInd   Pop    fgt    hkr    jmp    jtn    pen    str    trn ";

    private readonly Game _game;
    private readonly Action<ISectorObject> _onSelectCapital;
    private readonly ListBox<EmpireWindowReport.Row> _list;

    public bool IsDismissed { get; private set; }

    public EmpireOverlay(Game game, Empire viewer, Action<ISectorObject> onSelectCapital)
    {
        _game = game;
        _onSelectCapital = onSelectCapital;
        _list = new ListBox<EmpireWindowReport.Row>(EmpireWindowReport.BuildRows(game, viewer), FormatRow);
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_list.HandleKey(key))
        {
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.Enter when _list.SelectedItem is { } row && row.Empire.Capital is { } capital:
                // Stacks Close Up on top instead of dismissing -- see StatusOverlay's own comment.
                _onSelectCapital(capital);
                break;
            case ConsoleKey.Escape or ConsoleKey.F8:
                IsDismissed = true;
                break;
        }
    }

    private string FormatRow(EmpireWindowReport.Row row)
    {
        var emp = row.Empire;
        var name = emp.Name.PadRight(12)[..12];
        var capital = emp.Capital;
        var techCode = capital is not null ? TechCodes[(int)capital.TechLevel] : TechCodes[(int)TechLevel.PreTech];
        var (planets, totalPop, shipyardIndustry, totalShips) = EmpireWindowReport.GetEmpireStatus(emp, _game);

        var tail = row.Full
            ? string.Concat(ShipColumns.Select(t => $"{totalShips[t],6} "))
            : " ----   ----   ----   ----   ----   ----   ----  ";

        return $"{name} {techCode} {planets,3} {shipyardIndustry / 10.0,4:F1} {totalPop / 100.0,5:F1} {tail}";
    }

    public void Draw(FrameBuffer fb)
    {
        var width = Math.Min(Width, fb.Width);
        var height = Math.Min(_list.Items.Count + 3, fb.Height - 2);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        fb.DrawText(x + Math.Max(1, (width - 8) / 2), y, " Empire ", ConsoleColor.White, ConsoleColor.Black);
        // Dotted underline, no overline -- single panel below, same treatment as FleetOverlay's own
        // position header.
        fb.DrawText(x + 1, y + 1, Header.PadRight(width - 2), ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2,
            underline: UnderlineStyle.Dotted);
        _list.Draw(fb, x + 1, y + 2, width - 2, height - 3, ConsoleColor.Gray, ConsoleColor.Black, ConsoleColor.Black, ConsoleColor.Gray);
    }
}
