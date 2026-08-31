namespace Reconstructed4021.Core.Types;

/// <c>CommandTypes</c> (ORDERS.PAS:37-45) -- one compiled order in a fleet's order queue.
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
}
