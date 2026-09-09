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
    /// <paramref name="source"/>, and refuel there. Positive/negative <c>TransferAmount</c> matches
    /// <see cref="Turns.FleetMovementHandler"/>'s own pickup/drop-off sign convention.
    /// </summary>
    public static IReadOnlyList<FleetOrder> Resupply(Planet source, Planet destination, CargoType cargo, int amount) => [
        new(CommandType.Destination, DestinationObject: source),
        new(CommandType.Transfer, TransferCargo: cargo, TransferAmount: amount),
        new(CommandType.Destination, DestinationObject: destination),
        new(CommandType.Transfer, TransferCargo: cargo, TransferAmount: -amount),
        new(CommandType.Destination, DestinationObject: source),
        new(CommandType.Refuel),
    ];
}
