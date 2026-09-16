namespace Reconstructed4021.Core.Entities;

/// <summary>
/// An empire-wide policy for how ships distribute across orbital shells when defending — one plan
/// for fleets, one for starbases. Confirmed empire-wide, not per-planet, by the construction/economy
/// research (GetDefenseSettings/SetDefenseSettings key off the empire, not a world).
/// </summary>
public sealed class DefenseSettings
{
    // Starbases is left at the typed constant's own all-zero default -- not a port gap: Pascal's own
    // InitDefenseRecord (DATACNST.PAS:373-379) only sets ShellDefDist (Fleets), never StarbaseDefDist,
    // even though DATASTRC.PAS:165-168 declares both fields. See ShellDefensePlan.CreateDefault's own
    // doc comment for why Fleets can't default the same bare way.
    public ShellDefensePlan Fleets { get; } = ShellDefensePlan.CreateDefault();
    public ShellDefensePlan Starbases { get; } = new();
}
