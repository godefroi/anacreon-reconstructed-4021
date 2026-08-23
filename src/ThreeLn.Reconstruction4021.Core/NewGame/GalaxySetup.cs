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
    /// DATACNST.PAS:273-294 — minimum tech level required to have a population on a given world
    /// class, feeding CreateRandomWorlds' class/tech-table retry loop.
    /// </summary>
    private static readonly FrozenDictionary<WorldClass, TechLevel> _minTechForClass = new Dictionary<WorldClass, TechLevel> {
        [WorldClass.Ambrosia] = TechLevel.PreTech, [WorldClass.Arid] = TechLevel.Primitive, [WorldClass.Artificial] = TechLevel.Jump, [WorldClass.Barren] = TechLevel.PreWarp,
        [WorldClass.ClassJ] = TechLevel.PreTech, [WorldClass.ClassK] = TechLevel.PreTech, [WorldClass.ClassL] = TechLevel.PreTech, [WorldClass.ClassM] = TechLevel.PreTech,
        [WorldClass.Desert] = TechLevel.Primitive, [WorldClass.EarthLike] = TechLevel.PreTech, [WorldClass.Forest] = TechLevel.PreTech, [WorldClass.GasGiant] = TechLevel.PreWarp,
        [WorldClass.Hostile] = TechLevel.PreAtomic, [WorldClass.Ice] = TechLevel.PreAtomic, [WorldClass.Jungle] = TechLevel.PreTech, [WorldClass.Ocean] = TechLevel.PreWarp,
        [WorldClass.Paradise] = TechLevel.PreTech, [WorldClass.Poisonous] = TechLevel.Atomic, [WorldClass.Ruins] = TechLevel.PreTech, [WorldClass.Underground] = TechLevel.Primitive,
        [WorldClass.Volcanic] = TechLevel.PreAtomic,
    }.ToFrozenDictionary();

    /// <summary>NEWGAME.PAS:929-931 — CreateRndPlanet's military-buildup scaling factor by tech level.</summary>
    private static readonly FrozenDictionary<TechLevel, double> _rndMilTechAdjustment = new Dictionary<TechLevel, double> {
        [TechLevel.PreTech] = 0.01, [TechLevel.Primitive] = 0.02, [TechLevel.PreAtomic] = 0.04, [TechLevel.Atomic] = 0.05,
        [TechLevel.PreWarp] = 0.10, [TechLevel.Warp] = 0.30, [TechLevel.Jump] = 0.35, [TechLevel.Bio] = 0.60,
        [TechLevel.Starship] = 0.75, [TechLevel.PreGate] = 0.95, [TechLevel.Gate] = 1.00,
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

    /// <summary>
    /// NEWGAME.PAS:234-256 (GetRandomXY) — picks a random point in [<paramref name="upperLeft"/>,
    /// <paramref name="lowerRight"/>], retrying up to 100 times when <paramref name="checkOccupancy"/>
    /// is set until it lands on an empty, unmined, non-dense-nebula cell. Pascal's own failure path
    /// sets XY to a sentinel and reports a non-fatal ScenaError, letting scenario loading continue;
    /// this port has no scenario-error-flag machinery (that's 2e's concern), so a real "no room left"
    /// failure throws instead of returning a value silently.
    /// </summary>
    public Coordinate GetRandomXY(Galaxy.Galaxy galaxy, Coordinate upperLeft, Coordinate lowerRight, bool checkOccupancy)
    {
        Coordinate xy;
        var count = 0;
        do {
            xy = new Coordinate(PascalMath.Rnd(random, upperLeft.X, lowerRight.X), PascalMath.Rnd(random, upperLeft.Y, lowerRight.Y));
            count++;
        } while (checkOccupancy && count <= 100 && !IsGoodForRandomWorld(galaxy, xy));

        if (count > 100)
            throw new InvalidOperationException("No room for random world in zone.");

        return xy;
    }

    /// <summary>
    /// GetRandomXY's own acceptance predicate (NEWGAME.PAS:246-249): GetObject(...).ObjTyp=Void AND
    /// EnemyMine(XY)=Indep AND GetNebula(XY)&lt;&gt;DenseNebula. The EnemyMine check is real Pascal's own
    /// mine-owner sentinel quirk, not a simplification: PRIMINTR.PAS packs a sector's mine owner into
    /// one nibble (Special div 16), and GALAXY.PAS:50/133 seeds every sector's default with
    /// NoSRMField=Ord(Indep)*16 — the same bit pattern PutMine writes for an explicitly Indep-owned
    /// mine. So "never mined" and "mined, but by Indep" are indistinguishable in real Pascal, and
    /// EnemyMine(XY)=Indep is true (passes) for both; only a mine owned by an actual player empire
    /// blocks placement. <see cref="IsPhysicallyOccupied"/> alone (no mine check at all) is
    /// CreateSRMs's own, different occupancy check — see its doc comment.
    /// </summary>
    private static bool IsGoodForRandomWorld(Galaxy.Galaxy galaxy, Coordinate c) =>
        !IsPhysicallyOccupied(galaxy, c) &&
        galaxy.GetMineOwner(c) is null or { IsIndependent: true } &&
        galaxy.GetNebula(c) != NebulaType.DenseNebula;

    /// <summary>
    /// NEWGAME.PAS:924-951 (CreateRndPlanet) — an independent, unowned world seeded from a random
    /// "military index" roll rather than scenario-supplied quantities. Doesn't set TrillumReserve
    /// itself, matching Pascal: its one real call site (CreateRandomWorlds) sets that separately right
    /// after, via RandomTrillumReserves.
    /// </summary>
    public Planet CreateRndPlanet(Galaxy.Galaxy galaxy, Coordinate location, WorldClass cls, TechLevel tech)
    {
        var planet = new Planet { Location = location, Class = cls };
        ((IEconomicWorld)planet).InitializeSelfSufficiency();

        var efficiency = PascalMath.Rnd(random, 40, 60);
        var basePop = (int)((1 + (efficiency - 50) / 500.0) * AnnualTickHandler.BasePopulationByTech[tech]);
        var population = PascalMath.Jitter(random, basePop, 10);

        var mi = PascalMath.Rnd(random, 1, 33) + PascalMath.Rnd(random, 1, 34) + PascalMath.Rnd(random, 1, 33);
        mi = PascalMath.PascalRound(mi * _rndMilTechAdjustment[tech]);

        var ships = new ShipCounts {
            Fighters = 80 * mi, HunterKillers = 7 * mi, Jumpships = 10 * mi, Jumptransports = 6 * mi,
            Penetrators = 4 * mi, Starships = mi, Transports = 30 * mi,
        };
        var cargo = new CargoHold { Legions = 40 * mi, Chemicals = 30 * mi, Metals = 50 * mi, Supplies = 25 * mi, Trillum = 10 * mi };
        var defenses = new DefenseCounts { DefenseSatellites = 30 * mi, Gdms = 50 * mi, IonCannons = 40 * mi };

        ApplySetup(planet, tech, WorldType.Independent, Empire.Independent, population, efficiency, ships, cargo, defenses, checkTech: true);

        galaxy.Planets.Add(planet);
        return planet;
    }

    /// <summary>
    /// NEWGAME.PAS:1122-1163 (CreateRandomWorlds). <paramref name="classTable"/>/<paramref name="techTable"/>
    /// stand in for Pascal's scenario-populated 100-entry percentile tables (CLASSTABLE/TECHTABLE
    /// commands) — building those from a .SCN file is 2e's job; this only needs the finished tables.
    /// </summary>
    public IReadOnlyList<Planet> CreateRandomWorlds(Galaxy.Galaxy galaxy, int count, Coordinate upperLeft, Coordinate lowerRight,
        IReadOnlyList<WorldClass> classTable, IReadOnlyList<TechLevel> techTable, int trillumReserveBase)
    {
        var planets = new List<Planet>(count);

        for (var i = 0; i < count; i++) {
            var coord = GetRandomXY(galaxy, upperLeft, lowerRight, checkOccupancy: true);

            WorldClass cls;
            TechLevel tech;
            var safety = 0;
            do {
                cls = classTable[PascalMath.Rnd(random, 1, 100) - 1];
                tech = techTable[PascalMath.Rnd(random, 1, 100) - 1];
                safety++;
            } while (safety <= 100 && tech < _minTechForClass[cls]);

            if (safety > 100)
                throw new InvalidOperationException("Incompatible class and tech tables.");

            var planet = CreateRndPlanet(galaxy, coord, cls, tech);
            planet.TrillumReserve = RandomTrillumReserves(cls, trillumReserveBase);
            planets.Add(planet);
        }

        return planets;
    }

    /// <summary>
    /// NEWGAME.PAS:1261-1291 (NebulaeBand) — a diagonal-drifting strip of plain Nebula across the
    /// whole galaxy. Pascal's own bounds/loop are 1-based against SizeOfGalaxy; this replicates that
    /// arithmetic verbatim (so the Rnd call sequence/values match real Pascal exactly) and only
    /// shifts to this port's 0-based Coordinate at the point of painting a cell.
    /// </summary>
    public void NebulaeBand(Galaxy.Galaxy galaxy)
    {
        var initX = PascalMath.Rnd(random, 1, galaxy.Size);
        var xDisp = initX <= galaxy.Size / 4 ? PascalMath.Rnd(random, 0, 3)
            : initX >= galaxy.Size * 3 / 4 ? PascalMath.Rnd(random, -3, 0)
            : PascalMath.Rnd(random, -3, 3);

        var startX = initX;
        for (var y = 1; y <= galaxy.Size; y++) {
            var low = startX - PascalMath.Rnd(random, 1, 5);
            var high = startX + PascalMath.Rnd(random, 1, 5);
            for (var x = low; x <= high; x++) {
                if (x >= 1 && x <= galaxy.Size)
                    galaxy.SetNebula(new Coordinate(x - 1, y - 1), NebulaType.Nebula);
            }

            startX += xDisp;
        }
    }

    /// <summary>NEWGAME.PAS:1293-1313 (NebulaePatches) — a scatter of diamond-shaped Nebula patches. See <see cref="NebulaeBand"/> for the 1-based-arithmetic/0-based-paint convention.</summary>
    public void NebulaePatches(Galaxy.Galaxy galaxy, int patchCount)
    {
        for (var patch = 0; patch < patchCount; patch++) {
            var initX = PascalMath.Rnd(random, 1, galaxy.Size);
            var initY = PascalMath.Rnd(random, 1, galaxy.Size);

            var yLow = initY - PascalMath.Rnd(random, 1, 3);
            var yHigh = initY + PascalMath.Rnd(random, 1, 3);
            for (var y = yLow; y <= yHigh; y++) {
                var spread = 4 - Math.Abs(y - initY);
                var xLow = initX - PascalMath.Rnd(random, 1, spread);
                var xHigh = initX + PascalMath.Rnd(random, 1, spread);
                for (var x = xLow; x <= xHigh; x++) {
                    if (x >= 1 && x <= galaxy.Size && y >= 1 && y <= galaxy.Size)
                        galaxy.SetNebula(new Coordinate(x - 1, y - 1), NebulaType.Nebula);
                }
            }
        }
    }

    /// <summary>NEWGAME.PAS:1100-1120 (CreateGate). No RNG — slot-allocation-failure (NextStargateSlot&lt;=0) has no C# equivalent, Galaxy.Stargates is an unbounded List.</summary>
    public static Stargate CreateGate(Galaxy.Galaxy galaxy, Coordinate location, StargateKind kind, Empire owner)
    {
        var stargate = new Stargate { Location = location, Owner = owner, Kind = kind, LinkedTo = null };
        galaxy.Stargates.Add(stargate);
        return stargate;
    }

    /// <summary>
    /// NEWGAME.PAS:1351-1370 (CreateSRMs) — only places a mine on a cell with nothing else in it. No
    /// RNG. Its occupancy check is GetObject(...).ObjTyp=Void alone (PRIMINTR.PAS:202-206) — unlike
    /// GetRandomXY's own occupancy check (see IsGoodForRandomWorld), it does not consult EnemyMine at
    /// all, so re-mining an already-mined-but-otherwise-empty cell is real, allowed Pascal behavior,
    /// not an oversight — matches PutMine's own body (PRIMINTR.PAS:187-191), which unconditionally
    /// overwrites with no existing-mine guard.
    /// </summary>
    public static void CreateSRMs(Galaxy.Galaxy galaxy, Coordinate upperLeft, Coordinate lowerRight, Empire owner)
    {
        for (var x = upperLeft.X; x <= lowerRight.X; x++)
        for (var y = upperLeft.Y; y <= lowerRight.Y; y++) {
            var location = new Coordinate(x, y);
            if (IsInGalaxy(galaxy, location) && !IsPhysicallyOccupied(galaxy, location))
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
    /// comment defers that to the movement phase), so scan the handful of collections directly; every
    /// call site only ever checks a bounded explicit rectangle, not a whole galaxy, so a linear scan
    /// per cell is cheap here (unlike 2d's random-retry case, which needs a transient set instead).
    /// Deliberately does not check Galaxy.Fleets — real ObjectTypes includes Flt, but nothing in
    /// scenario loading (2e) ever creates one before this runs (confirmed: NEWGAME.PAS never spawns a
    /// starting fleet, per this phase's own "explicitly out of scope" design note), so it can't be
    /// reachably non-empty here.
    /// </summary>
    private static bool IsPhysicallyOccupied(Galaxy.Galaxy galaxy, Coordinate c) =>
        galaxy.Planets.Any(p => p.Location == c) || galaxy.Starbases.Any(s => s.Location == c) ||
        galaxy.Stargates.Any(g => g.Location == c);
}
