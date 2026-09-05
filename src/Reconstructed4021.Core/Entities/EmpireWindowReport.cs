using Reconstructed4021.Core.Npe;

namespace Reconstructed4021.Core.Entities;

/// <summary>
/// EmpireWindow's row order (EMPWIND.PAS: DrawEmpireWindow) — the viewer's own row first, always
/// full detail, then every other still-active empire whose capital the viewer knows about, in
/// <see cref="Game.Empires"/>' own fixed roster order (Pascal's <c>Empire1 TO Empire8</c> loop). A
/// known-but-not-scouted capital still earns a row, just with <see cref="Row.Full"/> false —
/// <c>GetCapital</c>/<c>Known</c> gate whether the row exists at all, <c>Scouted</c> only gates detail.
/// Named apart from the unrelated turn-start-greeting <see cref="Reconstructed4021.Core.EmpireStatusReport"/>
/// (PROLOG.PAS's EmpireStatus) — same Pascal-ish name, a different report for a different screen.
/// </summary>
public static class EmpireWindowReport
{
    public readonly record struct Row(Empire Empire, bool Full);

    public static List<Row> BuildRows(Game game, Empire viewer)
    {
        var rows = new List<Row> { new(viewer, true) };

        foreach (var emp in game.Empires) {
            if (emp == viewer || emp.Status == Types.EmpireStatus.Eliminated || emp.Capital is null) {
                continue;
            }
            if (!Game.Known(viewer, emp.Capital)) {
                continue;
            }
            rows.Add(new Row(emp, Game.Scouted(viewer, emp.Capital)));
        }

        return rows;
    }

    /// <summary>GetEmpireStatus (INTRFACE.PAS:654-719) — reuses Npe.NpeToolkit's own aggregation,
    /// shared with StateDeptReport rather than duplicated here.</summary>
    public static (int Planets, int TotalPop, int ShipyardIndustry, ShipCounts TotalShips) GetEmpireStatus(Empire emp, Game game)
        => NpeToolkit.GetEmpireStatus(emp, game);
}
