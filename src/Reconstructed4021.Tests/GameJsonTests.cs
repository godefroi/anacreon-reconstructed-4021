using Reconstructed4021.Core;
using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.SaveFormat;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// <see cref="Core.SaveFormat.GameJson"/>, this port's native JSON save format. Round-trip is checked via
/// <see cref="DeepGraphComparer"/> -- an exhaustive reflection walk of every public property
/// reachable from <see cref="Game"/> -- rather than hand-picked field assertions: this format's own
/// job is "serialize whatever's there," so the test that matters is "did anything get lost or
/// corrupted," not "does this one known field survive." Covers every real reference `.SAV` file
/// (via <see cref="SavGameLoader"/>, exercising Kingdom `TurnHandlers` state, `UnimplementedNpeBlobs`,
/// News with `OtherEmpire`/`TechGrant`, stargate `LinkedTo`, in-transit fleets) plus one freshly
/// built <see cref="ScenarioLoader"/> game (exercises the "nothing has happened yet" edge: empty
/// `EntityVisibility` sets, an empty Kingdom `State`/`FleetStates`).
/// </summary>
public class GameJsonTests
{
    private static Game LoadSav(string fileName) =>
        new SavGameLoader().LoadGame(File.ReadAllBytes(Path.Combine(PascalGroundTruth.PascalHarness.RepoRoot, "reference", "saves", fileName)));

    private static async Task AssertRoundTrips(Game game)
    {
        var json = GameJson.Serialize(game);
        var roundTripped = GameJson.Deserialize(json, new Random(0));

        var diffs = DeepGraphComparer.FindDifferences(game, roundTripped);

        await Assert.That(diffs).IsEmpty();
    }

    [Test]
    public async Task RoundTrips_MinimalHandbuiltGame()
    {
        // Smallest possible graph with a genuine cycle (Planet.Owner -> Empire -> Planets.Known ->
        // same Planet) -- isolates a runaway serializer from a slow/bad reference-preservation setup
        // before throwing a full .SAV file at it.
        var empire = new Empire { Name = "Test Empire" };
        var galaxy = new Galaxy(10);
        var planet = new Planet { Location = new Coordinate(1, 1), Owner = empire };
        galaxy.Planets.Add(planet);
        empire.Planets.MarkKnown(planet);

        var game = new Game(galaxy);
        game.Empires.Add(empire);
        game.CurrentEmpire = empire;

        await AssertRoundTrips(game);
    }

    [Test]
    public async Task RoundTrips_Intro1()
    {
        // Kingdom2 empires with real persona/state/fleetStates -- the richest TurnHandlers coverage.
        await AssertRoundTrips(LoadSav("INTRO_1.SAV"));
    }

    [Test]
    public async Task Intro1_ActuallyExercisesTheOrphanEmpirePath()
    {
        // RoundTrips_Intro1 above only proves the orphan-empire path (CombatOutcome.DestroyEmpire
        // drops a defeated empire from Game.Empires but not from a Kingdom handler's own State
        // dictionary -- see GameJson's EntityIndex remarks) round-trips correctly *if* INTRO_1.SAV
        // still contains an orphan. Pin that precondition here so a future change that stops
        // producing one fails loudly instead of silently un-covering the path.
        var json = GameJson.Serialize(LoadSav("INTRO_1.SAV"));
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        var realEmpireCount = (int)node["realEmpireCount"]!;
        var writtenEmpireCount = node["empires"]!.AsArray().Count;

        await Assert.That(writtenEmpireCount).IsGreaterThan(realEmpireCount);
    }

    [Test]
    public async Task DestroyedKingdom_HasNoTurnHandlerAfterRoundTrip()
    {
        // Counterpart to Intro1_ActuallyExercisesTheOrphanEmpirePath: that test pins a *different*,
        // still-living Kingdom's own diplomacy dictionary correctly retaining a dead empire as a
        // reference (expected, matches real Pascal's fixed per-empire array). This one is the case
        // that actually was a bug before CombatOutcome.DestroyEmpire removed its own Game.TurnHandlers
        // entry: a destroyed Kingdom's *own* handler used to linger in Game.TurnHandlers and got
        // serialized (and, on read-back, resurrected) as if it were still a real, active empire.
        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);

