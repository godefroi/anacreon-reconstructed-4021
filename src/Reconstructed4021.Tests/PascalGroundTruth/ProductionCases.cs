using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs shared between the golden-file generator (GoldenFileTests, requires fpc and git —
/// dynamically skipped otherwise) and the always-on AnnualTickHandlerProductionTests.MatchesGoldenFile.
/// Only inputs live here — expected outputs live exclusively in reference/verify/golden/
/// production.golden, computed by a real FreePascal run of the real, patched UpdateWorld (via
/// reference/verify/runworld.pas's production domain).
///
/// runworld.pas sets the planet's owning empire's full Pascal TechnologySet unconditionally
/// (UPDATE.PAS:1367-1368 intersects it with TechDev[Tech] to decide what a world can produce) and
/// its ISSP dial (ImpExp) to DefaultISSP ($5555, DATACNST.PAS:516 — every real planet's value at
/// settlement, PRIMINTR.PAS:631, which GetIndustrialDistribution's sqrt-based formulas are sensitive
/// to), matching what any reachable game state actually has.
///
/// Cargo.Chemicals/Metals stay outside this domain's own golden comparison for a real, still-open
/// reason — see AnnualTickHandlerProductionTests's own doc comment and docs/OPEN_GAPS.md.
///
/// Every case here has at most one developed industry within BioInd..SYTInd at a time; no case
/// exercises two simultaneously-developed industries under scarce raw materials at once, which would
/// exercise Production's own single loop more thoroughly.
///
/// AllShipsUnlocked doesn't reach the harness — it isn't a Pascal concept, it's the C# port's
/// per-empire ship-research gate (ShipTechAvailable), needed only so Owner.Technology.Ships can be
/// populated before RunAnnualTick; runworld.pas's Technology set is unconditionally the full
/// TechnologyTypes range (reducing to TechDev[Tech] once intersected), regardless of this flag.
/// </summary>
public sealed record ProductionCase(
    string Name, WorldClass Class, WorldType Type, int Population, int Efficiency, TechLevel Tech,
    bool AmbAddict, bool AllShipsUnlocked,
    int IndusBio, int IndusChe, int IndusMin, int IndusSYG, int IndusSYJ, int IndusSYS, int IndusSYT,
    int IndusSup, int IndusTri,
    int CargoMen, int CargoNnj, int CargoAmb, int CargoChe, int CargoMet, int CargoSup, int CargoTri,
    int TrillumReserve) : INamedCase;

internal static class ProductionCases
{
    public static readonly IReadOnlyList<ProductionCase> All = [
        // ProduceRawMaterial runs before UpdateIndustry in the pipeline, so this result depends only
        // on the Industry level set here, not on anything GetIndustrialDistribution/UpdateIndustry
        // compute afterward for this tick.
        new(Name: "TrillumDrawsDownReserves", Class: WorldClass.EarthLike, Type: WorldType.TrillumMine,
            Population: 1000, Efficiency: 100, Tech: TechLevel.Gate, AmbAddict: false, AllShipsUnlocked: false,
            IndusBio: 0, IndusChe: 0, IndusMin: 0, IndusSYG: 0, IndusSYJ: 0, IndusSYS: 0, IndusSYT: 0,
            IndusSup: 0, IndusTri: 100,
            CargoMen: 0, CargoNnj: 0, CargoAmb: 0, CargoChe: 0, CargoMet: 0, CargoSup: 1000, CargoTri: 0,
            TrillumReserve: 500),

        // TechDev[PreTchLvl] = [sup] only, so che/tri production is gated off entirely despite
        // Industry.Chemical/TrillumMining both being fully developed (100) — matching UPDATE.PAS:1359-1369.
        // Population=0 kept tiny (TotalProd treats Pop<=0 as 1) so it can't perturb Cargo.Supplies via
        // UseUpFood in the always-on test's full RunAnnualTick.
        new(Name: "PreTechOnlyProducesSupplies", Class: WorldClass.EarthLike, Type: WorldType.TrillumMine,
            Population: 0, Efficiency: 100, Tech: TechLevel.PreTech, AmbAddict: false, AllShipsUnlocked: false,
            IndusBio: 0, IndusChe: 100, IndusMin: 0, IndusSYG: 0, IndusSYJ: 0, IndusSYS: 0, IndusSYT: 0,
            IndusSup: 100, IndusTri: 100,
            CargoMen: 0, CargoNnj: 0, CargoAmb: 0, CargoChe: 0, CargoMet: 0, CargoSup: 0, CargoTri: 0,
            TrillumReserve: 500),

        // Exercises the entire pipeline in composed order, including GetIndustrialDistribution's
        // Gamma/Beta cascade (Capital has a principal industry, so it takes the non-PI-less branch)
        // and Production building all seven ship types from one developed ShipyardGeneral level.
        new(Name: "FullPipelineCapitalWorld", Class: WorldClass.EarthLike, Type: WorldType.Capital,
            Population: 1000, Efficiency: 100, Tech: TechLevel.Gate, AmbAddict: false, AllShipsUnlocked: true,
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
            Population: 1000, Efficiency: 100, Tech: TechLevel.Gate, AmbAddict: false, AllShipsUnlocked: false,
            IndusBio: 100, IndusChe: 0, IndusMin: 0, IndusSYG: 0, IndusSYJ: 0, IndusSYS: 0, IndusSYT: 0,
            IndusSup: 0, IndusTri: 0,
            CargoMen: 0, CargoNnj: 0, CargoAmb: 5, CargoChe: 5000, CargoMet: 5000, CargoSup: 5000, CargoTri: 5000,
            TrillumReserve: 5000),
    ];

    /// <summary>MethodDataSource shape for AnnualTickHandlerProductionTests.MatchesGoldenFile — one
    /// Func per case, per TUnit's guidance for reference types.</summary>
    public static IEnumerable<Func<ProductionCase>> AsDataSource() => All.Select(c => (Func<ProductionCase>)(() => c));
}
