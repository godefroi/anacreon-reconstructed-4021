using System.Collections.Frozen;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Turns;

/// <summary>
/// Runs the annual economy tick (UPDATE.PAS:UpdateUniverse). Commit 1 covered population growth,
/// efficiency, food consumption, and revolution/rebellion for planets. Commit 2 added raw material
/// and ship/cargo production for planets (plus 2b/2c, ambrosia addiction and military buildup).
/// Commit 3 (this one) adds tech-level advancement/regression. Starbase economy and construction/
/// empire-level updates are later commits (see docs/ROADMAP.md and the insertion-point map on
/// <see cref="UpdateWorld"/>). HostileLife shipped early with Commit 1 (it's cheap and sits right
/// after UpdateRevolution in Pascal) even though it isn't its own roadmap line.
/// </summary>
public sealed class AnnualTickHandler(Random random) : IAnnualTickHandler
{
    private const int MaxResources = 9999;
    private const int SuppliesPerBillion = 25;

    // TIP (Total Industrial Production) constants (DATACNST.PAS:24-26,31,33,37,40). Named after the
    // Pascal identifiers since they're bare balance knobs there too, not variables with better names.
    private const double K1 = 1.76;
    private const double K2 = 10;
    private const double K3 = 0.75;

    /// <summary>Absolute-production constant. Always 0 in the shipped balance data, but Pascal keeps
    /// every term it multiplies/adds visible rather than hand-simplifying them away, so this
    /// translation does too (DATACNST.PAS:31).</summary>
    private const double K4 = 0.0;

    private const double K6 = 11000.0;
    private const double SafetyAdj = 1.05;
    private const double AmbrosiaAdj = 1.45;

    // Ambrosia addiction constants (DATACNST.PAS:41-45).
    private const double DrugsPerBillion = 11.5;
    private const int ChanceToAddict = 25;
    private const double AddictDeathCoeff = 0.12;
    private const double AddictEffCoeff = 0.9;
    private const double AddictRevICoeff = 0.55;

    /// <summary>% chance a world advances toward its empire's capital tech level each tick (DATACNST.PAS:61).</summary>
    private const int TechLevelIncreaseChance = 16;

    private static readonly FrozenDictionary<WorldClass, int> _maxPopulationByClass = new Dictionary<WorldClass, int> {
        [WorldClass.Ambrosia] = 4830,
        [WorldClass.Arid] = 4100,
        [WorldClass.Artificial] = 3100,
        [WorldClass.Barren] = 2340,
        [WorldClass.ClassJ] = 4610,
        [WorldClass.ClassK] = 4600,
        [WorldClass.ClassL] = 4220,
        [WorldClass.ClassM] = 4800,
        [WorldClass.Desert] = 3580,
        [WorldClass.EarthLike] = 4500,
        [WorldClass.Forest] = 4710,
        [WorldClass.GasGiant] = 2010,
        [WorldClass.Hostile] = 4010,
        [WorldClass.Ice] = 1920,
        [WorldClass.Jungle] = 4720,
        [WorldClass.Ocean] = 3520,
        [WorldClass.Paradise] = 5000,
        [WorldClass.Poisonous] = 2100,
        [WorldClass.Ruins] = 4590,
        [WorldClass.Underground] = 4500,
        [WorldClass.Volcanic] = 3950,
    }.ToFrozenDictionary();

    /// <summary>Average population at 50% efficiency, by tech level (DATACNST.PAS:221-223).</summary>
    private static readonly FrozenDictionary<TechLevel, int> _basePopulationByTech = new Dictionary<TechLevel, int> {
        [TechLevel.PreTech] = 3,
        [TechLevel.Primitive] = 10,
        [TechLevel.PreAtomic] = 100,
        [TechLevel.Atomic] = 250,
        [TechLevel.PreWarp] = 500,
        [TechLevel.Warp] = 700,
        [TechLevel.Jump] = 1100,
        [TechLevel.Bio] = 1700,
        [TechLevel.Starship] = 2000,
        [TechLevel.PreGate] = 2500,
        [TechLevel.Gate] = 3000,
    }.ToFrozenDictionary();

    /// <summary>
    /// Starvation-driven revolution-index sensitivity by tech level: low-tech worlds are used to
    /// starving and revolt less readily than high-tech ones (UPDATE.PAS:1126-1128, a table local to
    /// UseUpFood in Pascal — distinct from the global TechAdj used below by TotalProd).
    /// </summary>
    private static readonly FrozenDictionary<TechLevel, int> _starvationRevoltAdjustmentByTech = new Dictionary<TechLevel, int> {
        [TechLevel.PreTech] = 100,
        [TechLevel.Primitive] = 100,
        [TechLevel.PreAtomic] = 30,
        [TechLevel.Atomic] = 20,
        [TechLevel.PreWarp] = 15,
        [TechLevel.Warp] = 13,
        [TechLevel.Jump] = 12,
        [TechLevel.Bio] = 13,
        [TechLevel.Starship] = 14,
        [TechLevel.PreGate] = 14,
        [TechLevel.Gate] = 15,
    }.ToFrozenDictionary();

    /// <summary>% of population in military at 50 military index, by world type (DATACNST.PAS:351-356).</summary>
    private static readonly FrozenDictionary<WorldType, int> _optimumMilitaryByType = new Dictionary<WorldType, int> {
        [WorldType.Agricultural] = 5,
        [WorldType.Ambrosia] = 100,
        [WorldType.Base] = 200,
        [WorldType.BaseStarbase] = 200,
        [WorldType.Capital] = 200,
        [WorldType.Chemical] = 10,
        [WorldType.Independent] = 80,
        [WorldType.JumpshipBase] = 100,
        [WorldType.JumpshipBaseStarbase] = 100,
        [WorldType.Mine] = 20,
        [WorldType.NinjaWorld] = 150,
        [WorldType.Outpost] = 0,
        [WorldType.RawMaterialMine] = 20,
        [WorldType.RawMaterialMineStarbase] = 20,
        [WorldType.StarshipBase] = 150,
        [WorldType.StarshipBaseStarbase] = 150,
        [WorldType.TransportBase] = 80,
        [WorldType.TransportBaseStarbase] = 80,
        [WorldType.University] = 1,
        [WorldType.Terraform] = 10,
        [WorldType.TrillumMine] = 30,
    }.ToFrozenDictionary();

    /// <summary>TIP adjustment by tech level, used only by TotalProd (DATACNST.PAS:226-228 — the
    /// global TechAdj, distinct from UseUpFood's same-named local table above).</summary>
    private static readonly FrozenDictionary<TechLevel, int> _totalProductionTechAdjustment = new Dictionary<TechLevel, int> {
        [TechLevel.PreTech] = 25,
        [TechLevel.Primitive] = 40,
        [TechLevel.PreAtomic] = 49,
        [TechLevel.Atomic] = 57,
        [TechLevel.PreWarp] = 66,
        [TechLevel.Warp] = 80,
        [TechLevel.Jump] = 85,
        [TechLevel.Bio] = 90,
        [TechLevel.Starship] = 94,
        [TechLevel.PreGate] = 97,
        [TechLevel.Gate] = 100,
    }.ToFrozenDictionary();

    /// <summary>Production adjustment by tech level: TechAdj2, used by the IP/Alpha formulas (DATACNST.PAS:231-233).</summary>
    private static readonly FrozenDictionary<TechLevel, int> _industrialProductionTechAdjustment = new Dictionary<TechLevel, int> {
        [TechLevel.PreTech] = 12,
        [TechLevel.Primitive] = 24,
        [TechLevel.PreAtomic] = 36,
        [TechLevel.Atomic] = 47,
        [TechLevel.PreWarp] = 58,
        [TechLevel.Warp] = 67,
        [TechLevel.Jump] = 76,
        [TechLevel.Bio] = 84,
        [TechLevel.Starship] = 90,
        [TechLevel.PreGate] = 95,
        [TechLevel.Gate] = 100,
    }.ToFrozenDictionary();

