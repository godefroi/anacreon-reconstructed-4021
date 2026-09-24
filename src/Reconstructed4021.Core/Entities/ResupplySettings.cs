using System.Collections.Frozen;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Entities;

/// <summary>
/// Per-planet "auto-resupply" dial (GitHub issue #85) -- no Pascal equivalent, same shape as
/// <see cref="RedirectionSettings"/> (issue #8): a plain settings object <see cref="AutoResupply"/>
/// reads every tick, never mutated by the turn loop itself.
/// </summary>
public sealed class ResupplySettings
{
    public bool Enabled { get; set; }

    /// <summary>Caps every dispatch this source makes, tier 1 or tier 2 alike -- 0 means uncapped, matching <see cref="ResupplyCargoOverlay"/>'s own "blank = max" convention.</summary>
    public int MaxAmount { get; set; }

    /// <summary>Tier 1: explicit destinations, priority by list order. Coordinates, not <see cref="Planet"/> references -- same reason <see cref="RedirectionSettings.Destination"/> stores a coordinate: no GameJson forward-reference problem, and a destination that changes owner or is destroyed just silently drops out at dispatch time.</summary>
    public List<Coordinate> Destinations { get; init; } = [];

    /// <summary>
    /// Which cargo types a world of this type may auto-ship, keyed by <see cref="WorldType"/>. New
    /// data, not a reuse of <see cref="WorldDesignation.PrincipalIndustry"/>: Ambrosia's principal
    /// industry (Bioindustry) also covers ninja legions and doesn't map to one cargo type, and
    /// Capital's (ShipyardGeneral) doesn't map to a raw-material cargo type at all -- both need an
    /// explicit entry here instead. RawMaterialMine ships all three of its own
    /// <see cref="WorldDesignation.RawMaterialSplit"/> outputs (that 40/40/20 split informs
    /// production, not shipping eligibility). Capital ships whatever it's holding of
    /// Chemicals/Metals/Trillum/Supplies -- confirmed with the user against the issue's own
    /// self-contradictory eligibility table -- but never Ambrosia. Every other <see cref="WorldType"/>
    /// isn't a resupply source at all (empty list; <c>WorldInfoOverlay</c> hides the tab).
    /// </summary>
    private static readonly FrozenDictionary<WorldType, CargoType[]> _eligibleCargo = new Dictionary<WorldType, CargoType[]> {
        [WorldType.Agricultural] = [CargoType.Supplies],
        [WorldType.Chemical] = [CargoType.Chemicals],
        [WorldType.Mine] = [CargoType.Metals],
        [WorldType.TrillumMine] = [CargoType.Trillum],
        [WorldType.RawMaterialMine] = [CargoType.Chemicals, CargoType.Metals, CargoType.Trillum],
        [WorldType.Ambrosia] = [CargoType.Ambrosia],
        [WorldType.Capital] = [CargoType.Chemicals, CargoType.Metals, CargoType.Trillum, CargoType.Supplies],
    }.ToFrozenDictionary();

    private static readonly CargoType[] _none = [];

    public static IReadOnlyList<CargoType> EligibleCargo(WorldType type) =>
        _eligibleCargo.GetValueOrDefault(type, _none);
}
