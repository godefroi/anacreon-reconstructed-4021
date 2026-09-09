using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Entities;

/// <summary>Whether a ship type rides along on a redirected dispatch. <see cref="AsNeeded"/> only has
/// meaning for <see cref="ShipType.Transport"/>/<see cref="ShipType.Jumptransport"/> -- see
/// <see cref="ProductionRedirection.ComputeDispatch"/>.</summary>
public enum RedirectionMode { No, Yes, AsNeeded }

/// <summary>
/// Per-planet "production redirection" dial (GitHub issue #8) -- no Pascal equivalent, confirmed against
/// UPDATE.PAS's <c>Production</c> procedure (:844-925), which only ever grows the world's own ship/cargo
/// pool and never dispatches anything. Only ever acts on units produced in the same tick that triggers
/// it, never the planet's existing stockpile -- see <see cref="ProductionRedirection"/>.
/// </summary>
public sealed class RedirectionSettings
{
    /// <summary>Redirection is off when this is null -- no separate enabled flag needed.</summary>
    public Coordinate? Destination { get; set; }

    public bool IncludeLegions { get; set; }
    public bool IncludeNinjaLegions { get; set; }

    /// <summary>Missing entries default to <see cref="RedirectionMode.No"/>.</summary>
    public Dictionary<ShipType, RedirectionMode> Ships { get; init; } = new();

    /// <summary>Backs the auto-generated "Redirect-N" fleet name -- per-planet, not global.</summary>
    public int NextDispatchNumber { get; set; } = 1;

    /// <summary>
    /// Compiles a <see cref="CommandType.Join"/> order onto the dispatched fleet -- see that command's
    /// own doc comment (<see cref="Turns.FleetMovementHandler"/>). <see cref="PreserveOverflowOnJoin"/>
    /// is inert while this is off.
    /// </summary>
    public bool JoinOnArrival { get; set; }

    /// <summary>Off clamps at <see cref="PascalMath.MaxResources"/> when joining an owned world (excess lost); on spills whatever doesn't fit into a Holding-N fleet instead of losing it.</summary>
    public bool PreserveOverflowOnJoin { get; set; }
}
