using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Entities;

public sealed class ConstructionSite
{
    public required Coordinate Location { get; set; }
    public Empire Owner { get; set; } = Empire.Independent;
    public ConstructionType Building { get; set; }
    public int YearsToCompletion { get; set; }
}