        var conqueror = EmpireFactory.CreateEmpire("Conqueror", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        var conquerorCapital = new Planet { Location = new Coordinate(0, 0), Owner = conqueror, Class = WorldClass.EarthLike, Type = WorldType.Capital, TechLevel = TechLevel.Jump };
        conqueror.Capital = conquerorCapital;
        galaxy.Planets.Add(conquerorCapital);

        var kingdom = EmpireFactory.CreateEmpire("Kingdom", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        kingdom.NpeType = NpeEmpireType.Kingdom1;
        var kingdomCapital = new Planet { Location = new Coordinate(50, 50), Owner = conqueror, Class = WorldClass.EarthLike, Type = WorldType.Independent, TechLevel = TechLevel.Jump };
        kingdom.Capital = kingdomCapital;
        // Game.Empires is now a permanent roster, so kingdom.Capital survives past DestroyEmpire and
        // gets serialized by GameJson -- unlike before this redesign, the referenced Planet needs a
        // real home in the galaxy's own list for EncodeObjectRef to resolve it.
        galaxy.Planets.Add(kingdomCapital);

        game.Empires.Add(conqueror);
        game.Empires.Add(kingdom);
        game.TurnHandlers[kingdom] = new KingdomTurnHandler(kingdom, NpeEmpireType.Kingdom1, new FixedRandom(0));

        CombatOutcome.ConquerEmpire(conqueror, kingdom, game, new FixedRandom(0));

        await Assert.That(game.TurnHandlers).DoesNotContainKey(kingdom);

        var json = GameJson.Serialize(game);
        var roundTripped = GameJson.Deserialize(json, new Random(0));

        await Assert.That(roundTripped.TurnHandlers.Keys.Select(e => e.Name)).DoesNotContain(kingdom.Name);
        await Assert.That(roundTripped.Empires.Select(e => e.Name)).DoesNotContain(kingdom.Name);
    }

    [Test]
    public async Task RoundTrips_Intro2()
    {
        await AssertRoundTrips(LoadSav("INTRO_2.SAV"));
    }

    [Test]
    public async Task RoundTrips_Gauntlet1()
    {
        // Pirate empire -> UnimplementedNpeBlobs.
        await AssertRoundTrips(LoadSav("GAUNTLET_1.SAV"));
    }

    [Test]
    public async Task RoundTrips_Imperium1()
    {
        await AssertRoundTrips(LoadSav("IMPERIUM_1.SAV"));
    }

    [Test]
    public async Task RoundTrips_Aftermat1()
    {
        await AssertRoundTrips(LoadSav("AFTERMAT_1.SAV"));
    }

    [Test]
    public async Task RoundTrips_Princes1()
    {
        await AssertRoundTrips(LoadSav("PRINCES_1.SAV"));
    }

    [Test]
    public async Task RoundTrips_FleetOrders()
    {
        // In-transit fleets (Destination set, fractional Fuel).
        await AssertRoundTrips(LoadSav("FLEET_ORDERS.SAV"));
    }

    [Test]
    public async Task RoundTrips_Confront1()
    {
        // Guardian/Berserker empires -> UnimplementedNpeBlobs; OtherEmpire/TechGrant news.
        await AssertRoundTrips(LoadSav("Confront_1.SAV"));
    }

    [Test]
    public async Task RoundTrips_Confront2()
    {
        await AssertRoundTrips(LoadSav("Confront_2.SAV"));
    }

    [Test]
    public async Task RoundTrips_StargatePrep()
    {
        await AssertRoundTrips(LoadSav("STARGATE_PREP.SAV"));
    }

    [Test]
    public async Task RoundTrips_StargateStarted()
    {
        await AssertRoundTrips(LoadSav("STARGATE_STARTED.SAV"));
    }

    [Test]
    public async Task RoundTrips_StargateNearDone()
    {
        await AssertRoundTrips(LoadSav("STARGATE_NEARDONE.SAV"));
    }

    [Test]
    public async Task RoundTrips_StargateDone()
    {
        // LinkedTo actually set (not just the Limbo sentinel).
        await AssertRoundTrips(LoadSav("STARGATE_DONE.SAV"));
    }

    [Test]
    public async Task RoundTrips_FreshlyCreatedScenario()
    {
        // No .SAV involved at all: exercises the "nothing has happened yet" shape -- empty
        // EntityVisibility sets, empty Kingdom State/FleetStates dictionaries, no News.
        // PascalRandom (a real, varying RNG), not FixedRandom: a real scenario's procedural world
        // generation has collision-retry loops (e.g. "re-roll until an unused coordinate"), and
        // FixedRandom always returning the same value hangs one of those forever -- the same reason
        // ScenarioLoaderGoldenTests uses PascalRandom for real .SCN files, not FixedRandom.
        var random = new PascalRandom(12345);
        var loader = new ScenarioLoader(new Core.NewGame.GalaxySetup(random), random);
        var text = File.ReadAllText(Path.Combine(PascalGroundTruth.PascalHarness.RepoRoot, "reference", "scenarios", "dos_131", "INTRO.SCN"));
        var players = new[] { new ScenarioLoader.PlayerInfo("test_player_1", "test_pass_1", IsEmpress: false) };

        var game = loader.Load(text, players);

        await AssertRoundTrips(game);
    }
}
