using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Entities;

/// <summary>Shared shape for the two entity kinds that move under their own power: Fleet and Starbase (command bases/fortresses are mobile).</summary>
public interface IMovable
{
    Coordinate? Destination { get; set; }
    FleetStatus Status { get; set; }
}
