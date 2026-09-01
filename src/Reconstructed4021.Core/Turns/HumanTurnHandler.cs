using Reconstructed4021.Core.Entities;

namespace Reconstructed4021.Core.Turns;

/// <summary>The interactive UI handler for a human empire. See <see cref="ITurnHandler"/>.</summary>
public sealed class HumanTurnHandler : ITurnHandler
{
    public bool IsHuman => true;

    /// <summary>
    /// A human's actions already happened synchronously through the UI (menu commands mutating
    /// Game directly) before End Turn was ever pressed -- PLAYTURN.PAS's own interactive command
    /// loop runs entirely before UpdateTurn is called. Nothing left to do here.
    /// </summary>
    public void PlayTurn(Empire empire, Game game) { }
}
