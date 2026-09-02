using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// Verifies fog-of-war mechanics: fleet visibility is ephemeral (cleared each turn),
/// planet/starbase/stargate/construction visibility is accumulated (Known persists).
/// Scouting radii match INTRFACE.PAS: adjacent cells, capital/starbase radius &lt; 6, planet &lt;= 5.
/// First discovery of a never-Known entity requires starbase range AND a 50% roll
/// (INTRFACE.PAS:1445-1453); capital range only re-detects an already-Known entity
/// (INTRFACE.PAS:1437-1440) — these are different rules, exercised separately below.
/// </summary>
public class VisibilityHandlerTests
{
    private static readonly Random _alwaysSucceeds = new FixedRandom(0);
    private static readonly Random _alwaysFails = new FixedRandom(1);

    private static Game BuildGame(Core.Galaxy.Galaxy? galaxy = null, params (string name, Empire empire)[] empires)
    {
        galaxy ??= new Core.Galaxy.Galaxy(size: 20);
        var game = new Game(galaxy);
        foreach (var (_, e) in empires) {
            game.Empires.Add(e);
            game.TurnHandlers[e] = new FakeTurnHandler();
        }
        return game;
    }

    private sealed class FakeTurnHandler : ITurnHandler
    {
        public bool IsHuman => false;
        public void PlayTurn(Empire empire, Game game) { }
    }

