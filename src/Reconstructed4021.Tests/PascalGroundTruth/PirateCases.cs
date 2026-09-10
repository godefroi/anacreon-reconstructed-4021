namespace Reconstructed4021.Tests.PascalGroundTruth;

/// <summary>
/// Golden-file ground truth for <see cref="Reconstructed4021.LegacyNpe.PirateTurnHandler"/> against
/// NPE01.PAS's real <c>ImplementPirateNPE</c>, one turn, run through <c>runworld.pas</c>'s
/// <c>npepirate</c> domain (<c>RunNpePirateCase</c>) — see that procedure's own doc comment for what
/// each <see cref="Mode"/> builds and exercises. Both sides use a fixed <c>Galaxy</c>/<c>SizeOfGalaxy</c>
/// of 25 and the same fixed coordinates/ship counts per mode.
///
/// A second seed per mode is included only where it's confirmed (by actually running the harness
/// across a range of seeds, not assumed) to produce an actually different result — Modes 3 and 5 each
/// have exactly one Rnd-free-or-effectively-so path for this case's own fixed setup (Mode 3's give-up
/// branch never calls Rnd at all; Mode 5's attacker is so overwhelmingly stronger than its undefended
/// target that it takes zero casualties regardless of which combat rolls happen), so a second seed
/// there would only coincidentally repeat the same output, not exercise anything a single case doesn't
/// already cover.
/// </summary>
public sealed record PirateCase(string Name, int Mode, uint Seed, int HeavyBX = 0, int HeavyBY = 0) : INamedCase;

internal static class PirateCases
{
    public static readonly IReadOnlyList<PirateCase> All = [
        new(Name: "PatrolDeploySeed1", Mode: 1, Seed: 1, HeavyBX: 3, HeavyBY: 3),
        new(Name: "PatrolDeploySeed7", Mode: 1, Seed: 7, HeavyBX: 1, HeavyBY: 1),
        new(Name: "WaitForTrnCatchesTargetSeed1", Mode: 2, Seed: 1),
        new(Name: "WaitForTrnCatchesTargetSeed1000", Mode: 2, Seed: 1000),
        new(Name: "WaitForTrnGivesUp", Mode: 3, Seed: 1),
        new(Name: "AttackTrnCatchesTargetSeed1", Mode: 4, Seed: 1),
        new(Name: "AttackTrnCatchesTargetSeed4", Mode: 4, Seed: 4),
        new(Name: "AttackWrldConquers", Mode: 5, Seed: 1),
    ];

    public static IEnumerable<Func<PirateCase>> AsDataSource() => All.Select(c => (Func<PirateCase>)(() => c));
}
