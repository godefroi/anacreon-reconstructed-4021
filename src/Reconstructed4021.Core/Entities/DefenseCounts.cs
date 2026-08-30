using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Entities;

/// <summary>How many of each defense type are present. Real, stored state — not derivable.</summary>
public sealed class DefenseCounts
{
    public int Lams { get; set; }
    public int DefenseSatellites { get; set; }
    public int Gdms { get; set; }
    public int IonCannons { get; set; }

    /// <summary>Pascal's DefnsArray[DefnsTypes] indexing — same pattern as ShipCounts/CargoHold, lets code loop over defense types generically.</summary>
    public int this[DefenseType type]
    {
        get => type switch {
            DefenseType.Lam => Lams,
            DefenseType.DefenseSatellite => DefenseSatellites,
            DefenseType.Gdm => Gdms,
            DefenseType.IonCannon => IonCannons,
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
        set {
            switch (type) {
                case DefenseType.Lam: Lams = value; break;
                case DefenseType.DefenseSatellite: DefenseSatellites = value; break;
                case DefenseType.Gdm: Gdms = value; break;
                case DefenseType.IonCannon: IonCannons = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }
    }
}
