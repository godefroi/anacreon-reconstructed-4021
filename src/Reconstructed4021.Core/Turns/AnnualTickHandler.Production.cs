using System.Collections.Frozen;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Turns;

/// <summary>
/// The production pipeline (UPDATE.PAS:1371-1379 planet / :1403-1414 starbase): raw materials,
/// industry growth, ship/cargo builds — plus SupplyLink/SurplusLink, the starbase-only raw-material
/// redistribution that brackets it. See AnnualTickHandler.cs for the type's overall file layout.
/// </summary>
public sealed partial class AnnualTickHandler
{
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

    /// <summary>
    /// % of industry that is effective, by world class and industry: ClassIndAdj (DATACNST.PAS:321-343).
    /// Public, not private: <see cref="Npe.NpeToolkit.GetNewDesignation"/> and
    /// <see cref="Entities.WorldDesignation"/>'s own Designate-window suitability hint both read the
    /// same table rather than re-transcribing 21 rows of balance data.
    /// </summary>
    public static readonly FrozenDictionary<(WorldClass, IndustryType), int> ClassIndustryAdjustment =
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
    /// ConsCargoNeeded[ConstrTypes,CargoTypes] (DATACNST.PAS:539-548) — raw material needed per year
    /// to build each construction type. Used by <see cref="UpdateConstruction"/>, not production —
    /// kept here alongside the other RawM/ConsCargoNeeded-shaped tables.
    /// </summary>
    private static readonly FrozenDictionary<ConstructionType, FrozenDictionary<CargoType, int>> _constructionCargoNeeded = new Dictionary<ConstructionType, FrozenDictionary<CargoType, int>> {
        [ConstructionType.Minefield] = RawMaterialRow((CargoType.Chemicals, 110), (CargoType.Metals, 500), (CargoType.Trillum, 80)),
        [ConstructionType.CommandBase] = RawMaterialRow((CargoType.Chemicals, 460), (CargoType.Metals, 2300), (CargoType.Trillum, 180)),
        [ConstructionType.Fortress] = RawMaterialRow((CargoType.Chemicals, 840), (CargoType.Metals, 2870), (CargoType.Trillum, 250)),
        [ConstructionType.IndustrialComplex] = RawMaterialRow((CargoType.Chemicals, 590), (CargoType.Metals, 2600), (CargoType.Trillum, 150)),
        [ConstructionType.Outpost] = RawMaterialRow((CargoType.Chemicals, 350), (CargoType.Metals, 1120), (CargoType.Trillum, 150)),
        [ConstructionType.Gate] = RawMaterialRow((CargoType.Chemicals, 2530), (CargoType.Metals, 3920), (CargoType.Trillum, 1450)),
        [ConstructionType.WarpLink] = RawMaterialRow((CargoType.Chemicals, 1560), (CargoType.Metals, 2550), (CargoType.Trillum, 290)),
        [ConstructionType.Disrupter] = RawMaterialRow((CargoType.Chemicals, 1110), (CargoType.Metals, 1180), (CargoType.Trillum, 1120)),
    }.ToFrozenDictionary();


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