    /// <summary>Units of metal needed per 100 points of industrial development: NewIndRawN (DATACNST.PAS:450-452).</summary>
    private static readonly FrozenDictionary<IndustryType, int> _industryMetalCost = new Dictionary<IndustryType, int> {
        [IndustryType.Bioindustry] = 100,
        [IndustryType.Chemical] = 500,
        [IndustryType.Mining] = 100,
        [IndustryType.ShipyardGeneral] = 1900,
        [IndustryType.ShipyardJump] = 1200,
        [IndustryType.ShipyardStarship] = 1500,
        [IndustryType.ShipyardTransport] = 1000,
        [IndustryType.Supply] = 0,
        [IndustryType.TrillumMining] = 300,
    }.ToFrozenDictionary();

    /// <summary>% of industry that is effective, by world class and industry: ClassIndAdj (DATACNST.PAS:321-343).</summary>
    private static readonly FrozenDictionary<(WorldClass, IndustryType), int> _classIndustryAdjustment =
        BuildClassIndustryAdjustment();

    private static FrozenDictionary<(WorldClass, IndustryType), int> BuildClassIndustryAdjustment()
    {
        var industries = Enum.GetValues<IndustryType>();
        var table = new Dictionary<(WorldClass, IndustryType), int>();

        void Row(WorldClass cls, params int[] values)
        {
            for (var i = 0; i < industries.Length; i++)
                table[(cls, industries[i])] = values[i];
        }

        //        Bio Che Min SYG SYJ SYS SYT Sup Tri
        Row(WorldClass.Ambrosia, 100, 100, 75, 100, 100, 100, 100, 100, 75);
        Row(WorldClass.Arid, 100, 80, 100, 100, 100, 100, 100, 85, 100);
        Row(WorldClass.Artificial, 100, 40, 40, 250, 200, 300, 300, 40, 40);
        Row(WorldClass.Barren, 100, 60, 175, 100, 100, 100, 100, 40, 175);
        Row(WorldClass.ClassJ, 100, 120, 120, 100, 100, 100, 100, 100, 90);
        Row(WorldClass.ClassK, 100, 90, 120, 100, 100, 100, 100, 100, 100);
        Row(WorldClass.ClassL, 100, 100, 100, 100, 100, 100, 100, 90, 120);
        Row(WorldClass.ClassM, 100, 100, 100, 100, 100, 100, 100, 120, 90);
        Row(WorldClass.Desert, 100, 60, 80, 100, 100, 100, 100, 60, 190);
        Row(WorldClass.EarthLike, 100, 100, 100, 100, 100, 100, 100, 100, 100);
        Row(WorldClass.Forest, 100, 120, 100, 100, 100, 100, 100, 145, 100);
        Row(WorldClass.GasGiant, 100, 150, 50, 150, 125, 175, 150, 40, 60);
        Row(WorldClass.Hostile, 100, 100, 100, 100, 100, 100, 100, 100, 100);
        Row(WorldClass.Ice, 100, 90, 80, 100, 100, 100, 100, 60, 80);
        Row(WorldClass.Jungle, 100, 130, 100, 100, 100, 100, 100, 125, 100);
        Row(WorldClass.Ocean, 100, 135, 40, 100, 100, 100, 100, 130, 40);
        Row(WorldClass.Paradise, 120, 150, 125, 100, 100, 100, 100, 200, 150);
        Row(WorldClass.Poisonous, 100, 200, 80, 100, 100, 100, 100, 40, 80);
        Row(WorldClass.Ruins, 100, 100, 100, 100, 100, 100, 100, 100, 100);
        Row(WorldClass.Underground, 100, 100, 150, 100, 100, 100, 100, 70, 125);
        Row(WorldClass.Volcanic, 100, 125, 175, 100, 100, 100, 100, 75, 150);

        return table.ToFrozenDictionary();
    }

    /// <summary>
    /// How a world type's industry is distributed: TypeData (DATACNST.PAS:469-491). Sup is calculated
    /// first from population; the remaining columns are either "% of what's left after Sup" (worlds
    /// with no principal industry) or "max setting for the principal industry" (worlds with one) —
    /// see the branch in <see cref="GetIndustrialDistribution"/>.
    /// </summary>
    private static readonly FrozenDictionary<(WorldType, IndustryType), double> _typeData = BuildTypeData();

