namespace ThreeLn.Reconstruction4021.Core.Entities;

/// <summary>How many of each defense type are present. Real, stored state — not derivable.</summary>
public sealed class DefenseCounts
{
    public int Lams { get; set; }
    public int DefenseSatellites { get; set; }
    public int Gdms { get; set; }
    public int IonCannons { get; set; }
}
