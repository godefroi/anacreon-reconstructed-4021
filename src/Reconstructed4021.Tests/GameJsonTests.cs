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
        game.TurnHandlers[empire] = new HumanTurnHandler(); // Deserialize reconstructs one for every NpeType-null empire -- give the original one too, or the round trip "gains" it.

        await AssertRoundTrips(game);
    }

    [Test]
    public async Task RoundTrips_ObjectOwnedNamesAndCoordinateBookmark()
    {
        // Exercises NamesConverter directly: an object-owned name from an empire that doesn't own the
        // named planet (matches Pascal's "name anything you can see" feature), plus a bare
        // coordinate-only bookmark with no object at all. None of the real .SAV fixtures the other
        // RoundTrips_* tests load carry any player-defined names, so this is the only coverage of
        // that converter at all.
        var galaxy = new Galaxy(10);
        var owner = new Empire { Name = "Owner" };
        var watcher = new Empire { Name = "Watcher" };
        var planet = new Planet { Location = new Coordinate(1, 1), Owner = owner };
        galaxy.Planets.Add(planet);
        planet.Names[owner] = "Home";
        planet.Names[watcher] = "Enemy Capital";
        owner.Bookmarks.Add(new LocationBookmark { Name = "Empty Space", Location = new Coordinate(5, 5) });

        var game = new Game(galaxy);
        game.Empires.Add(owner);
        game.Empires.Add(watcher);
        game.CurrentEmpire = owner;
        game.TurnHandlers[owner] = new HumanTurnHandler();
        game.TurnHandlers[watcher] = new HumanTurnHandler();

        await AssertRoundTrips(game);
    }

    [Test]
    public async Task RoundTrips_AfterNamedFleetIsDestroyed()
    {
        // Confirms the "cleanup is automatic" claim (ISectorObject.Names's own remarks) actually
        // holds at the one place a live-but-orphaned reference could do real damage: this format's
        // own entity-id tables, built fresh from what's currently in Galaxy.Fleets at serialize time.
        // A destroyed fleet's own Names dictionary is simply never reached from anywhere once it's
        // removed from that list -- nothing to explicitly clean up, nothing to crash on save.
        var galaxy = new Galaxy(10);
        var owner = new Empire { Name = "Owner" };
        var watcher = new Empire { Name = "Watcher" };
        var fleet = new Fleet { Location = new Coordinate(1, 1), Owner = owner };
        fleet.Names[owner] = "My Fleet";
        fleet.Names[watcher] = "Enemy Raiders";
        galaxy.Fleets.Add(fleet);

        var game = new Game(galaxy);
        game.Empires.Add(owner);
        game.Empires.Add(watcher);
        game.CurrentEmpire = owner;
        game.TurnHandlers[owner] = new HumanTurnHandler();
        game.TurnHandlers[watcher] = new HumanTurnHandler();

        galaxy.Fleets.Remove(fleet); // CombatOutcome.DestroyFleet's own primitive, inlined -- internal, not reachable from this assembly.

        await AssertRoundTrips(game);
    }

    [Test]
    public async Task RoundTrips_HumanEmpireGetsAFreshTurnHandlerNotSerializedState()
    {
        // WriteTurnHandlers skips HumanTurnHandler entirely (nothing to persist -- its PlayTurn is a
        // no-op) rather than throwing NotSupportedException the way it does for any other
        // unrecognized ITurnHandler; ReadTurnHandlers reconstructs a fresh one for every NpeType-null
        // empire instead, the same way ScenarioLoader.RunCreatePlayerEmpire does for a new game.
        // DeepGraphComparer can't see this on its own -- TurnHandlers is [JsonIgnore]d -- hence the
        // explicit assertions below alongside AssertRoundTrips.
        var galaxy = new Galaxy(10);
        var human = new Empire { Name = "Human" };
        var game = new Game(galaxy);
        game.Empires.Add(human);
        game.CurrentEmpire = human;
        game.TurnHandlers[human] = new HumanTurnHandler();

        await AssertRoundTrips(game);

        var roundTripped = GameJson.Deserialize(GameJson.Serialize(game), new Random(0));
        var handler = roundTripped.TurnHandlers[roundTripped.Empires[0]];
        await Assert.That(handler).IsTypeOf<HumanTurnHandler>();
        await Assert.That(handler.IsHuman).IsTrue();
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
        // Game.Empires permanently retains an eliminated empire's identity (Status.Eliminated) --
        // only Game.TurnHandlers (AI decision data) is disposed, matching CleanUpNPE's own
        // Dispose(Data). This pins that a destroyed Kingdom's own handler doesn't linger in
        // TurnHandlers and doesn't get serialized/resurrected as if it were still active, while its
        // Game.Empires membership and Status/DefeatedBy do survive the round trip.
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
        await Assert.That(roundTripped.Empires.Select(e => e.Name)).Contains(kingdom.Name);

        var roundTrippedKingdom = roundTripped.Empires.Single(e => e.Name == kingdom.Name);
        await Assert.That(roundTrippedKingdom.Status).IsEqualTo(EmpireStatus.Eliminated);
        await Assert.That(roundTrippedKingdom.DefeatedBy?.Name).IsEqualTo(conqueror.Name);
    }

    /// <summary>
    /// The exact crash reported live: WriteEmpires -> WriteIds threw KeyNotFoundException, because
    /// empire.Fleets.Known/Scouted can hold a Fleet that's no longer in game.Galaxy.Fleets.
    /// VisibilityHandler.RefreshVisibility only rebuilds fleet visibility once per turn ("Fleet
    /// visibility is ephemeral", its own doc comment) -- a fleet destroyed mid-turn (combat, most
    /// directly, via CombatOutcome.DestroyFleet) leaves a dangling Known/Scouted entry in every
    /// empire that had scouted it until that empire's own next turn, and Save Game can run at any
    /// point well inside that window. WriteIds now skips a stale id instead of throwing.
    /// </summary>
    [Test]
    public async Task Serialize_SkipsFleetNoLongerInGalaxyFromEmpireVisibilitySets()
    {
        var galaxy = new Galaxy(10);
        var game = new Game(galaxy);

        var viewer = new Empire { Name = "Viewer" };
        var owner = new Empire { Name = "FleetOwner" };
        game.Empires.Add(viewer);
        game.Empires.Add(owner);
        game.CurrentEmpire = viewer;
        game.TurnHandlers[viewer] = new HumanTurnHandler();
        game.TurnHandlers[owner] = new HumanTurnHandler();

        var fleet = new Fleet { Location = new Coordinate(2, 2), Owner = owner };
        galaxy.Fleets.Add(fleet);
        viewer.Fleets.MarkScouted(fleet); // implies Known too (EntityVisibility's own invariant)

        // Destroyed mid-turn, matching CombatOutcome.DestroyFleet -- removed from the galaxy, but
        // nothing eagerly prunes viewer.Fleets' own stale entry (that's RefreshVisibility's job, not
        // due again until viewer's own next turn).
        galaxy.Fleets.Remove(fleet);

        var json = GameJson.Serialize(game); // must not throw
        var roundTripped = GameJson.Deserialize(json, new Random(0));

        var roundTrippedViewer = roundTripped.Empires.Single(e => e.Name == "Viewer");
        await Assert.That(roundTrippedViewer.Fleets.Known).IsEmpty();
        await Assert.That(roundTrippedViewer.Fleets.Scouted).IsEmpty();
    }

    /// <summary>
    /// Same underlying cause as <see cref="Serialize_SkipsFleetNoLongerInGalaxyFromEmpireVisibilitySets"/>,
    /// one level removed: EncodeObjectRef's own Fleet case (a fleet order's own DestinationObject, a
    /// Kingdom mission's Target/HomeBase) had the identical unguarded-indexer problem. A live fleet's
    /// own order can point at another fleet that's since been destroyed -- degrades to a null
    /// destinationObject instead of throwing.
    /// </summary>
    [Test]
    public async Task Serialize_SkipsFleetOrderDestinationNoLongerInGalaxy()
    {
        var galaxy = new Galaxy(10);
        var game = new Game(galaxy);

        var empire = new Empire { Name = "Test Empire" };
        game.Empires.Add(empire);
        game.CurrentEmpire = empire;
        game.TurnHandlers[empire] = new HumanTurnHandler();

        var movingFleet = new Fleet { Location = new Coordinate(1, 1), Owner = empire };
        var targetFleet = new Fleet { Location = new Coordinate(3, 3), Owner = empire };
        galaxy.Fleets.Add(movingFleet);
        galaxy.Fleets.Add(targetFleet);
        movingFleet.Orders.Add(new FleetOrder(CommandType.Destination, DestinationObject: targetFleet));

        galaxy.Fleets.Remove(targetFleet); // destroyed after the order was compiled against it

        var json = GameJson.Serialize(game); // must not throw
        var roundTripped = GameJson.Deserialize(json, new Random(0));

        var roundTrippedFleet = roundTripped.Galaxy.Fleets.Single(f => f.Location == new Coordinate(1, 1));
        await Assert.That(roundTrippedFleet.Orders).Count().IsEqualTo(1);
        await Assert.That(roundTrippedFleet.Orders[0].DestinationObject).IsNull();
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
