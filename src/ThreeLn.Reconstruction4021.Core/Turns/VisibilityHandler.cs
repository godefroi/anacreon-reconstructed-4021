using System.Collections.Frozen;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;

namespace ThreeLn.Reconstruction4021.Core.Turns;

/// <summary>
/// Refreshes an empire's fog-of-war each turn, matching INTRFACE.PAS:Scout/ScoutFleets/ScoutObjects/DetermineIfScouted.
/// - Fleet visibility is ephemeral: cleared and rebuilt each turn (ScoutFleets).
/// - Planet/starbase/stargate/construction visibility is accumulated: Known persists, Scouted is updated.
/// - Scouting radius: adjacent cells (Chebyshev), plus capital/starbase/planet scans at distance < 6 or <= 5.
/// </summary>
public sealed class VisibilityHandler : IVisibilityHandler
{
    // Starbase and capital scan radii: distance < 6 means within 5 cells (Chebyshev metric, INTRFACE.PAS:1387,1439).
    private const int StarbaseScanRadius = 6;
    private const int CapitalScanRadius = 6;
    // Planet detection radius for most fleet types: distance <= 5 (INTRFACE.PAS:1411).
    private const int PlanetDetectRadius = 5;

    private static readonly FrozenSet<(int dx, int dy)> _adjacentOffsets = new[]
    {
        (0, 0), (-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1),
    }.ToFrozenSet();

    public void RefreshVisibility(Empire empire, Game game)
    {
        // Fleet visibility is ephemeral — clear completely each turn (INTRFACE.PAS:ScoutFleets rewrites all).
        empire.Fleets.Clear();

        // Planet/starbase/stargate/construction visibility: clear only Scouted, keep Known (INTRFACE.PAS:ClearScoutSet).
        empire.Planets.ClearScouted();
        empire.Starbases.ClearScouted();
        empire.Stargates.ClearScouted();
        empire.ConstructionSites.ClearScouted();

        // Rebuild fleet visibility (INTRFACE.PAS:ScoutFleets).
        ScoutFleets(empire, game);

        // Rebuild entity visibility (INTRFACE.PAS:ScoutObjects + DetermineIfScouted).
        ScoutObjects(empire, game);
    }

    private static void ScoutFleets(Empire empire, Game game)
    {
        // Own fleets are always scouted.
        foreach (var fleet in game.Galaxy.Fleets)
            if (fleet.Owner == empire)
                empire.Fleets.MarkScouted(fleet);

        // Enemy fleets are scouted if:
        // - NOT a Hunter-Killer and adjacent to an empire world or fleet, or
        // - within base scan range.
        foreach (var fleet in game.Galaxy.Fleets) {
            if (fleet.Owner == empire)
                continue;

            var isHk = fleet.Type == Types.FleetType.HunterKillerFleet;

            // Check adjacency to empire worlds or fleets (INTRFACE.PAS:1502-1531).
            if (!isHk && IsAdjacentToEmpireTerritory(fleet.Location, empire, game)) {
                empire.Fleets.MarkScouted(fleet);
                continue;
            }

            // Check starbase scan range (INTRFACE.PAS:1536-1541).
            if (IsInRangeOfStarbase(fleet.Location, empire, game)) {
                empire.Fleets.MarkScouted(fleet);
                continue;
            }

            // Check planet detection (INTRFACE.PAS:1544-1547).
            if (!isHk && fleet.Type != Types.FleetType.Penetrator &&
                IsInRangeOfPlanet(fleet.Location, empire, game)) {
                empire.Fleets.MarkKnown(fleet);
            }
        }
    }

    private static void ScoutObjects(Empire empire, Game game)
    {
        // Scout around each of the empire's owned planets and fleets (INTRFACE.PAS:1565-1582).
        foreach (var planet in game.Galaxy.Planets)
            if (planet.Owner == empire)
                ScoutAdjacent(planet.Location, empire, game);

        foreach (var fleet in game.Galaxy.Fleets)
            if (fleet.Owner == empire && fleet.Status != Types.FleetStatus.Lost)
                ScoutAdjacent(fleet.Location, empire, game);

        // Determine scouted status for all existing entities (INTRFACE.PAS:1585-1614).
        DetermineIfScoutedPlanets(game.Galaxy.Planets, empire, game);
        DetermineIfScoutedStarbases(game.Galaxy.Starbases, empire, game);
        DetermineIfScoutedStargates(game.Galaxy.Stargates, empire, game);
        DetermineIfScoutedConstructions(game.Galaxy.ConstructionSites, empire, game);
    }

