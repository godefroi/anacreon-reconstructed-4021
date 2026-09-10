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
///
/// Mode 6's own single-candidate design (a lone positive-scoring world, matching
/// PirateTurnHandlerTests' own DeploysRaiderFleetAtBestScoringTarget) confirms the gate conditions,
/// composition draft, and cargo-transfer arithmetic are all transcribed correctly, and that a real
/// draft/deploy actually happens — but two things limit what it proves about GetTarget's own
/// Round/RndVar scoring formula specifically. First, with only one candidate the formula's exact
/// output is unobservable — it only has to come out positive to be picked (confirmed: changing the
/// Trillum gain multiplier from 5 to 4 still scored positive and wasn't caught). Second, this
/// candidate's own Protect is 0, so (1-Protect/FltPower) is exactly 1.0 regardless of FltPower —
/// Round never even receives a fractional argument here, so the actual rounding behavior (as opposed
/// to the formula's sign) is never exercised at all. Closing both gaps needs a candidate with nonzero
/// Protect (so Round sees a real fraction) and a second, close-scoring candidate (so the exact
/// rounding decides the winner, making it observable via destx/desty) — not built here; a real gap,
/// not an oversight to be quietly assumed away.
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
        new(Name: "RaiderDeploySeed1", Mode: 6, Seed: 1),
        new(Name: "RaiderDeploySeed2", Mode: 6, Seed: 2),
    ];

    public static IEnumerable<Func<PirateCase>> AsDataSource() => All.Select(c => (Func<PirateCase>)(() => c));
}
