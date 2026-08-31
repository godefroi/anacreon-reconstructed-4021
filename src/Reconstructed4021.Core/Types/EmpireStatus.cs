namespace Reconstructed4021.Core.Types;

/// <summary>
/// EmpireActive/InUse's real shape (PRIMINTR.PAS:962-965's EmpireActive reads just
/// EmpireData[Emp].InUse; INTRFACE.PAS:1612-1658's DestroyEmpire sets InUse:=False) plus the
/// intermediate window Pascal's own ConquerEmpire (ATTACK.PAS:985-1139) leaves a defeated human in
/// before their own next turn-prologue (PROLOG.PAS:456-488's EmpireNews) finishes the teardown.
/// See docs/PORT_DESIGN.md's "Empire elimination" section.
/// </summary>
public enum EmpireStatus
{
    /// <summary>EmpireActive(Emp) true, never defeated (or recovered a new capital).</summary>
    Active,

    /// <summary>
    /// A human whose capital just fell with no other world to fall back to (ConquerEmpire's human
    /// branch, ATTACK.PAS:1124-1128). EmpireActive stays true, Capital is null, Empire.DefeatedBy
    /// names the conqueror, but nothing else is torn down yet — fleets/starbases/other planets are
    /// untouched, and diplomacy/AI treat this empire exactly like any other. Never set for an NPE —
    /// an NPE always goes straight to Eliminated.
    /// </summary>
    PendingElimination,

    /// <summary>
    /// EmpireActive(Emp) false. Fully torn down: worlds/starbases reassigned to Independent, fleets
    /// aborted then destroyed, news cleared, Game.TurnHandlers entry removed (CleanUpNPE's own
    /// Dispose(Data), NPE.PAS:34-45). Still a permanent Game.Empires member.
    /// </summary>
    Eliminated,
}
