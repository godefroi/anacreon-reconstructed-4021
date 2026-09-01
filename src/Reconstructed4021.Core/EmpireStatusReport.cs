using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core;

/// <summary>
/// PROLOG.PAS's EmpireStatus (:490-615): aggregate totals for one empire's post-greeting status
/// report -- shown after the turn-start greeting, skipped only on a player's very last turn. Pure
/// computation, no UI; the Tui project's own EmpireStatusWindow formats this into the real
/// narrative text.
/// </summary>
public sealed record EmpireStatusReport(
    int TotalWorlds,
    int TotalPopulation,
    int AverageEfficiency,
    int AverageIndustry,
    TechLevel TechLevel,
    IReadOnlyList<TechCatalog.TechGrantIdentity> ExtraTechnologies,
    ShipCounts TotalShips)
{
    public static EmpireStatusReport For(Empire empire, Game game)
    {
        var worlds = new List<IEconomicWorld>();
        worlds.AddRange(game.Galaxy.Planets.Where(p => ReferenceEquals(p.Owner, empire)));
        worlds.AddRange(game.Galaxy.Starbases.Where(s => ReferenceEquals(s.Owner, empire)));

        var totalPopulation = 0;
        var totalEfficiency = 0;
        var totalIndustry = 0;
        var totalShips = new ShipCounts();

        foreach (var world in worlds) {
            totalPopulation += world.Population;
            totalEfficiency += world.Efficiency;
            totalIndustry += AnnualTickHandler.TotalProd(world.Population, world.TechLevel);
            foreach (var t in Enum.GetValues<ShipType>()) {
                totalShips[t] += world.Ships[t];
            }
        }

        // SetOfActiveFleets (PROLOG.PAS:543): every fleet this port models is already "active" by
        // construction -- a destroyed one is removed from Galaxy.Fleets outright, never left around
        // in an inactive state.
        foreach (var fleet in game.Galaxy.Fleets.Where(f => ReferenceEquals(f.Owner, empire))) {
            foreach (var t in Enum.GetValues<ShipType>()) {
                totalShips[t] += fleet.Ships[t];
            }
        }

        var averageEfficiency = worlds.Count > 0 ? PascalMath.PascalRound((double)totalEfficiency / worlds.Count) : 0;
        var averageIndustry = worlds.Count > 0 ? PascalMath.PascalRound((double)totalIndustry / worlds.Count) : 0;

        var tech = empire.TechnologyLevel;
        var baseLevel = tech > TechLevel.PreTech ? (TechLevel)((int)tech - 1) : tech;
        var extraTechnologies = TechCatalog.UnlockedBeyond(empire.Technology, baseLevel);

        return new EmpireStatusReport(worlds.Count, totalPopulation, averageEfficiency, averageIndustry, tech, extraTechnologies, totalShips);
    }
}
