using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs shared between the golden-file generator (GoldenFileTests) and the always-on
/// AnnualTickHandlerDefensesTests.MatchesGoldenFile. Expected outputs live exclusively in
/// reference/verify/golden/defenses.golden, computed patch-based: a real run of the actual,
/// only-minimally-touched UpdateWorld against a hand-assembled Universe^ (reference/verify/
/// runworld.pas's defenses domain), covering UpdateDefenses (UPDATE.PAS:1278-1351).
///
/// Every case is Owned (Empire1) with its own capital pointing at itself, matching MilitaryCase's
/// own "CapitalTech always equals Tech" trick so UpdateTechLevel can't drift Tech mid-tick. The real
/// UpdateMilitary step still runs first in the same real UpdateWorld pipeline and may grow
/// Cargo.Legions toward its own optimum before UpdateDefenses ever reads TroopStrength — not
/// shielded against; whatever the real pipeline produces end to end is what both sides compare
/// against, the same principle MilitaryCase's own doc comment explains. Planet-only: the three
/// starbase-specific multipliers (Outpost quarters Optimum and zeroes DefenseSatellite; CommandBase/
/// Fortress quadruple both Optimum and BuildRate) aren't reachable through this domain's ObjTyp=Pln
/// harness — covered instead by hardcoded tests in AnnualTickHandlerDefensesTests, the same split
/// AnnualTickHandlerStarbaseTests already uses for starbase-only behavior with no sqrt/pow cascade
/// worth a Pascal round-trip.
/// </summary>
public sealed record DefensesCase(
    string Name, int PlanetPop, TechLevel Tech, int Legions, int NinjaLegions, WorldType Type,
    int Efficiency, int CargoChe, int CargoMet, int CargoTri, int TechnologyBitmask, int RngFixedValue) : INamedCase;

internal static class DefensesCases
{
    // TechnologyBitmask bit i = TechnologyTypes(i+1) (LAM..dis, skipping NoRes) — same 26-bit
    // encoding EmpireCases/EmpireFactoryCases already use. Bits 0-3 are LAM,def,GDM,ion.
    private const int AllFourDefensesResearched = 0b1111;
    private const int NothingResearched = 0;

    public static readonly IReadOnlyList<DefensesCase> All = [
        // Capital allows LAM; Starship tech clears every MinTechForDefense threshold
        // (Gdm=Atomic,Ion=Jump,DefSat=Bio,Lam=Starship); abundant raw material never clamps Build.
        new(Name: "AllResearchedAmpleMaterialsGrowsAllFour", PlanetPop: 20000, Tech: TechLevel.Starship,
            Legions: 100, NinjaLegions: 0, Type: WorldType.Capital, Efficiency: 50,
            CargoChe: 99999, CargoMet: 99999, CargoTri: 99999,
            TechnologyBitmask: AllFourDefensesResearched, RngFixedValue: 0),

        // Identical inputs except Type=Agricultural — UPDATE.PAS:1322's "Typ IN [BseTyp,CapTyp]" gate
        // forces LAM's OptimumDef to 0 regardless of tech/research, while def/GDM/ion are unaffected
        // by this specific gate (their own magnitude still shifts vs. the Capital case only because
        // WorldType also changes UpdateMilitary's own optimum-military percentage upstream).
        new(Name: "LamZeroedOnNonBaseCapitalWorld", PlanetPop: 20000, Tech: TechLevel.Starship,
            Legions: 100, NinjaLegions: 0, Type: WorldType.Agricultural, Efficiency: 50,
            CargoChe: 99999, CargoMet: 99999, CargoTri: 99999,
            TechnologyBitmask: AllFourDefensesResearched, RngFixedValue: 0),

        // Nothing researched -> DefenseTechAvailable's "IsIndependent || Technology.Defenses.Contains"
        // half fails for every type regardless of tech level -> Defns stays 0 across the board.
        new(Name: "NothingResearchedStaysZero", PlanetPop: 20000, Tech: TechLevel.Starship,
            Legions: 100, NinjaLegions: 0, Type: WorldType.Capital, Efficiency: 50,
            CargoChe: 99999, CargoMet: 99999, CargoTri: 99999,
            TechnologyBitmask: NothingResearched, RngFixedValue: 0),

        // Everything "researched" (bitmask=15) but world Tech=Jump only clears Gdm(Atomic) and
        // Ion(Jump)'s thresholds, not DefenseSatellite(Bio) or Lam(Starship) -- isolates
        // DefenseTechAvailable's per-world TechDev[Tech] half from the empire-research half above.
        // Type=Capital keeps LAM's own Base/Capital gate from confounding the result.
        new(Name: "WorldTechLevelBelowThresholdBlocksDespiteResearch", PlanetPop: 20000, Tech: TechLevel.Jump,
            Legions: 100, NinjaLegions: 0, Type: WorldType.Capital, Efficiency: 50,
            CargoChe: 99999, CargoMet: 99999, CargoTri: 99999,
            TechnologyBitmask: AllFourDefensesResearched, RngFixedValue: 0),

        // che/met/tri=1 each -- Build gets clamped down to what 1 unit of the scarcest input affords,
        // and LAM (first in DefI's LAM..ion loop order) consumes what little che exists before
        // def/GDM/ion ever get a turn, leaving them clamped to 0 -- a real sequential draw-down
        // dependency, not four independent checks against the same untouched cargo.
        new(Name: "RawMaterialShortageClampsBuildInLoopOrder", PlanetPop: 20000, Tech: TechLevel.Starship,
            Legions: 100, NinjaLegions: 0, Type: WorldType.Capital, Efficiency: 50,
            CargoChe: 1, CargoMet: 1, CargoTri: 1,
            TechnologyBitmask: AllFourDefensesResearched, RngFixedValue: 0),
    ];

    /// <summary>MethodDataSource shape for AnnualTickHandlerDefensesTests.MatchesGoldenFile.</summary>
    public static IEnumerable<Func<DefensesCase>> AsDataSource() => All.Select(c => (Func<DefensesCase>)(() => c));
}
