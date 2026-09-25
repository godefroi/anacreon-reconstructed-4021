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

    /// <summary>Caps every dispatch this source makes, regardless of which group the destination came from -- 0 means uncapped, matching <see cref="ResupplyCargoOverlay"/>'s own "blank = max" convention.</summary>
    public int MaxAmount { get; set; }

    /// <summary>
    /// Hand-managed, ordered destinations that always take precedence over the implicit "everything
    /// else" group's own population ranking -- priority by list order. Coordinates, not
    /// <see cref="Planet"/> references -- same reason <see cref="RedirectionSettings.Destination"/>
    /// stores a coordinate: no GameJson forward-reference problem. <see cref="AutoResupply.Apply"/>
    /// prunes a coordinate out the moment it stops resolving to a same-owner <see cref="Planet"/>
    /// (conquered, or otherwise lost) -- see that method's own doc comment.
    /// </summary>
    public List<Coordinate> Priority { get; init; } = [];

    /// <summary>
    /// Hand-managed destinations this source never auto-dispatches to, overriding even a starvation
    /// shortfall -- an absolute exclusion, not merely a low priority. Pruned the same way <see cref="Priority"/>
    /// is. A destination is never in both lists at once: adding it to one removes it from the other
    /// (<c>GalaxyMapScreen.PickResupplyDestination</c>).
    /// </summary>
    public List<Coordinate> Never { get; init; } = [];

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
