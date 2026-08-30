using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Entities;

/// <summary>
/// Resources currently loaded/stockpiled. Real, stored state — how much space is available for
/// more is a separate, computed concern (see the fleet/economy phases), not a field here.
/// </summary>
public sealed class CargoHold
{
    public int Legions { get; set; }
    public int NinjaLegions { get; set; }
    public int Ambrosia { get; set; }
    public int Chemicals { get; set; }
    public int Metals { get; set; }
    public int Supplies { get; set; }
    public int Trillum { get; set; }

    /// <summary>Pascal's CargoArray[CargoTypes] indexing — lets production code loop over cargo types generically.</summary>
    public int this[CargoType type]
    {
        get => type switch {
            CargoType.Legion => Legions,
            CargoType.NinjaLegion => NinjaLegions,
            CargoType.Ambrosia => Ambrosia,
            CargoType.Chemicals => Chemicals,
            CargoType.Metals => Metals,
            CargoType.Supplies => Supplies,
            CargoType.Trillum => Trillum,
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
        set {
            switch (type) {
                case CargoType.Legion: Legions = value; break;
                case CargoType.NinjaLegion: NinjaLegions = value; break;
                case CargoType.Ambrosia: Ambrosia = value; break;
                case CargoType.Chemicals: Chemicals = value; break;
                case CargoType.Metals: Metals = value; break;
                case CargoType.Supplies: Supplies = value; break;
                case CargoType.Trillum: Trillum = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }
    }
}
