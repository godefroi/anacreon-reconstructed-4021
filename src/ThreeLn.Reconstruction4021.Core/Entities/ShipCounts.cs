namespace ThreeLn.Reconstruction4021.Core.Entities;

/// <summary>How many of each ship type are present. Real, stored state — not derivable.</summary>
public sealed class ShipCounts
{
    public int Fighters { get; set; }
    public int HunterKillers { get; set; }
    public int Jumpships { get; set; }
    public int Jumptransports { get; set; }
    public int Penetrators { get; set; }
    public int Starships { get; set; }
    public int Transports { get; set; }
}