    /// <summary>
    /// UPDATE.PAS:1371-1379 (planet) / :1403-1414 (starbase, STyp=cmp only, bracketed by
    /// beforeProduce/afterProduce). Pascal stages this through a wider-range TempCargo buffer and
    /// writes it back via PutTotalCargo's ThgLmt clamp at the end (UPDATE.PAS:436-456); operating
    /// directly on <see cref="IShipCargoHolder.Cargo"/> (a plain unclamped int per field, unlike
    /// Pascal's clamped 0..9999 CargoArray subrange) and clamping once at the end reproduces the same
    /// semantics without a redundant temporary — including for a starbase's <see cref="SurplusLink"/>,
    /// which specifically depends on Cargo holding values above MaxResources mid-pipeline, before that
    /// final clamp.
    /// </summary>
    /// <summary>
    /// Public, not private: <see cref="Entities.WorldProductionPreview"/> calls this same real pipeline
    /// against a throwaway world clone for the Production screen's own preview, rather than
    /// reimplementing the math a second time the way real Pascal's own GetProdInfo/GetIndusInfo do.
    /// </summary>
    public void RunProductionPipeline(IEconomicWorld world, HashSet<CargoType> reportedShortfalls, Action? beforeProduce = null, Action? afterProduce = null)
    {
        beforeProduce?.Invoke();

        var effectiveTech = EffectiveTechnologyLevel(world);
        var ip = (IndustryConstants.IndustrialProductionTechAdjustment[world.TechLevel] / 100.0) * ((world.Efficiency + 250) / 100.0) / K6;

        ProduceRawMaterial(world, effectiveTech, ip);
        var industryDistribution = GetIndustrialDistribution(world);
        UpdateIndustry(world, industryDistribution, reportedShortfalls);
        Production(world, effectiveTech, ip, reportedShortfalls);

        afterProduce?.Invoke();

        ClampCargo(world.Cargo);
    }

