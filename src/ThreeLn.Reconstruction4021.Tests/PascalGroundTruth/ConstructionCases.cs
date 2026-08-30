using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs shared between the golden-file generator (GoldenFileTests, requires fpc and git —
/// dynamically skipped otherwise) and the always-on AnnualTickHandlerConstructionTests.MatchesGoldenFile.
/// Only inputs live here — expected outputs live exclusively in reference/verify/golden/
/// construction.golden, computed patch-based: runworld.pas's construction domain calls
/// UpdateConstruction directly (restored in UPDATE.PAS.patch, alongside ConstructStarbase/
/// ConstructStargate and five small Intrface-only helpers relocated verbatim — see runworld.pas's
/// own header comment).
///
/// Both fleets are always at the same location as the construction site, owned by the same empire —
/// eligibility filtering (location/ownership matching) is pure C#-side LINQ with no separate Pascal
/// formula to cross-check, so it's covered by hardcoded tests instead (see
/// AnnualTickHandlerConstructionTests). What's golden-file-backed here is UseUpRawMaterial's own
/// draw-down arithmetic and, for the two completion cases, GetOptimumIndus's sqrt/pow cascade
/// (infeasible to hand-trace reliably, same reason ProductionCases leans on the Pascal harness).
/// </summary>
public sealed record ConstructionFleet(int Chemicals, int Metals, int Trillum);

public sealed record ConstructionCase(
    string Name, ConstructionType Building, int YearsToCompletion, TechLevel OwnerTechLevel, int RngFixedValue,
    ConstructionFleet? Fleet1, ConstructionFleet? Fleet2) : INamedCase;

internal static class ConstructionCases
{
    public static readonly IReadOnlyList<ConstructionCase> All = [
        // Minefield needs Che=110,Met=500,Tri=80 (DATACNST.PAS:539-548) — one fleet has plenty of
        // everything: countdown decrements, cargo drawn down by exactly the needed amounts.
        new(Name: "EnoughMaterialDecrementsCountdown", Building: ConstructionType.Minefield,
            YearsToCompletion: 2, OwnerTechLevel: TechLevel.PreTech, RngFixedValue: 0,
            Fleet1: new ConstructionFleet(Chemicals: 200, Metals: 600, Trillum: 100), Fleet2: null),

        // Same fleet amounts, but Chemicals (50) falls short of the 110 needed — proves the
        // scratch-copy-discard behavior: Metals/Trillum, individually sufficient, are NOT drawn down
        // either once Chemicals comes up short (UPDATE.PAS's goto ExitLoop discards the whole scratch
        // copy, not just the shortfall type), and the countdown itself doesn't decrement.
        new(Name: "InsufficientMaterialLeavesEverythingUnchanged", Building: ConstructionType.Minefield,
            YearsToCompletion: 2, OwnerTechLevel: TechLevel.PreTech, RngFixedValue: 0,
            Fleet1: new ConstructionFleet(Chemicals: 50, Metals: 600, Trillum: 100), Fleet2: null),

        // Neither fleet alone has enough of anything, but together they clear all three thresholds —
        // drawn down in fleet-list order (fleet 1 first, fleet 2 covers the remainder).
        new(Name: "TwoFleetsJointlySupplyMaterial", Building: ConstructionType.Minefield,
            YearsToCompletion: 2, OwnerTechLevel: TechLevel.PreTech, RngFixedValue: 0,
            Fleet1: new ConstructionFleet(Chemicals: 60, Metals: 300, Trillum: 50),
            Fleet2: new ConstructionFleet(Chemicals: 60, Metals: 300, Trillum: 50)),

        // YearsToCompletion=1 -> decrements to 0 this tick -> completion. Minefield is the simplest
        // completion branch: no new entity, just Galaxy.SetMine.
        new(Name: "CompletionCreatesMinefield", Building: ConstructionType.Minefield,
            YearsToCompletion: 1, OwnerTechLevel: TechLevel.PreTech, RngFixedValue: 0,
            Fleet1: new ConstructionFleet(Chemicals: 200, Metals: 600, Trillum: 100), Fleet2: null),

        // Gate needs Che=2530,Met=3920,Tri=1450 — the most expensive construction type, chosen so the
        // fleet amounts don't collide with any other case's numbers. Stargate creation has no RNG and
        // no sqrt/pow cascade (LinkedTo stays null), included mainly to exercise the Stargate branch
        // of UpdateConstruction's completion dispatch at all.
        new(Name: "CompletionCreatesStargate", Building: ConstructionType.Gate,
            YearsToCompletion: 1, OwnerTechLevel: TechLevel.PreTech, RngFixedValue: 0,
            Fleet1: new ConstructionFleet(Chemicals: 3000, Metals: 4000, Trillum: 1500), Fleet2: null),

        // IndustrialComplex needs Che=590,Met=2600,Tri=150. RngFixedValue=0 makes
        // Efficiency=Rnd(10,20)=10 and Population=Rnd(400,700)=400 deterministic; the resulting
        // Industry levels (via GetOptimumIndustry -> GetIndustrialDistribution's sqrt/pow cascade for
        // an Artificial-class, Base-type world) are exactly the kind of formula this test suite leans
        // on the real Pascal harness for rather than hand-tracing.
        new(Name: "CompletionCreatesIndustrialComplexStarbase", Building: ConstructionType.IndustrialComplex,
            YearsToCompletion: 1, OwnerTechLevel: TechLevel.Starship, RngFixedValue: 0,
            Fleet1: new ConstructionFleet(Chemicals: 700, Metals: 2700, Trillum: 200), Fleet2: null),
    ];

    /// <summary>MethodDataSource shape for AnnualTickHandlerConstructionTests.MatchesGoldenFile — one
    /// Func per case, per TUnit's guidance for reference types.</summary>
    public static IEnumerable<Func<ConstructionCase>> AsDataSource() => All.Select(c => (Func<ConstructionCase>)(() => c));
}
