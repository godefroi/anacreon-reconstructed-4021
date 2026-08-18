using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Entities;

public sealed class Probe
{
    public Coordinate? Destination { get; set; }
    public ProbeStatus Status { get; set; } = ProbeStatus.Ready;
}
