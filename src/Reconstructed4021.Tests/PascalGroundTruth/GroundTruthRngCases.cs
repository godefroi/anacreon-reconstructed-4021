namespace Reconstructed4021.Tests.PascalGroundTruth;

/// <summary>
/// Standing regression fixture for <see cref="GroundTruthRandom"/> against its Pascal twin
/// (INT.PAS.patch's GroundTruthSeed/GroundTruthNextU32, driven here via runworld.pas's
/// RunGroundTruthRngCase) -- the generator Rnd's real (non-ForcedRandomValue) branch now draws from,
/// this project's own replacement for reverse-engineering fpc's real Random (the now-deleted
/// PascalRandom.cs/RngCases.cs, which this fixture's seeds/ranges/counts are carried over from, so
/// the two read as obvious counterparts in git history). Each case's own Seed/Range/Count also drives
/// an independent check of <see cref="GroundTruthRandom.NextDouble"/> against
/// <c>GroundTruthRandomReal</c> (the bare-Random replacement), not just <c>Next(maxValue)</c>.
/// </summary>
public sealed record GroundTruthRngCase(string Name, uint Seed, int Range, int Count) : INamedCase;

internal static class GroundTruthRngCases
{
    public static readonly IReadOnlyList<GroundTruthRngCase> All = [
        new(Name: "SmallRangeManySeeds1", Seed: 12345, Range: 100, Count: 10),
        new(Name: "LargeRange", Seed: 12345, Range: 1000000, Count: 10),
        new(Name: "SeedOne", Seed: 1, Range: 65536, Count: 5),
        new(Name: "SeedZero", Seed: 0, Range: 9999, Count: 5),
        new(Name: "DegenerateRangeOne", Seed: 9999, Range: 1, Count: 3),
    ];

    public static IEnumerable<Func<GroundTruthRngCase>> AsDataSource() => All.Select(c => (Func<GroundTruthRngCase>)(() => c));
}
