using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Entities;

/// <summary>Percentage (0-100) of each ship type assigned somewhere — same shape as <see cref="ShipCounts"/>, different unit.</summary>
public sealed class ShipDistribution
{
    public int Fighters { get; set; }
    public int HunterKillers { get; set; }
    public int Jumpships { get; set; }
    public int Jumptransports { get; set; }
    public int Penetrators { get; set; }
    public int Starships { get; set; }
    public int Transports { get; set; }

    /// <summary>Same indexing pattern as <see cref="ShipCounts"/> — lets <see cref="Combat.CombatEngine.GetEnemy"/> loop over ship types generically.</summary>
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
