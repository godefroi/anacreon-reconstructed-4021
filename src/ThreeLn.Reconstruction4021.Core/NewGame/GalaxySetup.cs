using System.Collections.Frozen;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Turns;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.NewGame;

/// <summary>
/// World/starbase/stargate/mine/nebula placement at an explicit coordinate (NEWGAME.PAS's SetUpWorld
/// 885-922, CreateWorld 953-1025, CreateBase 1027-1098, CreateGate 1100-1120, CreateSRMs 1351-1370,
/// CreateNebula 1315-1331, plus RndShips/RndCargo/RndDefns 822-883 and RandomTrillumReserves 813-820
/// that SetUpWorld/CreateWorld themselves call). Randomized-*coordinate* placement
/// (GetRandomXY/CreateRndPlanet/CreateRandomWorlds) is a separate concern (Phase 2 commit 2d) built
/// on top of this one.
///
/// Scouting (Pascal's Scout(Emp,XY) call inside SetUpWorld) is deliberately not replicated here —
/// VisibilityHandler.RefreshVisibility already recomputes an empire's fog-of-war from scratch each
/// turn based on Owner/Location alone, so a newly placed, owned entity becomes visible the next time
/// it runs without needing an incremental "scout around this one new location" step. Special
/// (SetUpWorld's SetOfSpecialConditions parameter) is also skipped — every real NEWGAME.PAS call site
/// passes the empty set.
/// </summary>
public sealed class GalaxySetup(Random random)
{
    /// <summary>DATACNST.PAS:297-318 — average trillum reserves by WorldClass, feeding RandomTrillumReserves.</summary>
    private static readonly FrozenDictionary<WorldClass, int> _trillumReservesByClass = new Dictionary<WorldClass, int> {
        [WorldClass.Ambrosia] = 200, [WorldClass.Arid] = 350, [WorldClass.Artificial] = 50, [WorldClass.Barren] = 1200,
        [WorldClass.ClassJ] = 700, [WorldClass.ClassK] = 800, [WorldClass.ClassL] = 900, [WorldClass.ClassM] = 800,
        [WorldClass.Desert] = 1500, [WorldClass.EarthLike] = 800, [WorldClass.Forest] = 500, [WorldClass.GasGiant] = 450,
        [WorldClass.Hostile] = 500, [WorldClass.Ice] = 900, [WorldClass.Jungle] = 600, [WorldClass.Ocean] = 300,
        [WorldClass.Paradise] = 600, [WorldClass.Poisonous] = 350, [WorldClass.Ruins] = 200, [WorldClass.Underground] = 900,
        [WorldClass.Volcanic] = 1100,
    }.ToFrozenDictionary();

    /// <summary>
    /// NEWGAME.PAS:953-1025 (CreateWorld). Population gets its own ±15% jitter here (RndVar(Pp,15));
    /// SetUpWorld's own ships/cargo/defenses get a separate ±20% (see ApplySetup). CheckTech is always
    /// False here, matching Pascal's own CreateWorld call — only CreateRndPlanet (2d) passes True.
    /// </summary>
    public Planet CreateWorld(Galaxy.Galaxy galaxy, Coordinate location, WorldClass cls, TechLevel tech, WorldType type, Empire owner,
        int population, int efficiency, int trillumReserveBase, ShipCounts shipBase, CargoHold cargoBase, DefenseCounts defenseBase)
    {
        var planet = new Planet { Location = location, Class = cls };
        ((IEconomicWorld)planet).InitializeSelfSufficiency();

        ApplySetup(planet, tech, type, owner, PascalMath.Jitter(random, population, 15), efficiency,
            shipBase, cargoBase, defenseBase, checkTech: false);

        planet.TrillumReserve = RandomTrillumReserves(cls, trillumReserveBase);

        galaxy.Planets.Add(planet);
        if (type == WorldType.Capital)
            owner.Capital = planet;

        return planet;
    }

