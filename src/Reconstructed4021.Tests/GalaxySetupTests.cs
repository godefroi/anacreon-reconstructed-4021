using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// NEWGAME.PAS:885-1120,1315-1370 (SetUpWorld/CreateWorld/CreateBase/CreateGate/CreateSRMs/
/// CreateNebula), plus GetRandomXY/CreateRndPlanet/CreateRandomWorlds's randomized-coordinate
/// placement. Most of this class is hardcoded (FixedRandom(0) traced by hand);
/// RandomTrillumReserves/CreateRndPlanet/Nebula have their own golden-file-backed methods at the end
/// instead, since those three formulas' sqrt/pow cascades are error-prone to hand-verify.
///
/// FixedRandom(0) makes every Rnd(min,max) call return min (already established elsewhere in this
/// codebase), so every jitter here resolves deterministically to its lower bound and can be
/// hand-computed directly from source: spread=(int)(value*variation/100.0), result=value-spread.
/// </summary>
public class GalaxySetupTests
{
    private static ShipCounts Ships(int fighters = 0) => new() { Fighters = fighters };
    private static CargoHold Cargo(int chemicals = 0) => new() { Chemicals = chemicals };
    private static DefenseCounts Defenses(int gdms = 0) => new() { Gdms = gdms };

    [Test]
    public async Task CreateWorld_AppliesFieldsAndJittersPopulationShipsCargoDefenses()
    {
        var setup = new GalaxySetup(new FixedRandom(0));
        var galaxy = new Galaxy(size: 10);
        var owner = new Empire { Name = "Terra" };

        var planet = setup.CreateWorld(galaxy, new Coordinate(1, 1), WorldClass.EarthLike, TechLevel.Bio, WorldType.Agricultural, owner,
            population: 1000, efficiency: 80, trillumReserveBase: 100,
            shipBase: Ships(fighters: 100), cargoBase: Cargo(chemicals: 200), defenseBase: Defenses(gdms: 50));

        // Population jitters ±15% (NEWGAME.PAS:1016's RndVar(Pp,15)); ships/cargo/defenses get their
        // own separate ±20% inside SetUpWorld itself (RndShips/RndCargo/RndDefns, NEWGAME.PAS:822-883).
        await Assert.That(planet.Population).IsEqualTo(1000 - (int)(1000 * 0.15));
        await Assert.That(planet.Ships.Fighters).IsEqualTo(100 - (int)(100 * 0.2));
        await Assert.That(planet.Cargo.Chemicals).IsEqualTo(200 - (int)(200 * 0.2));
        await Assert.That(planet.Defenses.Gdms).IsEqualTo(50 - (int)(50 * 0.2));

        await Assert.That(planet.Efficiency).IsEqualTo(80);
        await Assert.That(planet.Type).IsEqualTo(WorldType.Agricultural);
        await Assert.That(planet.Owner).IsSameReferenceAs(owner);
        await Assert.That(planet.TechLevel).IsEqualTo(TechLevel.Bio);
        await Assert.That(planet.Class).IsEqualTo(WorldClass.EarthLike);
        await Assert.That(galaxy.Planets).Contains(planet);
    }

    /// <summary>CreatePlanet's ImpExp:=DefaultISSP (INTRFACE.PAS:354) — every real planet gets this at settlement.</summary>
    [Test]
    public async Task CreateWorld_InitializesSelfSufficiencyDials()
    {
        var setup = new GalaxySetup(new FixedRandom(0));
        var galaxy = new Galaxy(size: 10);
        var owner = new Empire { Name = "Terra" };

        var planet = setup.CreateWorld(galaxy, new Coordinate(0, 0), WorldClass.ClassM, TechLevel.Warp, WorldType.Mine, owner,
            population: 100, efficiency: 50, trillumReserveBase: 0, Ships(), Cargo(), Defenses());

        await Assert.That(planet.SelfSufficiency.Chemical).IsEqualTo(5);
        await Assert.That(planet.SelfSufficiency.Metal).IsEqualTo(5);
        await Assert.That(planet.SelfSufficiency.Supply).IsEqualTo(5);
        await Assert.That(planet.SelfSufficiency.Trillum).IsEqualTo(5);
    }

