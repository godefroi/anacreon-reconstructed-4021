using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs shared between the golden-file generator (GoldenFileTests, requires fpc and git —
/// dynamically skipped otherwise) and the always-on AnnualTickHandlerRevolutionTests.MatchesGoldenFile.
/// Only inputs live here — expected outputs live exclusively in reference/verify/golden/revolution.golden,
/// computed patch-based: a real run of the actual, only-minimally-touched UpdateWorld against a
/// hand-assembled Universe^ (reference/verify/runworld.pas's revolution domain), not a
/// per-procedure transcription.
///
/// UPDATE.PAS:905/971's ReportPlanetLack call (a +1 RevolutionIndex bump the first time a resource
/// type is reported short in a tick, triggered by UpdateIndustry/Production's real
/// raw-material-shortfall checks) is modeled here too — see <c>AnnualTickHandler.ReportResourceShortfall</c>.
///
/// Every case uses Type=Agricultural/Ninja=0 (hardcoded in the driver, matching every case here) and
/// is Owned with its own capital pointing at itself — CapitalTech always equals Tech, so
/// UpdateTechLevel can never drift TechLevel mid-tick, the same "capital tech level never actually
/// read" outcome the C# test gets from its own empire having no Capital at all.
/// </summary>
public sealed record RevolutionCase(
    string Name, int PlanetPop, WorldClass Class, TechLevel Tech,
    int Efficiency, int RevIndex, int Legions, WorldType Type) : INamedCase;

internal static class RevolutionCases
{
    public static readonly IReadOnlyList<RevolutionCase> All = [
        // Pop(2350)>MaxPop[Barren](2340) -> Rnd(-10,10)=-10 -> population becomes 2340 through
        // UpdatePopulation. Eff=80 falls in UpdateEfficiency's <=90 bracket (Rnd(0,3)=0) -> no-op.
        new(Name: "SanityAnchor", PlanetPop: 2350, Class: WorldClass.Barren,
            Tech: TechLevel.Warp, Efficiency: 80, RevIndex: 95, Legions: 0, Type: WorldType.Agricultural),

        // Pop(10)<75 -> Rnd(2,5)=2 -> population becomes 12, regardless of Class/Tech (that branch
        // never looks at MaxPop/BasePop). Eff=100 has no UpdateEfficiency bracket -> falls to the
        // default 0 increment -> no-op. Tiny population -> small sqrt(Pop) in Rebellion.
        new(Name: "TinyPopulation", PlanetPop: 10, Class: WorldClass.EarthLike,
            Tech: TechLevel.Gate, Efficiency: 100, RevIndex: 95, Legions: 0, Type: WorldType.Agricultural),

        // Pop(50000)>MaxPop[EarthLike](4500) -> Rnd(-10,10)=-10 -> population becomes 49990. Eff=80 is
        // again a no-op (<=90 bracket). Legions=5000 makes Military exceed OptimumMilitary (~1500 for
        // this Population/Type) -> exercises the military-suppression branch the roadmap flagged as
        // faithful-but-never-tested (UPDATE.PAS:715-735), which then also triggers (and this time
        // suppresses) a Rebellion call.
        new(Name: "MilitarySuppression", PlanetPop: 50000, Class: WorldClass.EarthLike,
            Tech: TechLevel.Gate, Efficiency: 80, RevIndex: 95, Legions: 5000, Type: WorldType.Agricultural),

        // Pop(50)<75 -> Rnd(2,5)=2 -> population becomes 52. RevIndex never exceeds 75 -> Rebellion
        // never fires.
        new(Name: "LowRevIndexNoRebellion", PlanetPop: 50, Class: WorldClass.EarthLike,
            Tech: TechLevel.Gate, Efficiency: 100, RevIndex: 50, Legions: 0, Type: WorldType.Agricultural),
    ];

    /// <summary>MethodDataSource shape for AnnualTickHandlerRevolutionTests.MatchesGoldenFile — one
    /// Func per case, per TUnit's guidance for reference types.</summary>
    public static IEnumerable<Func<RevolutionCase>> AsDataSource() => All.Select(c => (Func<RevolutionCase>)(() => c));
}
