using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Entities;

public sealed class Planet
{
    public required Coordinate Location { get; set; }
    public Empire Owner { get; set; } = Empire.Independent;

    public WorldClass Class { get; set; }
    public WorldType Type { get; set; }
    public SelfSufficiencySettings SelfSufficiency { get; } = new();
    public TechLevel TechLevel { get; set; }
    public int Efficiency { get; set; }
    public int RevolutionIndex { get; set; }

    /// <summary>
    /// Pascal's PlanetRecord.Special also declares Holocst/Plague/SelfSuff/Virgin flags, confirmed
    /// dead (never read by the turn-loop) by the architecture research — only ambrosia addiction is
    /// live, so that's the only one modeled here.
    /// </summary>
    public bool IsAddictedToAmbrosia { get; set; }

    public int Population { get; set; }
    public ShipCounts Ships { get; } = new();
    public CargoHold Cargo { get; } = new();
    public DefenseCounts Defenses { get; } = new();
    public IndustryLevels Industry { get; } = new();
    public int TrillumReserve { get; set; }
}
