namespace ThreeLn.Reconstruction4021.Core.Turns;

/// <summary>
/// Owns exactly what Pascal's UpdateTurn owns: play the current empire's turn, move fleets,
/// advance to the next empire, and run the annual tick exactly once per wrap back to the first
/// empire. Human and AI turns go through the same dispatch (game.TurnHandlers), so there's no
/// separate "auto-play the AI empires" path.
/// </summary>
public sealed class TurnEngine(
    IVisibilityHandler visibility,
    IFleetMovementHandler fleetMovement,
    IAnnualTickHandler annualTick)
{
    /// <summary>
    /// Plays game.CurrentEmpire's turn — via its assigned ITurnHandler, human or AI alike — then
    /// moves fleets, advances CurrentEmpire to the next empire, and runs the annual tick exactly
    /// once per wrap back to the first empire. Mirrors ANACREON.PAS's UpdateTurn (:211-291),
    /// sequential path, one iteration.
    /// </summary>
    public void AdvanceOneTurn(Game game)
    {
        var current = game.CurrentEmpire
            ?? throw new InvalidOperationException("Game.CurrentEmpire must be set before the first turn.");

        visibility.RefreshVisibility(current, game);
        game.TurnHandlers[current].PlayTurn(current, game);

        var next = game.NextEmpire(current);
        fleetMovement.AdvanceFleets(game, current, next);
        fleetMovement.AdvanceStarbases(game, next);

        if (game.IsFirstEmpire(next))
            annualTick.RunAnnualTick(game);

        game.CurrentEmpire = next;
    }
}
