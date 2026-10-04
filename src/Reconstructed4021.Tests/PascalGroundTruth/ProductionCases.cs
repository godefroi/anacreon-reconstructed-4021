using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs shared between the golden-file generator (GoldenFileTests, requires fpc and git —
/// dynamically skipped otherwise) and the always-on AnnualTickHandlerProductionTests.MatchesGoldenFile.
/// Only inputs live here — expected outputs live exclusively in reference/verify/golden/
/// production.golden, computed by a real FreePascal run of the real, patched UpdateWorld (via
/// reference/verify/runworld.pas's production domain).
///
/// TechnologyBitmask is the owning empire's researched TechnologySet (bit i = TechnologyTypes(i+1),
/// as in the empire domain); UPDATE.PAS:1367-1368 intersects it with TechDev[Tech] to decide what an
/// owned world can produce. It defaults to everything researched. runworld.pas also sets the planet's
/// ISSP dial (ImpExp) to DefaultISSP ($5555, DATACNST.PAS:516 — every real planet's value at
/// settlement, PRIMINTR.PAS:631, which GetIndustrialDistribution's sqrt-based formulas are sensitive
/// to), matching what any reachable game state actually has.
///
/// Every case here has at most one developed industry within BioInd..SYTInd at a time; no case
/// exercises two simultaneously-developed industries under scarce raw materials at once, which would
/// exercise Production's own single loop more thoroughly.
/// </summary>
public sealed record ProductionCase(
    string Name, WorldClass Class, WorldType Type, int Population, int Efficiency, TechLevel Tech,
    bool AmbAddict,
    int IndusBio, int IndusChe, int IndusMin, int IndusSYG, int IndusSYJ, int IndusSYS, int IndusSYT,
    int IndusSup, int IndusTri,
    int CargoMen, int CargoNnj, int CargoAmb, int CargoChe, int CargoMet, int CargoSup, int CargoTri,
    int TrillumReserve, bool Independent = false, int RevIndex = 0, int RngFixedValue = 0,
    int TechnologyBitmask = ProductionCases.EverythingResearched) : INamedCase;

internal static class ProductionCases
{
    public const int EverythingResearched = (1 << 26) - 1;

    // CargoType's bits in the 26-bit mask: defenses take 0-3, ships 4-10, then men..tri.
    private const int ChemicalsBit = 1 << 14;
    private const int TrillumBit = 1 << 17;

    public static readonly IReadOnlyList<ProductionCase> All = [
        // ProduceRawMaterial runs before UpdateIndustry in the pipeline, so this result depends only
        // on the Industry level set here, not on anything GetIndustrialDistribution/UpdateIndustry
        // compute afterward for this tick.
        new(Name: "TrillumDrawsDownReserves", Class: WorldClass.EarthLike, Type: WorldType.TrillumMine,
            Population: 1000, Efficiency: 100, Tech: TechLevel.Gate, AmbAddict: false,
            IndusBio: 0, IndusChe: 0, IndusMin: 0, IndusSYG: 0, IndusSYJ: 0, IndusSYS: 0, IndusSYT: 0,
            IndusSup: 0, IndusTri: 100,
            CargoMen: 0, CargoNnj: 0, CargoAmb: 0, CargoChe: 0, CargoMet: 0, CargoSup: 1000, CargoTri: 0,
            TrillumReserve: 500),

        // TechDev[PreTchLvl] = [sup] only, so che/tri production is gated off entirely despite
        // Industry.Chemical/TrillumMining both being fully developed (100) — matching UPDATE.PAS:1359-1369.
        // Population=0 kept tiny (TotalProd treats Pop<=0 as 1) so it can't perturb Cargo.Supplies via
        // UseUpFood in the always-on test's full RunAnnualTick.
        new(Name: "PreTechOnlyProducesSupplies", Class: WorldClass.EarthLike, Type: WorldType.TrillumMine,
            Population: 0, Efficiency: 100, Tech: TechLevel.PreTech, AmbAddict: false,
            IndusBio: 0, IndusChe: 100, IndusMin: 0, IndusSYG: 0, IndusSYJ: 0, IndusSYS: 0, IndusSYT: 0,
            IndusSup: 100, IndusTri: 100,
            CargoMen: 0, CargoNnj: 0, CargoAmb: 0, CargoChe: 0, CargoMet: 0, CargoSup: 0, CargoTri: 0,
            TrillumReserve: 500),

        // Exercises the entire pipeline in composed order, including GetIndustrialDistribution's
        // Gamma/Beta cascade (Capital has a principal industry, so it takes the non-PI-less branch)
        // and Production building all seven ship types from one developed ShipyardGeneral level.
        new(Name: "FullPipelineCapitalWorld", Class: WorldClass.EarthLike, Type: WorldType.Capital,
            Population: 1000, Efficiency: 100, Tech: TechLevel.Gate, AmbAddict: false,
            IndusBio: 0, IndusChe: 0, IndusMin: 0, IndusSYG: 100, IndusSYJ: 0, IndusSYS: 0, IndusSYT: 0,
            IndusSup: 0, IndusTri: 100,
            CargoMen: 0, CargoNnj: 0, CargoAmb: 0, CargoChe: 5000, CargoMet: 5000, CargoSup: 5000, CargoTri: 5000,
            TrillumReserve: 5000),

        // Preserves UPDATE.PAS:896-916's asymmetric raw-material handling: Ambrosia's requirement is
        // checked (and throttles ninja production when scarce) but only che/met/sup/tri are ever
        // actually subtracted from cargo by *production*. See
        // AnnualTickHandlerProductionTests.NinjaWorldAmbrosiaIsDrainedByUseUpAmbrosiaNotProduction for
        // why Cargo.Ambrosia isn't part of this case's golden-checked fields.
        new(Name: "NinjaProductionThrottledByScarceAmbrosia", Class: WorldClass.EarthLike, Type: WorldType.NinjaWorld,
            Population: 1000, Efficiency: 100, Tech: TechLevel.Gate, AmbAddict: false,
            IndusBio: 100, IndusChe: 0, IndusMin: 0, IndusSYG: 0, IndusSYJ: 0, IndusSYS: 0, IndusSYT: 0,
            IndusSup: 0, IndusTri: 0,
            CargoMen: 0, CargoNnj: 0, CargoAmb: 5, CargoChe: 5000, CargoMet: 5000, CargoSup: 5000, CargoTri: 5000,
            TrillumReserve: 5000),

        // Same world twice, out of chemicals/metals so ship production reports a raw-material lack
        // (UPDATE.PAS:905) and industry growth reports IndLack (UPDATE.PAS:971). ReportPlanetLack only
        // bumps RevIndex for an owned world (UPDATE.PAS:46-47); the independent twin must stay at 0.
        new(Name: "ShortfallRaisesRevIndexForEmpireWorld", Class: WorldClass.EarthLike, Type: WorldType.Capital,
            Population: 1000, Efficiency: 100, Tech: TechLevel.Gate, AmbAddict: false,
            IndusBio: 0, IndusChe: 0, IndusMin: 0, IndusSYG: 100, IndusSYJ: 0, IndusSYS: 0, IndusSYT: 0,
            IndusSup: 0, IndusTri: 100,
            CargoMen: 0, CargoNnj: 0, CargoAmb: 0, CargoChe: 0, CargoMet: 0, CargoSup: 5000, CargoTri: 5000,
            TrillumReserve: 5000, RevIndex: 50),
        new(Name: "ShortfallLeavesRevIndexAloneForIndependentWorld", Class: WorldClass.EarthLike, Type: WorldType.Capital,
            Population: 1000, Efficiency: 100, Tech: TechLevel.Gate, AmbAddict: false,
            IndusBio: 0, IndusChe: 0, IndusMin: 0, IndusSYG: 100, IndusSYJ: 0, IndusSYS: 0, IndusSYT: 0,
            IndusSup: 0, IndusTri: 100,
            CargoMen: 0, CargoNnj: 0, CargoAmb: 0, CargoChe: 0, CargoMet: 0, CargoSup: 5000, CargoTri: 5000,
            TrillumReserve: 5000, Independent: true, RevIndex: 50),

        // The cases above all run at Efficiency 100 and mostly at Gate tech. These vary efficiency,
        // tech and revolution index, which feed TotalProd, the TechDev gates and UpdateRevolution.
        new(Name: "MidTechLowEfficiencyCapital", Class: WorldClass.ClassM, Type: WorldType.Capital,
            Population: 2500, Efficiency: 60, Tech: TechLevel.Warp, AmbAddict: false,
            IndusBio: 0, IndusChe: 40, IndusMin: 40, IndusSYG: 60, IndusSYJ: 0, IndusSYS: 0, IndusSYT: 0,
            IndusSup: 30, IndusTri: 30,
            CargoMen: 500, CargoNnj: 0, CargoAmb: 0, CargoChe: 3000, CargoMet: 3000, CargoSup: 3000, CargoTri: 3000,
            TrillumReserve: 3000, RevIndex: 20),
        new(Name: "UnrestfulBaseWorldAtJumpTech", Class: WorldClass.Ocean, Type: WorldType.Base,
            Population: 800, Efficiency: 35, Tech: TechLevel.Jump, AmbAddict: false,
            IndusBio: 10, IndusChe: 20, IndusMin: 25, IndusSYG: 40, IndusSYJ: 20, IndusSYS: 0, IndusSYT: 10,
            IndusSup: 20, IndusTri: 15,
            CargoMen: 300, CargoNnj: 0, CargoAmb: 0, CargoChe: 1500, CargoMet: 1200, CargoSup: 2000, CargoTri: 800,
            TrillumReserve: 1500, RevIndex: 80),
        new(Name: "IndependentAgriculturalAtBioTech", Class: WorldClass.Jungle, Type: WorldType.Agricultural,
            Population: 1500, Efficiency: 75, Tech: TechLevel.Bio, AmbAddict: false,
            IndusBio: 80, IndusChe: 10, IndusMin: 10, IndusSYG: 0, IndusSYJ: 0, IndusSYS: 0, IndusSYT: 0,
            IndusSup: 50, IndusTri: 10,
            CargoMen: 200, CargoNnj: 0, CargoAmb: 0, CargoChe: 500, CargoMet: 500, CargoSup: 4000, CargoTri: 500,
            TrillumReserve: 2000, Independent: true, RevIndex: 10),

        // HostileLife (UPDATE.PAS:458-515), which runs last in UpdateWorld for a hostile-life world.
        // Few troops leave ChanceOfAttack high: at RngFixedValue 0 both rolls pass, so the aliens
        // kill population. At 30 the attack roll (31) still passes but the population roll fails,
        // so they attack troops instead. A large garrison drives ChanceOfAttack to 0, and the 20%
        // roll has the aliens join.
        new(Name: "HostileLifeKillsPopulation", Class: WorldClass.Hostile, Type: WorldType.Agricultural,
            Population: 1000, Efficiency: 50, Tech: TechLevel.Warp, AmbAddict: false,
            IndusBio: 0, IndusChe: 0, IndusMin: 0, IndusSYG: 0, IndusSYJ: 0, IndusSYS: 0, IndusSYT: 0,
            IndusSup: 0, IndusTri: 0,
            CargoMen: 100, CargoNnj: 0, CargoAmb: 0, CargoChe: 0, CargoMet: 0, CargoSup: 5000, CargoTri: 0,
            TrillumReserve: 1000),
        new(Name: "HostileLifeAttacksTroops", Class: WorldClass.Hostile, Type: WorldType.Agricultural,
            Population: 1000, Efficiency: 50, Tech: TechLevel.Warp, AmbAddict: false,
            IndusBio: 0, IndusChe: 0, IndusMin: 0, IndusSYG: 0, IndusSYJ: 0, IndusSYS: 0, IndusSYT: 0,
            IndusSup: 0, IndusTri: 0,
            CargoMen: 500, CargoNnj: 50, CargoAmb: 0, CargoChe: 0, CargoMet: 0, CargoSup: 5000, CargoTri: 0,
            TrillumReserve: 1000, RngFixedValue: 30),
        new(Name: "HostileLifeJoinsTroops", Class: WorldClass.Hostile, Type: WorldType.Agricultural,
            Population: 1000, Efficiency: 100, Tech: TechLevel.Warp, AmbAddict: false,
            IndusBio: 0, IndusChe: 0, IndusMin: 0, IndusSYG: 0, IndusSYJ: 0, IndusSYS: 0, IndusSYT: 0,
            IndusSup: 0, IndusTri: 0,
            CargoMen: 4000, CargoNnj: 0, CargoAmb: 0, CargoChe: 0, CargoMet: 0, CargoSup: 5000, CargoTri: 0,
            TrillumReserve: 1000),

        // UseUpFood with no supplies (UPDATE.PAS:1118-1161). A large Gate-tech population starves
        // enough that TechAdj[Tech]*(Starve/10) exceeds the 45-point cap; a small Atomic-tech one
        // starves a little, with a different TechAdj row.
        new(Name: "StarvationRevoltCappedAt45", Class: WorldClass.EarthLike, Type: WorldType.Agricultural,
            Population: 5000, Efficiency: 100, Tech: TechLevel.Gate, AmbAddict: false,
            IndusBio: 0, IndusChe: 0, IndusMin: 0, IndusSYG: 0, IndusSYJ: 0, IndusSYS: 0, IndusSYT: 0,
            IndusSup: 0, IndusTri: 0,
            CargoMen: 0, CargoNnj: 0, CargoAmb: 0, CargoChe: 0, CargoMet: 0, CargoSup: 0, CargoTri: 0,
            TrillumReserve: 1000),
        new(Name: "StarvationAtAtomicTech", Class: WorldClass.EarthLike, Type: WorldType.Agricultural,
            Population: 300, Efficiency: 100, Tech: TechLevel.Atomic, AmbAddict: false,
            IndusBio: 0, IndusChe: 0, IndusMin: 0, IndusSYG: 0, IndusSYJ: 0, IndusSYS: 0, IndusSYT: 0,
            IndusSup: 0, IndusTri: 0,
            CargoMen: 0, CargoNnj: 0, CargoAmb: 0, CargoChe: 0, CargoMet: 0, CargoSup: 0, CargoTri: 0,
            TrillumReserve: 1000),

        // ProduceTrillum's three reserve tiers (UPDATE.PAS:757-795), at the same trillum industry as
        // TrillumDrawsDownReserves: no reserves at all; reserves*20 below this year's production;
        // reserves*10 below it, where the 1-in-2 roll passes at RngFixedValue 0.
        new(Name: "TrillumReservesExhausted", Class: WorldClass.EarthLike, Type: WorldType.TrillumMine,
            Population: 1000, Efficiency: 100, Tech: TechLevel.Gate, AmbAddict: false,
            IndusBio: 0, IndusChe: 0, IndusMin: 0, IndusSYG: 0, IndusSYJ: 0, IndusSYS: 0, IndusSYT: 0,
            IndusSup: 0, IndusTri: 100,
            CargoMen: 0, CargoNnj: 0, CargoAmb: 0, CargoChe: 0, CargoMet: 0, CargoSup: 1000, CargoTri: 0,
            TrillumReserve: 0),
        new(Name: "TrillumReservesVeryLow", Class: WorldClass.EarthLike, Type: WorldType.TrillumMine,
            Population: 1000, Efficiency: 100, Tech: TechLevel.Gate, AmbAddict: false,
            IndusBio: 0, IndusChe: 0, IndusMin: 0, IndusSYG: 0, IndusSYJ: 0, IndusSYS: 0, IndusSYT: 0,
            IndusSup: 0, IndusTri: 100,
            CargoMen: 0, CargoNnj: 0, CargoAmb: 0, CargoChe: 0, CargoMet: 0, CargoSup: 1000, CargoTri: 0,
            TrillumReserve: 5),
        new(Name: "TrillumReservesLow", Class: WorldClass.EarthLike, Type: WorldType.TrillumMine,
            Population: 1000, Efficiency: 100, Tech: TechLevel.Gate, AmbAddict: false,
            IndusBio: 0, IndusChe: 0, IndusMin: 0, IndusSYG: 0, IndusSYJ: 0, IndusSYS: 0, IndusSYT: 0,
            IndusSup: 0, IndusTri: 100,
            CargoMen: 0, CargoNnj: 0, CargoAmb: 0, CargoChe: 0, CargoMet: 0, CargoSup: 1000, CargoTri: 0,
            TrillumReserve: 15),

        // An owned world only produces cargo its empire has researched, not everything its tech level
        // allows (UPDATE.PAS:1367-1368, 819). Research grants one item at a time, so an empire can
        // reach Atomic without trillum: no production, no reserve drain, no reserve news. The same
        // empire missing chemicals at Gate makes none from a fully developed chemical industry.
        new(Name: "UnresearchedTrillumNotProduced", Class: WorldClass.EarthLike, Type: WorldType.TrillumMine,
            Population: 1000, Efficiency: 100, Tech: TechLevel.Atomic, AmbAddict: false,
            IndusBio: 0, IndusChe: 0, IndusMin: 0, IndusSYG: 0, IndusSYJ: 0, IndusSYS: 0, IndusSYT: 0,
            IndusSup: 0, IndusTri: 100,
            CargoMen: 0, CargoNnj: 0, CargoAmb: 0, CargoChe: 0, CargoMet: 0, CargoSup: 1000, CargoTri: 0,
            TrillumReserve: 5, TechnologyBitmask: EverythingResearched & ~TrillumBit),
        new(Name: "UnresearchedChemicalsNotProduced", Class: WorldClass.EarthLike, Type: WorldType.Chemical,
            Population: 1000, Efficiency: 100, Tech: TechLevel.Gate, AmbAddict: false,
            IndusBio: 0, IndusChe: 100, IndusMin: 0, IndusSYG: 0, IndusSYJ: 0, IndusSYS: 0, IndusSYT: 0,
            IndusSup: 0, IndusTri: 0,
            CargoMen: 0, CargoNnj: 0, CargoAmb: 0, CargoChe: 0, CargoMet: 0, CargoSup: 1000, CargoTri: 0,
            TrillumReserve: 1000, TechnologyBitmask: EverythingResearched & ~ChemicalsBit),
    ];

    /// <summary>MethodDataSource shape for AnnualTickHandlerProductionTests.MatchesGoldenFile — one
    /// Func per case, per TUnit's guidance for reference types.</summary>
    public static IEnumerable<Func<ProductionCase>> AsDataSource() => All.Select(c => (Func<ProductionCase>)(() => c));
}