    [Test]
    public async Task OwnFleetsAreAlwaysScouted()
    {
        var human = new Empire { Name = "Human" };
        var game = BuildGame(null, ("Human", human));

        var fleet = new Fleet {
            Owner = human,
            Location = new Coordinate(0, 0),
            Status = FleetStatus.Ready,
        };
        fleet.Ships.Starships = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new VisibilityHandler(_alwaysFails);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Fleets.Scouted).Contains(fleet);
    }

    [Test]
    public async Task EnemyFleetAdjacentToOwnerTerritoryIsScouted()
    {
        var human = new Empire { Name = "Human" };
        var enemy = new Empire { Name = "Enemy" };
        var game = BuildGame(null, ("Human", human), ("Enemy", enemy));

        var humanPlanet = new Planet { Owner = human, Location = new Coordinate(5, 5) };
        game.Galaxy.Planets.Add(humanPlanet);

        var enemyFleet = new Fleet {
            Owner = enemy,
            Location = new Coordinate(5, 6), // Adjacent to human planet
            Status = FleetStatus.Ready,
        };
        enemyFleet.Ships.Starships = 1;
        game.Galaxy.Fleets.Add(enemyFleet);

        var handler = new VisibilityHandler(_alwaysFails);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Fleets.Scouted).Contains(enemyFleet);
    }

    [Test]
    public async Task EnemyFleetAdjacentToStarbaseWithNoScanIsScoutedViaTerritory()
    {
        // IndustrialComplex grants no scan radius (IsInRangeOfStarbase excludes it), so this
        // isolates the territory-adjacency rule (any owned object occupies the sector, not just
        // planets/fleets — INTRFACE.PAS:349,375,417,514) from the starbase-scan rule.
        var human = new Empire { Name = "Human" };
        var enemy = new Empire { Name = "Enemy" };
        var game = BuildGame(null, ("Human", human), ("Enemy", enemy));

        var complex = new Starbase {
            Owner = human,
            Location = new Coordinate(10, 10),
            Kind = StarbaseKind.IndustrialComplex,
        };
        game.Galaxy.Starbases.Add(complex);

        var enemyFleet = new Fleet {
            Owner = enemy,
            Location = new Coordinate(10, 11), // Adjacent to the complex
            Status = FleetStatus.Ready,
        };
        enemyFleet.Ships.Starships = 1;
        game.Galaxy.Fleets.Add(enemyFleet);

        var handler = new VisibilityHandler(_alwaysFails);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Fleets.Scouted).Contains(enemyFleet);
    }

    [Test]
    public async Task HunterKillerFleetNotScoutedByAdjacency()
    {
        var human = new Empire { Name = "Human" };
        var enemy = new Empire { Name = "Enemy" };
        var game = BuildGame(null, ("Human", human), ("Enemy", enemy));

        var humanPlanet = new Planet { Owner = human, Location = new Coordinate(5, 5) };
        game.Galaxy.Planets.Add(humanPlanet);

        var hkFleet = new Fleet {
            Owner = enemy,
            Location = new Coordinate(5, 6), // Adjacent, but it's HK
            Status = FleetStatus.Ready,
        };
        hkFleet.Ships.HunterKillers = 1;
        game.Galaxy.Fleets.Add(hkFleet);

        var handler = new VisibilityHandler(_alwaysFails);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Fleets.Scouted).DoesNotContain(hkFleet);
    }

    [Test]
    public async Task FleetInRangeOfStarbaseIsScouted()
    {
        var human = new Empire { Name = "Human" };
        var enemy = new Empire { Name = "Enemy" };
        var game = BuildGame(null, ("Human", human), ("Enemy", enemy));

        var starbase = new Starbase {
            Owner = human,
            Location = new Coordinate(10, 10),
            Kind = StarbaseKind.CommandBase,
        };
        game.Galaxy.Starbases.Add(starbase);

        var enemyFleet = new Fleet {
            Owner = enemy,
            Location = new Coordinate(13, 10), // Distance 3 from starbase (< 6)
            Status = FleetStatus.Ready,
        };
        enemyFleet.Ships.Starships = 1;
        game.Galaxy.Fleets.Add(enemyFleet);

        // Fleet scouting's starbase-range check is unconditional (no RNG gate) per INTRFACE.PAS:1536-1541.
        var handler = new VisibilityHandler(_alwaysFails);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Fleets.Scouted).Contains(enemyFleet);
    }

    [Test]
    public async Task FleetOutOfStarbaseScanRangeNotScouted()
    {
        var human = new Empire { Name = "Human" };
        var enemy = new Empire { Name = "Enemy" };
        var game = BuildGame(null, ("Human", human), ("Enemy", enemy));

        var starbase = new Starbase {
            Owner = human,
            Location = new Coordinate(10, 10),
            Kind = StarbaseKind.CommandBase,
        };
        game.Galaxy.Starbases.Add(starbase);

        var enemyFleet = new Fleet {
            Owner = enemy,
            Location = new Coordinate(16, 10), // Distance 6 from starbase (not < 6)
            Status = FleetStatus.Ready,
        };
        enemyFleet.Ships.Starships = 1;
        game.Galaxy.Fleets.Add(enemyFleet);

        var handler = new VisibilityHandler(_alwaysSucceeds);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Fleets.Scouted).DoesNotContain(enemyFleet);
    }

    [Test]
    public async Task OwnedPlanetAlwaysScouted()
    {
        var human = new Empire { Name = "Human" };
        var game = BuildGame(null, ("Human", human));

        var planet = new Planet {
            Owner = human,
            Location = new Coordinate(5, 5),
        };
        game.Galaxy.Planets.Add(planet);

        var handler = new VisibilityHandler(_alwaysFails);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Planets.Scouted).Contains(planet);
    }

    [Test]
    public async Task AdjacentPlanetIsScouted()
    {
        var human = new Empire { Name = "Human" };
        var game = BuildGame(null, ("Human", human));

        var humanPlanet = new Planet {
            Owner = human,
            Location = new Coordinate(10, 10),
        };
        game.Galaxy.Planets.Add(humanPlanet);

        var enemyPlanet = new Planet {
            Owner = new Empire { Name = "Enemy" },
            Location = new Coordinate(11, 10), // Adjacent
        };
        game.Galaxy.Planets.Add(enemyPlanet);

        var handler = new VisibilityHandler(_alwaysFails);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Planets.Scouted).Contains(enemyPlanet);
    }

    [Test]
    public async Task NeverKnownPlanetInCapitalRangeIsNotScouted()
    {
        // Regression test for the bug: capital range must not grant FIRST discovery — only
        // starbase range (with a 50% roll) can do that (INTRFACE.PAS:1442-1453). Capital range
        // only re-detects an entity that was already Known on some earlier turn.
        var human = new Empire { Name = "Human" };
        var game = BuildGame(null, ("Human", human));

        var capital = new Planet {
            Owner = human,
            Location = new Coordinate(10, 10),
        };
        game.Galaxy.Planets.Add(capital);
        human.Capital = capital;

        var neverSeenPlanet = new Planet {
            Owner = new Empire { Name = "Enemy" },
            Location = new Coordinate(14, 10), // Distance 4 from capital (< 6), never adjacent
        };
        game.Galaxy.Planets.Add(neverSeenPlanet);

        var handler = new VisibilityHandler(_alwaysSucceeds);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Planets.Scouted).DoesNotContain(neverSeenPlanet);
        await Assert.That(human.Planets.Known).DoesNotContain(neverSeenPlanet);
    }

    [Test]
    public async Task KnownPlanetInCapitalRangeIsReScouted()
    {
        // Once a planet has been Known (e.g. via prior adjacency), capital range unconditionally
        // re-detects it (INTRFACE.PAS:1437-1440) — no RNG involved for this upgrade path.
        var human = new Empire { Name = "Human" };
        var game = BuildGame(null, ("Human", human));

        var capital = new Planet { Owner = human, Location = new Coordinate(10, 10) };
        game.Galaxy.Planets.Add(capital);
        human.Capital = capital;

        var distantPlanet = new Planet {
            Owner = new Empire { Name = "Enemy" },
            Location = new Coordinate(14, 10), // Distance 4 from capital (< 6)
        };
        game.Galaxy.Planets.Add(distantPlanet);
        human.Planets.MarkKnown(distantPlanet); // Simulate prior discovery (e.g. past adjacency).

        var handler = new VisibilityHandler(_alwaysFails);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Planets.Scouted).Contains(distantPlanet);
    }

    [Test]
    public async Task PlanetBeyondCapitalScanRangeNotScouted()
    {
        var human = new Empire { Name = "Human" };
        var game = BuildGame(null, ("Human", human));

        var capital = new Planet { Owner = human, Location = new Coordinate(10, 10) };
        game.Galaxy.Planets.Add(capital);
        human.Capital = capital;

        var distantPlanet = new Planet {
            Owner = new Empire { Name = "Enemy" },
            Location = new Coordinate(16, 10), // Distance 6 from capital (not < 6)
        };
        game.Galaxy.Planets.Add(distantPlanet);
        human.Planets.MarkKnown(distantPlanet);

        var handler = new VisibilityHandler(_alwaysFails);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Planets.Scouted).DoesNotContain(distantPlanet);
    }

    [Test]
    public async Task UnknownPlanetInStarbaseRangeDiscoveredOnSuccessfulRoll()
    {
        var human = new Empire { Name = "Human" };
        var game = BuildGame(null, ("Human", human));

        var starbase = new Starbase {
            Owner = human,
            Location = new Coordinate(10, 10),
            Kind = StarbaseKind.CommandBase,
        };
        game.Galaxy.Starbases.Add(starbase);

        var undiscoveredPlanet = new Planet {
            Owner = new Empire { Name = "Enemy" },
            Location = new Coordinate(13, 10), // Distance 3 (< 6)
        };
        game.Galaxy.Planets.Add(undiscoveredPlanet);

        var handler = new VisibilityHandler(_alwaysSucceeds);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Planets.Scouted).Contains(undiscoveredPlanet);
    }

    [Test]
    public async Task UnknownPlanetInStarbaseRangeNotDiscoveredOnFailedRoll()
    {
        var human = new Empire { Name = "Human" };
        var game = BuildGame(null, ("Human", human));

        var starbase = new Starbase {
            Owner = human,
            Location = new Coordinate(10, 10),
            Kind = StarbaseKind.CommandBase,
        };
        game.Galaxy.Starbases.Add(starbase);

        var undiscoveredPlanet = new Planet {
            Owner = new Empire { Name = "Enemy" },
            Location = new Coordinate(13, 10), // Distance 3 (< 6)
        };
        game.Galaxy.Planets.Add(undiscoveredPlanet);

        var handler = new VisibilityHandler(_alwaysFails);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Planets.Scouted).DoesNotContain(undiscoveredPlanet);
        await Assert.That(human.Planets.Known).DoesNotContain(undiscoveredPlanet);
    }

    [Test]
    public async Task FleetEphemeralButPlanetAccumulated_TwoTurn()
    {
        var human = new Empire { Name = "Human" };
        var enemy = new Empire { Name = "Enemy" };
        var game = BuildGame(null, ("Human", human), ("Enemy", enemy));

        var humanPlanet = new Planet { Owner = human, Location = new Coordinate(5, 5) };
        game.Galaxy.Planets.Add(humanPlanet);

        var enemyPlanet = new Planet { Owner = enemy, Location = new Coordinate(5, 6) }; // Adjacent
        game.Galaxy.Planets.Add(enemyPlanet);

        var enemyFleet = new Fleet {
            Owner = enemy,
            Location = new Coordinate(5, 6), // Adjacent to human planet
            Status = FleetStatus.Ready,
        };
        enemyFleet.Ships.Starships = 1;
        game.Galaxy.Fleets.Add(enemyFleet);

        var handler = new VisibilityHandler(_alwaysFails);

        // Turn 1: scout both planet and fleet
        handler.RefreshVisibility(human, game);
        await Assert.That(human.Planets.Scouted).Contains(enemyPlanet);
        await Assert.That(human.Fleets.Scouted).Contains(enemyFleet);

        // Move fleet far away; move human planet away so enemy planet is no longer adjacent/scanned
        enemyFleet.Location = new Coordinate(0, 0);
        humanPlanet.Location = new Coordinate(10, 10);

        // Turn 2: planet should still be known (not scouted), fleet should disappear
        handler.RefreshVisibility(human, game);
        await Assert.That(human.Planets.Scouted).DoesNotContain(enemyPlanet); // Scouted cleared
        await Assert.That(human.Planets.Known).Contains(enemyPlanet); // Known persists
        await Assert.That(human.Fleets.Scouted).DoesNotContain(enemyFleet); // Fleet visibility reset
    }

    [Test]
    public async Task KnownStarbaseInRangeOfCapitalIsReScouted()
    {
        var human = new Empire { Name = "Human" };
        var game = BuildGame(null, ("Human", human));

        var capital = new Planet { Owner = human, Location = new Coordinate(10, 10) };
        game.Galaxy.Planets.Add(capital);
        human.Capital = capital;

        var enemy = new Empire { Name = "Enemy" };
        var starbase = new Starbase {
            Owner = enemy,
            Location = new Coordinate(14, 10), // Distance 4 from capital (< 6)
            Kind = StarbaseKind.CommandBase,
        };
        game.Galaxy.Starbases.Add(starbase);
        human.Starbases.MarkKnown(starbase);

        var handler = new VisibilityHandler(_alwaysFails);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Starbases.Scouted).Contains(starbase);
    }

    [Test]
    public async Task ConstructionSiteInAdjacentCellIsScouted()
    {
        var human = new Empire { Name = "Human" };
        var game = BuildGame(null, ("Human", human));

        var planet = new Planet { Owner = human, Location = new Coordinate(10, 10) };
        game.Galaxy.Planets.Add(planet);

        var enemy = new Empire { Name = "Enemy" };
        var constr = new ConstructionSite {
            Owner = enemy,
            Location = new Coordinate(10, 11), // Adjacent
        };
        game.Galaxy.ConstructionSites.Add(constr);

        var handler = new VisibilityHandler(_alwaysFails);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.ConstructionSites.Scouted).Contains(constr);
    }

    [Test]
    public async Task FleetScoutingClears9CellsAroundLocation()
    {
        var human = new Empire { Name = "Human" };
        var enemy = new Empire { Name = "Enemy" };
        var game = BuildGame(null, ("Human", human), ("Enemy", enemy));

        var fleet = new Fleet {
            Owner = human,
            Location = new Coordinate(10, 10),
            Status = FleetStatus.Ready,
        };
        fleet.Ships.Starships = 1;
        game.Galaxy.Fleets.Add(fleet);

        // Add 9 enemy planets in all adjacent cells
        for (var dx = -1; dx <= 1; dx++) {
            for (var dy = -1; dy <= 1; dy++) {
                var planet = new Planet {
                    Owner = enemy,
                    Location = new Coordinate(10 + dx, 10 + dy),
                };
                game.Galaxy.Planets.Add(planet);
            }
        }

        var handler = new VisibilityHandler(_alwaysFails);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Planets.Scouted.Count).IsEqualTo(9);
    }

    [Test]
    public async Task KnownIndependentWorldInCapitalRangeIsReScouted()
    {
        var human = new Empire { Name = "Human" };
        var game = BuildGame(null, ("Human", human));

        var capital = new Planet {
            Owner = human,
            Location = new Coordinate(10, 10),
        };
        game.Galaxy.Planets.Add(capital);
        human.Capital = capital;

        var independentWorld = new Planet {
            Owner = Empire.Independent,
            Location = new Coordinate(14, 10), // Distance 4 from capital (< 6)
        };
        game.Galaxy.Planets.Add(independentWorld);
        human.Planets.MarkKnown(independentWorld);

        var handler = new VisibilityHandler(_alwaysFails);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Planets.Scouted).Contains(independentWorld);
    }

    [Test]
    public async Task CommandBaseScansBut_IndustrialComplexDoesNot()
    {
        var human = new Empire { Name = "Human" };
        var enemy = new Empire { Name = "Enemy" };
        var game = BuildGame(null, ("Human", human), ("Enemy", enemy));

        var commandBase = new Starbase {
            Owner = human,
            Location = new Coordinate(10, 10),
            Kind = StarbaseKind.CommandBase,
        };
        game.Galaxy.Starbases.Add(commandBase);

        var industrialComplex = new Starbase {
            Owner = human,
            Location = new Coordinate(15, 10), // Distance 5 from both starbases
            Kind = StarbaseKind.IndustrialComplex,
        };
        game.Galaxy.Starbases.Add(industrialComplex);

        var targetFleet = new Fleet {
            Owner = enemy,
            Location = new Coordinate(13, 10),
            Status = FleetStatus.Ready,
        };
        targetFleet.Ships.Starships = 1;
        game.Galaxy.Fleets.Add(targetFleet);

        var handler = new VisibilityHandler(_alwaysFails);
        handler.RefreshVisibility(human, game);

        // Should be scouted because of command base, not industrial complex
        await Assert.That(human.Fleets.Scouted).Contains(targetFleet);
    }

    [Test]
    public async Task FleetInRangeOfOutpostIsScouted()
    {
        // INTRFACE.PAS:1388 is `GetBaseType(BaseID)<>cmp` — every StarbaseTypes member except
        // IndustrialComplex scans, Outpost included. Not a CommandBase/Fortress allowlist.
        var human = new Empire { Name = "Human" };
        var enemy = new Empire { Name = "Enemy" };
        var game = BuildGame(null, ("Human", human), ("Enemy", enemy));

        var outpost = new Starbase {
            Owner = human,
            Location = new Coordinate(10, 10),
            Kind = StarbaseKind.Outpost,
        };
        game.Galaxy.Starbases.Add(outpost);

        var enemyFleet = new Fleet {
            Owner = enemy,
            Location = new Coordinate(13, 10), // Distance 3 from outpost (< 6)
            Status = FleetStatus.Ready,
        };
        enemyFleet.Ships.Starships = 1;
        game.Galaxy.Fleets.Add(enemyFleet);

        var handler = new VisibilityHandler(_alwaysFails);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Fleets.Scouted).Contains(enemyFleet);
    }

    [Test]
    public async Task ConstructionSiteInStarbaseRangeButNotAdjacentIsNotScouted()
    {
        // Pascal's ScoutObjects (INTRFACE.PAS:1584-1610) only runs DetermineIfScouted's range/roll
        // check for Pln, Base, and Gate — never Con. A construction site outside adjacency range
        // must stay unscouted regardless of starbase range or roll outcome.
        var human = new Empire { Name = "Human" };
        var enemy = new Empire { Name = "Enemy" };
        var game = BuildGame(null, ("Human", human), ("Enemy", enemy));

        var commandBase = new Starbase {
            Owner = human,
            Location = new Coordinate(10, 10),
            Kind = StarbaseKind.CommandBase,
        };
        game.Galaxy.Starbases.Add(commandBase);

        var constr = new ConstructionSite {
            Owner = enemy,
            Location = new Coordinate(13, 10), // Distance 3 from the command base (< 6), not adjacent
        };
        game.Galaxy.ConstructionSites.Add(constr);

        var handler = new VisibilityHandler(_alwaysSucceeds);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.ConstructionSites.Scouted).DoesNotContain(constr);
    }

    [Test]
    public async Task OwnedStarbaseNeverKnownNeedsTheSameRollAsAnyoneElse()
    {
        // INTRFACE.PAS:1421-1454 — the "GetStatus(Obj)=Emp" ownership short-circuit lives inside the
        // `IF Known(Emp,Obj) THEN` branch, not as a top-level check. An owned entity nothing has ever
        // marked Known (no creation-time hook exists in this codebase yet, see DetermineIfScouted's
        // doc comment) needs the same 50%-roll-in-its-own-scan-range path as anyone else. This
        // starbase is trivially within its own scan range (distance 0), so only the roll gates it.
        var human = new Empire { Name = "Human" };
        var game = BuildGame(null, ("Human", human));

        var starbase = new Starbase {
            Owner = human,
            Location = new Coordinate(10, 10),
            Kind = StarbaseKind.CommandBase,
        };
        game.Galaxy.Starbases.Add(starbase);

        var handler = new VisibilityHandler(_alwaysFails);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Starbases.Known).DoesNotContain(starbase);
        await Assert.That(human.Starbases.Scouted).DoesNotContain(starbase);
    }

    [Test]
    public async Task FleetInRangeOfOwnedPlanetIsMarkedKnown()
    {
        var human = new Empire { Name = "Human" };
        var enemy = new Empire { Name = "Enemy" };
        var game = BuildGame(null, ("Human", human), ("Enemy", enemy));

        var planet = new Planet { Owner = human, Location = new Coordinate(10, 10) };
        game.Galaxy.Planets.Add(planet);

        var enemyFleet = new Fleet {
            Owner = enemy,
            Location = new Coordinate(15, 10), // Chebyshev distance 5 -- in range (<=5), not adjacent
            Status = FleetStatus.Ready,
        };
        enemyFleet.Ships.Starships = 1;
        game.Galaxy.Fleets.Add(enemyFleet);

        var handler = new VisibilityHandler(_alwaysFails);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Fleets.Known).Contains(enemyFleet);
        await Assert.That(human.Fleets.Scouted).DoesNotContain(enemyFleet);
    }

    /// <summary>INTRFACE.PAS:1411 -- InRangeOfPlanet requires GetNebula(ObjXY)=NoNeb; even a plain (non-Dark, non-Dense) Nebula on the target cell blocks a planet's own passive detection range, not just adjacency-independent Dark Nebula scan-stopping.</summary>
    [Test]
    public async Task FleetInRangeOfPlanetButCellHasNebula_NotMarkedKnown()
    {
        var human = new Empire { Name = "Human" };
        var enemy = new Empire { Name = "Enemy" };
        var game = BuildGame(null, ("Human", human), ("Enemy", enemy));

        var planet = new Planet { Owner = human, Location = new Coordinate(10, 10) };
        game.Galaxy.Planets.Add(planet);

        var enemyFleet = new Fleet {
            Owner = enemy,
            Location = new Coordinate(15, 10),
            Status = FleetStatus.Ready,
        };
        enemyFleet.Ships.Starships = 1;
        game.Galaxy.Fleets.Add(enemyFleet);
        game.Galaxy.SetNebula(enemyFleet.Location, NebulaType.Nebula);

        var handler = new VisibilityHandler(_alwaysFails);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Fleets.Known).DoesNotContain(enemyFleet);
    }

    /// <summary>
    /// INTRFACE.PAS:Scout (:91-137) stops scanning the rest of the 3x3 ring once it processes a Dark
    /// Nebula cell -- the cell itself still gets scouted, but nothing later in the fixed clockwise
    /// order does, matching ProbeScout's own analogous rule. _probeScoutOffsets order: (0,0) center,
    /// (0,-1) N, (1,-1) NE, ... -- North (index 1) is Dark Nebula here, so North itself is scouted but
    /// Northeast (index 2, immediately after it in the fixed order) is not.
    /// </summary>
    [Test]
    public async Task ScoutAdjacent_DarkNebulaCellIsScoutedButStopsTheRestOfTheRing()
    {
        var human = new Empire { Name = "Human" };
        var game = BuildGame(null, ("Human", human));

        var ownPlanet = new Planet { Owner = human, Location = new Coordinate(10, 10) };
        game.Galaxy.Planets.Add(ownPlanet);

        game.Galaxy.SetNebula(new Coordinate(10, 9), NebulaType.DarkNebula); // North of (10,10)

        var onDarkNebulaCell = new Planet { Owner = Empire.Independent, Location = new Coordinate(10, 9) }; // North
        var afterInScanOrder = new Planet { Owner = Empire.Independent, Location = new Coordinate(11, 9) }; // Northeast
        game.Galaxy.Planets.Add(onDarkNebulaCell);
        game.Galaxy.Planets.Add(afterInScanOrder);

        var handler = new VisibilityHandler(_alwaysFails);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Planets.Scouted).Contains(onDarkNebulaCell);
        await Assert.That(human.Planets.Scouted).DoesNotContain(afterInScanOrder);
    }

    /// <summary>INTRFACE.PAS:Scout (:119-127): first contact (not yet Known) with something that isn't the scouting empire's own fires POk news -- the same news item ProbeScout fires (NEWS.PAS:34/126), not a coincidentally similar one.</summary>
    [Test]
    public async Task ScoutAdjacent_FirstContactWithUnknownEntity_FiresProbeOkNews()
    {
        var human = new Empire { Name = "Human" };
        var game = BuildGame(null, ("Human", human));

        var ownPlanet = new Planet { Owner = human, Location = new Coordinate(10, 10) };
        game.Galaxy.Planets.Add(ownPlanet);

        var unknownWorld = new Planet { Owner = Empire.Independent, Location = new Coordinate(10, 9) }; // Adjacent
        game.Galaxy.Planets.Add(unknownWorld);

        var handler = new VisibilityHandler(_alwaysFails);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.News).Count().IsEqualTo(1);
        await Assert.That(human.News[0].Headline).IsEqualTo(NewsType.ProbeOk);
        await Assert.That(human.News[0].Subject).IsSameReferenceAs(unknownWorld);
    }

    [Test]
    public async Task ScoutAdjacent_OwnEntity_NeverFiresProbeOkNews()
    {
        var human = new Empire { Name = "Human" };
        var game = BuildGame(null, ("Human", human));

        var ownPlanet = new Planet { Owner = human, Location = new Coordinate(10, 10) };
        var ownAdjacentPlanet = new Planet { Owner = human, Location = new Coordinate(10, 9) };
        game.Galaxy.Planets.Add(ownPlanet);
        game.Galaxy.Planets.Add(ownAdjacentPlanet);

        var handler = new VisibilityHandler(_alwaysFails);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.News).IsEmpty();
    }

    /// <summary>An entity already Known (just not yet Scouted this turn -- e.g. re-detected via capital range on a prior turn) is a re-contact, not a first contact -- no POk news, even though it still gets (re-)Scouted.</summary>
    [Test]
    public async Task ScoutAdjacent_AlreadyKnownEntity_ReScoutedWithoutFiringNewsAgain()
    {
        var human = new Empire { Name = "Human" };
        var game = BuildGame(null, ("Human", human));

        var ownPlanet = new Planet { Owner = human, Location = new Coordinate(10, 10) };
        game.Galaxy.Planets.Add(ownPlanet);

        var alreadyKnownWorld = new Planet { Owner = Empire.Independent, Location = new Coordinate(10, 9) };
        game.Galaxy.Planets.Add(alreadyKnownWorld);
        human.Planets.MarkKnown(alreadyKnownWorld);

        var handler = new VisibilityHandler(_alwaysFails);
        handler.RefreshVisibility(human, game);

        await Assert.That(human.News).IsEmpty();
        await Assert.That(human.Planets.Scouted).Contains(alreadyKnownWorld);
    }
}
