using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Entities;

public sealed class Fleet : IMovable
{
    public required Coordinate Location { get; set; }
    public Empire Owner { get; set; } = Empire.Independent;

    public ShipCounts Ships { get; } = new();
    public CargoHold Cargo { get; } = new();

    /// <summary>
    /// Pascal splits this across FuelHigh/Fuel to dodge 16-bit Integer overflow
    /// (FuelHigh*MaxInt + Fuel) — no reason to carry that split into C#.
    /// </summary>
    public int Fuel { get; set; }

    public Coordinate? Destination { get; set; }
    public FleetStatus Status { get; set; } = FleetStatus.Ready;

    /// <summary>
    /// A fleet's classification is derived from its ship composition, never stored — Pascal itself
    /// computes this (TypeOfFleet, PRIMINTR.PAS:808-833) rather than keeping a type field, so a
    /// fleet's type can never drift out of sync with the ships actually present.
    /// </summary>
    public FleetType Type
    {
        get
        {
            var s = Ships;
            if (s.Starships + s.Penetrators + s.Jumpships + s.Fighters + s.Jumptransports + s.Transports == 0)
                return FleetType.HunterKillerFleet;
            if (s.Starships + s.Penetrators + s.Fighters + s.Transports == 0)
                return FleetType.JumpFleet;
            if (s.Starships + s.Jumpships + s.Jumptransports + s.Transports + s.Fighters == 0)
                return FleetType.Penetrator;
            if (s.Starships + s.Fighters + s.Transports == 0)
                return FleetType.AdvancedWarpFleet;
            return FleetType.Standard;
        }
    }
}
