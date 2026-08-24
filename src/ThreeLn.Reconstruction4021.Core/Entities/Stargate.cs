using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Entities;

public sealed class Stargate : ISectorObject
{
    public required Coordinate Location { get; set; }
    public Empire Owner { get; set; } = Empire.Independent;
    public StargateKind Kind { get; set; }

    /// <summary>
    /// Where the paired gate/link leads, or null if not yet linked to anything. Pascal's
    /// ConstructStargate (UPDATE.PAS:94-100) sets Dest:=Limbo (an explicit "unlinked" sentinel) at
    /// creation — establishing a real link is a separate, not-yet-ported mechanic.
    /// </summary>
    public Coordinate? LinkedTo { get; set; }
}
