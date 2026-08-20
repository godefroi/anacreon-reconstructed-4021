namespace ThreeLn.Reconstruction4021.Tests;

/// <summary>Deterministic stand-in for the game's RNG — every roll returns the same fixed value.</summary>
public sealed class FixedRandom(int nextValue) : Random
{
    public override int Next(int maxValue) => nextValue;
}