    /// <summary>NEWGAME.PAS:813-820 (RandomTrillumReserves), hand-derived with FixedRandom(0): temp=Max(100-25,0)=75, then PascalRound(75*8.0+1)=601 for EarthLike's 800 (DATACNST.PAS:307).</summary>
    [Test]
    public async Task CreateWorld_ComputesTrillumReserveFromClassAndRegion()
    {
        var setup = new GalaxySetup(new FixedRandom(0));
        var galaxy = new Galaxy(size: 10);
        var owner = new Empire { Name = "Terra" };

        var planet = setup.CreateWorld(galaxy, new Coordinate(0, 0), WorldClass.EarthLike, TechLevel.Warp, WorldType.Mine, owner,
            population: 100, efficiency: 50, trillumReserveBase: 100, Ships(), Cargo(), Defenses());

        await Assert.That(planet.TrillumReserve).IsEqualTo(601);
    }

    [Test]
    public async Task CreateWorld_CapitalTypeSetsEmpireCapital()
    {
        var setup = new GalaxySetup(new FixedRandom(0));
        var galaxy = new Galaxy(size: 10);
        var owner = new Empire { Name = "Terra" };

        var planet = setup.CreateWorld(galaxy, new Coordinate(0, 0), WorldClass.ClassM, TechLevel.Warp, WorldType.Capital, owner,
            population: 100, efficiency: 50, trillumReserveBase: 0, Ships(), Cargo(), Defenses());

        await Assert.That(owner.Capital).IsSameReferenceAs(planet);
    }

    [Test]
    public async Task CreateWorld_NonCapitalTypeLeavesEmpireCapitalUnset()
    {
        var setup = new GalaxySetup(new FixedRandom(0));
        var galaxy = new Galaxy(size: 10);
        var owner = new Empire { Name = "Terra" };

        setup.CreateWorld(galaxy, new Coordinate(0, 0), WorldClass.ClassM, TechLevel.Warp, WorldType.Mine, owner,
            population: 100, efficiency: 50, trillumReserveBase: 0, Ships(), Cargo(), Defenses());

        await Assert.That(owner.Capital).IsNull();
    }

    [Test]
    [Arguments(StarbaseKind.Outpost, WorldType.Outpost)]
    [Arguments(StarbaseKind.CommandBase, WorldType.Base)]
    [Arguments(StarbaseKind.Fortress, WorldType.Base)]
    public async Task CreateBase_DerivesWorldTypeFromKind(StarbaseKind kind, WorldType expectedType)
    {
        var setup = new GalaxySetup(new FixedRandom(0));
        var galaxy = new Galaxy(size: 10);
        var owner = new Empire { Name = "Terra" };

        var starbase = setup.CreateBase(galaxy, new Coordinate(0, 0), kind, explicitType: null, TechLevel.Warp, owner,
            population: 100, efficiency: 50, Ships(), Cargo(), Defenses());

        await Assert.That(starbase.Type).IsEqualTo(expectedType);
        await Assert.That(starbase.Kind).IsEqualTo(kind);
        await Assert.That(galaxy.Starbases).Contains(starbase);
    }

    /// <summary>
    /// NEWGAME.PAS:1079-1084: IndustrialComplex isn't in the out/[cmm,frt] branches, so its WorldType
    /// comes straight from the scenario's own explicit field — real Pascal behavior, not simplified
    /// away even though every real dos_131 scenario happens to specify WorldType.Base for it too.
    /// </summary>
    [Test]
    public async Task CreateBase_IndustrialComplexUsesExplicitWorldType()
    {
        var setup = new GalaxySetup(new FixedRandom(0));
        var galaxy = new Galaxy(size: 10);
        var owner = new Empire { Name = "Terra" };

        var starbase = setup.CreateBase(galaxy, new Coordinate(0, 0), StarbaseKind.IndustrialComplex, WorldType.University, TechLevel.Warp, owner,
            population: 100, efficiency: 50, Ships(), Cargo(), Defenses());

        await Assert.That(starbase.Type).IsEqualTo(WorldType.University);
    }