    private static FrozenDictionary<(WorldType, IndustryType), double> BuildTypeData()
    {
        var industries = Enum.GetValues<IndustryType>();
        var table = new Dictionary<(WorldType, IndustryType), double>();

        void Row(WorldType typ, params double[] values)
        {
            for (var i = 0; i < industries.Length; i++)
                table[(typ, industries[i])] = values[i];
        }

        //        Bio  Che  Min  SYG  SYJ  SYS  SYT  Sup  Tri
        Row(WorldType.Agricultural, 0, 40, 40, 0, 0, 0, 0, 10.0, 20);
        Row(WorldType.Ambrosia, 100, 1.2, 5.0, 0, 0, 0, 0, 1.1, 5.0);
        Row(WorldType.Base, 0, 1.2, 1.7, 100, 0, 0, 0, 1.1, 1.7);
        Row(WorldType.BaseStarbase, 0, 0.5, 0.5, 100, 0, 0, 0, 0.5, 0.5);
        Row(WorldType.Capital, 0, 2.0, 2.5, 100, 0, 0, 0, 1.5, 2.0);
        Row(WorldType.Chemical, 0, 80, 10, 0, 0, 0, 0, 1.1, 10);
        Row(WorldType.Independent, 0, 2.0, 2.5, 100, 0, 0, 0, 1.5, 2.0);
        Row(WorldType.JumpshipBase, 0, 1.2, 1.7, 0, 100, 0, 0, 1.1, 1.7);
        Row(WorldType.JumpshipBaseStarbase, 0, 0.5, 0.5, 0, 100, 0, 0, 0.5, 0.5);
        Row(WorldType.Mine, 0, 10, 80, 0, 0, 0, 0, 1.1, 10);
        Row(WorldType.NinjaWorld, 100, 1.2, 5.0, 0, 0, 0, 0, 1.1, 5.0);
        Row(WorldType.Outpost, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        Row(WorldType.RawMaterialMine, 0, 40, 40, 0, 0, 0, 0, 1.1, 20);
        Row(WorldType.RawMaterialMineStarbase, 0, 40, 40, 0, 0, 0, 0, 0.5, 20);
        Row(WorldType.StarshipBase, 0, 1.2, 1.3, 0, 0, 100, 0, 1.1, 1.2);
        Row(WorldType.StarshipBaseStarbase, 0, 0.5, 0.5, 0, 0, 100, 0, 0.5, 0.5);
        Row(WorldType.TransportBase, 0, 1.2, 1.7, 0, 0, 0, 100, 1.1, 1.2);
        Row(WorldType.TransportBaseStarbase, 0, 0.5, 0.5, 0, 0, 0, 100, 0.5, 0.5);
        Row(WorldType.University, 0, 10, 10, 0, 0, 0, 0, 1.1, 5);
        Row(WorldType.Terraform, 0, 30, 10, 0, 0, 0, 0, 1.1, 20);
        Row(WorldType.TrillumMine, 0, 10, 10, 0, 0, 0, 0, 1.1, 80);

        return table.ToFrozenDictionary();
    }

    /// <summary>Principal (population-scaling) industry per world type: PrincipalIndustry (DATACNST.PAS:493-514).</summary>
    private static readonly FrozenDictionary<WorldType, IndustryType> _principalIndustry = new Dictionary<WorldType, IndustryType> {
        [WorldType.Agricultural] = IndustryType.Supply,
        [WorldType.Ambrosia] = IndustryType.Bioindustry,
        [WorldType.Base] = IndustryType.ShipyardGeneral,
        [WorldType.BaseStarbase] = IndustryType.ShipyardGeneral,
        [WorldType.Capital] = IndustryType.ShipyardGeneral,
        [WorldType.Chemical] = IndustryType.Chemical,
        [WorldType.Independent] = IndustryType.ShipyardGeneral,
        [WorldType.JumpshipBase] = IndustryType.ShipyardJump,
        [WorldType.JumpshipBaseStarbase] = IndustryType.ShipyardJump,
        [WorldType.Mine] = IndustryType.Mining,
        [WorldType.NinjaWorld] = IndustryType.Bioindustry,
        [WorldType.Outpost] = IndustryType.ShipyardGeneral,
        [WorldType.RawMaterialMine] = IndustryType.Mining,
        [WorldType.RawMaterialMineStarbase] = IndustryType.Mining,
        [WorldType.StarshipBase] = IndustryType.ShipyardStarship,
        [WorldType.StarshipBaseStarbase] = IndustryType.ShipyardStarship,
        [WorldType.TransportBase] = IndustryType.ShipyardTransport,
        [WorldType.TransportBaseStarbase] = IndustryType.ShipyardTransport,
        [WorldType.University] = IndustryType.Mining,
        [WorldType.Terraform] = IndustryType.Mining,
        [WorldType.TrillumMine] = IndustryType.TrillumMining,
    }.ToFrozenDictionary();

    /// <summary>
    /// World types with no principal industry (TypeData's PI columns are all zero): the remainder
    /// after Sup industry is split by fixed percentage instead of solved for via Gamma/Beta
    /// (INTRFACE.PAS:292-303).
    /// </summary>
    private static readonly FrozenSet<WorldType> _rawMaterialOnlyTypes = new HashSet<WorldType> {
        WorldType.Agricultural,
        WorldType.Chemical,
        WorldType.Mine,
        WorldType.RawMaterialMine,
        WorldType.RawMaterialMineStarbase,
        WorldType.University,
        WorldType.Terraform,
        WorldType.TrillumMine,
    }.ToFrozenSet();

    /// <summary>
    /// Gamma[CheInd/MinInd/TriInd, MainInd] (INTRFACE.PAS:230-240), keyed by the principal industry —
    /// the only column values ever read, since MainInd is always Bio/SYG/SYJ/SYS/SYT.
    /// </summary>
    private static readonly FrozenDictionary<IndustryType, double> _gammaChemical = new Dictionary<IndustryType, double> {
        [IndustryType.Bioindustry] = 1.12857,
        [IndustryType.ShipyardGeneral] = 0.19143,
        [IndustryType.ShipyardJump] = 0.50000,
        [IndustryType.ShipyardStarship] = 0.36714,
        [IndustryType.ShipyardTransport] = 0.25286,
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<IndustryType, double> _gammaMining = new Dictionary<IndustryType, double> {
        [IndustryType.Bioindustry] = 0.10000,
        [IndustryType.ShipyardGeneral] = 0.30514,
        [IndustryType.ShipyardJump] = 0.27286,
        [IndustryType.ShipyardStarship] = 0.53714,
        [IndustryType.ShipyardTransport] = 0.83571,
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<IndustryType, double> _gammaTrillum = new Dictionary<IndustryType, double> {
        [IndustryType.Bioindustry] = 0.10000,
        [IndustryType.ShipyardGeneral] = 0.07627,
        [IndustryType.ShipyardJump] = 0.20400,
        [IndustryType.ShipyardStarship] = 0.16667,
        [IndustryType.ShipyardTransport] = 0.08000,
    }.ToFrozenDictionary();

    /// <summary>
    /// ThgAdj[IndusTypes,che/met/sup/tri] (DATACNST.PAS:409-421) — the raw-material-producing slice
    /// of the table, consumed by <see cref="ProduceRawMaterial"/>. Only nonzero cells are listed;
    /// every other (industry,cargo) pair produces nothing.
    /// </summary>
    private static readonly FrozenDictionary<(IndustryType, CargoType), int> _thgAdjRawMaterial = new Dictionary<(IndustryType, CargoType), int> {
        [(IndustryType.Chemical, CargoType.Chemicals)] = 175,
        [(IndustryType.Mining, CargoType.Metals)] = 350,
        [(IndustryType.Supply, CargoType.Supplies)] = 320,
        [(IndustryType.TrillumMining, CargoType.Trillum)] = 75,
    }.ToFrozenDictionary();

    /// <summary>
    /// ThgAdj[IndusTypes,men/nnj/amb] (DATACNST.PAS:409-421) — the cargo-producing slice of the
    /// table, consumed by <see cref="Production"/>. men is never nonzero for any industry in the
    /// shipped data, so it has no entries here.
    /// </summary>
    private static readonly FrozenDictionary<(IndustryType, CargoType), int> _thgAdjCargoProduction = new Dictionary<(IndustryType, CargoType), int> {
        [(IndustryType.Bioindustry, CargoType.NinjaLegion)] = 10,
        [(IndustryType.Bioindustry, CargoType.Ambrosia)] = 175,
    }.ToFrozenDictionary();

    /// <summary>ThgAdj[IndusTypes,fgt..trn] (DATACNST.PAS:409-421) — the ship-producing slice, consumed by <see cref="Production"/>.</summary>
    private static readonly FrozenDictionary<(IndustryType, ShipType), int> _thgAdjShips = new Dictionary<(IndustryType, ShipType), int> {
        [(IndustryType.ShipyardGeneral, ShipType.Fighter)] = 27,
        [(IndustryType.ShipyardGeneral, ShipType.HunterKiller)] = 5,
        [(IndustryType.ShipyardGeneral, ShipType.Jumpship)] = 9,
        [(IndustryType.ShipyardGeneral, ShipType.Jumptransport)] = 5,
        [(IndustryType.ShipyardGeneral, ShipType.Penetrator)] = 4,
        [(IndustryType.ShipyardGeneral, ShipType.Starship)] = 2,
        [(IndustryType.ShipyardGeneral, ShipType.Transport)] = 10,
        [(IndustryType.ShipyardJump, ShipType.HunterKiller)] = 25,
        [(IndustryType.ShipyardJump, ShipType.Jumpship)] = 50,
        [(IndustryType.ShipyardJump, ShipType.Jumptransport)] = 30,
        [(IndustryType.ShipyardStarship, ShipType.Penetrator)] = 40,
        [(IndustryType.ShipyardStarship, ShipType.Starship)] = 15,
        [(IndustryType.ShipyardTransport, ShipType.Fighter)] = 75,
        [(IndustryType.ShipyardTransport, ShipType.Transport)] = 45,
    }.ToFrozenDictionary();

    /// <summary>RawM[ShipTypes,CargoTypes] (DATACNST.PAS:426-446) — units of raw material per 100 units built.</summary>
    private static readonly FrozenDictionary<ShipType, FrozenDictionary<CargoType, int>> _rawMaterialForShips = new Dictionary<ShipType, FrozenDictionary<CargoType, int>> {
        [ShipType.Fighter] = RawMaterialRow((CargoType.Chemicals, 5), (CargoType.Metals, 30), (CargoType.Trillum, 2)),
        [ShipType.HunterKiller] = RawMaterialRow((CargoType.Chemicals, 100), (CargoType.Metals, 110), (CargoType.Trillum, 18)),
        [ShipType.Jumpship] = RawMaterialRow((CargoType.Chemicals, 65), (CargoType.Metals, 70), (CargoType.Trillum, 12)),
        [ShipType.Jumptransport] = RawMaterialRow((CargoType.Chemicals, 100), (CargoType.Metals, 110), (CargoType.Trillum, 16)),
        [ShipType.Penetrator] = RawMaterialRow((CargoType.Chemicals, 95), (CargoType.Metals, 275), (CargoType.Trillum, 20)),
        [ShipType.Starship] = RawMaterialRow((CargoType.Chemicals, 175), (CargoType.Metals, 520), (CargoType.Trillum, 30)),
        [ShipType.Transport] = RawMaterialRow((CargoType.Chemicals, 90), (CargoType.Metals, 600), (CargoType.Trillum, 10)),
    }.ToFrozenDictionary();

    /// <summary>RawM[men/nnj/amb,CargoTypes] (DATACNST.PAS:426-446). men has no entries — its row is all zero.</summary>
    private static readonly FrozenDictionary<CargoType, FrozenDictionary<CargoType, int>> _rawMaterialForCargoProducts = new Dictionary<CargoType, FrozenDictionary<CargoType, int>> {
        [CargoType.NinjaLegion] = RawMaterialRow((CargoType.Ambrosia, 100), (CargoType.Chemicals, 50)),
        [CargoType.Ambrosia] = RawMaterialRow((CargoType.Chemicals, 110)),
    }.ToFrozenDictionary();

    private static FrozenDictionary<CargoType, int> RawMaterialRow(params (CargoType material, int amount)[] entries) =>
        entries.ToFrozenDictionary(e => e.material, e => e.amount);

    /// <summary>
    /// Minimum tech level at which each cargo type appears in TechDev (DATACNST.PAS:359-370). TechDev
    /// is monotonically increasing in TechLevel — every level's set is a superset of the previous
    /// one's — so "first tech level it appears at" is equivalent to full set membership at every
    /// later level, and much smaller to state than replicating all 11 sets.
    /// </summary>
    private static readonly FrozenDictionary<CargoType, TechLevel> _minTechForCargo = new Dictionary<CargoType, TechLevel> {
        [CargoType.Supplies] = TechLevel.PreTech,
        [CargoType.Legion] = TechLevel.Primitive,
        [CargoType.Metals] = TechLevel.Primitive,
        [CargoType.Chemicals] = TechLevel.PreAtomic,
        [CargoType.Trillum] = TechLevel.Atomic,
        [CargoType.Ambrosia] = TechLevel.Bio,
        [CargoType.NinjaLegion] = TechLevel.Starship,
    }.ToFrozenDictionary();

    /// <summary>Minimum tech level at which each ship type appears in TechDev (DATACNST.PAS:359-370). See <see cref="_minTechForCargo"/>.</summary>
    private static readonly FrozenDictionary<ShipType, TechLevel> _minTechForShip = new Dictionary<ShipType, TechLevel> {
        [ShipType.Fighter] = TechLevel.PreWarp,
        [ShipType.Transport] = TechLevel.Warp,
        [ShipType.Jumpship] = TechLevel.Jump,
        [ShipType.Jumptransport] = TechLevel.Jump,
        [ShipType.HunterKiller] = TechLevel.Bio,
        [ShipType.Penetrator] = TechLevel.Bio,
        [ShipType.Starship] = TechLevel.Starship,
    }.ToFrozenDictionary();

    /// <summary>ISSP: how far over/under self-sufficient an industry's dial is set (DATACNST.PAS:524-525).</summary>
    private static readonly double[] _issp = [0.01, 0.10, 0.25, 0.50, 0.75, 1.00, 1.50, 2.00, 3.00, 4.00, 5.00];

    /// <summary>World types SupplyLink/SurplusLink treat as raw-material sources (UPDATE.PAS:541-542,587-588's inline set) — distinct from <see cref="_rawMaterialOnlyTypes"/>, which serves GetIndustrialDistribution and includes types (University, Terraform, and every *Starbase-suffixed type) this set doesn't.</summary>
    private static readonly FrozenSet<WorldType> _supplyLinkEligibleTypes = new HashSet<WorldType> {
        WorldType.Agricultural, WorldType.Chemical, WorldType.Mine, WorldType.RawMaterialMine, WorldType.TrillumMine,
    }.ToFrozenSet();

    /// <summary>The 8 compass directions (DirX/DirY, DATACNST.PAS), excluding the center — SupplyLink/SurplusLink never transfer with a starbase's own sector.</summary>
    private static readonly (int dx, int dy)[] _eightNeighborOffsets = [
        (-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1),
    ];

    // Loop ranges matching Pascal's subrange FOR loops, in enum declaration order.
    private static readonly IndustryType[] _rawMaterialIndustries = [
        IndustryType.Chemical, IndustryType.Mining, IndustryType.ShipyardGeneral, IndustryType.ShipyardJump,
        IndustryType.ShipyardStarship, IndustryType.ShipyardTransport, IndustryType.Supply, IndustryType.TrillumMining,
    ]; // CheInd TO TriInd
    private static readonly CargoType[] _rawMaterialCargoTypes = [CargoType.Chemicals, CargoType.Metals, CargoType.Supplies, CargoType.Trillum]; // che TO tri
    private static readonly IndustryType[] _productionIndustries = [
        IndustryType.Bioindustry, IndustryType.Chemical, IndustryType.Mining, IndustryType.ShipyardGeneral,
        IndustryType.ShipyardJump, IndustryType.ShipyardStarship, IndustryType.ShipyardTransport,
    ]; // BioInd TO SYTInd
    private static readonly CargoType[] _productionCargoTypes = [CargoType.Legion, CargoType.NinjaLegion, CargoType.Ambrosia]; // men TO amb
    private static readonly ShipType[] _allShipTypes = Enum.GetValues<ShipType>(); // fgt TO trn
    private static readonly CargoType[] _rawMaterialCheckOrder = [CargoType.Ambrosia, CargoType.Chemicals, CargoType.Metals, CargoType.Supplies, CargoType.Trillum]; // amb TO tri
    private static readonly CargoType[] _rawMaterialDeductOrder = [CargoType.Chemicals, CargoType.Metals, CargoType.Supplies, CargoType.Trillum]; // che TO tri — amb is checked but never deducted, see ApplyRawMaterialConstraint

    public void RunAnnualTick(Game game)
    {
        game.Year++;

        // Per-tick scratch accumulator for TotalRevolutionIndex. Pascal zeroes NewTotalRevIndex at
        // the top of UpdateUniverse (UPDATE.PAS:1445); only Rebellion writes to it (:657,671); and
        // UpdateEmpire SETS (not adds to) Empire.TotalRevIndex from it at the end of the tick (:432)
        // — so the value is replaced each year from that year's rebellion activity alone, not
        // accumulated across years. Committing it here (rather than waiting for the full UpdateEmpire
        // in a later commit) is what makes UpdateRevolution's TotalRevIndex(Emp) feedback read
        // (UPDATE.PAS:699) testable already.
        var newTotalRevIndex = new Dictionary<Empire, int>();

        foreach (var planet in game.Galaxy.Planets) {
            UpdateWorld(planet, newTotalRevIndex);
        }

        // Starbases run after every planet has completed its own full tick (UPDATE.PAS:1450-1466's
        // planet loop, then starbase loop) — SupplyLink pulls from neighbouring planets' this-year
        // cargo, not last year's.
        foreach (var starbase in game.Galaxy.Starbases) {
            UpdateStarbase(starbase, game.Galaxy, newTotalRevIndex);
        }

        foreach (var empire in game.Empires) {
            empire.TotalRevolutionIndex = newTotalRevIndex.GetValueOrDefault(empire, 0);
        }
    }

    /// <summary>
    /// UPDATE.PAS:1355-1391, planet branch.
    /// <code>
    /// Production pipeline (ProduceRawMaterial/GetIndustrialDistribution/UpdateIndustry/Production)
    /// UpdateEfficiency
    /// UpdateTechLevel
    /// UpdatePopulation
    /// UseUpFood
    /// UseUpAmbrosia
    /// UpdateMilitary
    /// [deferred: UpdateDefenses]                                                           &lt;- between
    /// UpdateRevolution
    /// HostileLife (if Class == Hostile)
    /// </code>
    /// </summary>
    private void UpdateWorld(Planet planet, Dictionary<Empire, int> newTotalRevIndex)
    {
        RunProductionPipeline(planet);
        UpdateEfficiency(planet);
        UpdateTechLevel(planet);
        UpdatePopulation(planet);
        UseUpFood(planet);
        UseUpAmbrosia(planet);
        UpdateMilitary(planet);
        UpdateRevolution(planet, newTotalRevIndex);

        if (planet.Class == WorldClass.Hostile)
            HostileLife(planet);
    }

    /// <summary>
    /// UPDATE.PAS:1392-1430, starbase branch. UpdateEfficiency/UpdateTechLevel run unconditionally for
    /// every starbase (deferred: UpdateDefenses, UPDATE.PAS:1429 — lands with the combat phase, which
    /// needs it as baseline defensive state); the rest of the economy pipeline — production
    /// (SupplyLink/SurplusLink-bracketed) and population/food/ambrosia/military/revolution — runs only
    /// for industrial complexes (STyp=cmp), gating the *entire* pipeline on being a complex, not just
    /// production (UPDATE.PAS:1420-1427).
    /// </summary>
    private void UpdateStarbase(Starbase starbase, Galaxy.Galaxy galaxy, Dictionary<Empire, int> newTotalRevIndex)
    {
        var isComplex = starbase.Kind == StarbaseKind.IndustrialComplex;

        if (isComplex)
            RunProductionPipeline(starbase, () => SupplyLink(starbase, galaxy), () => SurplusLink(starbase, galaxy));

        UpdateEfficiency(starbase);
        UpdateTechLevel(starbase);

        if (isComplex) {
            UpdatePopulation(starbase);
            UseUpFood(starbase);
            UseUpAmbrosia(starbase);
            UpdateMilitary(starbase);
            UpdateRevolution(starbase, newTotalRevIndex);
        }
    }

    /// <summary>
    /// UPDATE.PAS:1371-1379 (planet) / :1403-1414 (starbase, STyp=cmp only, bracketed by
    /// beforeProduce/afterProduce). Pascal stages this through a wider-range TempCargo buffer and
    /// writes it back via PutTotalCargo's ThgLmt clamp at the end (UPDATE.PAS:436-456); operating
    /// directly on <see cref="IEconomicWorld.Cargo"/> (a plain unclamped int per field, unlike
    /// Pascal's clamped 0..9999 CargoArray subrange) and clamping once at the end reproduces the same
    /// semantics without a redundant temporary — including for a starbase's <see cref="SurplusLink"/>,
    /// which specifically depends on Cargo holding values above MaxResources mid-pipeline, before that
    /// final clamp.
    /// </summary>
    private void RunProductionPipeline(IEconomicWorld world, Action? beforeProduce = null, Action? afterProduce = null)
    {
        beforeProduce?.Invoke();

        var effectiveTech = EffectiveTechnologyLevel(world);
        var ip = (_industrialProductionTechAdjustment[world.TechLevel] / 100.0) * ((world.Efficiency + 250) / 100.0) / K6;

        ProduceRawMaterial(world, effectiveTech, ip);
        var industryDistribution = GetIndustrialDistribution(world);
        UpdateIndustry(world, industryDistribution);
        Production(world, effectiveTech, ip);

        afterProduce?.Invoke();

        ClampCargo(world.Cargo);
    }

    /// <summary>
    /// Pulls raw materials from adjacent (Chebyshev distance 1), same-empire raw-material worlds into
    /// a starbase's cargo before it produces this tick (UPDATE.PAS:517-560) — runs before
    /// ProduceRawMaterial so pulled materials are available to the same tick's production, matching
    /// Pascal's real call order.
    /// </summary>
    private void SupplyLink(Starbase starbase, Galaxy.Galaxy galaxy)
    {
        foreach (var neighbor in AdjacentSameEmpireRawMaterialPlanets(starbase, galaxy)) {
            foreach (var cargo in _rawMaterialCargoTypes) {
                var transfer = neighbor.Cargo[cargo] > 250 ? neighbor.Cargo[cargo] - Rnd(200, 250) : 0;
                neighbor.Cargo[cargo] -= transfer;
                starbase.Cargo[cargo] += transfer;
            }
        }
    }

    /// <summary>
    /// Returns whatever raw materials a starbase holds above MaxResources back out to adjacent,
    /// same-empire raw-material worlds after this tick's production (UPDATE.PAS:562-604) — the
    /// counterpart to <see cref="SupplyLink"/>. A neighbor's own Cargo is always &lt;=MaxResources
    /// here: every planet completes its own ClampCargo before any starbase in this tick's
    /// RunAnnualTick loop runs (see there), so MaxResources-neighbor.Cargo[cargo] can't go negative.
    /// </summary>
    private void SurplusLink(Starbase starbase, Galaxy.Galaxy galaxy)
    {
        foreach (var neighbor in AdjacentSameEmpireRawMaterialPlanets(starbase, galaxy)) {
            foreach (var cargo in _rawMaterialCargoTypes) {
                if (starbase.Cargo[cargo] <= MaxResources)
                    continue;

                var transfer = Math.Min(MaxResources - neighbor.Cargo[cargo], starbase.Cargo[cargo] - MaxResources);
                neighbor.Cargo[cargo] += transfer;
                starbase.Cargo[cargo] -= transfer;
            }
        }
    }

    /// <summary>
    /// The planets SupplyLink/SurplusLink transfer with: same empire, one of the five raw-material-
    /// producing world types (UPDATE.PAS:541-542,587-588's inline set), at Chebyshev distance 1 (the
    /// 8 compass directions, DirX/DirY — never the starbase's own sector).
    /// </summary>
    private static IEnumerable<Planet> AdjacentSameEmpireRawMaterialPlanets(Starbase starbase, Galaxy.Galaxy galaxy)
    {
        foreach (var (dx, dy) in _eightNeighborOffsets) {
            var x = starbase.Location.X + dx;
            var y = starbase.Location.Y + dy;
            if (x < 0 || x >= galaxy.Size || y < 0 || y >= galaxy.Size)
                continue;

            var coord = new Coordinate(x, y);
            foreach (var planet in galaxy.Planets)
                if (planet.Location == coord && planet.Owner == starbase.Owner && _supplyLinkEligibleTypes.Contains(planet.Type))
                    yield return planet;
        }
    }

    /// <summary>
    /// The Technology set gating which resources a world can currently produce (UPDATE.PAS:1359-1369).
    /// Independent worlds are capped one tech level behind their nominal <see cref="TechLevel"/>; owned worlds use
    /// their own <see cref="TechLevel"/> directly, further gated per-ship by <see cref="ShipTechAvailable"/> against
    /// the empire's individually-researched ships (raw materials/cargo aren't individually researched
    /// — see <see cref="UnlockedTechnology"/>'s own doc comment — so no further empire-level gate
    /// applies to them beyond <see cref="TechLevel"/>).
    /// </summary>
    private static TechLevel EffectiveTechnologyLevel(IEconomicWorld world) =>
        world.Owner.IsIndependent && world.TechLevel > TechLevel.PreTech
            ? world.TechLevel - 1
            : world.TechLevel;

    private static bool CargoTechAvailable(CargoType cargo, TechLevel effectiveTech) =>
        effectiveTech >= _minTechForCargo[cargo];

    private static bool ShipTechAvailable(IEconomicWorld world, ShipType ship, TechLevel effectiveTech)
    {
        if (effectiveTech < _minTechForShip[ship])
            return false;

        // Independent worlds have no empire research record to check against; owned worlds also need
        // the empire to have individually unlocked this ship (nothing populates this yet — new-game
        // setup, a later phase — so ship production is inert outside tests that seed it directly).
        return world.Owner.IsIndependent || world.Owner.Technology.Ships.Contains(ship);
    }

    /// <summary>Total Industrial Production of a world given population and tech level (MISC.PAS:247-265).</summary>
    private static int TotalProd(int population, TechLevel tech)
    {
        if (population <= 0)
            population = 1;

        var value = K1 * Math.Pow(population + K2, K3) * _totalProductionTechAdjustment[tech] / 100.0;
        return PascalRound(Math.Clamp(value, 0, 999));
    }

    /// <summary>Produces che/met/sup/tri raw materials from the industries that make them (UPDATE.PAS:797-831).</summary>
    private void ProduceRawMaterial(IEconomicWorld world, TechLevel effectiveTech, double ip)
    {
        foreach (var industry in _rawMaterialIndustries) {
            var level = world.Industry[industry];
            if (level <= 0)
                continue;

            var prodAdj = ip * (level + K4) * (level + K4);

            foreach (var cargo in _rawMaterialCargoTypes) {
                if (!_thgAdjRawMaterial.TryGetValue((industry, cargo), out var adjustment) || adjustment == 0)
                    continue;
                if (!CargoTechAvailable(cargo, effectiveTech))
                    continue;

                var production = Math.Max(1, ClampResource(prodAdj * adjustment));

                if (cargo == CargoType.Trillum)
                    production = ProduceTrillum(world, production, world.Cargo[CargoType.Trillum]);

                world.Cargo[cargo] += production;
            }
        }
    }

    /// <summary>
    /// Decrements trillum reserves by the amount produced, throttling production and raising the
    /// revolution index as reserves run low (UPDATE.PAS:757-795). Skips AddNews — no news subsystem yet.
    /// A starbase's TrillumReserve reads as MaxResources and discards writes (see IEconomicWorld), so
    /// this is a guaranteed no-op past the first line for one — matching TrillumReserves/
    /// PutTrillumReserves's Base cases (PRIMINTR.PAS:487,496) exactly.
    /// </summary>
    private int ProduceTrillum(IEconomicWorld world, int production, int currentTrillumCargo)
    {
        var availableCapacity = MaxResources - Math.Min(currentTrillumCargo, MaxResources);
        production = Math.Min(production, availableCapacity);

        var reserves = world.TrillumReserve;
        if (reserves == 0) {
            production = 0;
            ChangeRevIndex(world, Rnd(10, 20));
        } else if (reserves * 20L < production) {
            ChangeRevIndex(world, Rnd(5, 10));
        } else if (reserves * 10L < production && Rnd(1, 2) == 1) {
            ChangeRevIndex(world, Rnd(3, 5));
        }

        world.TrillumReserve = Math.Max(0, reserves - PascalRound(production / 100.0));
        return production;
    }

    /// <summary>
    /// Calculates the optimum industrial distribution for a world (INTRFACE.PAS:223-342). Purely a
    /// function of the world's current stats — computed fresh each tick, not stored.
    /// </summary>
    private Dictionary<IndustryType, double> GetIndustrialDistribution(IEconomicWorld world)
    {
        var dist = new Dictionary<IndustryType, double>();
        foreach (var industry in Enum.GetValues<IndustryType>())
            dist[industry] = 0;

        var alpha = (_industrialProductionTechAdjustment[world.TechLevel] / 100.0) * (world.Efficiency + 250) / K6;

        double tip = TotalProd(world.Population, world.TechLevel);
        if (world.IsAddictedToAmbrosia)
            tip *= AmbrosiaAdj; // kept as a real value here — UpdateIndustry rounds its own copy instead, see there.
        if (tip > 999)
            tip = 999;

        var temp = tip / 10000.0;
        var beta = new Dictionary<IndustryType, double>();
        foreach (var industry in Enum.GetValues<IndustryType>()) {
            var value = temp * _classIndustryAdjustment[(world.EffectiveClass, industry)];
            beta[industry] = value == 0 ? 1 : value;
        }

        var supplyIssp = _issp[world.SelfSufficiencyIndex(IndustryType.Supply)];
        var supplyThgAdj = _thgAdjRawMaterial[(IndustryType.Supply, CargoType.Supplies)];
        var supplyDist = (Math.Sqrt(SafetyAdj * supplyIssp * (SuppliesPerBillion / 100.0) * world.Population /
                                    ((supplyThgAdj / 100.0) * alpha)) - K4) / beta[IndustryType.Supply];
        if (supplyDist > 95 || supplyDist < 0 || world.Type == WorldType.Agricultural)
            supplyDist = 95;
        dist[IndustryType.Supply] = supplyDist;

        if (_rawMaterialOnlyTypes.Contains(world.Type)) {
            var remaining = 100 - dist[IndustryType.Supply];
            dist[IndustryType.Chemical] = remaining * (_typeData[(world.Type, IndustryType.Chemical)] / 100.0);
            dist[IndustryType.Mining] = remaining * (_typeData[(world.Type, IndustryType.Mining)] / 100.0);
            dist[IndustryType.TrillumMining] = remaining * (_typeData[(world.Type, IndustryType.TrillumMining)] / 100.0);
        } else {
            var mainIndustry = _principalIndustry[world.Type];
            var cheIssp = _issp[world.SelfSufficiencyIndex(IndustryType.Chemical)];
            var minIssp = _issp[world.SelfSufficiencyIndex(IndustryType.Mining)];
            var triIssp = _issp[world.SelfSufficiencyIndex(IndustryType.TrillumMining)];

            // A is always 0 at K4=0 but written out, like every other K4 term here, so the constant
            // stays visible if the balance data ever changes it (INTRFACE.PAS:311).
            var a = -K4 * (1 / beta[IndustryType.Chemical] + 1 / beta[IndustryType.Mining] + 1 / beta[IndustryType.TrillumMining]);
            var b = Math.Sqrt(SafetyAdj * cheIssp * _gammaChemical[mainIndustry]) / beta[IndustryType.Chemical]
                  + Math.Sqrt(SafetyAdj * minIssp * _gammaMining[mainIndustry]) / beta[IndustryType.Mining]
                  + Math.Sqrt(SafetyAdj * triIssp * _gammaTrillum[mainIndustry]) / beta[IndustryType.TrillumMining];

            var mainDist = (100 - (dist[IndustryType.Supply] + a + K4 * b)) / (1 + beta[mainIndustry] * b);
            var mainCap = _typeData[(world.Type, mainIndustry)];
            if (mainDist > mainCap)
                mainDist = mainCap;
            dist[mainIndustry] = mainDist;

            var cheDist = ((dist[mainIndustry] * beta[mainIndustry] + K4) * Math.Sqrt(SafetyAdj * cheIssp * _gammaChemical[mainIndustry]) - K4) / beta[IndustryType.Chemical];
            dist[IndustryType.Chemical] = cheDist < 0 ? 1 : cheDist;

            var minDist = ((dist[mainIndustry] * beta[mainIndustry] + K4) * Math.Sqrt(SafetyAdj * minIssp * _gammaMining[mainIndustry]) - K4) / beta[IndustryType.Mining];
            dist[IndustryType.Mining] = minDist < 0 ? 1 : minDist;

            var triDist = ((dist[mainIndustry] * beta[mainIndustry] + K4) * Math.Sqrt(SafetyAdj * triIssp * _gammaTrillum[mainIndustry]) - K4) / beta[IndustryType.TrillumMining];
            dist[IndustryType.TrillumMining] = triDist < 0 ? 1 : triDist;
        }

        return dist;
    }

    /// <summary>Moves developed industry level toward the optimum distribution, consuming metal (UPDATE.PAS:927-1005).</summary>
    private void UpdateIndustry(IEconomicWorld world, Dictionary<IndustryType, double> industryDistribution)
    {
        double tip = TotalProd(world.Population, world.TechLevel);
        if (world.IsAddictedToAmbrosia)
            tip = PascalRound(tip * AmbrosiaAdj); // rounded here — GetIndustrialDistribution keeps its own copy real, see there.
        if (tip > 999)
            tip = 999;

        var temp = tip / 10000.0;
        foreach (var industry in Enum.GetValues<IndustryType>()) {
            var dist = industryDistribution[industry];
            var optimumLevel = PascalRound(temp * dist * _classIndustryAdjustment[(world.EffectiveClass, industry)]);
            if (dist > 0 && optimumLevel == 0)
                optimumLevel = 1;

            var current = world.Industry[industry];
            int consRate;
            int rawNeeded;
            var metalCost = _industryMetalCost[industry];

            if (current < optimumLevel) {
                consRate = Math.Max(1, PascalRound(optimumLevel * (world.Efficiency / 500.0)));
                consRate = Math.Min(consRate, optimumLevel - current);
                rawNeeded = ClampResource(consRate / 100.0 * metalCost);
                if (rawNeeded > world.Cargo.Metals) {
                    // Safe from a divide-by-zero on Supply (metalCost=0): that case makes rawNeeded 0
                    // above, so this branch (rawNeeded > Cargo.Metals >= 0) can't be reached for it.
                    consRate = (int)(100 * (world.Cargo.Metals / (double)metalCost));
                    rawNeeded = world.Cargo.Metals;
                }
            } else if (current > optimumLevel) {
                consRate = Math.Min(-1, -PascalRound(world.Efficiency / 2.0));
                if (current + consRate < optimumLevel)
                    consRate = optimumLevel - current;
                rawNeeded = 0;
            } else {
                consRate = 0;
                rawNeeded = 0;
            }

            if (current + consRate > 999) {
                consRate = 999 - current;
                rawNeeded = ClampResource(consRate / 100.0 * metalCost);
            } else if (current + consRate < 0) {
                consRate = -current;
                rawNeeded = 0;
            }

            world.Industry[industry] = current + consRate;

            rawNeeded = Math.Min(world.Cargo.Metals, rawNeeded);
            world.Cargo.Metals -= rawNeeded;
        }
    }

    /// <summary>Produces ships, legions, ninjas, and ambrosia from developed industry (UPDATE.PAS:844-925).</summary>
    private void Production(IEconomicWorld world, TechLevel effectiveTech, double ip)
    {
        foreach (var industry in _productionIndustries) {
            var level = world.Industry[industry];
            if (level <= 0)
                continue;

            var prodAdj = ip * (level + K4) * (level + K4);

            foreach (var ship in _allShipTypes)
                ProduceShip(world, effectiveTech, industry, ship, prodAdj);
            foreach (var cargo in _productionCargoTypes)
                ProduceCargo(world, effectiveTech, industry, cargo, prodAdj);
        }
    }

    private void ProduceShip(IEconomicWorld world, TechLevel effectiveTech, IndustryType industry, ShipType ship, double prodAdj)
    {
        if (!_thgAdjShips.TryGetValue((industry, ship), out var adjustment) || adjustment == 0)
            return;
        if (!ShipTechAvailable(world, ship, effectiveTech))
            return;

        var production = ClampResource(prodAdj * adjustment);
        if (production <= 0)
            production = 1;

        production = Math.Min(production, MaxResources - world.Ships[ship]);
        production = ApplyRawMaterialConstraint(world, production,
            _rawMaterialForShips.GetValueOrDefault(ship, FrozenDictionary<CargoType, int>.Empty));

        world.Ships[ship] = Math.Min(MaxResources, world.Ships[ship] + production);
    }

    private void ProduceCargo(IEconomicWorld world, TechLevel effectiveTech, IndustryType industry, CargoType cargo, double prodAdj)
    {
        if (!_thgAdjCargoProduction.TryGetValue((industry, cargo), out var adjustment) || adjustment == 0)
            return;
        if (!CargoTechAvailable(cargo, effectiveTech))
            return;

        var production = ClampResource(prodAdj * adjustment);

        // Only ninja/ambrosia-type worlds make ninjas/ambrosia; ambrosia also needs the right class.
        if (cargo == CargoType.NinjaLegion && world.Type != WorldType.NinjaWorld)
            production = 0;
        else if (cargo == CargoType.Ambrosia && world.Type != WorldType.Ambrosia)
            production = 0;
        else if (cargo == CargoType.Ambrosia && world.EffectiveClass is not (WorldClass.Ambrosia or WorldClass.Paradise))
            production = 0;
        else if (production <= 0)
            production = 1;

        production = Math.Min(production, MaxResources - Math.Min(world.Cargo[cargo], MaxResources));
        production = ApplyRawMaterialConstraint(world, production,
            _rawMaterialForCargoProducts.GetValueOrDefault(cargo, FrozenDictionary<CargoType, int>.Empty));

        world.Cargo[cargo] += production;
    }

    /// <summary>
    /// Reduces production if there isn't enough raw material on hand, and deducts what's consumed
    /// (UPDATE.PAS:895-916). Ambrosia's requirement is checked (and can throttle production) but never
    /// actually deducted from cargo — a faithfully-preserved Pascal quirk, not a translation bug: the
    /// check loop covers amb..tri, the deduction loop only che..tri.
    /// </summary>
    private static int ApplyRawMaterialConstraint(IEconomicWorld world, int production, FrozenDictionary<CargoType, int> rawMaterialCost)
    {
        var rawNeeded = new Dictionary<CargoType, int>();
        foreach (var rawMaterial in _rawMaterialCheckOrder) {
            if (!rawMaterialCost.TryGetValue(rawMaterial, out var costPer100)) {
                rawNeeded[rawMaterial] = 0;
                continue;
            }

            var needed = ClampResource(production * (costPer100 / 100.0));
            if (needed > world.Cargo[rawMaterial]) {
                production = ClampResource(world.Cargo[rawMaterial] / (double)costPer100 * 100);
                needed = ClampResource(production * (costPer100 / 100.0));
            }
            rawNeeded[rawMaterial] = needed;
        }

        foreach (var rawMaterial in _rawMaterialDeductOrder) {
            var needed = Math.Min(world.Cargo[rawMaterial], rawNeeded.GetValueOrDefault(rawMaterial));
            world.Cargo[rawMaterial] -= needed;
        }

        return production;
    }

    /// <summary>Final write-back clamp matching PutTotalCargo (UPDATE.PAS:447-456).</summary>
    private static void ClampCargo(CargoHold cargo)
    {
        foreach (var type in Enum.GetValues<CargoType>())
            cargo[type] = ClampResource(cargo[type]);
    }

    private void UpdateEfficiency(IEconomicWorld world)
    {
        var inc = world.Owner.IsIndependent
            ? Rnd(0, 1)
            : world.Efficiency switch {
                <= 25 => Rnd(5, 12),
                <= 50 => Rnd(3, 8),
                <= 75 => Rnd(2, 5),
                <= 90 => Rnd(0, 3),
                <= 99 => Rnd(0, 1),
                _ => 0,
            };

        world.Efficiency = Math.Min(100, world.Efficiency + inc);
    }

    /// <summary>
    /// UPDATE.PAS:1032-1072. Skips AddNews (no news subsystem yet, same precedent as every other
    /// UpdateWorld step). Independent worlds drift upward on their own (1-in-50 chance per tick);
    /// owned worlds instead chase their empire's capital tech level up or down. A world with no
    /// capital to compare against (Owner.Capital is null) is a state Pascal's GetCapital can't
    /// produce for a real empire — every empire is founded with one — so this is purely a defensive
    /// no-op for incomplete test/setup state, not a modeled game rule.
    /// </summary>
    private void UpdateTechLevel(IEconomicWorld world)
    {
        if (world.TechLevel == TechLevel.Gate)
            return;

        if (world.Owner.IsIndependent) {
            if (Rnd(1, 50) == 1)
                world.TechLevel++;
            return;
        }

        var capitalTech = world.Owner.Capital?.TechLevel;
        if (capitalTech is null)
            return;

        if (capitalTech > world.TechLevel) {
            if (Rnd(1, 100) <= TechLevelIncreaseChance)
                world.TechLevel++;
        } else if (capitalTech < world.TechLevel) {
            if (Rnd(1, 15) == 1)
                world.TechLevel--;
        }
    }

    private void UpdatePopulation(IEconomicWorld world)
    {
        var maxPop = _maxPopulationByClass[world.EffectiveClass];
        var basePop = _basePopulationByTech[world.TechLevel];

        double increase;
        if (world.Population > maxPop)
            increase = Rnd(-10, 10);
        else if (world.Population < 75)
            increase = Rnd(2, 5);
        else if (world.Population > basePop)
            increase = basePop / 100.0;
        else
            increase = 128.0 * world.Population / maxPop;

        world.Population += PascalRound(increase);
    }

    private void UseUpFood(IEconomicWorld world)
    {
        var foodNeeded = ClampResource((world.Population / 100.0) * SuppliesPerBillion);

        if (foodNeeded > world.Cargo.Supplies) {
            var lack = foodNeeded - world.Cargo.Supplies;
            world.Cargo.Supplies = 0;

            var starve = Math.Min(lack / 6, world.Population / 10);
            world.Population -= starve;

            if (starve > 0) {
                var revInc = Math.Min(
                    (int)(_starvationRevoltAdjustmentByTech[world.TechLevel] * (starve / 10.0)),
                    45);
                ChangeRevIndex(world, revInc);
            }
        } else {
            world.Cargo.Supplies -= foodNeeded;
        }
    }

    /// <summary>
    /// UPDATE.PAS:1163-1276. Skips AddNews — no news subsystem yet (same precedent as UseUpFood and
    /// UpdateRevolution above) — but every state effect (population, efficiency, revolution index,
    /// tech level, industry, addiction flag) is kept.
    /// </summary>
    private void UseUpAmbrosia(IEconomicWorld world)
    {
        var ambNeeded = ClampResource((world.Population / 100.0) * DrugsPerBillion);

        if (world.IsAddictedToAmbrosia) {
            if (ambNeeded <= world.Cargo.Ambrosia) {
                world.Cargo.Ambrosia -= ambNeeded;
                return;
            }

            // Not enough ambrosia: people die, efficiency and revolution index suffer, and one of
            // four random side effects (nothing / riots / industrial sabotage / tech regression) fires.
            var lack = ambNeeded - world.Cargo.Ambrosia;
            world.Cargo.Ambrosia = 0;

            var die = Math.Min(ClampResource(AddictDeathCoeff * lack), world.Population / 7);
            world.Population -= die;

            var effChange = Math.Min((int)(AddictEffCoeff * die), world.Efficiency);
            world.Efficiency -= effChange;

            ChangeRevIndex(world, (int)(AddictRevICoeff * die));

            switch (Rnd(1, 10)) {
                case >= 5 and <= 7:
                    world.Population -= ClampResource((Rnd(50, 120) / 100.0) * die);
                    break;
                case 8 or 9:
                    foreach (var industry in Enum.GetValues<IndustryType>())
                        world.Industry[industry] -= (int)(world.Industry[industry] * Rnd(0, 20) / 100.0);
                    break;
                case 10:
                    if (world.TechLevel > TechLevel.PreTech)
                        world.TechLevel--;
                    break;
            }

            if (Rnd(1, 100) <= ChanceToAddict)
                world.IsAddictedToAmbrosia = false;
        } else if (world.Cargo.Ambrosia > 0) {
            if (ambNeeded <= world.Cargo.Ambrosia && Rnd(1, 100) < ChanceToAddict)
                world.IsAddictedToAmbrosia = true;

            ambNeeded /= 2;
            world.Cargo.Ambrosia = Math.Max(0, world.Cargo.Ambrosia - ambNeeded);
        }
    }

    /// <summary>
    /// UPDATE.PAS:606-617. Grows a world's military (Cargo.Legions) toward the optimum for its type
    /// and population; only ever grows it, never shrinks it — an already-above-optimum world (e.g.
    /// one being reinforced ahead of an attack) is left alone here, not walked back down.
    /// </summary>
    private void UpdateMilitary(IEconomicWorld world)
    {
        var optimumMilitary = ClampResource(Jitter(
            PascalRound(world.Population / 150.0 * _optimumMilitaryByType[world.Type]), 10));
        if (optimumMilitary > world.Cargo.Legions)
            world.Cargo.Legions = ClampResource(
                world.Cargo.Legions + world.Population / 10.0 * (_optimumMilitaryByType[world.Type] / 100.0));
    }

    private void UpdateRevolution(IEconomicWorld world, Dictionary<Empire, int> newTotalRevIndex)
    {
        var owner = world.Owner;

        // Decrease revolution index (UPDATE.PAS:698-703).
        var empRevAdj = Jitter(TotalRevIndex(owner), 50);
        if (world.Type == WorldType.Capital)
            ChangeRevIndex(world, -Rnd(20, 30));
        else
            ChangeRevIndex(world, empRevAdj + Rnd(-5, 2));

        if (owner.IsIndependent)
            return;

        // Military presence affects revolution (UPDATE.PAS:705-735).
        var optimumMilitary = ClampResource(Jitter(
            PascalRound(world.Population / 150.0 * _optimumMilitaryByType[world.Type]), 10));
        var military = ClampResource(world.Cargo.Legions + 5.0 * world.Cargo.NinjaLegions);

        if (military > optimumMilitary) {
            if (world.RevolutionIndex > 30) {
                var factor = Rnd(1, (military - optimumMilitary) / 100);
                ChangeRevIndex(world, -factor);
            } else if (world.Type != WorldType.Capital && world.Type != WorldType.Base) {
                if (Rnd(1, 5) == 1)
                    ChangeRevIndex(world, Rnd(5, 15));
            }
        }

        // Rebellion trigger (UPDATE.PAS:737-753). The news-only threshold tiers below 75 (RebelW1-4)
        // have no state effect and are skipped — no news subsystem yet.
        if (world.RevolutionIndex > 75 && Rnd(1, 100) < world.RevolutionIndex && world.Type != WorldType.Capital)
            Rebellion(world, military, newTotalRevIndex);
    }

    private void Rebellion(IEconomicWorld world, int military, Dictionary<Empire, int> newTotalRevIndex)
    {
        var owner = world.Owner;
        var rebels = Math.Max(1, ClampResource(Math.Sqrt(world.Population) * 65));
        var menLost = rebels / 5;
        var chanceToEndRebel = military / Math.Sqrt(rebels) * 1.414213;

        var lost = Math.Min(world.Cargo.Legions, menLost);
        world.Cargo.Legions -= lost;
        menLost -= lost;
        lost = Math.Min(world.Cargo.NinjaLegions, menLost / 5);
        world.Cargo.NinjaLegions -= lost;

        if (Rnd(1, 100) < chanceToEndRebel) {
            // Empire puts down the rebellion.
            ChangeRevIndex(world, Rnd(-15, 5));
            newTotalRevIndex[owner] = newTotalRevIndex.GetValueOrDefault(owner) - Rnd(1, 5);
        } else {
            // World rebels and goes independent.
            world.Owner = Empire.Independent;
            world.Type = WorldType.Independent;
            // InitializeISSP resets a planet's self-sufficiency to DefaultISSP ($5555 — all four
            // dials at the midpoint of the 0-10 range, DATACNST.PAS:516); a no-op for a starbase
            // (PRIMINTR.PAS:627-633 has no Base case) — see IEconomicWorld.
            world.InitializeSelfSufficiency();
            ChangeRevIndex(world, -Rnd(40, 50));
            world.Cargo.Legions = rebels;
            newTotalRevIndex[owner] = newTotalRevIndex.GetValueOrDefault(owner) + Rnd(5, 10);
        }

        world.Population = ClampResource(world.Population - military / 1000.0);
        world.Efficiency = Math.Max(0, world.Efficiency - Rnd(5, 15));
    }

    private void HostileLife(Planet planet)
    {
        var menAdj = (planet.Efficiency + 50) * ((planet.Cargo.Legions + 5 * planet.Cargo.NinjaLegions) / 100.0);
        var chanceOfAttack = Math.Max(0, 25 - PascalRound((menAdj - 2000) / 100.0));

        if (Rnd(1, 100) <= chanceOfAttack) {
            if (Rnd(1, 100) <= 25) {
                var popKilled = Math.Min(planet.Population, Rnd(10, 50));
                planet.Population -= popKilled;
                ChangeRevIndex(planet, Rnd(5, 15));
            } else {
                var menKilled = Math.Min(planet.Cargo.Legions, Rnd(200, 300));
                var nnjKilled = Math.Min(planet.Cargo.NinjaLegions, Rnd(20, 50));
                planet.Cargo.Legions -= menKilled;
                planet.Cargo.NinjaLegions -= nnjKilled;
            }
        } else if (!planet.Owner.IsIndependent && Rnd(1, 100) <= 20) {
            planet.Cargo.NinjaLegions += Rnd(50, 150);
        }
    }

    /// <summary>Empire's revolution-index feedback into per-world changes (PRIMINTR.PAS:1097-1105).</summary>
    private static int TotalRevIndex(Empire empire) =>
        empire.IsIndependent ? 0 : empire.TotalRevolutionIndex + empire.RevolutionFactor;

    /// <summary>Clamps a world's revolution index to [0,100] (PRIMINTR.PAS:ChangeRevIndex).</summary>
    private static void ChangeRevIndex(IEconomicWorld world, int change) =>
        world.RevolutionIndex = Math.Clamp(world.RevolutionIndex + change, 0, 100);

    /// <summary>Clamps a produced/consumed quantity to [0,MaxResources], truncating (Pascal source: MISC.PAS's ThgLmt).</summary>
    private static int ClampResource(double x) => x > MaxResources ? MaxResources : x < 0 ? 0 : (int)x;

    /// <summary>Pascal's Round: nearest integer, halves away from zero (not banker's rounding).</summary>
    private static int PascalRound(double x) => x >= 0 ? (int)(x + 0.5) : (int)(x - 0.5);

    /// <summary>Random integer in [min,max] inclusive; returns min if the range is empty or inverted (INT.PAS:Rnd).</summary>
    private int Rnd(int min, int max) => max <= min ? min : random.Next(max - min + 1) + min;

    /// <summary>Randomly varies a value by up to variation% in either direction (Pascal source: INT.PAS's RndVar).</summary>
    private int Jitter(int value, int variation)
    {
        var spread = (int)(value * (variation / 100.0));
        return Rnd(value - spread, value + spread);
    }
}
