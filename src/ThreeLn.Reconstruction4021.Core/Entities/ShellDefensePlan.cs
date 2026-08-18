namespace ThreeLn.Reconstruction4021.Core.Entities;

/// <summary>What percentage of each ship type is assigned to each orbital shell when defending.</summary>
public sealed class ShellDefensePlan
{
    public ShipDistribution DeepSpace { get; } = new();
    public ShipDistribution HighOrbit { get; } = new();
    public ShipDistribution Orbit { get; } = new();
    public ShipDistribution SubOrbit { get; } = new();
    public ShipDistribution Ground { get; } = new();
}
