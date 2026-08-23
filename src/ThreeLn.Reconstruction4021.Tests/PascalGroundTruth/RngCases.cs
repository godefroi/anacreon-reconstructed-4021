namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Not a UpdateWorld/GalaxySetup domain — a standing regression fixture for <see cref="PascalRandom"/>
/// (see its own doc comment and runworld.pas's RunRngCase). Seeds/ranges/counts are arbitrary; what
/// matters is exercising more than one Mersenne Twister "tempered output" per case (Count > 1) and, in
/// StateBlockBoundary, crossing the generator's 624-word internal state refill (see PascalRandom's
/// UpdateState) — the one place a subtly wrong port would most plausibly diverge from real fpc output
/// without a small-count test ever catching it.
/// </summary>
public sealed record RngCase(string Name, uint Seed, int Range, int Count) : INamedCase;

internal static class RngCases
{
    public static readonly IReadOnlyList<RngCase> All = [
        new(Name: "SmallRangeManySeeds1", Seed: 12345, Range: 100, Count: 10),
        new(Name: "LargeRange", Seed: 12345, Range: 1000000, Count: 10),
        new(Name: "SeedOne", Seed: 1, Range: 65536, Count: 5),
        new(Name: "SeedZero", Seed: 0, Range: 9999, Count: 5),
        new(Name: "DegenerateRangeOne", Seed: 9999, Range: 1, Count: 3),
        // Crosses the 624-word state refill boundary (Index starts at 625, so this needs >625 draws).
        new(Name: "StateBlockBoundary", Seed: 42, Range: 9999, Count: 701),
    ];

    public static IEnumerable<Func<RngCase>> AsDataSource() => All.Select(c => (Func<RngCase>)(() => c));
}