    [Test]
    public async Task CreateBase_IndustrialComplexWithoutExplicitTypeThrows()
    {
        var setup = new GalaxySetup(new FixedRandom(0));
        var galaxy = new Galaxy(size: 10);
        var owner = new Empire { Name = "Terra" };

        await Assert.That(() => setup.CreateBase(galaxy, new Coordinate(0, 0), StarbaseKind.IndustrialComplex, explicitType: null, TechLevel.Warp, owner,
            population: 100, efficiency: 50, Ships(), Cargo(), Defenses())).Throws<ArgumentException>();
    }

    /// <summary>A starbase can be a capital in real Pascal too (NEWGAME.PAS:1095-1096) — exercises Empire.Capital's IEconomicWorld? widening end to end.</summary>
    [Test]
    public async Task CreateBase_CapitalTypeSetsEmpireCapital()
    {
        var setup = new GalaxySetup(new FixedRandom(0));
        var galaxy = new Galaxy(size: 10);
        var owner = new Empire { Name = "Terra" };

        var starbase = setup.CreateBase(galaxy, new Coordinate(0, 0), StarbaseKind.IndustrialComplex, WorldType.Capital, TechLevel.Warp, owner,
            population: 100, efficiency: 50, Ships(), Cargo(), Defenses());

        await Assert.That(owner.Capital).IsSameReferenceAs(starbase);
    }

    [Test]
    public async Task CreateGate_AddsStargateWithNoLink()
    {
        var galaxy = new Galaxy(size: 10);
        var owner = new Empire { Name = "Terra" };

        var stargate = GalaxySetup.CreateGate(galaxy, new Coordinate(3, 4), StargateKind.Gate, owner);

        await Assert.That(stargate.Location).IsEqualTo(new Coordinate(3, 4));
        await Assert.That(stargate.Owner).IsSameReferenceAs(owner);
        await Assert.That(stargate.Kind).IsEqualTo(StargateKind.Gate);
        await Assert.That(stargate.LinkedTo).IsNull();
        await Assert.That(galaxy.Stargates).Contains(stargate);
    }

    /// <summary>NEWGAME.PAS:1361-1368: only places a mine on a cell holding nothing else (GetObject(...).ObjTyp=Void).</summary>
    [Test]
    public async Task CreateSRMs_OnlyMinesEmptyCells()
    {
        var galaxy = new Galaxy(size: 10);
        var owner = new Empire { Name = "Terra" };
        var occupied = new Coordinate(1, 1);
        galaxy.Planets.Add(new Planet { Location = occupied });

        GalaxySetup.CreateSRMs(galaxy, new Coordinate(0, 0), new Coordinate(2, 2), owner);

        await Assert.That(galaxy.GetMineOwner(occupied)).IsNull();
        await Assert.That(galaxy.GetMineOwner(new Coordinate(0, 0))).IsSameReferenceAs(owner);
        await Assert.That(galaxy.GetMineOwner(new Coordinate(2, 2))).IsSameReferenceAs(owner);
    }

    [Test]
    public async Task CreateSRMs_SkipsCoordinatesOutsideTheGalaxy()
    {
        var galaxy = new Galaxy(size: 3);
        var owner = new Empire { Name = "Terra" };

        GalaxySetup.CreateSRMs(galaxy, new Coordinate(2, 2), new Coordinate(4, 4), owner);

        await Assert.That(galaxy.GetMineOwner(new Coordinate(2, 2))).IsSameReferenceAs(owner);
        await Assert.That(galaxy.GetMineOwner(new Coordinate(3, 3))).IsNull();
        await Assert.That(galaxy.GetMineOwner(new Coordinate(4, 4))).IsNull();
    }

