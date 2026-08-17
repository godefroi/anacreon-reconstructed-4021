namespace ThreeLn.Reconstruction4021.Core.Galaxy;

/// <summary>
/// A position in the galaxy grid. Distance is Chebyshev (max of the axis deltas), the metric the
/// original game uses throughout for adjacency, scan range, and disrupter range (MISC.PAS:119-122)
/// — not Euclidean.
/// </summary>
public readonly record struct Coordinate(int X, int Y)
{
    public int DistanceTo(Coordinate other) =>
        Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));
}
