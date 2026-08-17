namespace ThreeLn.Reconstruction4021.Core.Entities;

/// <summary>Developed industry level per industry type. Real, stored state — not derivable.</summary>
public sealed class IndustryLevels
{
    public int Bioindustry { get; set; }
    public int Chemical { get; set; }
    public int Mining { get; set; }
    public int ShipyardGeneral { get; set; }
    public int ShipyardJump { get; set; }
    public int ShipyardStarship { get; set; }
    public int ShipyardTransport { get; set; }
    public int Supply { get; set; }
    public int TrillumMining { get; set; }
}
