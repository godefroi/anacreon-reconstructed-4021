using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Entities;

public sealed class ConstructionSite : ISectorObject
{
    public required Coordinate Location { get; set; }
    public Empire Owner { get; set; } = Empire.Independent;

    /// <summary>See <see cref="ISectorObject.Names"/>.</summary>
    public Dictionary<Empire, string> Names { get; set; } = new();

    public ConstructionType Building { get; set; }
    public int YearsToCompletion { get; set; }
}
