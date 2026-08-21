using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs shared between the golden-file generator (GoldenFileTests, [Explicit], requires fpc)
/// and the always-on AnnualTickHandlerMilitaryTests.MatchesGoldenFile. Only inputs live here —
/// expected outputs live exclusively in reference/verify/golden/military.golden, computed by a real
/// FreePascal run of reference/verify/military.pas, never hand-typed.
///
/// military.pas's UpdateMilitaryScenario starts exactly at the point UpdateMilitary itself starts —
/// it does NOT run UpdatePopulation first, unlike RunAnnualTick, which runs the whole UpdateWorld
/// pipeline (production, efficiency, population, food, ambrosia, THEN military) first. So every case
/// tracks two population values: PlanetPop (fed to the C# Planet before RunAnnualTick) and HarnessPop
/// (what UpdatePopulation will have turned it into by the time UpdateMilitary actually runs, hand-
/// derived below and cross-checked against a direct fpc run) — feeding PlanetPop straight to the
/// harness would silently double-apply the population adjustment. Tech=PreTech (BasePop=3) is used
/// wherever PlanetPop already sits above 75: UpdatePopulation's "population &gt; BasePop" branch then
/// adds PascalRound(3/100)=0, a no-RNG, no-op growth step for any PlanetPop in (3,MaxPop] — which
/// means those cases are insensitive to RngFixedValue, letting BelowOptimumGrows and
/// RandomOffsetShiftsOptimum share the exact same PlanetPop/HarnessPop despite different RNG. The
/// other two cases fall into UpdatePopulation's other deterministic-under-FixedRandom(0) branches
/// (see each case's comment). Efficiency=100 keeps UpdateEfficiency a no-op regardless of RNG (its
/// switch has no bracket above 99); Cargo.Supplies=9999 keeps UseUpFood from starving any of these
/// populations. Every case's Type is Agricultural or Base (never Capital/Base-adjacent exclusions
/// that would change UpdateRevolution's branching in a way that touches Cargo.Legions) and starts
/// RevolutionIndex at its class default of 0, low enough that no single tick's adjustments can reach
/// Rebellion's &gt;75 threshold — confirmed per case below, not assumed — so UpdateRevolution (which
/// runs immediately after UpdateMilitary in the same tick) can never overwrite the Cargo.Legions value
/// this test is actually checking.
/// </summary>
public sealed record MilitaryCase(
    string Name, int PlanetPop, int HarnessPop, WorldClass Class, TechLevel Tech,
    int Legions, WorldType Type, int RngFixedValue) : INamedCase;

