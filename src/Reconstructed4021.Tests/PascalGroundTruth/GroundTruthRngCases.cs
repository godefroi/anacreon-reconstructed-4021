namespace Reconstructed4021.Tests.PascalGroundTruth;

/// <summary>
/// Standing regression fixture for <see cref="GroundTruthRandom"/> against its Pascal twin
/// (INT.PAS.patch's GroundTruthSeed/GroundTruthNextU32, driven here via runworld.pas's
/// RunGroundTruthRngCase) -- the same role <see cref="RngCases"/> plays for <see cref="PascalRandom"/>,
/// but for the generator this project now owns on both sides instead of reverse-engineered fpc Random.
/// Reuses the same seeds/ranges/counts as RngCases so the two fixtures read as obvious counterparts.
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
