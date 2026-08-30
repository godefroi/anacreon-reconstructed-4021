namespace Reconstructed4021.Core.Entities;

/// <summary>
/// An empire-wide policy for how ships distribute across orbital shells when defending — one plan
/// for fleets, one for starbases. Confirmed empire-wide, not per-planet, by the construction/economy
/// research (GetDefenseSettings/SetDefenseSettings key off the empire, not a world).
/// </summary>
public sealed class DefenseSettings
{
    public ShellDefensePlan Fleets { get; } = new();
    public ShellDefensePlan Starbases { get; } = new();
}