    /// <summary>
    /// NEWGAME.PAS:1027-1098 (CreateBase). <paramref name="explicitType"/> is only consulted for a
    /// kind besides Outpost/CommandBase/Fortress (NEWGAME.PAS:1079-1084's BaseTyp=out/IN[cmm,frt]
    /// branches cover those three explicitly; everything else — IndustrialComplex included — reads
    /// WorldTypes(T) straight from the scenario). Every real IndustrialComplex case in
    /// reference/scenarios/dos_131 happens to specify WorldType.Base too, but the mechanism itself is
    /// real, not hypothetical — don't hardcode Base for IndustrialComplex the way
    /// AnnualTickHandler.Construction.cs's own CreateStarbase does for a *completed construction site*
    /// (a different Pascal procedure, ConstructStarbase, with its own distinct, hardcoded logic).
    /// </summary>
    public Starbase CreateBase(Galaxy.Galaxy galaxy, Coordinate location, StarbaseKind kind, WorldType? explicitType, TechLevel tech, Empire owner,
        int population, int efficiency, ShipCounts shipBase, CargoHold cargoBase, DefenseCounts defenseBase)
    {
        var type = kind switch {
            StarbaseKind.Outpost => WorldType.Outpost,
            StarbaseKind.CommandBase or StarbaseKind.Fortress => WorldType.Base,
            _ => explicitType ?? throw new ArgumentException(
                $"{kind} needs an explicit world type — NEWGAME.PAS's CreateBase reads it straight from the scenario for any kind besides Outpost/CommandBase/Fortress.",
                nameof(explicitType)),
        };

        var starbase = new Starbase { Location = location, Kind = kind };
        ApplySetup(starbase, tech, type, owner, PascalMath.Jitter(random, population, 15), efficiency,
            shipBase, cargoBase, defenseBase, checkTech: false);

        galaxy.Starbases.Add(starbase);
        if (type == WorldType.Capital)
            owner.Capital = starbase;

        return starbase;
    }

    /// <summary>
    /// NEWGAME.PAS:885-922 (SetUpWorld) minus SetClass/GetCoord/Scout (see class doc comment) — Class
    /// is a planet-only concept in this port (IEconomicWorld.EffectiveClass is a Starbase's hardcoded
    /// Artificial), so callers that need it (CreateWorld) set Planet.Class themselves before this runs.
    /// </summary>
    private void ApplySetup(IEconomicWorld world, TechLevel tech, WorldType type, Empire owner, int population, int efficiency,
        ShipCounts shipBase, CargoHold cargoBase, DefenseCounts defenseBase, bool checkTech)
    {
        world.Type = type;
        world.Owner = owner;
        world.TechLevel = tech;
        world.Population = population;
        world.Efficiency = efficiency;

        var optimum = AnnualTickHandler.GetOptimumIndustry(world);
        foreach (var industry in Enum.GetValues<IndustryType>())
            world.Industry[industry] = optimum[industry];

        // RndShips/RndCargo/RndDefns (NEWGAME.PAS:822-883), each a ±20% jitter (ThgLmt(RndVar(N,20))),
        // zeroed if CheckTech and not yet unlocked at Tech. Every enum here is declared in the same
        // order Pascal iterates it in (confirmed for TechCatalog's own catalog ordering already), so
        // the RNG call order — and therefore golden-file parity — matches exactly.
        foreach (var ship in Enum.GetValues<ShipType>()) {
            var value = PascalMath.ClampResource(PascalMath.Jitter(random, shipBase[ship], 20));
            world.Ships[ship] = checkTech && TechCatalog.MinTechForShip[ship] > tech ? 0 : value;
        }
        foreach (var cargo in Enum.GetValues<CargoType>()) {
            var value = PascalMath.ClampResource(PascalMath.Jitter(random, cargoBase[cargo], 20));
            world.Cargo[cargo] = checkTech && TechCatalog.MinTechForCargo[cargo] > tech ? 0 : value;
        }

        var defenses = world switch {
            Planet p => p.Defenses,
            Starbase s => s.Defenses,
            _ => throw new ArgumentOutOfRangeException(nameof(world), world, "Only Planet/Starbase have Defenses — not part of IEconomicWorld itself."),
        };
        foreach (var defense in Enum.GetValues<DefenseType>()) {
            var value = PascalMath.ClampResource(PascalMath.Jitter(random, defenseBase[defense], 20));
            defenses[defense] = checkTech && TechCatalog.MinTechForDefense[defense] > tech ? 0 : value;
        }
    }

