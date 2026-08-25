namespace ThreeLn.Reconstruction4021.Core;

/// <summary>
/// Pascal-semantics numeric helpers (INT.PAS's Rnd/RndVar/IntLmt, MISC.PAS's ThgLmt, and Pascal's
/// banker's-rounding Round) shared across the annual-tick handler, new-game setup, and the combat
/// engine — all need the exact same randomness/rounding behavior to stay comparable against real
/// Pascal output.
/// </summary>
public static class PascalMath
{
    public const int MaxResources = 9999;

    /// <summary>
    /// Random integer in [min,max] inclusive; returns min if the range is empty or inverted (INT.PAS:Rnd).
    /// This is pristine Rnd's real degenerate-range clamp, unconditional — the golden-file harness's
    /// own ForcedRandomValue test override (reference/verify/patches/INT.PAS.patch) checks this clamp
    /// *before* substituting a forced value, not after, so a forced test run agrees with this method
    /// (and with FixedRandom) at min==max too. See that patch's own comment for why the check order
    /// matters (a real, found divergence in NebulaCases.PatchesMultipleRng2 before the patch was
    /// reordered to match).
    /// </summary>
    public static int Rnd(Random random, int min, int max) => max <= min ? min : random.Next(max - min + 1) + min;

    /// <summary>Randomly varies a value by up to variation% in either direction (Pascal source: INT.PAS's RndVar).</summary>
    public static int Jitter(Random random, int value, int variation)
    {
        var spread = (int)(value * (variation / 100.0));
        return Rnd(random, value - spread, value + spread);
    }

    /// <summary>
    /// Pascal's Round: nearest integer, with exact halves rounded to the nearest even integer
    /// (confirmed directly against the ground-truth harness's own FreePascal compiler:
    /// <c>Round(2.5)=2</c>, <c>Round(3.5)=4</c>, <c>Round(-2.5)=-2</c> — banker's rounding, not
    /// half-away-from-zero as this method previously assumed and documented. That assumption went
    /// unnoticed through every earlier phase's golden-file coverage because none of their formulas
    /// happened to land exactly on a .5 boundary; Phase 5's combat GetEnemy split (clean 5%/10%/15%
    /// percentages against round ship counts) was the first to actually hit one. <see cref="Math.Round(double)"/>
    /// with <see cref="MidpointRounding.ToEven"/> is this exact behavior, not an approximation.
    /// </summary>
    public static int PascalRound(double x) => (int)Math.Round(x, MidpointRounding.ToEven);

    /// <summary>Clamps a produced/consumed quantity to [0,MaxResources], truncating (Pascal source: MISC.PAS's ThgLmt).</summary>
    public static int ClampResource(double x) => x > MaxResources ? MaxResources : x < 0 ? 0 : (int)x;

    /// <summary>
    /// Truncates to Turbo Pascal's 16-bit signed Integer range (<see cref="short.MaxValue"/>, exactly
    /// Pascal's <c>MaxInt</c>), clamping rather than overflowing (INT.PAS's IntLmt) — combat's own
    /// overflow guard before further arithmetic, distinct from <see cref="ClampResource"/>'s narrower
    /// [0,MaxResources] game-balance clamp.
    /// </summary>
    public static int IntLmt(double x) => x > short.MaxValue ? short.MaxValue : x < -short.MaxValue ? -short.MaxValue : (int)x;

    /// <summary>
    /// Integer square root, rounded to the nearest integer (Pascal source: INT.PAS's ISqrt,
    /// INT.PAS:85-106). Ported as the same odd-number-summation integer algorithm rather than
    /// Math.Sqrt+round, to avoid any floating-point boundary risk for large inputs.
    /// </summary>
    public static int ISqrt(int x)
    {
        var oddSeq = -1;
        var square = 0;

        do {
            oddSeq += 2;
            square += oddSeq;
        } while (x >= square);

        var root = (oddSeq >> 1) + 1;
        return x <= square - root ? root - 1 : root;
    }
}
