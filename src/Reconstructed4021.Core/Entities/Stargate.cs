using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Entities;

public sealed class Stargate : ISectorObject
{
    public required Coordinate Location { get; set; }
    public Empire Owner { get; set; } = Empire.Independent;

    /// <summary>See <see cref="ISectorObject.Names"/>.</summary>
    public Dictionary<Empire, string> Names { get; set; } = new();

    public StargateKind Kind { get; set; }

    /// <summary>
    /// Where the paired gate/link leads, or null if not yet linked to anything. Pascal's
    /// ConstructStargate (UPDATE.PAS:94-100) sets Dest:=Limbo (an explicit "unlinked" sentinel) at
    /// creation — establishing a real link is a separate, not-yet-ported mechanic.
    /// </summary>
    public Coordinate? LinkedTo { get; set; }
}
