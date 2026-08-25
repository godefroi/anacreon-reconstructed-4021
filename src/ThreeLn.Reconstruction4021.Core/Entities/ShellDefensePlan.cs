using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Entities;

/// <summary>What percentage of each ship type is assigned to each orbital shell when defending.</summary>
public sealed class ShellDefensePlan
{
    public ShipDistribution DeepSpace { get; } = new();
    public ShipDistribution HighOrbit { get; } = new();
    public ShipDistribution Orbit { get; } = new();
    public ShipDistribution SubOrbit { get; } = new();
    public ShipDistribution Ground { get; } = new();

    /// <summary>Pascal's DefenseDistributionArray[ShellPos,...] indexing — lets GetEnemy (Combat/CombatEngine.cs) loop over shells generically.</summary>
    public ShipDistribution this[ShellPosition position] => position switch {
        ShellPosition.DeepSpace => DeepSpace,
        ShellPosition.HighOrbit => HighOrbit,
        ShellPosition.Orbit => Orbit,
        ShellPosition.SubOrbit => SubOrbit,
        ShellPosition.Ground => Ground,
        _ => throw new ArgumentOutOfRangeException(nameof(position)),
    };
}
