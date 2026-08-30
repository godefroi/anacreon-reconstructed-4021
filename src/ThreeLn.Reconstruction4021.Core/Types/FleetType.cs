namespace ThreeLn.Reconstruction4021.Core.Types;

/// <summary>
/// A fleet's classification, computed from its ship composition (see Fleet.Type) — never stored.
/// Matches Pascal's FleetTypes / TypeOfFleet (PRIMINTR.PAS:808-833).
/// </summary>
public enum FleetType
{
    Standard,
    JumpFleet,
    HunterKillerFleet,
    Penetrator,
    AdvancedWarpFleet,
}
