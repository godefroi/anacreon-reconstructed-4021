namespace Reconstructed4021.Core.Entities;

public interface IShipCargoHolder
{
    ShipCounts Ships { get; }
    CargoHold Cargo { get; }
}
