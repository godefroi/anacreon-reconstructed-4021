using System.Collections.Frozen;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;

namespace Reconstructed4021.Core.Turns;

/// <summary>
/// Refreshes an empire's fog-of-war each turn, matching INTRFACE.PAS:Scout/ScoutFleets/ScoutObjects/DetermineIfScouted.
/// - Fleet visibility is ephemeral: cleared and rebuilt each turn (ScoutFleets).
/// - Planet/starbase/stargate/construction visibility is accumulated: Known persists, Scouted is updated.
/// - Scouting radius: adjacent cells (Chebyshev), plus capital/starbase/planet scans at distance &lt; 6 or &lt;= 5.
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

    // Directions = (NoDir,No,Ne,Ea,Se,So,Sw,We,Nw), DirX/DirY (DATACNST.PAS:561-564) -- center, then
    // clockwise from north. Order matters here, unlike _adjacentOffsets above: both real callers of
    // this table (ProbeScout, INTRFACE.PAS:1289-1344; Scout, INTRFACE.PAS:91-137/ScoutAdjacent below)
    // can stop scanning partway through the ring on a Dark Nebula cell, so which offsets come before
    // that early exit is part of the real behavior, not an implementation detail.
    private static readonly (int dx, int dy)[] _probeScoutOffsets = [
        (0, 0), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1),
    ];

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

        // Resolve any in-transit probes (INTRFACE.PAS:UpdateProbes) -- ANACREON.PAS's SetUpTurn calls
        // this in the same breath as the three steps above, right before this empire's own turn plays.
        ResolveProbes(empire, game);
    }

    /// <summary>
    /// UpdateProbes (INTRFACE.PAS:1346-1359). A probe has no in-flight position (see
    /// Empire.ProbesInTransit's own doc comment) — it resolves entirely in this one call, then
    /// returns to the available pool, so every entry is scouted and the whole list cleared together.
    /// </summary>
    private void ResolveProbes(Empire empire, Game game)
    {
        foreach (var destination in empire.ProbesInTransit) {
            ScoutFromProbe(empire, destination, game);
        }

        empire.ProbesInTransit.Clear();
    }

    /// <summary>
    /// ProbeScout (INTRFACE.PAS:1289-1344): scans the destination's 3x3 ring in Pascal's fixed
    /// order. A cell already Scouted by this empire is skipped entirely (no re-roll, no re-mark). An
    /// unscouted occupant with legions present risks the probe: a successful destroy roll stops the
    /// scan for the rest of the ring, firing <c>ProbeDestroyed</c> to the probe's own empire and
    /// <c>ProbeDestroyedByYou</c> to the occupant's owner (matching ProbeScout's Exit — UpdateProbes
    /// still returns this probe to Ready either way, so "destroyed" only means less of the ring gets
    /// scouted this call). Otherwise, first contact with a not-yet-Known, not-own-territory occupant
    /// fires <c>ProbeOk</c>. A Dark Nebula cell stops the scan too, regardless of what else happened
    /// at that cell.
    /// </summary>
    private void ScoutFromProbe(Empire empire, Coordinate destination, Game game)
    {
        foreach (var (dx, dy) in _probeScoutOffsets) {
            var x = destination.X + dx;
            var y = destination.Y + dy;

            if (x < 0 || x >= game.Galaxy.Size || y < 0 || y >= game.Galaxy.Size) {
                continue;
            }

            var cell = new Coordinate(x, y);
            var target = FindProbeTarget(cell, empire, game);

            if (target is { AlreadyScouted: false } occupant) {
                var chanceToDestroy = PascalMath.ISqrt(occupant.Legions);
                if (occupant.Owner != Empire.Independent && PascalMath.Rnd(random, 1, 100) < chanceToDestroy) {
                    empire.AddNews(Types.NewsType.ProbeDestroyed, occupant.Entity);
                    occupant.Owner.AddNews(Types.NewsType.ProbeDestroyedByYou, occupant.Entity, otherEmpire: empire);
                    return;
                }

                if (!occupant.AlreadyKnown && occupant.Owner != empire) {
                    empire.AddNews(Types.NewsType.ProbeOk, occupant.Entity);
                }

                occupant.MarkScouted();
            }

            if (game.Galaxy.GetNebula(cell) == Types.NebulaType.DarkNebula) {
                return;
            }
        }
    }

    /// <summary>
    /// GetObject/GetStatus/GetCargo/Scouted/Known (PRIMINTR.PAS) as ProbeScout uses them: whichever
    /// single non-fleet entity (Sector[x]^[y].Obj) occupies a cell, or null for Void. Fleets never
    /// factor in here — ProbeScout only ever calls GetObject, never GetFleets.
    /// </summary>
    private static ProbeTarget? FindProbeTarget(Coordinate cell, Empire empire, Game game)
    {
        foreach (var planet in game.Galaxy.Planets) {
            if (planet.Location == cell) {
                return new ProbeTarget(planet, planet.Owner, planet.Cargo.Legions,
                    empire.Planets.Known.Contains(planet), empire.Planets.Scouted.Contains(planet),
                    () => empire.Planets.MarkScouted(planet));
            }
        }

        foreach (var starbase in game.Galaxy.Starbases) {
            if (starbase.Location == cell) {
                return new ProbeTarget(starbase, starbase.Owner, starbase.Cargo.Legions,
                    empire.Starbases.Known.Contains(starbase), empire.Starbases.Scouted.Contains(starbase),
                    () => empire.Starbases.MarkScouted(starbase));
            }
        }

        foreach (var stargate in game.Galaxy.Stargates) {
            if (stargate.Location == cell) {
                return new ProbeTarget(stargate, stargate.Owner, Legions: 0,
                    empire.Stargates.Known.Contains(stargate), empire.Stargates.Scouted.Contains(stargate),
                    () => empire.Stargates.MarkScouted(stargate));
            }
        }

        foreach (var constr in game.Galaxy.ConstructionSites) {
            if (constr.Location == cell) {
                return new ProbeTarget(constr, constr.Owner, Legions: 0,
                    empire.ConstructionSites.Known.Contains(constr), empire.ConstructionSites.Scouted.Contains(constr),
                    () => empire.ConstructionSites.MarkScouted(constr));
            }
        }

        return null;
    }

    private readonly record struct ProbeTarget(
        ISectorObject Entity, Empire Owner, int Legions, bool AlreadyKnown, bool AlreadyScouted, Action MarkScouted);

    private static void ScoutFleets(Empire empire, Game game)
    {
        // Own fleets are always scouted.
        foreach (var fleet in game.Galaxy.Fleets) {
            if (fleet.Owner == empire) {
                empire.Fleets.MarkScouted(fleet);
            }
        }

        // Enemy fleets are scouted if:
        // - NOT a Hunter-Killer and adjacent to an empire world or fleet, or
        // - within base scan range.
        foreach (var fleet in game.Galaxy.Fleets) {
            if (fleet.Owner == empire) {
                continue;
            }

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
        foreach (var planet in game.Galaxy.Planets) {
            if (planet.Owner == empire) {
                ScoutAdjacent(planet.Location, empire, game);
            }
        }

        foreach (var fleet in game.Galaxy.Fleets) {
            if (fleet.Owner == empire && fleet.Status != Types.FleetStatus.Lost) {
                ScoutAdjacent(fleet.Location, empire, game);
            }
        }

        // Determine scouted status for all existing entities (INTRFACE.PAS:1584-1610). Construction
        // sites are deliberately excluded: Pascal's ScoutObjects only runs this range/roll check for
        // Pln, Base, and Gate — a construction site can only become Scouted via adjacency
        // (ScoutAdjacent above) or by being freshly created by its owner (Construction sets
        // ScoutedBy/KnownBy directly, INTRFACE.PAS:507-508 — not modeled yet, no construction phase exists).
        DetermineIfScouted(game.Galaxy.Planets, empire.Planets, empire, game, p => p.Location, p => p.Owner);
        DetermineIfScouted(game.Galaxy.Starbases, empire.Starbases, empire, game, s => s.Location, s => s.Owner);
        DetermineIfScouted(game.Galaxy.Stargates, empire.Stargates, empire, game, g => g.Location, g => g.Owner);
    }

    /// <summary>
    /// ScoutAdjacent doubles as PRIMINTR.PAS's own <c>Scout(Emp,XY)</c> primitive — internal, not
    /// private, so <see cref="Combat.CombatOutcome"/>'s ConquerWorld can call it directly for its own
    /// <c>Scout(Emp,XY)</c> call rather than reimplementing a second, inevitably-diverging copy.
    /// </summary>
    internal static void ScoutAdjacent(Coordinate center, Empire empire, Game game)
    {
        // Same fixed clockwise-from-center order ProbeScout uses (_probeScoutOffsets' own doc comment)
        // -- order matters here too: the dark-nebula check below can stop the scan partway through the
        // ring, so which offsets come before it is real behavior, not an implementation detail.
        foreach (var (dx, dy) in _probeScoutOffsets) {
            var x = center.X + dx;
            var y = center.Y + dy;

            // Skip out-of-bounds.
            if (x < 0 || x >= game.Galaxy.Size || y < 0 || y >= game.Galaxy.Size) {
                continue;
            }

            var adj = new Coordinate(x, y);

            // Scout all objects at this location.
            foreach (var planet in game.Galaxy.Planets) {
                if (planet.Location == adj) {
                    ScoutOneEntity(planet, empire.Planets, empire);
                }
            }

            foreach (var starbase in game.Galaxy.Starbases) {
                if (starbase.Location == adj) {
                    ScoutOneEntity(starbase, empire.Starbases, empire);
                }
            }

            foreach (var stargate in game.Galaxy.Stargates) {
                if (stargate.Location == adj) {
                    ScoutOneEntity(stargate, empire.Stargates, empire);
                }
            }

            foreach (var constr in game.Galaxy.ConstructionSites) {
                if (constr.Location == adj) {
                    ScoutOneEntity(constr, empire.ConstructionSites, empire);
                }
            }

            // Dark nebula blocks further adjacent scouting (INTRFACE.PAS:Scout line 132-133): the
            // cell the scan just landed on still gets scouted (above), but nothing farther around the
            // ring does once it's Dark Nebula.
            if (game.Galaxy.GetNebula(adj) == Types.NebulaType.DarkNebula) {
                return;
            }
        }
    }

    /// <summary>
    /// Scout's own per-object body (INTRFACE.PAS:119-130): first contact with something not yet
    /// Known and not the scouting empire's own fires <c>POk</c> news (real Pascal's message text is
    /// "Imperial probe has scouted *." even here — this same news item is <c>ProbeScout</c>'s too,
    /// NEWS.PAS:34/126 confirms it's genuinely one shared constant, not a coincidentally similar
    /// name), then the entity is marked Scouted either way. A no-op if already Scouted.
    /// </summary>
    private static void ScoutOneEntity<T>(T entity, EntityVisibility<T> visibility, Empire empire) where T : notnull, ISectorObject
    {
        if (visibility.Scouted.Contains(entity)) {
            return;
        }

        if (!visibility.Known.Contains(entity) && entity.Owner != empire) {
            empire.AddNews(Types.NewsType.ProbeOk, entity);
        }

        visibility.MarkScouted(entity);
    }

    /// <summary>
    /// Matches INTRFACE.PAS:1421-1454 (DetermineIfScouted). An entity never yet Known can only be
    /// discovered via a 50% roll while in starbase scan range — capital range and ownership both play
    /// no part in first discovery (INTRFACE.PAS:1445-1453's roll branch is a plain sibling of the
    /// `Known` branch, not nested inside it — ownership only short-circuits detection for an entity
    /// ALREADY Known). An entity already Known but not Scouted (its Scouted tier decayed since a prior
    /// turn — see ClearScouted/VisibilityHandler.RefreshVisibility) is unconditionally re-detected if
    /// owned, and otherwise by capital or starbase range. Three different rules, not one rule applied
    /// three times; conflating "owned" with "always visible regardless of Known" was a second bug in
    /// this method, found after the first (range checks applied unconditionally to any not-yet-scouted
    /// entity) had already been fixed once. In practice this rarely matters yet: planets self-scout via
    /// ScoutAdjacent's own-location offset regardless, and starbases/stargates are Known from creation
    /// in Pascal (CreateStarbase/CreateStargate set KnownBy immediately) — a guarantee this codebase
    /// can't yet reproduce, since no new-game/construction setup exists to call an equivalent hook. A
    /// test-constructed owned starbase/stargate with nothing to establish Known first now needs the
    /// same 50%-roll-in-its-own-scan-range path as everyone else, same as real Pascal would require of
    /// an owned object nothing had ever scouted or created via the normal channels.
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

            var entityLocation = location(entity);

            if (visibility.Known.Contains(entity)) {
                // Known but not scouted: owned entities are unconditionally re-detected; otherwise
                // capital or starbase range re-detects it (INTRFACE.PAS:1437-1442).
                if (owner(entity) == empire) {
                    visibility.MarkScouted(entity);
                    continue;
                }

                var capital = empire.Capital;
                if (capital != null && Chebyshev(capital.Location, entityLocation) < CapitalScanRadius) {
                    visibility.MarkScouted(entity);
                    continue;
                }

                if (IsInRangeOfStarbase(entityLocation, empire, game))
                    visibility.MarkScouted(entity);
            } else {
                // Not yet known: 50% chance (Rnd(1,2)=1), starbase range only, regardless of
                // ownership (INTRFACE.PAS:1445-1453).
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
        // INTRFACE.PAS:1411 — distance <= 5, and the *target* cell itself must have no nebula at all
        // (GetNebula(ObjXY)=NoNeb) -- Nebula/DarkNebula/DenseNebula all block a planet's own passive
        // detection range the same way, not just DarkNebula's stronger "stop scouting past this cell"
        // rule ScoutAdjacent enforces below.
        if (game.Galaxy.GetNebula(location) != Types.NebulaType.None) {
            return false;
        }

        return game.Galaxy.Planets.Any(p =>
            p.Owner == empire &&
            Chebyshev(p.Location, location) <= PlanetDetectRadius);
    }

    private static int Chebyshev(Coordinate a, Coordinate b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
