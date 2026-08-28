namespace ThreeLn.Reconstruction4021.Tests;

/// <summary>
/// Deterministic stand-in for the game's RNG — every roll returns the same fixed value.
/// <paramref name="nextDouble"/> is separate from <paramref name="nextValue"/> because
/// <see cref="Random.NextDouble"/> isn't implemented in terms of <see cref="Random.Next(int)"/> (it
/// goes through the base class's own <c>Sample</c>), so overriding only <c>Next(int)</c> leaves
/// <c>NextDouble()</c> non-deterministic — needed by Npe/NpeToolkit.cs's tests, which read Pascal's
/// bare <c>Random</c> (0..1) via <c>random.NextDouble()</c>.
/// </summary>
public sealed class FixedRandom(int nextValue, double nextDouble = 0) : Random
{
    public override int Next(int maxValue) => nextValue;
    public override double NextDouble() => nextDouble;
}
