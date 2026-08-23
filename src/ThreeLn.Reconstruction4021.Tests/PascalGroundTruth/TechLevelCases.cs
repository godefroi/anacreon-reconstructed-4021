using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs shared between the golden-file generator (GoldenFileTests, requires fpc and git —
/// dynamically skipped otherwise) and the always-on AnnualTickHandlerTechLevelTests.MatchesGoldenFile. Only inputs live
/// here — expected outputs live exclusively in reference/verify/golden/techlevel.golden, computed
/// patch-based: a real run of the actual, only-minimally-touched UpdateWorld against a hand-assembled
/// Universe^ (reference/verify/runworld.pas), not a per-procedure transcription — never
/// hand-typed either way. IsIndependent/CapitalTech here still stand in for the C# side's own
/// Owner.IsIndependent/Owner.Capital, but on the Pascal side they now drive a real Emp=Indep check and
/// a real GetCapital/GetTech lookup, not a parameterized stand-in for that logic.
///
/// Unlike RevolutionCase/MilitaryCase, UpdateTechLevel's own formula never reads Population — so
/// there's no PlanetPop/HarnessPop split needed here. Every case still uses a tiny Population (10),
/// which always takes UpdatePopulation's "&lt;75" branch (Rnd(2,5), independent of Class/Tech/MaxPop/
/// BasePop) so growth can never accidentally depend on whatever TechLevel this test is exercising, and
/// Efficiency=100 keeps UpdateEfficiency a no-op regardless of RNG. CapitalTech is unused (but still
/// present in each Independent case, matching the harness's fixed 4-field CLI shape) since
/// UpdateTechLevel's Independent branch never reads it.
/// </summary>
public sealed record TechLevelCase(
    string Name, TechLevel Tech, bool IsIndependent, TechLevel CapitalTech, int RngFixedValue) : INamedCase;

internal static class TechLevelCases
{
    public static readonly IReadOnlyList<TechLevelCase> All = [
        // Independent world: Rnd(1,50)=1+0=1 at RngFixedValue=0 always hits the 1-in-50 advance roll.
        new(Name: "IndependentWorldAdvances", Tech: TechLevel.Warp, IsIndependent: true,
            CapitalTech: TechLevel.PreTech, RngFixedValue: 0),

        // Already at the ceiling (Gate) -> the "Tech<>GteTchLvl" guard makes this a no-op regardless
        // of IsIndependent/RngFixedValue.
        new(Name: "IndependentWorldAtGateNeverAdvances", Tech: TechLevel.Gate, IsIndependent: true,
            CapitalTech: TechLevel.PreTech, RngFixedValue: 0),

        // Owned, behind its capital (Jump>Warp): Rnd(1,100)=1+0=1<=TechLvlInc(16) -> advances.
        new(Name: "OwnedWorldBehindCapitalAdvances", Tech: TechLevel.Warp, IsIndependent: false,
            CapitalTech: TechLevel.Jump, RngFixedValue: 0),

        // Same gap as above, but RngFixedValue=20 -> Rnd(1,100)=21>16 -> the roll fails, no advance.
        new(Name: "OwnedWorldBehindCapitalNoAdvanceWhenRollFails", Tech: TechLevel.Warp, IsIndependent: false,
            CapitalTech: TechLevel.Jump, RngFixedValue: 20),

        // Owned, ahead of its capital (PreWarp<Warp): Rnd(1,15)=1+0=1 -> hits the 1-in-15 regression roll.
        new(Name: "OwnedWorldAheadOfCapitalRegresses", Tech: TechLevel.Warp, IsIndependent: false,
            CapitalTech: TechLevel.PreWarp, RngFixedValue: 0),

        // Same gap as above, but RngFixedValue=5 -> Rnd(1,15)=6≠1 -> the roll fails, no regression.
        new(Name: "OwnedWorldAheadOfCapitalNoRegressWhenRollFails", Tech: TechLevel.Warp, IsIndependent: false,
            CapitalTech: TechLevel.PreWarp, RngFixedValue: 5),

        // Owned, exactly at its capital's tech level -> neither branch's condition is true -> no-op
        // regardless of RNG (both CapitalTech>Tech and CapitalTech<Tech are false).
        new(Name: "OwnedWorldAtCapitalTechIsNoOp", Tech: TechLevel.Warp, IsIndependent: false,
            CapitalTech: TechLevel.Warp, RngFixedValue: 0),

        // Owned, already at the ceiling -> the "Tech<>GteTchLvl" guard applies here too, independent
        // of the Independent branch tested above.
        new(Name: "OwnedWorldAtGateNeverAdvancesPastCeiling", Tech: TechLevel.Gate, IsIndependent: false,
            CapitalTech: TechLevel.Gate, RngFixedValue: 0),
    ];

    /// <summary>MethodDataSource shape for AnnualTickHandlerTechLevelTests.MatchesGoldenFile — one
    /// Func per case, per TUnit's guidance for reference types.</summary>
    public static IEnumerable<Func<TechLevelCase>> AsDataSource() => All.Select(c => (Func<TechLevelCase>)(() => c));
}