    /// <summary>NEWGAME.PAS:1315-1331: unlike CreateSRMs, paints over occupied cells too — no occupancy check.</summary>
    [Test]
    public async Task CreateNebula_PaintsEveryCellRegardlessOfOccupancy()
    {
        var galaxy = new Galaxy(size: 10);
        var occupied = new Coordinate(1, 1);
        galaxy.Planets.Add(new Planet { Location = occupied });

        GalaxySetup.CreateNebula(galaxy, new Coordinate(0, 0), new Coordinate(2, 2), NebulaType.DenseNebula);

        await Assert.That(galaxy.GetNebula(occupied)).IsEqualTo(NebulaType.DenseNebula);
        await Assert.That(galaxy.GetNebula(new Coordinate(0, 0))).IsEqualTo(NebulaType.DenseNebula);
        await Assert.That(galaxy.GetNebula(new Coordinate(2, 2))).IsEqualTo(NebulaType.DenseNebula);
    }

    [Test]
    public async Task GetRandomXY_ReturnsCoordinateWhenCellIsFree()
    {
        var setup = new GalaxySetup(new FixedRandom(0));
        var galaxy = new Galaxy(size: 10);

        var coord = setup.GetRandomXY(galaxy, new Coordinate(2, 3), new Coordinate(8, 9), checkOccupancy: true);

        await Assert.That(coord).IsEqualTo(new Coordinate(2, 3));
    }

    [Test]
    public async Task GetRandomXY_SkipsOccupancyCheckWhenNotRequested()
    {
        var setup = new GalaxySetup(new FixedRandom(0));
        var galaxy = new Galaxy(size: 10);
        galaxy.Planets.Add(new Planet { Location = new Coordinate(2, 3) });

        var coord = setup.GetRandomXY(galaxy, new Coordinate(2, 3), new Coordinate(8, 9), checkOccupancy: false);

        await Assert.That(coord).IsEqualTo(new Coordinate(2, 3));
    }

    /// <summary>
    /// FixedRandom(0) always rerolls the same coordinate, so an occupied-forever cell exercises
    /// Pascal's Count&gt;100 give-up path (NEWGAME.PAS:246-251) — including that the 101st roll landing
    /// on a free cell still fails, since Pascal's OR short-circuits Count&gt;100 before the good-cell check.
    /// </summary>
    [Test]
    public async Task GetRandomXY_ThrowsAfterTooManyRetriesOnAnOccupiedCell()
    {
        var setup = new GalaxySetup(new FixedRandom(0));
        var galaxy = new Galaxy(size: 10);
        galaxy.Planets.Add(new Planet { Location = new Coordinate(2, 3) });

        await Assert.That(() => setup.GetRandomXY(galaxy, new Coordinate(2, 3), new Coordinate(8, 9), checkOccupancy: true))
            .Throws<InvalidOperationException>();
    }

    /// <summary>
    /// GetRandomXY's occupancy check consults EnemyMine(XY)=Indep, not "is there any mine at all" —
    /// real Pascal can't tell "never mined" apart from "mined, but by Indep" (both are the same
    /// NoSRMField sentinel bit pattern, see IsGoodForRandomWorld's own doc comment), so an
    /// Independent-owned mine does not block placement.
    /// </summary>
    [Test]
    public async Task GetRandomXY_IndependentOwnedMineDoesNotBlockPlacement()
    {
        var setup = new GalaxySetup(new FixedRandom(0));
        var galaxy = new Galaxy(size: 10);
        galaxy.SetMine(new Coordinate(2, 3), Empire.Independent);

        var coord = setup.GetRandomXY(galaxy, new Coordinate(2, 3), new Coordinate(8, 9), checkOccupancy: true);

        await Assert.That(coord).IsEqualTo(new Coordinate(2, 3));
    }