    /// <summary>NEWGAME.PAS:813-820 (RandomTrillumReserves).</summary>
    private int RandomTrillumReserves(WorldClass cls, int regionReserves)
    {
        var temp = Math.Max(regionReserves + PascalMath.Rnd(random, -25, 25), 0);
        return PascalMath.PascalRound(temp * (_trillumReservesByClass[cls] / 100.0) + PascalMath.Rnd(random, 1, 100));
    }

    /// <summary>NEWGAME.PAS:1100-1120 (CreateGate). No RNG — slot-allocation-failure (NextStargateSlot&lt;=0) has no C# equivalent, Galaxy.Stargates is an unbounded List.</summary>
    public static Stargate CreateGate(Galaxy.Galaxy galaxy, Coordinate location, StargateKind kind, Empire owner)
    {
        var stargate = new Stargate { Location = location, Owner = owner, Kind = kind, LinkedTo = null };
        galaxy.Stargates.Add(stargate);
        return stargate;
    }

    /// <summary>NEWGAME.PAS:1351-1370 (CreateSRMs) — only places a mine on a cell with nothing else in it. No RNG.</summary>
    public static void CreateSRMs(Galaxy.Galaxy galaxy, Coordinate upperLeft, Coordinate lowerRight, Empire owner)
    {
        for (var x = upperLeft.X; x <= lowerRight.X; x++)
        for (var y = upperLeft.Y; y <= lowerRight.Y; y++) {
            var location = new Coordinate(x, y);
            if (IsInGalaxy(galaxy, location) && !IsOccupied(galaxy, location))
                galaxy.SetMine(location, owner);
        }
    }

    /// <summary>NEWGAME.PAS:1315-1331 (CreateNebula) — paints unconditionally, no occupancy check. No RNG.</summary>
    public static void CreateNebula(Galaxy.Galaxy galaxy, Coordinate upperLeft, Coordinate lowerRight, NebulaType type)
    {
        for (var x = upperLeft.X; x <= lowerRight.X; x++)
        for (var y = upperLeft.Y; y <= lowerRight.Y; y++) {
            var location = new Coordinate(x, y);
            if (IsInGalaxy(galaxy, location))
                galaxy.SetNebula(location, type);
        }
    }

    /// <summary>
    /// MISC.PAS:111-117 (InGalaxy), adapted to this port's already-established 0-based [0,Size) bounds
    /// convention (e.g. AnnualTickHandler.Production.cs's neighbor-offset check) rather than Pascal's
    /// 1-based [1,SizeOfGalaxy].
    /// </summary>
    private static bool IsInGalaxy(Galaxy.Galaxy galaxy, Coordinate c) =>
        c.X >= 0 && c.X < galaxy.Size && c.Y >= 0 && c.Y < galaxy.Size;

    /// <summary>
    /// GetObject(XY,ObjID).ObjTyp&lt;&gt;Void — no permanent occupancy index exists (Galaxy's own doc
    /// comment defers that to the movement phase), so scan the handful of collections directly;
    /// CreateSRMs only ever checks a bounded explicit rectangle, not a whole galaxy, so a linear scan
    /// per cell is cheap here (unlike 2d's random-retry case, which needs a transient set instead).
    /// </summary>
    private static bool IsOccupied(Galaxy.Galaxy galaxy, Coordinate c) =>
        galaxy.Planets.Any(p => p.Location == c) || galaxy.Starbases.Any(s => s.Location == c) ||
        galaxy.Stargates.Any(g => g.Location == c) || galaxy.GetMineOwner(c) is not null;
}
