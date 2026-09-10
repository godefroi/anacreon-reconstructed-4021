using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.SaveFormat;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;
using Reconstructed4021.LegacyNpe;

namespace Reconstructed4021.Tests;

/// <summary>
/// Investigative fixture ("Stuck Groups.json") for a reported Tactical Battle Display UX bug: a human
/// attack fleet against a Kingdom fleet roughly its own size (unlike "Garrisoned Outpost", whose
/// target is deliberately weak and dies in one round) -- durable enough to survive multiple rounds, so
/// a second Move prompt can be reached with both groups already at Orbit against a Fleet target, where
/// <c>InteractiveCombat.CanAdvance</c> blocks *any* further advance regardless of ship type. Same
/// generator-and-regression-check shape as <see cref="GarrisonedOutpostFixtureTests"/>.
/// </summary>
public class StuckGroupsFixtureTests
{
    private static readonly string FixturePath = Path.Combine(
        PascalGroundTruth.PascalHarness.RepoRoot, "assets", "saves", "Stuck Groups.json");

    private static Game BuildGame()
    {
        var random = new Random(4021);
        var galaxy = new Galaxy(5);

        var human = EmpireFactory.CreateEmpire("Human", null, isEmpress: false, TechLevel.Bio, restlessness: 0, centralModifier: false, foundingYear: 4021);
        var kingdom = EmpireFactory.CreateEmpire("Kingdom", null, isEmpress: false, TechLevel.Bio, restlessness: 0, centralModifier: false, foundingYear: 4021);
        kingdom.NpeType = NpeEmpireType.Kingdom2;

        var zeroShips = new ShipCounts();
        var zeroCargo = new CargoHold();
        var zeroDefenses = new DefenseCounts();
        var galaxySetup = new GalaxySetup(random);

        var humanCapital = galaxySetup.CreateWorld(galaxy, new Coordinate(0, 0), WorldClass.EarthLike, TechLevel.Bio, WorldType.Capital, human,
            population: 2000, efficiency: 80, trillumReserveBase: 100, zeroShips, zeroCargo, zeroDefenses);
        humanCapital.Cargo.Trillum = 200;

        var kingdomCapital = galaxySetup.CreateWorld(galaxy, new Coordinate(4, 4), WorldClass.EarthLike, TechLevel.Bio, WorldType.Capital, kingdom,
            population: 150, efficiency: 20, trillumReserveBase: 100, zeroShips, zeroCargo, zeroDefenses);
        kingdomCapital.Cargo.Trillum = 200;

        // Roughly matched forces, unlike Garrisoned Outpost's deliberately-weak target -- meant to
        // survive multiple rounds of exchange rather than dying in one, so a second Move prompt is
        // reachable with both groups already at their Orbit-vs-Fleet ceiling.
        var targetFleet = new Fleet { Location = new Coordinate(4, 1), Owner = kingdom, Status = FleetStatus.Ready, Fuel = 100 };
        targetFleet.Ships.Fighters = 2000;
        targetFleet.Ships.HunterKillers = 300;
        galaxy.Fleets.Add(targetFleet);

        var attackFleet = new Fleet { Location = new Coordinate(4, 1), Owner = human, Status = FleetStatus.Ready, Fuel = 500 };
        attackFleet.Ships.Fighters = 2000;
        attackFleet.Ships.HunterKillers = 300;
        attackFleet.Names[human] = "Warfleet";
        galaxy.Fleets.Add(attackFleet);

        var game = new Game(galaxy) { Id = new Guid("00000000-0000-0000-0000-000000000002"), Year = 4021, CurrentEmpire = human };
        game.Empires.Add(human);
        game.Empires.Add(kingdom);
        game.TurnHandlers[human] = new HumanTurnHandler();
        game.TurnHandlers[kingdom] = new KingdomTurnHandler(kingdom, NpeEmpireType.Kingdom2, random);

        return game;
    }

    [Test]
    public async Task StuckGroups_RoundTrips()
    {
        var game = BuildGame();
        var npeProvider = new LegacyNpeProvider();
        var roundTripped = GameJson.Deserialize(GameJson.Serialize(game, npeProvider), new Random(0), npeProvider);

        var diffs = DeepGraphComparer.FindDifferences(game, roundTripped);

        await Assert.That(diffs).IsEmpty();
    }

    /// <summary>Regenerates the fixture -- see <see cref="GarrisonedOutpostFixtureTests.RegenerateFixture"/>'s own doc comment for the convention. <c>dotnet test -- --treenode-filter "/*/*/StuckGroupsFixtureTests/RegenerateFixture"</c>.</summary>
    [Test, Explicit]
    public async Task RegenerateFixture()
    {
        File.WriteAllText(FixturePath, GameJson.Serialize(BuildGame(), new LegacyNpeProvider()));
        await Task.CompletedTask;
    }
}
