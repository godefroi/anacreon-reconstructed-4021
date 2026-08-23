using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Entities;

public sealed class Empire
{
    /// <summary>
    /// The shared "independent" faction unclaimed worlds belong to. A real faction with its own
    /// default DefenseSettings — genuinely read during combat against independent worlds
    /// (ATTACK.PAS:1383-1396) — not a stand-in for "no owner".
    /// </summary>
    public static Empire Independent { get; } = new() {
        Name = "Independent",
        IsIndependent = true,
    };

    public required string Name { get; set; }
    public string? Password { get; set; }
    public bool IsIndependent { get; init; }
    public bool IsEmpress { get; set; }

    /// <summary>
    /// A capital can be a planet or a starbase in real Pascal (NEWGAME.PAS's CreateBase calls
    /// SetCapital too, not just CreateWorld) — IEconomicWorld, not Planet, so both are representable.
    /// </summary>
    public IEconomicWorld? Capital { get; set; }
    public DefenseSettings DefenseSettings { get; } = new();
    public List<Probe> Probes { get; } = [];
    public List<LocationBookmark> Bookmarks { get; } = [];

    public int TotalRevolutionIndex { get; set; }
    public int RevolutionFactor { get; set; }
    public TechLevel TechnologyLevel { get; set; }
    public UnlockedTechnology Technology { get; } = new();

    public int FoundingYear { get; set; }

    /// <summary>Pascal's CentralEMD modifier — the only one of its 7 EmpireModifiers ever actually used.</summary>
    public bool LosesIfCapitalConquered { get; set; }

    public EntityVisibility<Planet> Planets { get; } = new();
    public EntityVisibility<Starbase> Starbases { get; } = new();
    public EntityVisibility<Fleet> Fleets { get; } = new();
    public EntityVisibility<Stargate> Stargates { get; } = new();
    public EntityVisibility<ConstructionSite> ConstructionSites { get; } = new();
}