    private static void ScoutAdjacent(Coordinate center, Empire empire, Game game)
    {
        foreach (var (dx, dy) in _adjacentOffsets) {
            var x = center.X + dx;
            var y = center.Y + dy;

            // Skip out-of-bounds.
            if (x < 0 || x >= game.Galaxy.Size || y < 0 || y >= game.Galaxy.Size)
                continue;

            var adj = new Coordinate(x, y);

            // Scout all objects at this location.
            foreach (var planet in game.Galaxy.Planets)
                if (planet.Location == adj)
                    empire.Planets.MarkScouted(planet);

            foreach (var starbase in game.Galaxy.Starbases)
                if (starbase.Location == adj)
                    empire.Starbases.MarkScouted(starbase);

            foreach (var stargate in game.Galaxy.Stargates)
                if (stargate.Location == adj)
                    empire.Stargates.MarkScouted(stargate);

            foreach (var constr in game.Galaxy.ConstructionSites)
                if (constr.Location == adj)
                    empire.ConstructionSites.MarkScouted(constr);

            // Dark nebula blocks further adjacent scouting (INTRFACE.PAS:Scout line 132-133).
            // TODO: implement when nebula mechanics are added.
        }
    }

    private static void DetermineIfScoutedPlanets(List<Planet> planets, Empire empire, Game game)
    {
        foreach (var planet in planets) {
            if (empire.Planets.Scouted.Contains(planet))
                continue;

            if (planet.Owner == empire) {
                empire.Planets.MarkScouted(planet);
                continue;
            }

            // Scout if within capital scan range (INTRFACE.PAS:1437-1440).
            var capital = empire.Capital;
            if (capital != null && Chebyshev(capital.Location, planet.Location) < CapitalScanRadius) {
                empire.Planets.MarkScouted(planet);
                continue;
            }

            // Scout if within starbase scan range (INTRFACE.PAS:1441-1442).
            if (IsInRangeOfStarbase(planet.Location, empire, game)) {
                empire.Planets.MarkScouted(planet);
            }
        }
    }

    private static void DetermineIfScoutedStarbases(List<Starbase> starbases, Empire empire, Game game)
    {
        foreach (var starbase in starbases) {
            if (empire.Starbases.Scouted.Contains(starbase))
                continue;

            if (starbase.Owner == empire) {
                empire.Starbases.MarkScouted(starbase);
                continue;
            }

            var capital = empire.Capital;
            if (capital != null && Chebyshev(capital.Location, starbase.Location) < CapitalScanRadius) {
                empire.Starbases.MarkScouted(starbase);
                continue;
            }

            if (IsInRangeOfStarbase(starbase.Location, empire, game)) {
                empire.Starbases.MarkScouted(starbase);
            }
        }
    }

    private static void DetermineIfScoutedStargates(List<Stargate> stargates, Empire empire, Game game)
    {
        foreach (var stargate in stargates) {
            if (empire.Stargates.Scouted.Contains(stargate))
                continue;

            if (stargate.Owner == empire) {
                empire.Stargates.MarkScouted(stargate);
                continue;
            }

            var capital = empire.Capital;
            if (capital != null && Chebyshev(capital.Location, stargate.Location) < CapitalScanRadius) {
                empire.Stargates.MarkScouted(stargate);
                continue;
            }

            if (IsInRangeOfStarbase(stargate.Location, empire, game)) {
                empire.Stargates.MarkScouted(stargate);
            }
        }
    }

    private static void DetermineIfScoutedConstructions(List<ConstructionSite> constructions, Empire empire, Game game)
    {
        foreach (var constr in constructions) {
            if (empire.ConstructionSites.Scouted.Contains(constr))
                continue;

            if (constr.Owner == empire) {
                empire.ConstructionSites.MarkScouted(constr);
                continue;
            }

            var capital = empire.Capital;
            if (capital != null && Chebyshev(capital.Location, constr.Location) < CapitalScanRadius) {
                empire.ConstructionSites.MarkScouted(constr);
                continue;
            }

            if (IsInRangeOfStarbase(constr.Location, empire, game)) {
                empire.ConstructionSites.MarkScouted(constr);
            }
        }
    }

    private static bool IsAdjacentToEmpireTerritory(Coordinate location, Empire empire, Game game)
    {
        foreach (var (dx, dy) in _adjacentOffsets) {
            var x = location.X + dx;
            var y = location.Y + dy;

            if (x < 0 || x >= game.Galaxy.Size || y < 0 || y >= game.Galaxy.Size)
                continue;

            var adj = new Coordinate(x, y);

            if (game.Galaxy.Planets.Any(p => p.Owner == empire && p.Location == adj))
                return true;
            if (game.Galaxy.Fleets.Any(f => f.Owner == empire && f.Location == adj))
                return true;
        }

        return false;
    }

    private static bool IsInRangeOfStarbase(Coordinate location, Empire empire, Game game)
    {
        // Command bases and fortresses scan (not industrial complexes/outposts — INTRFACE.PAS:1388).
        return game.Galaxy.Starbases.Any(s =>
            s.Owner == empire &&
            (s.Kind == Types.StarbaseKind.CommandBase || s.Kind == Types.StarbaseKind.Fortress) &&
            Chebyshev(s.Location, location) < StarbaseScanRadius);
    }

    private static bool IsInRangeOfPlanet(Coordinate location, Empire empire, Game game)
    {
        // INTRFACE.PAS:1411 — distance <= 5, not in nebula (deferred).
        return game.Galaxy.Planets.Any(p =>
            p.Owner == empire &&
            Chebyshev(p.Location, location) <= PlanetDetectRadius);
    }

    private static int Chebyshev(Coordinate a, Coordinate b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
