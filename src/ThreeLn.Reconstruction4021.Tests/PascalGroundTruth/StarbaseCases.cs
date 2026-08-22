namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs shared between the golden-file generator (GoldenFileTests, requires fpc and git —
/// dynamically skipped otherwise) and the always-on AnnualTickHandlerStarbaseTests.MatchesGoldenFile.
/// Only inputs live here — expected outputs live exclusively in reference/verify/golden/starbase.golden,
/// computed patch-based: a real run of the actual, only-minimally-touched UpdateWorld against a
/// hand-assembled Universe^ (reference/verify/patch-based/runworld.pas's starbase domain), not a
/// per-procedure transcription. Covers only SupplyLink/SurplusLink's arithmetic — the one part of
/// Commit 4 with real Pascal-transcription risk (a formula this session read out of UPDATE.PAS itself,
/// not something exercised elsewhere); the eligibility/gating logic (wrong empire, wrong world type,
/// distance, Kind-gating, Rebellion) is pure C#-side filtering with no separate Pascal arithmetic to
/// cross-check, so it stays covered by AnnualTickHandlerStarbaseTests' other, hardcoded tests instead.
///
/// Every case uses a fixed fixture matching those hardcoded tests' own MakeComplex/
/// MakeRawMaterialPlanet exactly (Population=0 on both sides, ClassM/AgrTyp neighbor at Chebyshev
/// distance 1, Warp-tech BaseStarbase complex) — only the two Chemicals seed values and RngFixedValue
/// vary per case, since Cargo.Chemicals is the one field neither UpdateIndustry's Metal-only growth
/// cost nor Production's ship-tech-gated builds can touch (see the driver's own doc comment).
/// </summary>
public sealed record StarbaseCase(string Name, int StarbaseChemicals, int NeighborChemicals, int RngFixedValue) : INamedCase;

internal static class StarbaseCases
{
    public static readonly IReadOnlyList<StarbaseCase> All = [
        // Cargo[che](1000)>250 -> transfer = 1000 - Rnd(200,250) = 1000-200 = 800 at RngFixedValue=0.
        new(Name: "SupplyLinkPull", StarbaseChemicals: 0, NeighborChemicals: 1000, RngFixedValue: 0),

        // Neighbor seeded at exactly 250 -> SupplyLink's own ">250" check is false, contributing
        // nothing, isolating this case to SurplusLink alone: transfer = Min(9999-250, 10050-9999) = 51.
        new(Name: "SurplusLinkPush", StarbaseChemicals: 10050, NeighborChemicals: 250, RngFixedValue: 0),
    ];

    /// <summary>MethodDataSource shape for AnnualTickHandlerStarbaseTests.MatchesGoldenFile — one Func
    /// per case, per TUnit's guidance for reference types.</summary>
    public static IEnumerable<Func<StarbaseCase>> AsDataSource() => All.Select(c => (Func<StarbaseCase>)(() => c));
}
