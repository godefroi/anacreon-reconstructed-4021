using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Entities;

/// <summary>
/// Hardcoded fleet-order generators for the player-facing "templated orders" feature -- not a
/// Pascal port concept (compare <c>Core.Npe.NpeTypes</c>'s own unrelated AI <c>MissionTypes</c>),
/// just a shortcut that builds an ordinary <see cref="FleetOrder"/> list out of the same
/// DEST/TRAN/REFU commands a player could type into Close-Up's own Orders tab (<c>Tui.CloseUpWindow</c>) by hand.
/// </summary>
public static class FleetOrderTemplates
{
    /// <summary>
    /// A one-shot supply run: pick up <paramref name="amount"/> of <paramref name="cargo"/> at
    /// <paramref name="source"/>, deliver it to <paramref name="destination"/>, return to
    /// <paramref name="source"/>. Positive/negative <c>TransferAmount</c> matches
    /// <see cref="Turns.FleetMovementHandler"/>'s own pickup/drop-off sign convention.
    ///
    /// No trailing Refuel: <see cref="Turns.FleetMovementHandler.ExecuteTransCOM"/>'s own
    /// <see cref="FleetLifecycle.ChangeCompositionOfFleet"/> call already tops the fleet's fuel off
    /// from whatever trillum sits at each stop, as a side effect of any cargo transfer there,
    /// regardless of <paramref name="cargo"/>'s own type -- a separate Refuel command would only
    /// duplicate that. It also has a real cost: an order list's last command is what determines
    /// whether the fleet's own arrival back at <paramref name="source"/> clears
    /// <see cref="Fleet.NextOrder"/> to 0 in that same turn (this port's own order-resolution engine
    /// never continues past an arrival within the same turn -- see
    /// <see cref="Turns.FleetMovementHandler.AdvanceFleet"/>'s own doc comment) or leaves it one turn
    /// short until a trailing command still needs to run. Ending on the final Destination instead of a
    /// Refuel after it is what lets <see cref="AutoResupply"/> treat this fleet as idle -- and
    /// redispatch it -- the same turn it gets home, not the turn after.
    /// </summary>
    public static IReadOnlyList<FleetOrder> Resupply(Planet source, Planet destination, CargoType cargo, int amount) => [
        new(CommandType.Destination, DestinationObject: source),
        new(CommandType.Transfer, TransferCargo: cargo, TransferAmount: amount),
        new(CommandType.Destination, DestinationObject: destination),
        new(CommandType.Transfer, TransferCargo: cargo, TransferAmount: -amount),
        new(CommandType.Destination, DestinationObject: source),
    ];
}
