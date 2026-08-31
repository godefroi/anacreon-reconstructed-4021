using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.SaveFormat;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// Header + Environment + Sector loading. Ground truth for `INTRO_1.SAV` cross-checked
/// against `scripts/savtool.py`'s own JSON parse of the same file (year 4021, player ordinal 0,
/// scenario "INTRO.SCN", `SizeOfGalaxy=21`, 90 sector cells with `Special=129` — nebula type 1,
/// no mine — and zero `MineScout` entries), not hand-derived from the format doc alone.
/// </summary>
public class SavGameLoaderTests
{
    private static byte[] LoadIntro1() =>
        File.ReadAllBytes(Path.Combine(PascalGroundTruth.PascalHarness.RepoRoot, "reference", "saves", "INTRO_1.SAV"));

    [Test]
    public async Task LoadGame_Intro1_ReadsEnvironment()
    {
        var game = new SavGameLoader().LoadGame(LoadIntro1());

        await Assert.That(game.Year).IsEqualTo(4021);
        await Assert.That(game.ScenarioFilename).IsEqualTo("INTRO.SCN");
    }

    [Test]
    public async Task LoadGame_Intro1_CurrentEmpireIsPlayerOrdinalZero()
    {
        // Environment.Player=0 -- resolves to empire slot 0 (Empire1), the same object identity
        // Empire Data populates with the real "Player_empire" name.
        var game = new SavGameLoader().LoadGame(LoadIntro1());

        await Assert.That(game.CurrentEmpire).IsNotNull();
        await Assert.That(game.CurrentEmpire!.IsIndependent).IsFalse();
    }

    [Test]
    public async Task LoadGame_Intro1_GalaxyIsSizedFromSector()
    {
        var game = new SavGameLoader().LoadGame(LoadIntro1());

        await Assert.That(game.Galaxy.Size).IsEqualTo(21);
    }

    [Test]
    public async Task LoadGame_Intro1_ReconstructsNebulaeFromSpecialLowNibble()
    {
        var game = new SavGameLoader().LoadGame(LoadIntro1());

        // Confirmed via savtool.py: 90 real cells carry Special=129 (nebula type 1, no mine);
        // (4,4)/(4,5)/(4,6) are three of them.
        await Assert.That(game.Galaxy.GetNebula(new Coordinate(4, 4))).IsEqualTo(NebulaType.Nebula);
        await Assert.That(game.Galaxy.GetNebula(new Coordinate(4, 5))).IsEqualTo(NebulaType.Nebula);
        await Assert.That(game.Galaxy.GetNebula(new Coordinate(4, 6))).IsEqualTo(NebulaType.Nebula);

        // A cell with no special data at all -- the sentinel Special=128 (no nebula, no mine).
        await Assert.That(game.Galaxy.GetNebula(new Coordinate(0, 0))).IsEqualTo(NebulaType.None);

        var nebulaCount = Enumerable.Range(0, 22)
            .SelectMany(x => Enumerable.Range(0, 22).Select(y => new Coordinate(x, y)))
            .Count(c => game.Galaxy.GetNebula(c) != NebulaType.None);
        await Assert.That(nebulaCount).IsEqualTo(90);
    }

    [Test]
    public async Task LoadGame_Intro1_HasNoMinefields()
    {
        var game = new SavGameLoader().LoadGame(LoadIntro1());

        // A fresh scenario start has no player-placed mines anywhere -- every sector's high
        // nibble is the Ord(Indep)=8 "no mine" sentinel.
        var mined = Enumerable.Range(0, 22)
            .SelectMany(x => Enumerable.Range(0, 22).Select(y => new Coordinate(x, y)))
            .Count(c => game.Galaxy.GetMineOwner(c) is not null);
        await Assert.That(mined).IsEqualTo(0);
    }

    private static byte[] LoadSave(string name) =>
        File.ReadAllBytes(Path.Combine(PascalGroundTruth.PascalHarness.RepoRoot, "reference", "saves", name));

    [Test]
    public async Task LoadGame_Intro1_ReadsAllFiftyPlanetsDensely()
    {
        var game = new SavGameLoader().LoadGame(LoadIntro1());

        await Assert.That(game.Galaxy.Planets.Count).IsEqualTo(50);
    }

