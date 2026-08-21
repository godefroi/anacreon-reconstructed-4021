namespace ThreeLn.Reconstruction4021.Core.Entities;

/// <summary>Percentage (0-100) of each ship type assigned somewhere — same shape as <see cref="ShipCounts"/>, different unit.</summary>
public sealed class ShipDistribution
{
    public int Fighters { get; set; }
    public int HunterKillers { get; set; }
    public int Jumpships { get; set; }
    public int Jumptransports { get; set; }
    public int Penetrators { get; set; }
    public int Starships { get; set; }
    public int Transports { get; set; }
}
