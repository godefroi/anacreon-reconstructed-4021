namespace Reconstructed4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs shared between the golden-file generator (GoldenFileTests, requires fpc and git —
/// dynamically skipped otherwise) and the always-on AnnualTickHandlerStarbaseTests.MatchesGoldenFile.
/// Only inputs live here — expected outputs live exclusively in reference/verify/golden/starbase.golden,
/// computed patch-based: a real run of the actual, only-minimally-touched UpdateWorld against a
/// hand-assembled Universe^ (reference/verify/runworld.pas's starbase domain), not a
/// per-procedure transcription. Covers only SupplyLink/SurplusLink's arithmetic — the one part of
/// the starbase branch with real Pascal-transcription risk (a formula read directly out of
/// UPDATE.PAS, not something exercised elsewhere); the eligibility/gating logic (wrong empire, wrong world type,
/// distance, Kind-gating, Rebellion) is pure C#-side filtering with no separate Pascal arithmetic to
/// cross-check, so it stays covered by AnnualTickHandlerStarbaseTests' other, hardcoded tests instead.
///
/// Every case uses a fixed fixture matching those hardcoded tests' own MakeComplex/
/// MakeRawMaterialPlanet exactly (Population=0 on both sides, ClassM/AgrTyp neighbor at Chebyshev
/// distance 1, Warp-tech BaseStarbase complex) — only the two Chemicals seed values and RngFixedValue
/// vary per case, since Cargo.Chemicals is the one field neither UpdateIndustry's Metal-only growth
/// cost nor Production's ship-tech-gated builds can touch (see the driver's own doc comment).
///
/// The empire's capital is the neighbor planet, at PreTech, so the starbase's tech can regress this
/// tick. StarbaseTech, Legions, TechnologyBitmask (bit i = TechnologyTypes(i+1); bits 0-3 are
/// LAM,def,GDM,ion) and MetalsAndTrillum (seeds both cargoes) let a case check which tech level gates
/// the starbase's defenses.
/// </summary>
public sealed record StarbaseCase(string Name, int StarbaseChemicals, int NeighborChemicals, int RngFixedValue, int? SecondNeighborChemicals = null,
    Core.Types.TechLevel StarbaseTech = Core.Types.TechLevel.Warp, int Legions = 0, int TechnologyBitmask = 0, int MetalsAndTrillum = 0) : INamedCase;

internal static class StarbaseCases
{
    public static readonly IReadOnlyList<StarbaseCase> All = [
        // Cargo[che](1000)>250 -> transfer = 1000 - Rnd(200,250) = 1000-200 = 800 at RngFixedValue=0.
        new(Name: "SupplyLinkPull", StarbaseChemicals: 0, NeighborChemicals: 1000, RngFixedValue: 0),

        // Neighbor seeded at exactly 250 -> SupplyLink's own ">250" check is false, contributing
        // nothing, isolating this case to SurplusLink alone: transfer = Min(9999-250, 10050-9999) = 51.
        new(Name: "SurplusLinkPush", StarbaseChemicals: 10050, NeighborChemicals: 250, RngFixedValue: 0),

        // Two neighbors, SE (6,6) and NW (4,4), both at 250 so SupplyLink pulls nothing. Surplus is
        // 20000-9999 = 10001; Pascal visits SE first (N,NE,E,SE,S,SW,W,NW), which takes
        // Min(9999-250, 10001) = 9749, leaving 252 for NW. A lexicographic walk visits NW first and
        // swaps the two results.
        new(Name: "SurplusLinkTwoNeighborsOrder", StarbaseChemicals: 20000, NeighborChemicals: 250, RngFixedValue: 0, SecondNeighborChemicals: 250),

        // The PreTech capital's Rnd(1,15)=1 regression roll drops the starbase Jump->Warp this tick,
        // below IonCannon's Jump threshold. UpdateWorld gates the starbase's defenses on the Technology
        // set from the tick-start Jump tech (UPDATE.PAS:1396-1401, 1429), so ion cannons still build
        // (issue #103).
        new(Name: "TechRegressesAcrossThresholdUsesTickStartTech", StarbaseChemicals: 5000, NeighborChemicals: 250, RngFixedValue: 0,
            StarbaseTech: Core.Types.TechLevel.Jump, Legions: 2000, TechnologyBitmask: 0b1111, MetalsAndTrillum: 5000),
    ];

    /// <summary>MethodDataSource shape for AnnualTickHandlerStarbaseTests.MatchesGoldenFile — one Func
    /// per case, per TUnit's guidance for reference types.</summary>
    public static IEnumerable<Func<StarbaseCase>> AsDataSource() => All.Select(c => (Func<StarbaseCase>)(() => c));
}
