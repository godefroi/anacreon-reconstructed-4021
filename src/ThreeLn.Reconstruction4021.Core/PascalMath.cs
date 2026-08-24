namespace ThreeLn.Reconstruction4021.Core;

/// <summary>
/// Pascal-semantics numeric helpers (INT.PAS's Rnd/RndVar, MISC.PAS's ThgLmt, and Pascal's
/// half-away-from-zero Round) shared between the annual-tick handler and new-game setup — both need
/// the exact same randomness/rounding behavior to stay comparable against real Pascal output.
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

    /// <summary>Pascal's Round: nearest integer, halves away from zero (not banker's rounding).</summary>
    public static int PascalRound(double x) => x >= 0 ? (int)(x + 0.5) : (int)(x - 0.5);

    /// <summary>Clamps a produced/consumed quantity to [0,MaxResources], truncating (Pascal source: MISC.PAS's ThgLmt).</summary>
    public static int ClampResource(double x) => x > MaxResources ? MaxResources : x < 0 ? 0 : (int)x;

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
