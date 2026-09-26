using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs shared between the golden-file generator (GoldenFileTests) and the always-on
/// AnnualTickHandlerMaturationTests.MatchesGoldenFile. Expected outputs live exclusively in
/// reference/verify/golden/maturation.golden, computed by running the real, patched UpdateWorld
/// <see cref="Years"/> times in a row on one world (reference/verify/runworld.pas's maturation
/// domain), the multi-year counterpart to ProductionCases' single tick.
///
/// Every case is a real world state from a played game (Dol Parem, year 4077), taken as-is: nonzero
/// starting Defenses, per-world ISSP dials, thousands of legions, and scarce trillum/metals are what
/// make UpdateIndustry, Production and UpdateDefenses compete for the same raw materials over several
/// years, which no single-tick case exercises. Ships start at zero on both sides since the harness
/// has no ship inputs; nothing in UpdateWorld reads the ships already on hand.
///
/// Arrays are in Pascal declaration order: Industry BioInd..TriInd, Cargo men..tri, Issp Che/Min/Sup/
/// Tri, Defenses LAM/def/GDM/ion.
///
/// Years stays at 10: the single-world Pascal harness never runs UpdateEmpire, while RunAnnualTick's
/// empire step can advance the capital's tech level after enough years (seen at year 12 for
/// "DolParemCapital"), which would drift the two sides for a reason outside this domain.
/// </summary>
public sealed record MaturationCase(
    string Name, WorldClass Class, WorldType Type, TechLevel Tech, int Population, int Efficiency,
    int[] Industry, int[] Cargo, int TrillumReserve, int[] Issp, int[] Defenses) : INamedCase
{
    public const int Years = 10;
}

internal static class MaturationCases
{
    public static readonly IReadOnlyList<MaturationCase> All = [
        new(Name: "DolParemMidsizeIndependent", Class: WorldClass.ClassK, Type: WorldType.Independent, Tech: TechLevel.Starship,
            Population: 1401, Efficiency: 100, Industry: [0, 62, 78, 138, 0, 0, 0, 62, 39],
            Cargo: [836, 0, 0, 293, 0, 308, 53], TrillumReserve: 910, Issp: [5, 5, 5, 5], Defenses: [0, 132, 1286, 329]),

        // The one case with non-default ISSP dials, and trillum-starved with thousands of legions, so
        // UpdateDefenses' LAM build is throttled by trillum every year.
        new(Name: "DolParemCapital", Class: WorldClass.EarthLike, Type: WorldType.Capital, Tech: TechLevel.Starship,
            Population: 3408, Efficiency: 100, Industry: [0, 128, 161, 233, 0, 0, 0, 121, 93],
            Cargo: [6582, 0, 0, 6458, 3547, 9147, 4], TrillumReserve: 679, Issp: [6, 6, 6, 7], Defenses: [2900, 3976, 9999, 4342]),

        new(Name: "DolParemBase", Class: WorldClass.EarthLike, Type: WorldType.Base, Tech: TechLevel.Starship,
            Population: 2237, Efficiency: 100, Industry: [0, 89, 112, 199, 0, 0, 0, 80, 56],
            Cargo: [3527, 0, 0, 300, 92, 561, 1786], TrillumReserve: 840, Issp: [5, 5, 5, 5], Defenses: [730, 1952, 6138, 598]),

        // A young world with no metals: mining 3 makes ~1 metal a year, so industry never grows on
        // its own. The next case is the same world with a metals/chemicals delivery.
        new(Name: "DolParemYoungWorldNoMetals", Class: WorldClass.ClassJ, Type: WorldType.Independent, Tech: TechLevel.Starship,
            Population: 209, Efficiency: 100, Industry: [0, 2, 3, 2, 0, 0, 0, 24, 1],
            Cargo: [239, 0, 0, 0, 0, 71, 33], TrillumReserve: 867, Issp: [5, 5, 5, 5], Defenses: [0, 0, 83, 0]),

        new(Name: "DolParemYoungWorldResupplied", Class: WorldClass.ClassJ, Type: WorldType.Independent, Tech: TechLevel.Starship,
            Population: 209, Efficiency: 100, Industry: [0, 2, 3, 2, 0, 0, 0, 24, 1],
            Cargo: [239, 0, 0, 500, 1000, 71, 33], TrillumReserve: 867, Issp: [5, 5, 5, 5], Defenses: [0, 0, 83, 0]),
    ];

    /// <summary>MethodDataSource shape for AnnualTickHandlerMaturationTests.MatchesGoldenFile.</summary>
    public static IEnumerable<Func<MaturationCase>> AsDataSource() => All.Select(c => (Func<MaturationCase>)(() => c));
}