    [Test]
    public async Task LoadGame_Intro2_ReadsRealFleets()
    {
        // Ground truth via scripts/savtool.py: 9 active fleets. Fleet index 232 (savtool's ordinal,
        // 0-based here since this port's Galaxy.Fleets is a plain list, not keyed by disk index):
        // owner ordinal 4 (Empire5), moving from (2,3) to (5,4), ships [604,0,260,162,21,0,32]
        // (Fighter..Transport order), cargo [318,0,0,0,0,0,0] (Legion only), fuelHigh=0/fuel=4079.
        var game = new SavGameLoader().LoadGame(LoadSave("INTRO_2.SAV"));

        await Assert.That(game.Galaxy.Fleets.Count).IsEqualTo(9);

        var fleet = game.Galaxy.Fleets.Single(f => f.Location == new Coordinate(2, 3) && f.Ships.Fighters == 604);
        await Assert.That(fleet.Destination).IsEqualTo(new Coordinate(5, 4));
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.InTransit);
        await Assert.That(fleet.Ships.Jumpships).IsEqualTo(260);
        await Assert.That(fleet.Ships.Jumptransports).IsEqualTo(162);
        await Assert.That(fleet.Ships.Penetrators).IsEqualTo(21);
        await Assert.That(fleet.Ships.Transports).IsEqualTo(32);
        await Assert.That(fleet.Cargo.Legions).IsEqualTo(318);
        await Assert.That(fleet.Fuel).IsEqualTo(4079.0);
    }

    [Test]
    public async Task LoadGame_Intro2_ArrivedFleetHasNullDestination()
    {
        // Fleet 234: xy==dest==(19,20), status=0 (Ready) -- "arrived," not "moving to its own
        // square." This port's own FleetMovementHandler represents that as Destination=null.
        var game = new SavGameLoader().LoadGame(LoadSave("INTRO_2.SAV"));

        var fleet = game.Galaxy.Fleets.Single(f => f.Location == new Coordinate(19, 20) && f.Fuel == 383.0);
        await Assert.That(fleet.Destination).IsNull();
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.Ready);
    }

    [Test]
    public async Task LoadGame_FleetOrders_DiscardsCommandQueueWithoutDesyncing()
    {
        // Fleet 240 (savtool's own index) has 4 queued CommandRecords -- discarded per the tracked
        // gap, but must not throw off the byte cursor for the rest of the file. 13 fleets total.
        var game = new SavGameLoader().LoadGame(LoadSave("FLEET_ORDERS.SAV"));

        await Assert.That(game.Galaxy.Fleets.Count).IsEqualTo(13);
    }

    [Test]
    public async Task LoadGame_Gauntlet1_ReadsStarbases()
    {
        // Ground truth: 10 active starbases. One at (33,36), owner ordinal 4 (Empire5), STyp=22
        // (IndustrialComplex, 22-20), Tech=9 (PreGate), Eff=75, dest==xy -- not moving.
        var game = new SavGameLoader().LoadGame(LoadSave("GAUNTLET_1.SAV"));

        await Assert.That(game.Galaxy.Starbases.Count).IsEqualTo(10);

        var starbase = game.Galaxy.Starbases.Single(s => s.Location == new Coordinate(33, 36));
        await Assert.That(starbase.Kind).IsEqualTo(StarbaseKind.IndustrialComplex);
        await Assert.That(starbase.TechLevel).IsEqualTo(TechLevel.PreGate);
        await Assert.That(starbase.Efficiency).IsEqualTo(75);
        await Assert.That(starbase.Destination).IsNull();
        await Assert.That(starbase.Status).IsEqualTo(FleetStatus.Ready);
    }

    [Test]
    public async Task LoadGame_StargateDone_DecodesGTypAsFullOrdinalMinus24()
    {
        // Ground truth: one completed stargate at (8,18), GTyp=24 (Gate), Dest=Limbo (0,0) -- a
        // lone gate genuinely has no link yet, unlike Fleet/Starbase's different Dest convention.
        var game = new SavGameLoader().LoadGame(LoadSave("STARGATE_DONE.SAV"));

        var stargate = game.Galaxy.Stargates.Single();
        await Assert.That(stargate.Location).IsEqualTo(new Coordinate(8, 18));
        await Assert.That(stargate.Kind).IsEqualTo(StargateKind.Gate);
        await Assert.That(stargate.LinkedTo).IsNull();
    }

    [Test]
    public async Task LoadGame_Confront2_DecodesCTypAsFullOrdinalMinus19()
    {
        // Ground truth: one construction site at (30,38), CTyp=23 (Outpost, 23-19), owner ordinal
        // 0 (Empire1), TimeToCompletion=2.
        var game = new SavGameLoader().LoadGame(LoadSave("Confront_2.SAV"));

        var site = game.Galaxy.ConstructionSites.Single();
        await Assert.That(site.Location).IsEqualTo(new Coordinate(30, 38));
        await Assert.That(site.Building).IsEqualTo(ConstructionType.Outpost);
        await Assert.That(site.YearsToCompletion).IsEqualTo(2);
    }

    [Test]
    public async Task LoadGame_Intro1_ReadsEmpireData()
    {
        // Ground truth: 5 InUse slots (Player_empire is the only player), capitals = planet
        // indices 1-5 respectively, TechLevel=7 (Bio), founding=4021, no CentralEMD modifier.
        // 3 trailing inactive slots correctly excluded from Game.Empires.
        var game = new SavGameLoader().LoadGame(LoadIntro1());

        await Assert.That(game.Empires.Count).IsEqualTo(5);

        var player = game.Empires.Single(e => e.Name == "Player_empire");
        await Assert.That(player.TechnologyLevel).IsEqualTo(TechLevel.Bio);
        await Assert.That(player.FoundingYear).IsEqualTo(4021);
        await Assert.That(player.LosesIfCapitalConquered).IsFalse();
        await Assert.That(player.Capital).IsNotNull();
        await Assert.That(player.Capital!.Location).IsEqualTo(game.Galaxy.Planets[0].Location);

        foreach (var name in new[] { "Trantor", "Lazarus", "Freberon", "First Sun" }) {
            await Assert.That(game.Empires.Any(e => e.Name == name)).IsTrue();
        }
    }

    [Test]
    public async Task LoadGame_Imperium1_AllEightSlotsActive()
    {
        var game = new SavGameLoader().LoadGame(LoadSave("IMPERIUM_1.SAV"));

        await Assert.That(game.Empires.Count).IsEqualTo(8);
    }

    [Test]
    public async Task LoadGame_Imperium1_DecodesTechnologyBitset()
    {
        // Ground truth (slot 0's raw TechnologySet ordinals): [3,4,5,7,8,11,12,15,16,17,18] ->
        // Defenses={Gdm,IonCannon}, Ships={Fighter,Jumpship,Jumptransport,Transport},
        // Resources={Legion,Chemicals,Metals,Supplies,Trillum}, Constructions={} (none unlocked).
        var game = new SavGameLoader().LoadGame(LoadSave("IMPERIUM_1.SAV"));

        var empire = game.Empires.Single(e => e.Name == "Imperium_pl_1");
        var tech = empire.Technology;

        await Assert.That(tech.Defenses).IsEquivalentTo([DefenseType.Gdm, DefenseType.IonCannon]);
        await Assert.That(tech.Ships).IsEquivalentTo([ShipType.Fighter, ShipType.Jumpship, ShipType.Jumptransport, ShipType.Transport]);
        await Assert.That(tech.Resources).IsEquivalentTo([CargoType.Legion, CargoType.Chemicals, CargoType.Metals, CargoType.Supplies, CargoType.Trillum]);
        await Assert.That(tech.Constructions).IsEmpty();
    }

    [Test]
    public async Task LoadGame_FleetOrders_DecodesGenericNewsSubjectAndTechGrant()
    {
        // Ground truth (savtool.py): slot 0 has headline=3 (TechLevelIncreased) with Loc1.ID
        // referencing Planet index 2, parm1=7; and headline=14 (EmpireGainedTechnology) with
        // Loc1.ID referencing Planet index 1, parm1=6 -- ordinal 6 is ShipType.HunterKiller
        // (Ship category). Slot 2 has headline=46 (MessageReceived, no real AddNews call site
        // anywhere in this port) with an empty Loc1 -- falls back to Position=(0,0), no Subject.
        var game = new SavGameLoader().LoadGame(LoadSave("FLEET_ORDERS.SAV"));
        var allNews = game.Empires.SelectMany(e => e.News).ToList();

        var techLevelNews = allNews.Single(n => n.Headline == NewsType.TechLevelIncreased && n.Parm1 == 7);
        await Assert.That(techLevelNews.Subject).IsNotNull();
        await Assert.That(techLevelNews.Subject).IsTypeOf<Planet>();

        var techGrantNews = allNews.Single(n => n.Headline == NewsType.EmpireGainedTechnology);
        await Assert.That(techGrantNews.Parm1).IsEqualTo(6);
        await Assert.That(techGrantNews.TechGrant).IsEqualTo(new TechCatalog.TechGrantIdentity(TechCategory.Ship, (int)ShipType.HunterKiller));

        var messageNews = allNews.Single(n => n.Headline == NewsType.MessageReceived);
        await Assert.That(messageNews.Subject).IsNull();
        await Assert.That(messageNews.Position).IsEqualTo(new Coordinate(0, 0));
        await Assert.That(messageNews.OtherEmpire).IsNull();
    }

    [Test]
    public async Task LoadGame_Confront2_DecodesOtherEmpireFromConfirmedHeadlines()
    {
        // Ground truth (savtool.py, ATTACK.PAS:1664/1669/INTRFACE.PAS:1329 confirmed directly):
        // FleetDestroyedByLams/FleetDamagedByLams both carry parm1=4 (Empire5); ProbeDestroyedByYou
        // carries parm1=7 (Empire8).
        var game = new SavGameLoader().LoadGame(LoadSave("Confront_2.SAV"));
        var allNews = game.Empires.SelectMany(e => e.News).ToList();

        var destroyedByLams = allNews.Single(n => n.Headline == NewsType.FleetDestroyedByLams);
        await Assert.That(destroyedByLams.OtherEmpire).IsNotNull();
        await Assert.That(destroyedByLams.Subject).IsTypeOf<Fleet>();

        var damagedByLams = allNews.Single(n => n.Headline == NewsType.FleetDamagedByLams);
        await Assert.That(damagedByLams.OtherEmpire).IsEqualTo(destroyedByLams.OtherEmpire);

        // Ground truth has 3 ProbeDestroyedByYou entries (slot 4 x1, slot 6 x2, both parm1=7) --
        // all three must decode OtherEmpire, not just the first.
        var probeDestroyedEntries = allNews.Where(n => n.Headline == NewsType.ProbeDestroyedByYou).ToList();
        await Assert.That(probeDestroyedEntries.Count).IsEqualTo(3);
        foreach (var entry in probeDestroyedEntries) {
            await Assert.That(entry.OtherEmpire).IsNotNull();
        }
    }

    [Test]
    public async Task LoadGame_Confront2_KeepsDestructionDetailParmsRaw()
    {
        // DestructionDetail has no OtherEmpire/TechGrant mapping -- Parm1/Parm2 stay plain ints.
        var game = new SavGameLoader().LoadGame(LoadSave("Confront_2.SAV"));
        var allNews = game.Empires.SelectMany(e => e.News).ToList();

        var detail = allNews.Single(n => n.Headline == NewsType.DestructionDetail && n.Parm1 == 1742);
        await Assert.That(detail.Parm2).IsEqualTo(6);
        await Assert.That(detail.OtherEmpire).IsNull();
        await Assert.That(detail.TechGrant).IsNull();
    }

    [Test]
    public async Task LoadGame_Intro1_ConstructsKingdomTurnHandlerForEachNpeEmpire()
    {
        // Ground truth: slots 1-4 (Trantor/Lazarus/Freberon/First Sun) are all Kingdom2NPE (typ=3).
        var game = new SavGameLoader().LoadGame(LoadIntro1());

        foreach (var name in new[] { "Trantor", "Lazarus", "Freberon", "First Sun" }) {
            var empire = game.Empires.Single(e => e.Name == name);
            await Assert.That(empire.NpeType).IsEqualTo(NpeEmpireType.Kingdom2);
            await Assert.That(game.TurnHandlers).ContainsKey(empire);
            await Assert.That(game.TurnHandlers[empire]).IsTypeOf<KingdomTurnHandler>();
            await Assert.That(game.TurnHandlers[empire].IsHuman).IsFalse();
        }
    }

    [Test]
    public async Task LoadGame_Intro1_KingdomTurnHandlerPlaysATurnFromLoadedState()
    {
        // Real correctness check for the persona/State/FleetStates deserialized from the blob:
        // NpeToolkit's own State[Independent] lookup would KeyNotFoundException if the 9-entry
        // State dictionary weren't populated correctly for all 9 slots (Empire1-8 + Indep), and a
        // malformed FleetStates/persona would surface as some other exception during a real turn.
        var game = new SavGameLoader().LoadGame(LoadIntro1());
        var trantor = game.Empires.Single(e => e.Name == "Trantor");

        game.TurnHandlers[trantor].PlayTurn(trantor, game);
    }

    [Test]
    public async Task LoadGame_Gauntlet1_StoresPirateBlobOpaquely()
    {
        // Ground truth: slot 5 ("Thinnva") is PirateNPE (typ=1) -- no ITurnHandler exists for
        // Pirate yet, so its 739-byte blob must round-trip opaquely instead of being dropped.
        var game = new SavGameLoader().LoadGame(LoadSave("GAUNTLET_1.SAV"));
        var pirate = game.Empires.Single(e => e.Name == "Thinnva");

        await Assert.That(pirate.NpeType).IsEqualTo(NpeEmpireType.Pirate);
        await Assert.That(game.TurnHandlers).DoesNotContainKey(pirate);
        await Assert.That(game.UnimplementedNpeBlobs).ContainsKey(pirate);
        await Assert.That(game.UnimplementedNpeBlobs[pirate].Length).IsEqualTo(739);
    }

    [Test]
    public async Task LoadGame_Confront1_StoresGuardianAndBerserkerBlobsOpaquely()
    {
        // Ground truth: slot 4 ("Solaria") is GuardianNPE (typ=5, 430-byte blob), slot 6 ("Datan")
        // is BerserkerNPE (typ=4, 930-byte blob) -- neither has an ITurnHandler yet.
        var game = new SavGameLoader().LoadGame(LoadSave("Confront_1.SAV"));

        var guardian = game.Empires.Single(e => e.Name == "Solaria");
        await Assert.That(guardian.NpeType).IsEqualTo(NpeEmpireType.Guardian);
        await Assert.That(game.TurnHandlers).DoesNotContainKey(guardian);
        await Assert.That(game.UnimplementedNpeBlobs[guardian].Length).IsEqualTo(430);

        var berserker = game.Empires.Single(e => e.Name == "Datan");
        await Assert.That(berserker.NpeType).IsEqualTo(NpeEmpireType.Berserker);
        await Assert.That(game.TurnHandlers).DoesNotContainKey(berserker);
        await Assert.That(game.UnimplementedNpeBlobs[berserker].Length).IsEqualTo(930);
    }

    /// <summary>
    /// Resolves docs/OPEN_GAPS.md's "SavGameLoader's DefeatedBy decode branch has no exercising
    /// reference save" -- none of the 13 captured .SAV files include a defeated human empire, so
    /// this exercises SavGameWriter's own encode of the human-defeat sentinel (ATTACK.PAS:1120-1131)
    /// followed by this loader's decode of it, rather than a real DOS-captured file. Hand-built
    /// (matching GameJsonTests.RoundTrips_MinimalHandbuiltGame's style) rather than routed through
    /// CombatOutcome.ConquerEmpire, since ConquerEmpire's own logic is already covered by
    /// CombatOutcomeTests -- this test's job is the .SAV encode/decode alone.
    /// </summary>
    [Test]
    public async Task RoundTrips_PendingEliminationHumanEmpire()
    {
        var galaxy = new Galaxy(size: 20);

        var conqueror = EmpireFactory.CreateEmpire("Conqueror", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        var conquerorCapital = new Planet { Location = new Coordinate(1, 1), Owner = conqueror, Class = WorldClass.EarthLike, Type = WorldType.Capital, TechLevel = TechLevel.Jump };
        conqueror.Capital = conquerorCapital;
        galaxy.Planets.Add(conquerorCapital);

        var human = EmpireFactory.CreateEmpire("Human", "pw", isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        human.Capital = null;
        human.Status = EmpireStatus.PendingElimination;
        human.DefeatedBy = conqueror;

        var game = new Core.Game(galaxy);
        game.Empires.Add(conqueror);
        game.Empires.Add(human);
        game.CurrentEmpire = conqueror;

        var bytes = SavGameWriter.WriteGame(game);
        var roundTripped = new SavGameLoader().LoadGame(bytes);

        var roundTrippedHuman = roundTripped.Empires.Single(e => e.Name == "Human");
        await Assert.That(roundTrippedHuman.Capital).IsNull();
        await Assert.That(roundTrippedHuman.Status).IsEqualTo(EmpireStatus.PendingElimination);
        await Assert.That(roundTrippedHuman.DefeatedBy?.Name).IsEqualTo("Conqueror");
    }
}
