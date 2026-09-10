using Reconstructed4021.Core.Types;
using static Reconstructed4021.Core.PascalMath;

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

    private static readonly IndustryType[] _shipyardIndustryTypes = [IndustryType.ShipyardGeneral, IndustryType.ShipyardJump, IndustryType.ShipyardStarship, IndustryType.ShipyardTransport];
    private const double ShipyardIndustryK6 = 11000.0; // K6 (DATACNST.PAS) — same constant AnnualTickHandler's own IP formula uses, a single scalar not worth extracting alongside IndustryConstants' table.

    /// <summary>
    /// The SInd half of GetEmpireStatus (INTRFACE.PAS:654-719) — a world's shipyard industry
    /// contribution, <c>IP*Sqr(Indus[IndI]+K4)</c> summed over the four shipyard industry types. K4 is
    /// always 0 (DATACNST.PAS), so it's dropped rather than carried as a dead term.
    /// </summary>
    private static double ShipyardIndustryOf(IEconomicWorld world)
    {
        var ip = (IndustryConstants.IndustrialProductionTechAdjustment[world.TechLevel] / 100.0) * ((world.Efficiency + 250) / 100.0) / ShipyardIndustryK6;
        var total = 0.0;
        foreach (var t in _shipyardIndustryTypes) {
            var level = world.Industry[t];
            total += ip * level * level;
        }
        return total;
    }

    /// <summary>
    /// GetEmpireStatus (INTRFACE.PAS:654-719) — Planets, TotalPop, SInd, and TotalShips for one
    /// empire. Public, not private: this port's own NPE AI (a real second consumer, see
    /// docs/PORT_DESIGN.md's "derive, don't duplicate" precedent) reuses this same aggregation for
    /// StateDeptReport rather than a duplicate copy. Starbases only add to SInd when
    /// <see cref="StarbaseKind.IndustrialComplex"/> (Pascal's <c>STyp=cmp</c> guard), matching the real
    /// per-kind gate; every starbase kind still counts toward Planets/TotalPop/TotalShips regardless.
    /// </summary>
    public static (int Planets, int TotalPop, int ShipyardIndustry, ShipCounts TotalShips) GetEmpireStatus(Empire emp, Game game)
    {
        var worlds = 0;
        var totalPop = 0;
        var shipyardIndustry = 0.0;
        var totalShips = new ShipCounts();

        void AddShips(ShipCounts ships)
        {
            foreach (var t in Enum.GetValues<ShipType>()) {
                totalShips[t] += ships[t];
            }
        }

        foreach (var planet in game.Galaxy.Planets) {
            if (planet.Owner != emp) {
                continue;
            }
            worlds++;
            totalPop += planet.Population;
            shipyardIndustry += ShipyardIndustryOf(planet);
            AddShips(planet.Ships);
        }

        foreach (var starbase in game.Galaxy.Starbases) {
            if (starbase.Owner != emp) {
                continue;
            }
            worlds++;
            totalPop += starbase.Population;
            AddShips(starbase.Ships);
            if (starbase.Kind == StarbaseKind.IndustrialComplex) {
                shipyardIndustry += ShipyardIndustryOf(starbase);
            }
        }

        foreach (var fleet in game.Galaxy.Fleets) {
            if (fleet.Owner == emp) {
                AddShips(fleet.Ships);
            }
        }

        return (worlds, totalPop, PascalRound(shipyardIndustry), totalShips);
    }
}
