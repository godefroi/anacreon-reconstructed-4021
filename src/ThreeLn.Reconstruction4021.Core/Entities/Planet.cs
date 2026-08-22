using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Entities;

public sealed class Planet : IEconomicWorld
{
    public required Coordinate Location { get; set; }
    public Empire Owner { get; set; } = Empire.Independent;

    public WorldClass Class { get; set; }
    public WorldType Type { get; set; }
    public SelfSufficiencySettings SelfSufficiency { get; } = new();
    public TechLevel TechLevel { get; set; }
    public int Efficiency { get; set; }
    public int RevolutionIndex { get; set; }

    /// <summary>
    /// Pascal's PlanetRecord.Special also declares Holocst/Plague/SelfSuff/Virgin flags, confirmed
    /// dead (never read by the turn-loop) by the architecture research — only ambrosia addiction is
    /// live, so that's the only one modeled here.
    /// </summary>
    public bool IsAddictedToAmbrosia { get; set; }

    public int Population { get; set; }
    public ShipCounts Ships { get; } = new();
    public CargoHold Cargo { get; } = new();
    public DefenseCounts Defenses { get; } = new();
    public IndustryLevels Industry { get; } = new();
    public int TrillumReserve { get; set; }

    // Explicit IEconomicWorld implementation, deliberately: these three exist only for
    // AnnualTickHandler's shared planet/starbase pipeline to call through the interface, not as part
    // of Planet's own public API — a caller working with a Planet directly wants Class/
    // SelfSufficiency/TrillumReserve, not a redundant EffectiveClass sitting next to Class. C# forbids
    // an accessibility modifier on an explicit implementation; that's a language restriction on this
    // form, not an omitted one.
    WorldClass IEconomicWorld.EffectiveClass => Class;

    int IEconomicWorld.SelfSufficiencyIndex(IndustryType industry) => industry switch {
        IndustryType.Chemical => SelfSufficiency.Chemical,
        IndustryType.Mining => SelfSufficiency.Metal,
        IndustryType.Supply => SelfSufficiency.Supply,
        IndustryType.TrillumMining => SelfSufficiency.Trillum,
        _ => throw new ArgumentOutOfRangeException(nameof(industry), industry,
            "Only Chemical/Mining/Supply/TrillumMining have a self-sufficiency dial (GetISSP, PRIMINTR.PAS:500-529)."),
    };

    void IEconomicWorld.InitializeSelfSufficiency()
    {
        SelfSufficiency.Chemical = 5;
        SelfSufficiency.Metal = 5;
        SelfSufficiency.Supply = 5;
        SelfSufficiency.Trillum = 5;
    }

    bool IEconomicWorld.IsPlanet => true;
}
