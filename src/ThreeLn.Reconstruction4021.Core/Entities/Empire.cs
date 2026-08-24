using ThreeLn.Reconstruction4021.Core.Galaxy;
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

    /// <summary>NoOfProbesPerEmpire (TYPES.PAS:33) — the fixed-size probe pool every empire draws from.</summary>
    public const int MaxProbesInTransit = 10;

    /// <summary>
    /// Destinations of probes currently away (Pascal's ProbeRecord.Dest for every slot with
    /// Status=PInTrans). A probe has no in-flight position or individual slot identity worth
    /// modeling — GetProbe (PRIMINTR.PAS:922-932) just grabs whichever numbered slot happens to be
    /// Ready, and UpdateProbes (INTRFACE.PAS:1346-1359) resolves every in-transit probe in one call —
    /// so "in transit" reduces to just this list of destinations.
    /// </summary>
    public List<Coordinate> ProbesInTransit { get; } = [];

    /// <summary>
    /// GetProbe+LaunchProbe (PRIMINTR.PAS:922-945), combined into one atomic call since neither slot
    /// identity nor a separate "no probes available" signal is needed beyond the bool return.
    /// </summary>
    public bool TryLaunchProbe(Coordinate destination)
    {
        if (ProbesInTransit.Count >= MaxProbesInTransit) {
            return false;
        }

        ProbesInTransit.Add(destination);
        return true;
    }

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
