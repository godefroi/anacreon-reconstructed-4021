using ThreeLn.Reconstruction4021.Core.Entities;

namespace ThreeLn.Reconstruction4021.Core.Turns;

/// <summary>
/// AI for Kingdom1 (passive) and Kingdom2 (aggressive) NPE empires — NPE02.PAS's
/// ImplementKingdom1NPE, the one real implementation both persona presets share (they differ only
/// in NPECharacterRecord's seed values, not in code). Shell only for now: PlayTurn's real logic
/// (NPE00.PAS's ReviewNews/DefendEmpire/ImperialExpansion/etc., NPE02.PAS's UpdateFleets) lands in
/// Phase 6 commit 6d, once 6c's NPEINTR.PAS toolkit it depends on exists. One instance per Kingdom
/// empire, via Game.TurnHandlers — Persona/per-enemy diplomacy state will live as this class's own
/// instance fields (see PORT_DESIGN.md), not a separate lookup table.
/// </summary>
public sealed class KingdomTurnHandler : ITurnHandler
{
    public bool IsHuman => false;

    public void PlayTurn(Empire empire, Game game) =>
        throw new NotImplementedException("KingdomTurnHandler.PlayTurn lands in Phase 6 commit 6d.");
}
