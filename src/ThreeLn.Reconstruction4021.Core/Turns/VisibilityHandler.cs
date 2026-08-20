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
public sealed class VisibilityHandler(Random random) : IVisibilityHandler
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

    private void ScoutObjects(Empire empire, Game game)
    {
        // Scout around each of the empire's owned planets and fleets (INTRFACE.PAS:1565-1582).
        foreach (var planet in game.Galaxy.Planets)
            if (planet.Owner == empire)
                ScoutAdjacent(planet.Location, empire, game);

        foreach (var fleet in game.Galaxy.Fleets)
            if (fleet.Owner == empire && fleet.Status != Types.FleetStatus.Lost)
                ScoutAdjacent(fleet.Location, empire, game);

        // Determine scouted status for all existing entities (INTRFACE.PAS:1584-1610). Construction
        // sites are deliberately excluded: Pascal's ScoutObjects only runs this range/roll check for
        // Pln, Base, and Gate — a construction site can only become Scouted via adjacency
        // (ScoutAdjacent above) or by being freshly created by its owner (Construction sets
        // ScoutedBy/KnownBy directly, INTRFACE.PAS:507-508 — not modeled yet, no construction phase exists).
        DetermineIfScouted(game.Galaxy.Planets, empire.Planets, empire, game, p => p.Location, p => p.Owner);
        DetermineIfScouted(game.Galaxy.Starbases, empire.Starbases, empire, game, s => s.Location, s => s.Owner);
        DetermineIfScouted(game.Galaxy.Stargates, empire.Stargates, empire, game, g => g.Location, g => g.Owner);
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

    /// <summary>
    /// Matches INTRFACE.PAS:1421-1454 (DetermineIfScouted). An entity never yet Known can only be
    /// discovered via a 50% roll while in starbase scan range — capital range plays no part in first
    /// discovery. An entity already Known but not Scouted (its Scouted tier decayed since a prior
    /// turn — see ClearScouted/VisibilityHandler.RefreshVisibility) is unconditionally re-detected by
    /// capital or starbase range. These are different rules, not one rule applied twice; conflating
    /// them (applying range checks unconditionally to any not-yet-scouted entity) was the bug fixed here.
    /// </summary>
    private void DetermineIfScouted<T>(
        IEnumerable<T> entities,
        EntityVisibility<T> visibility,
        Empire empire,
        Game game,
        Func<T, Coordinate> location,
        Func<T, Empire> owner) where T : notnull
    {
        foreach (var entity in entities) {
            if (visibility.Scouted.Contains(entity))
                continue;

            if (owner(entity) == empire) {
                visibility.MarkScouted(entity);
                continue;
            }

            var entityLocation = location(entity);

            if (visibility.Known.Contains(entity)) {
                // Known but not scouted: capital or starbase range re-detects it (INTRFACE.PAS:1437-1442).
                var capital = empire.Capital;
                if (capital != null && Chebyshev(capital.Location, entityLocation) < CapitalScanRadius) {
                    visibility.MarkScouted(entity);
                    continue;
                }

                if (IsInRangeOfStarbase(entityLocation, empire, game))
                    visibility.MarkScouted(entity);
            } else {
                // Not yet known: 50% chance (Rnd(1,2)=1), starbase range only (INTRFACE.PAS:1445-1453).
                if (IsInRangeOfStarbase(entityLocation, empire, game) && random.Next(2) == 0)
                    visibility.MarkScouted(entity);
            }
        }
    }

    private static bool IsAdjacentToEmpireTerritory(Coordinate location, Empire empire, Game game)
    {
        // Matches Pascal's GetStatus(Obj)=PlayerEmp check: Obj is whichever object occupies that
        // sector's slot — CreatePlanet/CreateStarbase/CreateStargate/Construction all write into it
        // (INTRFACE.PAS:349,375,417,514) — so any owned object there counts, not just planets.
        foreach (var (dx, dy) in _adjacentOffsets) {
            var x = location.X + dx;
            var y = location.Y + dy;

            if (x < 0 || x >= game.Galaxy.Size || y < 0 || y >= game.Galaxy.Size)
                continue;

            var adj = new Coordinate(x, y);

            if (game.Galaxy.Planets.Any(p => p.Owner == empire && p.Location == adj))
                return true;
            if (game.Galaxy.Starbases.Any(s => s.Owner == empire && s.Location == adj))
                return true;
            if (game.Galaxy.Stargates.Any(g => g.Owner == empire && g.Location == adj))
                return true;
            if (game.Galaxy.ConstructionSites.Any(c => c.Owner == empire && c.Location == adj))
                return true;
            if (game.Galaxy.Fleets.Any(f => f.Owner == empire && f.Location == adj))
                return true;
        }

        return false;
    }

    private static bool IsInRangeOfStarbase(Coordinate location, Empire empire, Game game)
    {
        // Every starbase kind scans except industrial complexes — INTRFACE.PAS:1388 is
        // `GetBaseType(BaseID)<>cmp`, not a CommandBase/Fortress allowlist; Outposts scan too.
        return game.Galaxy.Starbases.Any(s =>
            s.Owner == empire &&
            s.Kind != Types.StarbaseKind.IndustrialComplex &&
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
