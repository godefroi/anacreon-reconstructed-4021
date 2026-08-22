using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs shared between the golden-file generator (GoldenFileTests, requires fpc — dynamically
/// skipped otherwise) and the always-on AnnualTickHandlerAmbrosiaTests.MatchesGoldenFile. Only inputs live here —
/// expected outputs live exclusively in reference/verify/golden/ambrosia.golden, computed by a real
/// FreePascal run of ambrosia.pas, never hand-typed.
/// </summary>
public sealed record AmbrosiaCase(
    string Name,
    int PlanetPop,    // fed to the C# Planet before RunAnnualTick
    int HarnessPop,   // the post-UpdatePopulation value UseUpAmbrosia actually starts from
    TechLevel Tech,
    int Efficiency,
    int StartAmbrosia,
    bool StartAddicted,
    int RngFixedValue) : INamedCase;

internal static class AmbrosiaCases
{
    // All cases use Class=ClassM/Type=Capital/Efficiency=100 (see AnnualTickHandlerAmbrosiaTests's
    // header comment for why that combination makes every other UpdateWorld step this tick a
    // deterministic no-op except UseUpAmbrosia itself), and Tech=Warp/PlanetPop=1000, which lands
    // UpdatePopulation in the deterministic "current>basePop" branch: +PascalRound(700/100)=+7,
    // no RNG involved -> HarnessPop=1007.
    public static readonly IReadOnlyList<AmbrosiaCase> All = [
        new(Name: "SufficientSupply", PlanetPop: 1000, HarnessPop: 1007, Tech: TechLevel.Warp,
            Efficiency: 100, StartAmbrosia: 200, StartAddicted: true, RngFixedValue: 0),
        new(Name: "ShortageNoEffect", PlanetPop: 1000, HarnessPop: 1007, Tech: TechLevel.Warp,
            Efficiency: 100, StartAmbrosia: 50, StartAddicted: true, RngFixedValue: 0),
        new(Name: "ShortageRiot", PlanetPop: 1000, HarnessPop: 1007, Tech: TechLevel.Warp,
            Efficiency: 100, StartAmbrosia: 50, StartAddicted: true, RngFixedValue: 4),
        new(Name: "ShortageTechRegression", PlanetPop: 1000, HarnessPop: 1007, Tech: TechLevel.Warp,
            Efficiency: 100, StartAmbrosia: 50, StartAddicted: true, RngFixedValue: 9),
        new(Name: "BecomesAddicted", PlanetPop: 1000, HarnessPop: 1007, Tech: TechLevel.Warp,
            Efficiency: 100, StartAmbrosia: 200, StartAddicted: false, RngFixedValue: 0),
        new(Name: "StaysUnaddicted", PlanetPop: 1000, HarnessPop: 1007, Tech: TechLevel.Warp,
            Efficiency: 100, StartAmbrosia: 200, StartAddicted: false, RngFixedValue: 50),
    ];

    /// <summary>MethodDataSource shape for AnnualTickHandlerAmbrosiaTests.MatchesGoldenFile — one
    /// Func per case, per TUnit's guidance for reference types (https://tunit.dev/docs/writing-tests/method-data-source).</summary>
    public static IEnumerable<Func<AmbrosiaCase>> AsDataSource() => All.Select(c => (Func<AmbrosiaCase>)(() => c));
}
