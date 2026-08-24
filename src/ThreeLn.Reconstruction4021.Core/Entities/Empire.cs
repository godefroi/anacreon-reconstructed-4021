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

    /// <summary>
    /// GetNewsList/GetNewsItem (NEWS.PAS:139-205) collapse to this plain list once <see cref="NewsItem"/>
    /// is a real record: Pascal's hand-rolled singly-linked list plus its heap-availability guard
    /// (<c>MaxAvail&gt;20</c>) exist only because of DOS's 640KB heap, not a game rule, so neither is
    /// ported. EraseNews (NEWS.PAS:245-264) is just <c>News.Clear()</c> — no wrapper method, since
    /// nothing yet calls it (see docs/ROADMAP.md's Phase 4 notes on why the per-turn reset isn't wired
    /// yet: no consumer exists to validate the timing against).
    /// </summary>
    public List<NewsItem> News { get; } = [];

    /// <summary>
    /// AddNews (NEWS.PAS:207-228). Inlines Pascal's <c>(Player&lt;&gt;Indep) AND EmpireActive(Player)</c>
    /// guard as just <c>IsIndependent</c> — this port's <see cref="Game.Empires"/> only ever holds real,
    /// in-use empires by construction, so <c>EmpireActive</c>'s <c>InUse</c> check is redundant with
    /// list membership (same reasoning already applied to <see cref="ProbesInTransit"/>).
    /// </summary>
    public void AddNews(
        NewsType headline,
        ISectorObject? subject = null,
        Coordinate? position = null,
        Empire? otherEmpire = null,
        TechCatalog.TechGrantIdentity? techGrant = null,
        int p1 = 0,
        int p2 = 0,
        int p3 = 0)
    {
        if (IsIndependent) {
            return;
        }

        News.Add(new NewsItem(headline, subject, position, otherEmpire, techGrant, p1, p2, p3));
    }
}