    /// <summary>Unlike an Independent-owned one, a player-owned mine is a real EnemyMine(XY)&lt;&gt;Indep and does block placement.</summary>
    [Test]
    public async Task GetRandomXY_PlayerOwnedMineBlocksPlacement()
    {
        var setup = new GalaxySetup(new FixedRandom(0));
        var galaxy = new Galaxy(size: 10);
        galaxy.SetMine(new Coordinate(2, 3), new Empire { Name = "Terra" });

        await Assert.That(() => setup.GetRandomXY(galaxy, new Coordinate(2, 3), new Coordinate(8, 9), checkOccupancy: true))
            .Throws<InvalidOperationException>();
    }

    /// <summary>CreateSRMs's own occupancy check (GetObject(...).ObjTyp=Void alone) never consults EnemyMine — re-mining an already-mined cell is real, allowed Pascal behavior.</summary>
    [Test]
    public async Task CreateSRMs_CanReMineAnAlreadyMinedCell()
    {
        var galaxy = new Galaxy(size: 10);
        var firstOwner = new Empire { Name = "Terra" };
        var secondOwner = new Empire { Name = "Mars" };
        galaxy.SetMine(new Coordinate(1, 1), firstOwner);

        GalaxySetup.CreateSRMs(galaxy, new Coordinate(1, 1), new Coordinate(1, 1), secondOwner);

        await Assert.That(galaxy.GetMineOwner(new Coordinate(1, 1))).IsSameReferenceAs(secondOwner);
    }

    /// <summary>Dense nebula blocks random placement even though it doesn't block CreateSRMs/CreateNebula's own occupancy checks (NEWGAME.PAS:249).</summary>
    [Test]
    public async Task GetRandomXY_ThrowsWhenOnlyReachableCellHasDenseNebula()
    {
        var setup = new GalaxySetup(new FixedRandom(0));
        var galaxy = new Galaxy(size: 10);
        galaxy.SetNebula(new Coordinate(2, 3), NebulaType.DenseNebula);

        await Assert.That(() => setup.GetRandomXY(galaxy, new Coordinate(2, 3), new Coordinate(8, 9), checkOccupancy: true))
            .Throws<InvalidOperationException>();
    }

    /// <summary>
    /// NEWGAME.PAS:924-951, hand-derived with FixedRandom(0), tech=Warp: Eff=Rnd(40,60)=40;
    /// basePop=(int)(0.98*700)=686, Pop=Jitter(686,10)=686-68=618; MI=Rnd(1,33)+Rnd(1,34)+Rnd(1,33)=3,
    /// MI:=PascalRound(3*0.30)=1. Ship/cargo/defense bases scale linearly off MI=1, then each gets its
    /// own ApplySetup ±20% jitter (value-(int)(value*0.2)) and CheckTech=True zeroes anything not yet
    /// unlocked at Warp (Jumpship/Jumptransport/HunterKiller/Penetrator/Starship need Jump/Bio/Starship;
    /// DefenseSatellite/IonCannon need Bio/Jump).
    /// </summary>
    [Test]
    public async Task CreateRndPlanet_ComputesPopulationMilitaryIndexAndAppliesTechGate()
    {
        var setup = new GalaxySetup(new FixedRandom(0));
        var galaxy = new Galaxy(size: 10);

        var planet = setup.CreateRndPlanet(galaxy, new Coordinate(4, 5), WorldClass.EarthLike, TechLevel.Warp);

        await Assert.That(planet.Location).IsEqualTo(new Coordinate(4, 5));
        await Assert.That(planet.Class).IsEqualTo(WorldClass.EarthLike);
        await Assert.That(planet.TechLevel).IsEqualTo(TechLevel.Warp);
        await Assert.That(planet.Type).IsEqualTo(WorldType.Independent);
        await Assert.That(planet.Owner).IsSameReferenceAs(Empire.Independent);
        await Assert.That(planet.Efficiency).IsEqualTo(40);
        await Assert.That(planet.Population).IsEqualTo(618);

        await Assert.That(planet.Ships.Fighters).IsEqualTo(80 - 16);
        await Assert.That(planet.Ships.Transports).IsEqualTo(30 - 6);
        await Assert.That(planet.Ships.HunterKillers).IsEqualTo(0);
        await Assert.That(planet.Ships.Jumpships).IsEqualTo(0);
        await Assert.That(planet.Ships.Jumptransports).IsEqualTo(0);
        await Assert.That(planet.Ships.Penetrators).IsEqualTo(0);
        await Assert.That(planet.Ships.Starships).IsEqualTo(0);

        await Assert.That(planet.Cargo.Legions).IsEqualTo(40 - 8);
        await Assert.That(planet.Cargo.Chemicals).IsEqualTo(30 - 6);
        await Assert.That(planet.Cargo.Metals).IsEqualTo(50 - 10);
        await Assert.That(planet.Cargo.Supplies).IsEqualTo(25 - 5);
        await Assert.That(planet.Cargo.Trillum).IsEqualTo(10 - 2);

        await Assert.That(planet.Defenses.Gdms).IsEqualTo(50 - 10);
        await Assert.That(planet.Defenses.DefenseSatellites).IsEqualTo(0);
        await Assert.That(planet.Defenses.IonCannons).IsEqualTo(0);

        // Unlike CreateWorld, CreateRndPlanet doesn't set TrillumReserve itself (NEWGAME.PAS:924-951
        // has no RandomTrillumReserves/PutTrillumReserves call) — its one real caller, CreateRandomWorlds,
        // does that separately right after.
        await Assert.That(planet.TrillumReserve).IsEqualTo(0);
        await Assert.That(galaxy.Planets).Contains(planet);
    }

