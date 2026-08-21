using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs shared between the golden-file generator (GoldenFileTests, [Explicit], requires
/// fpc) and the always-on AnnualTickHandlerRevolutionTests.MatchesGoldenFile. Only inputs
/// live here — expected outputs live exclusively in reference/verify/golden/revolution.golden,
/// computed by a real FreePascal run of reference/verify/revolution.pas, never hand-typed.
///
/// revolution.pas's UpdateRevolutionScenario starts exactly at the point UpdateRevolution itself
/// starts — it does NOT run UpdatePopulation/UpdateEfficiency first, unlike RunAnnualTick, which runs
/// the whole UpdateWorld pipeline (RunProductionPipeline, UpdateEfficiency, UpdatePopulation,
/// UseUpFood, UseUpAmbrosia, UpdateMilitary, UpdateRevolution, in that order) first. So every case
/// tracks two population values: PlanetPop (fed to the C# Planet before RunAnnualTick) and HarnessPop
/// (what UpdatePopulation will have turned it into by the time UpdateRevolution actually runs,
/// hand-derived below) — feeding PlanetPop straight to the harness would silently double-apply the
/// population adjustment. UpdateMilitary needs no such split on the Legions side: since it runs
/// immediately before UpdateRevolution in the real pipeline (UPDATE.PAS:1386-1388),
/// UpdateRevolutionScenario itself chains Common's UpdateMilitaryScenario first and uses ITS output
/// as both the Military figure and Rebellion's starting Cargo[men] — Legions below is always the
/// pre-UpdateMilitary value, same as what's fed to the C# Planet.
/// Efficiency needs no such split: every case uses 80 or 100, both no-ops under UpdateEfficiency at
/// FixedRandom(0) (only brackets &lt;=75 actually increment) — confirmed per case below, not assumed.
/// Industry stays at its zero default throughout, so RunProductionPipeline can't perturb Cargo/
/// Population first, and every case supplies Cargo.Supplies=9999 so UseUpFood can't starve it either.
/// </summary>
public sealed record RevolutionCase(
    string Name, int PlanetPop, int HarnessPop, WorldClass Class, TechLevel Tech,
    int Efficiency, int RevIndex, int Legions, int Ninja, WorldType Type) : INamedCase;

internal static class RevolutionCases
{
    public static readonly IReadOnlyList<RevolutionCase> All = [
        // Pop(2350)>MaxPop[Barren](2340) -> Rnd(-10,10)=-10 -> HarnessPop=2340. Eff=80 falls in
        // UpdateEfficiency's <=90 bracket (Rnd(0,3)=0) -> no-op.
        new(Name: "SanityAnchor", PlanetPop: 2350, HarnessPop: 2340, Class: WorldClass.Barren,
            Tech: TechLevel.Warp, Efficiency: 80, RevIndex: 95, Legions: 0, Ninja: 0,
            Type: WorldType.Agricultural),

        // Pop(10)<75 -> Rnd(2,5)=2 -> HarnessPop=12, regardless of Class/Tech (that branch never
        // looks at MaxPop/BasePop). Eff=100 has no UpdateEfficiency bracket -> falls to the default 0
        // increment -> no-op. Tiny population -> small sqrt(Pop) in Rebellion.
        new(Name: "TinyPopulation", PlanetPop: 10, HarnessPop: 12, Class: WorldClass.EarthLike,
            Tech: TechLevel.Gate, Efficiency: 100, RevIndex: 95, Legions: 0, Ninja: 0,
            Type: WorldType.Agricultural),

        // Pop(50000)>MaxPop[EarthLike](4500) -> Rnd(-10,10)=-10 -> HarnessPop=49990. Eff=80 is again
        // a no-op (<=90 bracket). Legions=5000 makes Military(5000) exceed OptimumMilitary (~1500 for
        // this Population/Type) -> exercises the military-suppression branch the roadmap flagged as
        // faithful-but-never-tested (UPDATE.PAS:715-735), which then also triggers (and this time
        // suppresses) a Rebellion call.
        new(Name: "MilitarySuppression", PlanetPop: 50000, HarnessPop: 49990, Class: WorldClass.EarthLike,
            Tech: TechLevel.Gate, Efficiency: 80, RevIndex: 95, Legions: 5000, Ninja: 0,
            Type: WorldType.Agricultural),

        // Pop(50)<75 -> Rnd(2,5)=2 -> HarnessPop=52. RevIndex never exceeds 75 -> Rebellion never fires.
        new(Name: "LowRevIndexNoRebellion", PlanetPop: 50, HarnessPop: 52, Class: WorldClass.EarthLike,
            Tech: TechLevel.Gate, Efficiency: 100, RevIndex: 50, Legions: 0, Ninja: 0,
            Type: WorldType.Agricultural),
    ];

    /// <summary>MethodDataSource shape for AnnualTickHandlerRevolutionTests.MatchesGoldenFile — one
    /// Func per case, per TUnit's guidance for reference types.</summary>
    public static IEnumerable<Func<RevolutionCase>> AsDataSource() => All.Select(c => (Func<RevolutionCase>)(() => c));
}
