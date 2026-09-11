using Reconstructed4021.Core;
using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.SaveFormat;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;
using Reconstructed4021.LegacyNpe;

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
        new SavGameLoader(npeProvider: new LegacyNpeProvider()).LoadGame(File.ReadAllBytes(Path.Combine(PascalGroundTruth.PascalHarness.RepoRoot, "reference", "saves", fileName)));

    private static async Task AssertRoundTrips(Game game)
    {
        var npeProvider = new LegacyNpeProvider();
        var json = GameJson.Serialize(game, npeProvider);
        var roundTripped = GameJson.Deserialize(json, new Random(0), npeProvider);

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
        var json = GameJson.Serialize(LoadSav("INTRO_1.SAV"), new LegacyNpeProvider());
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

        var npeProvider = new LegacyNpeProvider();
        var json = GameJson.Serialize(game, npeProvider);
        var roundTripped = GameJson.Deserialize(json, new Random(0), npeProvider);

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
        movingFleet.NextOrder = 1;

        galaxy.Fleets.Remove(targetFleet); // destroyed after the order was compiled against it

        var json = GameJson.Serialize(game); // must not throw
        var roundTripped = GameJson.Deserialize(json, new Random(0));

        var roundTrippedFleet = roundTripped.Galaxy.Fleets.Single(f => f.Location == new Coordinate(1, 1));
        await Assert.That(roundTrippedFleet.Orders).Count().IsEqualTo(1);
        await Assert.That(roundTrippedFleet.Orders[0].DestinationObject).IsNull();
        await Assert.That(roundTrippedFleet.NextOrder).IsEqualTo(1);
    }

    /// <summary>
    /// A save written before <see cref="NewsItem.Resource"/> existed has none of the three
    /// <c>resourceDefense</c>/<c>resourceShip</c>/<c>resourceCargo</c> keys -- <see cref="GameJson"/>'s
    /// own <c>ReadNewsItem</c> must still decode <c>Resource</c> from the legacy combined-ordinal
    /// <c>Parm2</c> (<see cref="ResourceKind.LegacyParmSlot"/>), the same fallback
    /// <see cref="SavGameLoader"/> always needed for real <c>.SAV</c> bytes. Simulated by stripping
    /// those three keys back out of an otherwise-normal serialized save, rather than hand-writing a
    /// stale fixture that would bit-rot the moment another field is added.
    /// </summary>
    [Test]
    public async Task Deserialize_NewsItemWithoutResourceKeys_FallsBackToLegacyParmDecode()
    {
        var galaxy = new Galaxy(10);
        var game = new Game(galaxy);

        var empire = new Empire { Name = "Test Empire" };
        game.Empires.Add(empire);
        game.CurrentEmpire = empire;
        game.TurnHandlers[empire] = new HumanTurnHandler();

        var fleet = new Fleet { Location = new Coordinate(1, 1), Owner = empire };
        galaxy.Fleets.Add(fleet);
        empire.AddNews(NewsType.DestructionDetail, fleet, p1: 3);

        // Old code wrote the combined ResourceTypes ordinal straight into Parm2 and had no
        // resourceDefense/Ship/Cargo keys at all -- rewrite this otherwise-normal serialized save into
        // that exact pre-Resource shape rather than hand-writing a stale fixture that would bit-rot the
        // moment another field is added.
        var node = System.Text.Json.Nodes.JsonNode.Parse(GameJson.Serialize(game))!;
        var newsNode = node["empires"]![0]!["news"]![0]!.AsObject();
        newsNode["parm2"] = (int)ShipType.Starship + 5;
        newsNode.Remove("resourceDefense");
        newsNode.Remove("resourceShip");
        newsNode.Remove("resourceCargo");

        var roundTripped = GameJson.Deserialize(node.ToJsonString(), new Random(0));
        var roundTrippedNews = roundTripped.Empires.Single().News.Single();

        await Assert.That(roundTrippedNews.Parm1).IsEqualTo(3);
        await Assert.That(roundTrippedNews.Resource).IsEqualTo(new ResourceKind.Ship(ShipType.Starship));
    }

    /// <summary>
    /// The exact real bug report: a save written before this port fixed a missing-<c>+12</c> bug
    /// (<c>ReportResourceShortfall</c>/<c>ConstructionLacksRawMaterial</c> once wrote the bare 0-based
    /// <see cref="CargoType"/> ordinal, not the combined one) still shows "ion cannons" instead of the
    /// real resource today unless <c>Resource</c>'s legacy decode knows about that era too --
    /// <see cref="ResourceKind.FromLegacyCargoOnlyOrdinal"/>, not the generic
    /// <see cref="ResourceKind.FromOrdinal"/>, which would otherwise land ordinal 4 in the Defense
    /// range (<see cref="DefenseType.IonCannon"/>) instead of recognizing it as bug-era
    /// <see cref="CargoType.Metals"/>.
    /// </summary>
    [Test]
    public async Task Deserialize_PreBugfixShortfallNews_DecodesRealResourceNotIonCannons()
    {
        var galaxy = new Galaxy(10);
        var game = new Game(galaxy);

        var empire = new Empire { Name = "Test Empire" };
        game.Empires.Add(empire);
        game.CurrentEmpire = empire;
        game.TurnHandlers[empire] = new HumanTurnHandler();

        var planet = new Planet { Location = new Coordinate(1, 1), Owner = empire };
        galaxy.Planets.Add(planet);
        empire.AddNews(NewsType.DefensesLackResources, planet);

        var node = System.Text.Json.Nodes.JsonNode.Parse(GameJson.Serialize(game))!;
        var newsNode = node["empires"]![0]!["news"]![0]!.AsObject();
        newsNode["parm1"] = (int)CargoType.Metals; // the real pre-bugfix bytes: bare ordinal, no +12
        newsNode.Remove("resourceDefense");
        newsNode.Remove("resourceShip");
        newsNode.Remove("resourceCargo");

        var roundTripped = GameJson.Deserialize(node.ToJsonString(), new Random(0));
        var roundTrippedNews = roundTripped.Empires.Single().News.Single();

        await Assert.That(roundTrippedNews.Resource).IsEqualTo(new ResourceKind.Cargo(CargoType.Metals));
    }

    /// <summary>
    /// A save written before <see cref="Planet.Redirection"/> existed (GitHub issue #8) has no
    /// <c>redirection</c> key at all -- confirms it still loads, defaulting to a fresh
    /// <see cref="RedirectionSettings"/> (redirection off), same as the <see cref="SelfSufficiency"/>
    /// precedent it follows.
    /// </summary>
    [Test]
    public async Task Deserialize_PlanetWithoutRedirectionKey_DefaultsToRedirectionOff()
    {
        var galaxy = new Galaxy(10);
        var game = new Game(galaxy);

        var empire = new Empire { Name = "Test Empire" };
        game.Empires.Add(empire);
        game.CurrentEmpire = empire;
        game.TurnHandlers[empire] = new HumanTurnHandler();

        var planet = new Planet { Location = new Coordinate(1, 1), Owner = empire };
        galaxy.Planets.Add(planet);

        var node = System.Text.Json.Nodes.JsonNode.Parse(GameJson.Serialize(game))!;
        node["galaxy"]!["planets"]![0]!.AsObject().Remove("redirection");

        var roundTripped = GameJson.Deserialize(node.ToJsonString(), new Random(0));
        var roundTrippedPlanet = roundTripped.Galaxy.Planets.Single();

        await Assert.That(roundTrippedPlanet.Redirection.Destination).IsNull();
    }

    /// <summary>
    /// <see cref="TechCatalog.TechGrantIdentity"/>'s own JSON shape switched from writing the bare
    /// <c>Ordinal</c> int to the type's own name (<see cref="ShipType"/>/<see cref="DefenseType"/>/
    /// <see cref="CargoType"/>/<see cref="ConstructionType"/> depending on <c>Category</c>) -- a save
    /// still carrying the old <c>ordinal</c> key must keep decoding correctly, since a
    /// <c>TechGrantIdentity</c> is always disambiguated by its own <c>Category</c> first (no bug-era
    /// concern the way <see cref="ResourceKind"/>'s combined ordinal has).
    /// </summary>
    [Test]
    public async Task Deserialize_TechGrantWithLegacyOrdinalKey_StillDecodesCorrectly()
    {
        var galaxy = new Galaxy(10);
        var game = new Game(galaxy);

        var empire = new Empire { Name = "Test Empire" };
        game.Empires.Add(empire);
        game.CurrentEmpire = empire;
        game.TurnHandlers[empire] = new HumanTurnHandler();

        var planet = new Planet { Location = new Coordinate(1, 1), Owner = empire };
        galaxy.Planets.Add(planet);
        empire.AddNews(NewsType.EmpireGainedTechnology, planet, techGrant: new TechCatalog.TechGrantIdentity(TechCategory.Ship, (int)ShipType.Jumpship));

        var node = System.Text.Json.Nodes.JsonNode.Parse(GameJson.Serialize(game))!;
        var techGrantNode = node["empires"]![0]!["news"]![0]!["techGrant"]!.AsObject();
        await Assert.That(techGrantNode["type"]!.GetValue<string>()).IsEqualTo(nameof(ShipType.Jumpship));

        techGrantNode.Remove("type");
        techGrantNode["ordinal"] = (int)ShipType.Jumpship;

        var roundTripped = GameJson.Deserialize(node.ToJsonString(), new Random(0));
        var roundTrippedNews = roundTripped.Empires.Single().News.Single();

        await Assert.That(roundTrippedNews.TechGrant).IsEqualTo(new TechCatalog.TechGrantIdentity(TechCategory.Ship, (int)ShipType.Jumpship));
    }

    [Test]
    public async Task RoundTrips_Intro2()
    {
        await AssertRoundTrips(LoadSav("INTRO_2.SAV"));
    }

    [Test]
    public async Task RoundTrips_Gauntlet1()
    {
        // Pirate empire (Thinnva) -> PirateTurnHandler, round-tripped through native JSON like any
        // other ITurnHandler (DeepGraphComparer walks its internal FleetStates/HuntingGround/Sheep
        // properties the same generic way it already walks KingdomTurnHandler's).
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
        // GroundTruthRandom (a real, varying RNG), not FixedRandom: a real scenario's procedural world
        // generation has collision-retry loops (e.g. "re-roll until an unused coordinate"), and
        // FixedRandom always returning the same value hangs one of those forever.
        var random = new GroundTruthRandom(12345);
        var loader = new ScenarioLoader(new Core.NewGame.GalaxySetup(random), random, new LegacyNpeProvider());
        var text = File.ReadAllText(Path.Combine(PascalGroundTruth.PascalHarness.RepoRoot, "reference", "scenarios", "dos_131", "INTRO.SCN"));
        var players = new[] { new ScenarioLoader.PlayerInfo("test_player_1", "test_pass_1", IsEmpress: false) };

        var game = loader.Load(text, players);

        await AssertRoundTrips(game);
    }
}