    /// <summary>NEWGAME.PAS:1122-1163, single world: GetRandomXY resolves to upperLeft (FixedRandom(0)), and Rnd(1,100)=1 picks table index 0.</summary>
    [Test]
    public async Task CreateRandomWorlds_PlacesEachWorldFromTheClassAndTechTables()
    {
        var setup = new GalaxySetup(new FixedRandom(0));
        var galaxy = new Galaxy(size: 10);
        var classTable = Enumerable.Repeat(WorldClass.EarthLike, 100).ToArray();
        var techTable = Enumerable.Repeat(TechLevel.Warp, 100).ToArray();

        var planets = setup.CreateRandomWorlds(galaxy, count: 1, new Coordinate(2, 3), new Coordinate(8, 9), classTable, techTable, trillumReserveBase: 100);

        await Assert.That(planets).Count().IsEqualTo(1);
        var planet = planets[0];
        await Assert.That(planet.Location).IsEqualTo(new Coordinate(2, 3));
        await Assert.That(planet.Class).IsEqualTo(WorldClass.EarthLike);
        await Assert.That(planet.TechLevel).IsEqualTo(TechLevel.Warp);
        // Same class/base/FixedRandom(0) as CreateWorld_ComputesTrillumReserveFromClassAndRegion.
        await Assert.That(planet.TrillumReserve).IsEqualTo(601);
        await Assert.That(galaxy.Planets).Contains(planet);
    }

    /// <summary>
    /// MinTechForClass[Artificial]=Jump; PreTech never satisfies Tech&gt;=MinTechForClass[Class] under
    /// FixedRandom(0) (every retry picks the same table[0] entry), so Safety exceeds 100. Pascal's own
    /// ScenaError here is non-fatal and still creates a planet from the last roll; this port has no
    /// scenario-error-flag machinery yet (2e), so it throws instead of silently producing a mismatched world.
    /// </summary>
    [Test]
    public async Task CreateRandomWorlds_ThrowsWhenClassAndTechTablesAreIncompatible()
    {
        var setup = new GalaxySetup(new FixedRandom(0));
        var galaxy = new Galaxy(size: 10);
        var classTable = Enumerable.Repeat(WorldClass.Artificial, 100).ToArray();
        var techTable = Enumerable.Repeat(TechLevel.PreTech, 100).ToArray();

        await Assert.That(() => setup.CreateRandomWorlds(galaxy, count: 1, new Coordinate(2, 3), new Coordinate(8, 9), classTable, techTable, trillumReserveBase: 100))
            .Throws<InvalidOperationException>();
    }

