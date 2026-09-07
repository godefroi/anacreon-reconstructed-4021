namespace Reconstructed4021.Core.Types;

/// <c>CommandTypes</c> (ORDERS.PAS:37-45) -- one compiled order in a fleet's order queue.
/// <see cref="Refuel"/> is this port's own addition (no ORDERS.PAS token) -- see
/// <see cref="Entities.FleetOrderCompiler"/>'s own doc comment on that command.
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
}