internal static class MilitaryCases
{
    public static readonly IReadOnlyList<MilitaryCase> All = [
        // Tech=PreTech's ">BasePop" branch is a no-RNG, no-op growth step for any Population in
        // (3,MaxPop] -- PlanetPop(1500) stays HarnessPop(1500) unchanged. OptimumMilitary =
        // ClampResource(Jitter(PascalRound(1500/150*5)=50,10)) = 45 at RngFixedValue=0 (Jitter picks
        // the low end of [45,55]). Legions(0) < 45 -> grows. RevolutionIndex stays low: UpdateRevolution's
        // non-Capital decrease branch takes it to 0, then the military-suppression branch (military=0
        // still <= optimum=45) never fires, so RevIndex ends at 0 -- nowhere near Rebellion's 75.
        new(Name: "BelowOptimumGrows", PlanetPop: 1500, HarnessPop: 1500, Class: WorldClass.ClassM,
            Tech: TechLevel.PreTech, Legions: 0, Type: WorldType.Agricultural, RngFixedValue: 0),

        // Same optimum (45) but Legions(1000) already exceeds it -> UpdateMilitary is a no-op, left
        // exactly alone. UpdateRevolution's suppression branch DOES fire here (military=1000>45,
        // RevIndex(0) not >30, Type isn't Capital/Base, Rnd(1,5)=1 at FixedRandom(0) hits the 1-in-5
        // branch) and bumps RevIndex to 5 -- still nowhere near 75, so Rebellion never touches Legions.
        new(Name: "AboveOptimumNoOp", PlanetPop: 1500, HarnessPop: 1500, Class: WorldClass.ClassM,
            Tech: TechLevel.PreTech, Legions: 1000, Type: WorldType.Agricultural, RngFixedValue: 0),

        // Legions(45) exactly equals the optimum -> the strict "OptimumMilitary>MPop" guard is false,
        // so this is also a no-op (the boundary case: equal, not just above, must not grow). Same
        // suppression-branch bump to RevIndex=5 as AboveOptimumNoOp, same conclusion: no Rebellion.
        new(Name: "ExactlyAtOptimumNoOp", PlanetPop: 1500, HarnessPop: 1500, Class: WorldClass.ClassM,
            Tech: TechLevel.PreTech, Legions: 45, Type: WorldType.Agricultural, RngFixedValue: 0),

        // PlanetPop(0)<75 -> UpdatePopulation's Rnd(2,5)=2 at FixedRandom(0) -> HarnessPop=2,
        // regardless of Class/Tech (that branch never looks at MaxPop/BasePop). OptMilitary[Base]=200%:
        // raw=PascalRound(2/150*200)=3, Jitter(3,10)'s spread truncates to 0 so Rnd(3,3) returns
        // exactly 3 regardless of RNG -> OptimumMilitary=3. Legions(0)<3 triggers growth, but the
        // growth amount itself (2/10*(200/100)=0.4) truncates to 0 -- a real branch-taken-but-
        // no-visible-effect edge case, not the same as ExactlyAtOptimumNoOp's branch-never-taken one.
        // Type=Base is excluded from UpdateRevolution's Rnd(5,15) suppression bump entirely, so
        // RevIndex stays at its post-decrease value of 0.
        new(Name: "TinyPopulationGrowthTruncatesToZero", PlanetPop: 0, HarnessPop: 2, Class: WorldClass.ClassM,
            Tech: TechLevel.Warp, Legions: 0, Type: WorldType.Base, RngFixedValue: 0),

        // PlanetPop(15010) exceeds every WorldClass's MaxPop (highest is Forest at 4710), so
        // UpdatePopulation always takes the ">MaxPop" branch: Rnd(-10,10)=-10 at FixedRandom(0) ->
        // HarnessPop=15000. OptMilitary[Base]=200% pushes the raw optimum to PascalRound(15000/150*200)
        // =20000, which Jitter/ClampResource caps at MaxResources=9999 before the growth comparison --
        // exercises the clamp, not just the growth step. Same Type=Base exclusion as the case above
        // keeps UpdateRevolution from touching RevIndex enough to matter.
        new(Name: "HighOptimumPercentClamps", PlanetPop: 15010, HarnessPop: 15000, Class: WorldClass.ClassM,
            Tech: TechLevel.PreTech, Legions: 0, Type: WorldType.Base, RngFixedValue: 0),

        // Identical PlanetPop/Class/Tech/Legions/Type to BelowOptimumGrows, but RngFixedValue=3 --
        // since Tech=PreTech's growth branch has no RNG at all, HarnessPop is still 1500 despite the
        // different fixed value, isolating the change to Jitter's pick within OptimumMilitary's
        // [45,55] spread (45+3=48 here, vs 45 at RngFixedValue=0) and confirming the optimum -- not
        // just the deterministic growth amount, which ignores RNG entirely -- is wired through
        // correctly.
        new(Name: "RandomOffsetShiftsOptimum", PlanetPop: 1500, HarnessPop: 1500, Class: WorldClass.ClassM,
            Tech: TechLevel.PreTech, Legions: 0, Type: WorldType.Agricultural, RngFixedValue: 3),
    ];

    /// <summary>MethodDataSource shape for AnnualTickHandlerMilitaryTests.MatchesGoldenFile — one Func
    /// per case, per TUnit's guidance for reference types.</summary>
    public static IEnumerable<Func<MilitaryCase>> AsDataSource() => All.Select(c => (Func<MilitaryCase>)(() => c));
}
