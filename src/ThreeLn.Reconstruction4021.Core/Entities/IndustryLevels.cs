using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Entities;

/// <summary>Developed industry level per industry type. Real, stored state — not derivable.</summary>
public sealed class IndustryLevels
{
    public int Bioindustry { get; set; }
    public int Chemical { get; set; }
    public int Mining { get; set; }
    public int ShipyardGeneral { get; set; }
    public int ShipyardJump { get; set; }
    public int ShipyardStarship { get; set; }
    public int ShipyardTransport { get; set; }
    public int Supply { get; set; }
    public int TrillumMining { get; set; }

    /// <summary>Pascal's IndusArray[IndusTypes] indexing — lets production code loop over industries generically.</summary>
    public int this[IndustryType type]
    {
        get => type switch {
            IndustryType.Bioindustry => Bioindustry,
            IndustryType.Chemical => Chemical,
            IndustryType.Mining => Mining,
            IndustryType.ShipyardGeneral => ShipyardGeneral,
            IndustryType.ShipyardJump => ShipyardJump,
            IndustryType.ShipyardStarship => ShipyardStarship,
            IndustryType.ShipyardTransport => ShipyardTransport,
            IndustryType.Supply => Supply,
            IndustryType.TrillumMining => TrillumMining,
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
        set {
            switch (type) {
                case IndustryType.Bioindustry: Bioindustry = value; break;
                case IndustryType.Chemical: Chemical = value; break;
                case IndustryType.Mining: Mining = value; break;
                case IndustryType.ShipyardGeneral: ShipyardGeneral = value; break;
                case IndustryType.ShipyardJump: ShipyardJump = value; break;
                case IndustryType.ShipyardStarship: ShipyardStarship = value; break;
                case IndustryType.ShipyardTransport: ShipyardTransport = value; break;
                case IndustryType.Supply: Supply = value; break;
                case IndustryType.TrillumMining: TrillumMining = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }
    }
}
