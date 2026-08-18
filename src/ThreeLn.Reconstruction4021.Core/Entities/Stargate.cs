using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Entities;

public sealed class Stargate
{
    public required Coordinate Location { get; set; }
    public Empire Owner { get; set; } = Empire.Independent;
    public StargateKind Kind { get; set; }

    /// <summary>Where the paired gate/link leads.</summary>
    public required Coordinate LinkedTo { get; set; }
}
