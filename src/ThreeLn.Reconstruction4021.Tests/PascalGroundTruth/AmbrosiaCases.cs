using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs shared between the golden-file generator (GoldenFileTests, requires fpc and git —
/// dynamically skipped otherwise) and the always-on AnnualTickHandlerAmbrosiaTests.MatchesGoldenFile.
/// Only inputs live here — expected outputs live exclusively in reference/verify/golden/ambrosia.golden,
/// computed patch-based: a real run of the actual, only-minimally-touched UpdateWorld against a
/// hand-assembled Universe^ (reference/verify/runworld.pas's ambrosia domain), not a
/// per-procedure transcription.
///
/// PlanetPop alone is enough to drive every case: the real pipeline order (production, efficiency,
/// tech level, population, food, ambrosia) computes everything UseUpAmbrosia needs without a
/// separate hand-derived post-UpdatePopulation value.
///
/// Every case uses PlanetPop=1000/Class=ClassM/Type=Capital/Tech=Warp/Efficiency=100 (hardcoded in the
/// driver, matching every case here) — Type=Capital so Rebellion can never fire (UPDATE.PAS:751), and
/// Efficiency=100 keeps UpdateEfficiency's own switch (no bracket above 99) a no-op regardless of RNG,
/// so nothing else in the same tick's pipeline perturbs what these cases check.
/// </summary>
public sealed record AmbrosiaCase(
    string Name,
    int PlanetPop,
    TechLevel Tech,
    int Efficiency,
    int StartAmbrosia,
    bool StartAddicted,
    int RngFixedValue) : INamedCase;

internal static class AmbrosiaCases
{
    public static readonly IReadOnlyList<AmbrosiaCase> All = [
        new(Name: "SufficientSupply", PlanetPop: 1000, Tech: TechLevel.Warp,
            Efficiency: 100, StartAmbrosia: 200, StartAddicted: true, RngFixedValue: 0),
        new(Name: "ShortageNoEffect", PlanetPop: 1000, Tech: TechLevel.Warp,
            Efficiency: 100, StartAmbrosia: 50, StartAddicted: true, RngFixedValue: 0),
        new(Name: "ShortageRiot", PlanetPop: 1000, Tech: TechLevel.Warp,
            Efficiency: 100, StartAmbrosia: 50, StartAddicted: true, RngFixedValue: 4),
        new(Name: "ShortageTechRegression", PlanetPop: 1000, Tech: TechLevel.Warp,
            Efficiency: 100, StartAmbrosia: 50, StartAddicted: true, RngFixedValue: 9),
        new(Name: "BecomesAddicted", PlanetPop: 1000, Tech: TechLevel.Warp,
            Efficiency: 100, StartAmbrosia: 200, StartAddicted: false, RngFixedValue: 0),
        new(Name: "StaysUnaddicted", PlanetPop: 1000, Tech: TechLevel.Warp,
            Efficiency: 100, StartAmbrosia: 200, StartAddicted: false, RngFixedValue: 50),
    ];

    /// <summary>MethodDataSource shape for AnnualTickHandlerAmbrosiaTests.MatchesGoldenFile — one
    /// Func per case, per TUnit's guidance for reference types (https://tunit.dev/docs/writing-tests/method-data-source).</summary>
    public static IEnumerable<Func<AmbrosiaCase>> AsDataSource() => All.Select(c => (Func<AmbrosiaCase>)(() => c));
}
