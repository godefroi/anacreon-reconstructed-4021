using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.NewGame;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests;

/// <summary>
/// NEWGAME.PAS:885-1120,1315-1370 (SetUpWorld/CreateWorld/CreateBase/CreateGate/CreateSRMs/
/// CreateNebula). Hardcoded, not golden-file-backed yet: SetUpWorld/RndShips/RndCargo/RndDefns/
/// RandomTrillumReserves live in NEWGAME.PAS, which pulls in a much larger USES clause
/// (Crt/Dos/DOS2/EIO/WND/Menu/DFA/LoadSave/NPE/NPETypes) than any patch so far has needed to reach
/// into. Relocating just those five procedures (the same "small formula, not the whole unit"
/// technique already used for GetIndustrialDistribution/empirecreate) is deferred to Phase 2 commit
/// 2d, which needs the exact same relocation anyway — CreateRndPlanet, 2d's own subject, calls
/// SetUpWorld too — so doing it once there covers both commits' cases instead of twice.
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
}
