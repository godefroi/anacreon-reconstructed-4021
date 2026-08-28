using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.SaveFormat;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests;

/// <summary>
/// Phase 7b (Header + Environment + Sector). Ground truth for `INTRO_1.SAV` cross-checked
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
        // Empire Data will later populate with the real "Player_empire" name (Phase 7d).
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
}
