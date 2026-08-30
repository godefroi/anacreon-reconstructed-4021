using ThreeLn.Reconstruction4021.Core.Entities;

namespace ThreeLn.Reconstruction4021.Core.Turns;

/// <summary>
/// Plays one empire's turn — the interactive UI for a human, or an NPE AI's decision-making.
/// Assigned per-empire via Game.TurnHandlers, so different empires (or different AI variants)
/// can use different implementations without TurnEngine knowing the difference.
/// </summary>
public interface ITurnHandler
{
    /// <summary>True only for the interactive UI handler. Drives Game.AnyHumanPlayersRemain.</summary>
    bool IsHuman { get; }

    void PlayTurn(Empire empire, Game game);
}
