using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Entities;

/// <summary>A starbase — command bases and fortresses are mobile (IMovable); industrial complexes and outposts stay put but still carry the same fields.</summary>
public sealed class Starbase : IMovable
{
    public required Coordinate Location { get; set; }
    public Empire Owner { get; set; } = Empire.Independent;
    public StarbaseKind Kind { get; set; }
    public WorldType Type { get; set; }

    public TechLevel TechLevel { get; set; }
    public int Efficiency { get; set; }
    public int RevolutionIndex { get; set; }
    public bool IsAddictedToAmbrosia { get; set; }

    public int Population { get; set; }
    public ShipCounts Ships { get; } = new();
    public CargoHold Cargo { get; } = new();
    public DefenseCounts Defenses { get; } = new();
    public IndustryLevels Industry { get; } = new();

    /// <summary>Years until this starbase (if mobile) takes its next move step (DATASTRC.PAS:73).</summary>
    public int YearsUntilNextMove { get; set; }

    public Coordinate? Destination { get; set; }
    public FleetStatus Status { get; set; } = FleetStatus.Ready;
}