    /// <summary>
    /// ReportPlanetLack (UPDATE.PAS:39-56): the first time a given resource type is reported lacking
    /// in a tick, fires <paramref name="headline"/> and bumps RevolutionIndex by 1. Takes the headline
    /// as a parameter since Pascal's own two call sites pass different ones for the same underlying
    /// "not enough raw material" shape — <c>Lack</c> from Production's raw-material check
    /// (UPDATE.PAS:905), <c>IndLack</c> from UpdateIndustry's metals check (UPDATE.PAS:971).
    /// reportedShortfalls mirrors Pascal's OtherReports (a ResourceSet threaded through the whole
    /// per-tick UpdateWorld call, not reset between call sites) — <see cref="HashSet{T}.Add"/> already
    /// returns whether the item was new, so the guard and the insert are one call.
    ///
    /// Parm1 is Pascal's combined <c>ResourceTypes</c> ordinal (<c>men..tri</c> = 12..18), the same
    /// table <see cref="Tui.NewsWindow"/>'s <c>ResourceNames</c> array indexes -- <c>+12</c>, not this
    /// port's own bare <see cref="CargoType"/> ordinal (0..6), matching the <c>ShipType</c>+5
    /// convention <c>DestructionDetail</c>/<c>TransferDetail</c> already use for the same table. Fixed
    /// live: a Metals shortfall (ordinal 4) was rendering as <c>ResourceNames[4]</c>, "ion cannons".
    /// </summary>
    private static void ReportResourceShortfall(IEconomicWorld world, CargoType resource, NewsType headline, HashSet<CargoType> reportedShortfalls)
    {
        if (reportedShortfalls.Add(resource)) {
            world.Owner.AddNews(headline, world, p1: (int)resource + 12);
            ChangeRevIndex(world, 1);
        }
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
                if (starbase.Cargo[cargo] <= PascalMath.MaxResources)
                    continue;

                var transfer = Math.Min(PascalMath.MaxResources - neighbor.Cargo[cargo], starbase.Cargo[cargo] - PascalMath.MaxResources);
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
        effectiveTech >= TechCatalog.MinTechForCargo[cargo];

    private static bool ShipTechAvailable(IEconomicWorld world, ShipType ship, TechLevel effectiveTech)
    {
        if (effectiveTech < TechCatalog.MinTechForShip[ship])
            return false;

        // Independent worlds have no empire research record to check against; owned worlds also need
        // the empire to have individually unlocked this ship (nothing populates this yet — new-game
        // setup, a later phase — so ship production is inert outside tests that seed it directly).
        return world.Owner.IsIndependent || world.Owner.Technology.Ships.Contains(ship);
    }

    /// <summary>Total Industrial Production of a world given population and tech level (MISC.PAS:247-265). Public: also read directly by <see cref="EmpireStatusReport"/>'s own average-industry figure (PROLOG.PAS's EmpireStatus reads the exact same formula), and by <see cref="Entities.WorldProductionPreview"/> for the Production screen's own optimal-industry-target column.</summary>
    public static int TotalProd(int population, TechLevel tech)
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
    /// revolution index as reserves run low (UPDATE.PAS:757-795), firing
    /// <c>OutOfTrillumReserves</c>/<c>TrillumReservesVeryLow</c>/<c>TrillumReservesLow</c> alongside
    /// each of the three tiers. A starbase's TrillumReserve reads as MaxResources and discards writes
    /// (see IEconomicWorld), so this is a guaranteed no-op past the first line for one — matching
    /// TrillumReserves/PutTrillumReserves's Base cases (PRIMINTR.PAS:487,496) exactly.
    /// </summary>
    private int ProduceTrillum(IEconomicWorld world, int production, int currentTrillumCargo)
    {
        var availableCapacity = PascalMath.MaxResources - Math.Min(currentTrillumCargo, PascalMath.MaxResources);
        production = Math.Min(production, availableCapacity);

        var reserves = world.TrillumReserve;
        if (reserves == 0) {
            production = 0;
            world.Owner.AddNews(NewsType.OutOfTrillumReserves, world);
            ChangeRevIndex(world, Rnd(10, 20));
        } else if (reserves * 20L < production) {
            world.Owner.AddNews(NewsType.TrillumReservesVeryLow, world);
            ChangeRevIndex(world, Rnd(5, 10));
        } else if (reserves * 10L < production && Rnd(1, 2) == 1) {
            world.Owner.AddNews(NewsType.TrillumReservesLow, world);
            ChangeRevIndex(world, Rnd(3, 5));
        }

        world.TrillumReserve = Math.Max(0, reserves - PascalRound(production / 100.0));
        return production;
    }

    /// <summary>
    /// Calculates the optimum industrial distribution for a world (INTRFACE.PAS:223-342). Purely a
    /// function of the world's current stats — computed fresh each tick, not stored. Public (not
    /// private) and static (no instance state involved): new-game world placement (Core/NewGame/) is
    /// one real consumer, via GetOptimumIndustry below; <see cref="Entities.WorldProductionPreview"/>
    /// is another, for the Production screen's own display.
    /// </summary>
    public static Dictionary<IndustryType, double> GetIndustrialDistribution(IEconomicWorld world)
    {
        var dist = new Dictionary<IndustryType, double>();
        foreach (var industry in Enum.GetValues<IndustryType>())
            dist[industry] = 0;

        var alpha = (IndustryConstants.IndustrialProductionTechAdjustment[world.TechLevel] / 100.0) * (world.Efficiency + 250) / K6;

        double tip = TotalProd(world.Population, world.TechLevel);
        if (world.IsAddictedToAmbrosia)
            tip *= AmbrosiaAdj; // kept as a real value here — UpdateIndustry rounds its own copy instead, see there.
        if (tip > 999)
            tip = 999;

        var temp = tip / 10000.0;
        var beta = new Dictionary<IndustryType, double>();
        foreach (var industry in Enum.GetValues<IndustryType>()) {
            var value = temp * ClassIndustryAdjustment[(world.EffectiveClass, industry)];
            beta[industry] = value == 0 ? 1 : value;
        }

        var supplyIssp = SelfSufficiencySettings.Multipliers[world.SelfSufficiencyIndex(IndustryType.Supply)];
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
            var mainIndustry = WorldDesignation.PrincipalIndustry[world.Type];
            var cheIssp = SelfSufficiencySettings.Multipliers[world.SelfSufficiencyIndex(IndustryType.Chemical)];
            var minIssp = SelfSufficiencySettings.Multipliers[world.SelfSufficiencyIndex(IndustryType.Mining)];
            var triIssp = SelfSufficiencySettings.Multipliers[world.SelfSufficiencyIndex(IndustryType.TrillumMining)];

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
    private void UpdateIndustry(IEconomicWorld world, Dictionary<IndustryType, double> industryDistribution, HashSet<CargoType> reportedShortfalls)
    {
        double tip = TotalProd(world.Population, world.TechLevel);
        if (world.IsAddictedToAmbrosia)
            tip = PascalRound(tip * AmbrosiaAdj); // rounded here — GetIndustrialDistribution keeps its own copy real, see there.
        if (tip > 999)
            tip = 999;

        var temp = tip / 10000.0;
        foreach (var industry in Enum.GetValues<IndustryType>()) {
            var dist = industryDistribution[industry];
            var optimumLevel = PascalRound(temp * dist * ClassIndustryAdjustment[(world.EffectiveClass, industry)]);
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
                    ReportResourceShortfall(world, CargoType.Metals, NewsType.IndustryLacksMetals, reportedShortfalls);
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
    private void Production(IEconomicWorld world, TechLevel effectiveTech, double ip, HashSet<CargoType> reportedShortfalls)
    {
        foreach (var industry in _productionIndustries) {
            var level = world.Industry[industry];
            if (level <= 0)
                continue;

            var prodAdj = ip * (level + K4) * (level + K4);

            foreach (var ship in _allShipTypes)
                ProduceShip(world, effectiveTech, industry, ship, prodAdj, reportedShortfalls);
            foreach (var cargo in _productionCargoTypes)
                ProduceCargo(world, effectiveTech, industry, cargo, prodAdj, reportedShortfalls);
        }
    }

    private void ProduceShip(IEconomicWorld world, TechLevel effectiveTech, IndustryType industry, ShipType ship, double prodAdj, HashSet<CargoType> reportedShortfalls)
    {
        if (!_thgAdjShips.TryGetValue((industry, ship), out var adjustment) || adjustment == 0)
            return;
        if (!ShipTechAvailable(world, ship, effectiveTech))
            return;

        var production = ClampResource(prodAdj * adjustment);
        if (production <= 0)
            production = 1;

        production = Math.Min(production, PascalMath.MaxResources - world.Ships[ship]);
        production = ApplyRawMaterialConstraint(world, production,
            _rawMaterialForShips.GetValueOrDefault(ship, FrozenDictionary<CargoType, int>.Empty), reportedShortfalls);

        world.Ships[ship] = Math.Min(PascalMath.MaxResources, world.Ships[ship] + production);
    }

    private void ProduceCargo(IEconomicWorld world, TechLevel effectiveTech, IndustryType industry, CargoType cargo, double prodAdj, HashSet<CargoType> reportedShortfalls)
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

        production = Math.Min(production, PascalMath.MaxResources - Math.Min(world.Cargo[cargo], PascalMath.MaxResources));
        production = ApplyRawMaterialConstraint(world, production,
            _rawMaterialForCargoProducts.GetValueOrDefault(cargo, FrozenDictionary<CargoType, int>.Empty), reportedShortfalls);

        world.Cargo[cargo] += production;
    }

    /// <summary>
    /// Reduces production if there isn't enough raw material on hand, and deducts what's consumed
    /// (UPDATE.PAS:895-916). Ambrosia's requirement is checked (and can throttle production) but never
    /// actually deducted from cargo — a faithfully-preserved Pascal quirk, not a translation bug: the
    /// check loop covers amb..tri, the deduction loop only che..tri. A shortfall here only bumps
    /// RevolutionIndex for a planet (UPDATE.PAS:904's "IF ID.ObjTyp&lt;&gt;Base THEN" guards this
    /// specific ReportPlanetLack call, unlike UpdateIndustry's) — see <see cref="IEconomicWorld.IsPlanet"/>.
    /// </summary>
    private static int ApplyRawMaterialConstraint(IEconomicWorld world, int production, FrozenDictionary<CargoType, int> rawMaterialCost, HashSet<CargoType> reportedShortfalls)
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
                if (world.IsPlanet)
                    ReportResourceShortfall(world, rawMaterial, NewsType.LacksRawMaterial, reportedShortfalls);
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
}
