namespace Reconstructed4021.Core.Types;

/// <c>CommandTypes</c> (ORDERS.PAS:37-45) -- one compiled order in a fleet's order queue.
/// <see cref="Refuel"/> and <see cref="Join"/> are this port's own additions (no ORDERS.PAS token) --
/// see <see cref="Entities.FleetOrderCompiler"/>'s own doc comment on those commands.
public enum CommandType
{
    None,
    Destination,
    Transfer,
    Repeat,
    Abort,
    Sweep,
    Stop,
    Wait,
    Refuel,
    Join,
}
