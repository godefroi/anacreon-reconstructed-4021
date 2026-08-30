using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests.PascalGroundTruth;

/// <summary>
/// NEWGAME.PAS:813-820 (RandomTrillumReserves), relocated verbatim into the patched UPDATE.PAS
/// and called directly — no Universe^ state needed beyond what the harness brackets for symmetry
/// with every other domain. GalaxySetupTests.CreateWorld_ComputesTrillumReserveFromClassAndRegion
/// also covers this formula, with hand-derived hardcoded tests instead.
/// </summary>
public sealed record TrillumReservesCase(string Name, WorldClass Class, int RegionReserves, int RngFixedValue) : INamedCase;

internal static class TrillumReservesCases
{
    public static readonly IReadOnlyList<TrillumReservesCase> All = [
        // Same inputs as GalaxySetupTests.CreateWorld_ComputesTrillumReserveFromClassAndRegion's own
        // hand-derivation (601) — cross-checks that hand-derivation against real Pascal.
        new(Name: "EarthLikeBase100Rng0", Class: WorldClass.EarthLike, RegionReserves: 100, RngFixedValue: 0),
        new(Name: "BarrenBase0Rng5", Class: WorldClass.Barren, RegionReserves: 0, RngFixedValue: 5),
        new(Name: "PoisonousBase50Rng10", Class: WorldClass.Poisonous, RegionReserves: 50, RngFixedValue: 10),
    ];

    public static IEnumerable<Func<TrillumReservesCase>> AsDataSource() => All.Select(c => (Func<TrillumReservesCase>)(() => c));
}
