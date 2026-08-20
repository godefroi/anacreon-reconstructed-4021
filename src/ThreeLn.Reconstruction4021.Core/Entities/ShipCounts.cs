using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Entities;

/// <summary>How many of each ship type are present. Real, stored state — not derivable.</summary>
public sealed class ShipCounts
{
    public int Fighters { get; set; }
    public int HunterKillers { get; set; }
    public int Jumpships { get; set; }
    public int Jumptransports { get; set; }
    public int Penetrators { get; set; }
    public int Starships { get; set; }
    public int Transports { get; set; }

    /// <summary>Pascal's ShipArray[ShipTypes] indexing — lets production code loop over ship types generically.</summary>
    public int this[ShipType type]
    {
        get => type switch {
            ShipType.Fighter => Fighters,
            ShipType.HunterKiller => HunterKillers,
            ShipType.Jumpship => Jumpships,
            ShipType.Jumptransport => Jumptransports,
            ShipType.Penetrator => Penetrators,
            ShipType.Starship => Starships,
            ShipType.Transport => Transports,
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
        set {
            switch (type) {
                case ShipType.Fighter: Fighters = value; break;
                case ShipType.HunterKiller: HunterKillers = value; break;
                case ShipType.Jumpship: Jumpships = value; break;
                case ShipType.Jumptransport: Jumptransports = value; break;
                case ShipType.Penetrator: Penetrators = value; break;
                case ShipType.Starship: Starships = value; break;
                case ShipType.Transport: Transports = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }
    }
}
