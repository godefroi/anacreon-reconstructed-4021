using Reconstructed4021.Core.Entities;

namespace Reconstructed4021.Core.Turns;

/// <summary>
/// Moves fleets and starbases after an empire's turn ends. actingEmpire/nextEmpire mirrors
/// Pascal's asymmetric UpdateAllFleets(Player, NextEmpire(Player)) call — which empire's ships
/// move under which rules is fleet-subsystem logic, out of scope for the turn engine itself.
/// </summary>
public interface IFleetMovementHandler
{
    void AdvanceFleets(Game game, Empire actingEmpire, Empire nextEmpire);
    void AdvanceStarbases(Game game, Empire empire);

    /// <summary>
    /// Resolves as much of every stationary (Ready, no <see cref="Fleet.Destination"/>) fleet's pending
    /// order queue as it can, for every fleet <paramref name="empire"/> owns -- called once right before
    /// that empire's own turn (<paramref name="allowWait"/> true) and once right after it ends
    /// (<paramref name="allowWait"/> false). See <see cref="FleetMovementHandler.ResolveOrders"/>'s own
    /// doc comment for the two-phase cross-fleet resolution and why WAIT only ever advances on the
    /// former.
    /// </summary>
    void ResolveOrders(Game game, Empire empire, bool allowWait);
}
