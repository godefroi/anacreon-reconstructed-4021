using ThreeLn.Reconstruction4021.Core;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Turns;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests;

/// <summary>
/// Verifies fog-of-war mechanics: fleet visibility is ephemeral (cleared each turn),
/// planet/starbase/stargate/construction visibility is accumulated (Known persists).
/// Scouting radii match INTRFACE.PAS: adjacent cells, capital/starbase radius < 6, planet <= 5.
/// </summary>
public class VisibilityHandlerTests
{
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

        var handler = new VisibilityHandler();
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

        var handler = new VisibilityHandler();
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

        var handler = new VisibilityHandler();
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

        var handler = new VisibilityHandler();
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

        var handler = new VisibilityHandler();
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

        var handler = new VisibilityHandler();
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

        var handler = new VisibilityHandler();
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Planets.Scouted).Contains(enemyPlanet);
    }

    [Test]
    public async Task PlanetInCapitalScanRangeIsScouted()
    {
        var human = new Empire { Name = "Human" };
        var game = BuildGame(null, ("Human", human));

        var capital = new Planet {
            Owner = human,
            Location = new Coordinate(10, 10),
        };
        game.Galaxy.Planets.Add(capital);
        human.Capital = capital;

        var distantPlanet = new Planet {
            Owner = new Empire { Name = "Enemy" },
            Location = new Coordinate(14, 10), // Distance 4 from capital (< 6)
        };
        game.Galaxy.Planets.Add(distantPlanet);

        var handler = new VisibilityHandler();
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Planets.Scouted).Contains(distantPlanet);
    }

    [Test]
    public async Task PlanetBeyondCapitalScanRangeNotScouted()
    {
        var human = new Empire { Name = "Human" };
        var game = BuildGame(null, ("Human", human));

        var capital = new Planet {
            Owner = human,
            Location = new Coordinate(10, 10),
        };
        game.Galaxy.Planets.Add(capital);
        human.Capital = capital;

        var distantPlanet = new Planet {
            Owner = new Empire { Name = "Enemy" },
            Location = new Coordinate(16, 10), // Distance 6 from capital (not < 6)
        };
        game.Galaxy.Planets.Add(distantPlanet);

        var handler = new VisibilityHandler();
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Planets.Scouted).DoesNotContain(distantPlanet);
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

        var handler = new VisibilityHandler();

        // Turn 1: scout both planet and fleet
        handler.RefreshVisibility(human, game);
        await Assert.That(human.Planets.Scouted).Contains(enemyPlanet);
        await Assert.That(human.Fleets.Scouted).Contains(enemyFleet);

        // Move fleet far away
        enemyFleet.Location = new Coordinate(0, 0);

        // Turn 2: planet should still be known, fleet should disappear
        handler.RefreshVisibility(human, game);
        await Assert.That(human.Planets.Scouted).Contains(enemyPlanet); // Planet Known persists
        await Assert.That(human.Fleets.Scouted).DoesNotContain(enemyFleet); // Fleet visibility reset
    }

    [Test]
    public async Task StarbaseInRangeOfCapitalIsScouted()
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

        var handler = new VisibilityHandler();
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

        var handler = new VisibilityHandler();
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

        var handler = new VisibilityHandler();
        handler.RefreshVisibility(human, game);

        await Assert.That(human.Planets.Scouted.Count).IsEqualTo(9);
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

        var handler = new VisibilityHandler();
        handler.RefreshVisibility(human, game);

        // Should be scouted because of command base, not industrial complex
        await Assert.That(human.Fleets.Scouted).Contains(targetFleet);
    }
}
