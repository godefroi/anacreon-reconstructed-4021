using ThreeLn.Reconstruction4021.Core.Entities;

namespace ThreeLn.Reconstruction4021.Core.Turns;

/// <summary>
/// Moves fleets and starbases after an empire's turn ends. actingEmpire/nextEmpire mirrors
/// Pascal's asymmetric UpdateAllFleets(Player, NextEmpire(Player)) call — which empire's ships
/// move under which rules is fleet-subsystem logic, out of scope for the turn engine itself.
/// </summary>
public interface IFleetMovementHandler
{
    void AdvanceFleets(Game game, Empire actingEmpire, Empire nextEmpire);
    void AdvanceStarbases(Game game, Empire empire);
}
