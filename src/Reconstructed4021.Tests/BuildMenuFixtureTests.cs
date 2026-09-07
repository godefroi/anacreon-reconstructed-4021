using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.SaveFormat;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// Builds and pins the "Build Menu Smoke Test" fixture (`assets/saves/Build Menu Smoke Test.json`) --
/// same generator-and-regression-check shape as <see cref="GarrisonedOutpostFixtureTests"/>. A lone
/// human empire with <see cref="ConstructionType.Outpost"/>/<see cref="ConstructionType.CommandBase"/>
/// already unlocked (real gameplay would need to roll them first -- see this session's own tech-
/// advancement investigation -- unlocking them directly here keeps the fixture a one-step Build > New
/// smoke test, not a multi-turn research grind) and an empty galaxy otherwise, so the TUI's own
/// `--load` flag reaches a live New/Site Status/Abort test with nothing else to interact with.
/// </summary>
public class BuildMenuFixtureTests
{
    private static readonly string FixturePath = Path.Combine(
        PascalGroundTruth.PascalHarness.RepoRoot, "assets", "saves", "Build Menu Smoke Test.json");

    private static Game BuildGame()
    {
        var galaxy = new Galaxy(20);

        var human = EmpireFactory.CreateEmpire("Test Empire", null, isEmpress: false, TechLevel.Bio, restlessness: 0, centralModifier: false, foundingYear: 4021);
        human.Technology.Constructions.Add(ConstructionType.Outpost);
        human.Technology.Constructions.Add(ConstructionType.CommandBase);

        var capital = new Planet {
            Location = new Coordinate(0, 0), Owner = human, Class = WorldClass.EarthLike, Type = WorldType.Capital,
            TechLevel = TechLevel.Bio, Population = 1000, Efficiency = 80,
        };
        capital.Ships.Fighters = 100;
        galaxy.Planets.Add(capital);
        human.Capital = capital;
        human.Planets.MarkKnown(capital);
        human.Planets.MarkScouted(capital);

        // Fixed, not Game's own random default -- this fixture's committed JSON needs a stable Id to
        // stay byte-for-byte reproducible across regenerations.
        var game = new Game(galaxy) { Id = new Guid("00000000-0000-0000-0000-000000000003"), Year = 4021, CurrentEmpire = human };
        game.Empires.Add(human);
        game.TurnHandlers[human] = new HumanTurnHandler();

        return game;
    }

    [Test]
    public async Task BuildMenuSmokeTest_MatchesTheCommittedFixture()
    {
        var expected = GameJson.Serialize(BuildGame());

        await Assert.That(File.ReadAllText(FixturePath)).IsEqualTo(expected);
    }

    [Test]
    public async Task BuildMenuSmokeTest_RoundTrips()
    {
        var game = BuildGame();
        var roundTripped = GameJson.Deserialize(GameJson.Serialize(game), new Random(0));

        var diffs = DeepGraphComparer.FindDifferences(game, roundTripped);

        await Assert.That(diffs).IsEmpty();
    }

    /// <summary>
    /// Overwrites the committed fixture with <see cref="BuildGame"/>'s current output. <c>[Explicit]</c>
    /// keeps this out of the default <c>dotnet test</c> run; target it by name:
    /// <c>dotnet test -- --treenode-filter "/*/*/BuildMenuFixtureTests/RegenerateFixture"</c>.
    /// </summary>
    [Test, Explicit]
    public async Task RegenerateFixture()
    {
        File.WriteAllText(FixturePath, GameJson.Serialize(BuildGame()));
        await Task.CompletedTask;
    }
}