    /// <summary>
    /// NEWGAME.PAS:1261-1291, hand-derived with FixedRandom(0), Size=5: InitX=Rnd(1,5)=1;
    /// 1&lt;=Size/4(=1) so XDisp=Rnd(0,3)=0, meaning StartX never drifts off 1. Each row:
    /// low=1-Rnd(1,5)=0 (Pascal-space, out of [1,Size] so skipped), high=1+Rnd(1,5)=2 — only
    /// Pascal x=1,2 paint, i.e. 0-based x=0,1, for every row.
    /// </summary>
    [Test]
    public async Task NebulaeBand_PaintsAVerticalStripWhenDriftIsZero()
    {
        var setup = new GalaxySetup(new FixedRandom(0));
        var galaxy = new Galaxy(size: 5);

        setup.NebulaeBand(galaxy);

        for (var y = 0; y < 5; y++) {
            await Assert.That(galaxy.GetNebula(new Coordinate(0, y))).IsEqualTo(NebulaType.Nebula);
            await Assert.That(galaxy.GetNebula(new Coordinate(1, y))).IsEqualTo(NebulaType.Nebula);
            await Assert.That(galaxy.GetNebula(new Coordinate(2, y))).IsEqualTo(NebulaType.None);
        }
    }

    /// <summary>
    /// NEWGAME.PAS:1293-1313, hand-derived with FixedRandom(0): InitX=InitY=Rnd(1,10)=1;
    /// yLow=1-Rnd(1,3)=0, yHigh=1+Rnd(1,3)=2 (Pascal-space; y=0 out of bounds, skipped). For y=1,2:
    /// spread=4-Abs(y-1) is 4 or 3, but Rnd(1,spread) is always 1 either way, so xLow=0 (skipped),
    /// xHigh=2 — painting Pascal (1,1),(2,1),(1,2),(2,2), i.e. 0-based (0,0),(1,0),(0,1),(1,1).
    /// </summary>
    [Test]
    public async Task NebulaePatches_PaintsADiamondPerPatch()
    {
        var setup = new GalaxySetup(new FixedRandom(0));
        var galaxy = new Galaxy(size: 10);

        setup.NebulaePatches(galaxy, patchCount: 1);

        await Assert.That(galaxy.GetNebula(new Coordinate(0, 0))).IsEqualTo(NebulaType.Nebula);
        await Assert.That(galaxy.GetNebula(new Coordinate(1, 0))).IsEqualTo(NebulaType.Nebula);
        await Assert.That(galaxy.GetNebula(new Coordinate(0, 1))).IsEqualTo(NebulaType.Nebula);
        await Assert.That(galaxy.GetNebula(new Coordinate(1, 1))).IsEqualTo(NebulaType.Nebula);
        await Assert.That(galaxy.GetNebula(new Coordinate(2, 0))).IsEqualTo(NebulaType.None);
        await Assert.That(galaxy.GetNebula(new Coordinate(0, 2))).IsEqualTo(NebulaType.None);
    }

    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles))]
    [MethodDataSource(typeof(PascalGroundTruth.TrillumReservesCases), nameof(PascalGroundTruth.TrillumReservesCases.AsDataSource))]
    public async Task RandomTrillumReserves_MatchesGoldenFile(PascalGroundTruth.TrillumReservesCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("trillumreserves.golden");
        var setup = new GalaxySetup(new FixedRandom(c.RngFixedValue));
        var galaxy = new Galaxy(size: 10);
        var owner = new Empire { Name = "Terra" };

        var planet = setup.CreateWorld(galaxy, new Coordinate(0, 0), c.Class, TechLevel.Warp, WorldType.Mine, owner,
            population: 100, efficiency: 50, trillumReserveBase: c.RegionReserves, Ships(), Cargo(), Defenses());

        var expected = golden[c.Name];
        await Assert.That(planet.TrillumReserve).IsEqualTo(int.Parse(expected["reserves"]));
    }

    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles))]
    [MethodDataSource(typeof(PascalGroundTruth.RandomPlanetCases), nameof(PascalGroundTruth.RandomPlanetCases.AsDataSource))]
    public async Task CreateRndPlanet_MatchesGoldenFile(PascalGroundTruth.RandomPlanetCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("randomplanet.golden");
        var setup = new GalaxySetup(new FixedRandom(c.RngFixedValue));
        var galaxy = new Galaxy(size: 10);

        var planet = setup.CreateRndPlanet(galaxy, new Coordinate(4, 5), c.Class, c.Tech);

        var expected = golden[c.Name];
        await Assert.That(planet.Population).IsEqualTo(int.Parse(expected["population"]));
        await Assert.That(planet.Efficiency).IsEqualTo(int.Parse(expected["efficiency"]));
        await Assert.That(planet.Ships.Fighters).IsEqualTo(int.Parse(expected["fgt"]));
        await Assert.That(planet.Ships.HunterKillers).IsEqualTo(int.Parse(expected["hkr"]));
        await Assert.That(planet.Ships.Jumpships).IsEqualTo(int.Parse(expected["jmp"]));
        await Assert.That(planet.Ships.Jumptransports).IsEqualTo(int.Parse(expected["jtn"]));
        await Assert.That(planet.Ships.Penetrators).IsEqualTo(int.Parse(expected["pen"]));
        await Assert.That(planet.Ships.Starships).IsEqualTo(int.Parse(expected["ssp"]));
        await Assert.That(planet.Ships.Transports).IsEqualTo(int.Parse(expected["trn"]));
        await Assert.That(planet.Cargo.Legions).IsEqualTo(int.Parse(expected["cargomen"]));
        await Assert.That(planet.Cargo.Chemicals).IsEqualTo(int.Parse(expected["cargoche"]));
        await Assert.That(planet.Cargo.Metals).IsEqualTo(int.Parse(expected["cargomet"]));
        await Assert.That(planet.Cargo.Supplies).IsEqualTo(int.Parse(expected["cargosup"]));
        await Assert.That(planet.Cargo.Trillum).IsEqualTo(int.Parse(expected["cargotri"]));
        await Assert.That(planet.Defenses.Lams).IsEqualTo(int.Parse(expected["defLAM"]));
        await Assert.That(planet.Defenses.DefenseSatellites).IsEqualTo(int.Parse(expected["defDef"]));
        await Assert.That(planet.Defenses.Gdms).IsEqualTo(int.Parse(expected["defGDM"]));
        await Assert.That(planet.Defenses.IonCannons).IsEqualTo(int.Parse(expected["defIon"]));
    }

    /// <summary>Compares the whole painted grid as one string rather than per-cell asserts, so a mismatch shows a clear diff of both grids.</summary>
    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles))]
    [MethodDataSource(typeof(PascalGroundTruth.NebulaCases), nameof(PascalGroundTruth.NebulaCases.AsDataSource))]
    public async Task Nebula_MatchesGoldenFile(PascalGroundTruth.NebulaCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("nebula.golden");
        var setup = new GalaxySetup(new FixedRandom(c.RngFixedValue));
        var galaxy = new Galaxy(size: c.SizeOfGalaxy);

        if (c.Mode == 1)
            setup.NebulaeBand(galaxy);
        else
            setup.NebulaePatches(galaxy, c.PatchCount);

        var actualGrid = new char[c.SizeOfGalaxy * c.SizeOfGalaxy];
        for (var y = 0; y < c.SizeOfGalaxy; y++)
            for (var x = 0; x < c.SizeOfGalaxy; x++)
                actualGrid[y * c.SizeOfGalaxy + x] = galaxy.GetNebula(new Coordinate(x, y)) == NebulaType.Nebula ? '1' : '0';

        await Assert.That(new string(actualGrid)).IsEqualTo(golden[c.Name]["grid"]);
    }
}
