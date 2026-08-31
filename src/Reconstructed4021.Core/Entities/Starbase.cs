using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Entities;

/// <summary>A starbase — command bases and fortresses are mobile (<see cref="IMovable"/>); industrial complexes and outposts stay put but still carry the same fields.</summary>
public sealed class Starbase : IMovable, IEconomicWorld
{
    public required Coordinate Location { get; set; }
    public Empire Owner { get; set; } = Empire.Independent;

    /// <summary>See <see cref="ISectorObject.Names"/>.</summary>
    public Dictionary<Empire, string> Names { get; set; } = new();

    public StarbaseKind Kind { get; set; }
    public WorldType Type { get; set; }

    public TechLevel TechLevel { get; set; }
    public int Efficiency { get; set; }
    public int RevolutionIndex { get; set; }
    public bool IsAddictedToAmbrosia { get; set; }

    public int Population { get; set; }

    /// <summary>
    /// <c>init</c> (not a plain getter): <see cref="SaveFormat.GameJson"/> deserializes
    /// <see cref="Starbase"/> via ordinary reflection (it has no forward-reference problem of its
    /// own, see that class's own doc comment), which needs a way to assign the freshly-deserialized
    /// instance back since there's no setter. Every other caller still mutates this object's own
    /// properties in place; nothing reassigns the property itself.
    /// </summary>
    public ShipCounts Ships { get; init; } = new();

    /// <summary>See <see cref="Ships"/>'s remarks.</summary>
    public CargoHold Cargo { get; init; } = new();

    /// <summary>See <see cref="Ships"/>'s remarks.</summary>
    public DefenseCounts Defenses { get; init; } = new();

    /// <summary>See <see cref="Ships"/>'s remarks.</summary>
    public IndustryLevels Industry { get; init; } = new();

    public Coordinate? Destination { get; set; }
    public FleetStatus Status { get; set; } = FleetStatus.Ready;

    // Explicit IEconomicWorld implementation, same rationale as Planet's: these exist only for
    // AnnualTickHandler's shared pipeline, not as part of Starbase's own public API. Each one encodes
    // a specific PRIMINTR.PAS accessor's Base-case behavior — see IEconomicWorld's own doc comment.

    /// <summary>GetClass's Base case (PRIMINTR.PAS:418-420) — a starbase has no world-class field of its own; every one always reads as ArtCls.</summary>
    WorldClass IEconomicWorld.EffectiveClass => WorldClass.Artificial;

    /// <summary>GetISSP's Base case (PRIMINTR.PAS:525) — "Always lowest ISSP," not a per-starbase setting; a starbase has no ImpExp field of its own.</summary>
    int IEconomicWorld.SelfSufficiencyIndex(IndustryType industry) => 0;

    /// <summary>
    /// TrillumReserves/PutTrillumReserves's Base cases (PRIMINTR.PAS:487,496) — a starbase has no
    /// TriReserve field in Pascal at all, so reads always return MaxResources (effectively
    /// unlimited: high enough that ProduceTrillum's reserve-warning thresholds never trigger) and
    /// writes are silently discarded.
    /// </summary>
    int IEconomicWorld.TrillumReserve { get => 9999; set { } }

    /// <summary>InitializeISSP's CASE statement has no Base branch (PRIMINTR.PAS:627-633) — a no-op for a starbase.</summary>
    void IEconomicWorld.InitializeSelfSufficiency() { }

    bool IEconomicWorld.IsPlanet => false;

    void IEconomicWorld.Reassign(Empire newOwner) => Owner = newOwner;
}
