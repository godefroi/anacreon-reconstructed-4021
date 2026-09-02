using Reconstructed4021.Core.Entities;

namespace Reconstructed4021.Core.Combat;

/// <summary>
/// The player-driven round-by-round engine (ATTCOMM.PAS's own <c>Engage</c>/<c>GroupEngage</c>), as
/// opposed to <see cref="CombatResolution"/>'s fully automatic <c>FleetEngage</c>/<c>WorldEngage</c>
/// (ATTNPE.PAS, used by <see cref="CombatResolution.NPEAttack"/> for NPE attacks and for a human who
/// accepts "Standard battle configuration"). Both drive the exact same <see cref="CombatEngine.Battle"/>/
/// <see cref="CombatResolution.AdvanceGroups"/>/<see cref="CombatEngine.EnemySurrenders"/> primitives —
/// this type only adds the player-facing wrapper: one round per <see cref="Engage"/>/<see cref="Retreat"/>
/// call instead of a loop to completion, and manual <see cref="SetTarget"/> instead of ATTNPE.PAS's own
/// <c>Targetting</c>/<c>GetBestTarget</c> auto-aim, which real Pascal's interactive <c>Engage</c> never
/// calls at all — a group with no player-chosen target does zero damage while still taking return fire,
/// confirmed against source, not a bug.
/// </summary>
public sealed class InteractiveCombatState
{
    private static readonly Types.ShellPosition[] _allShellPositions = Enum.GetValues<Types.ShellPosition>();

    public IReadOnlyList<GroupRecord> Groups { get; }
    public EnemyForces Enemy { get; }
    public CombatDataRecord CombatData { get; }
    public AttackTally Casualties { get; } = new();
    public AttackTally Killed { get; } = new();

    /// <summary>
    /// This round's per-(AttackType,group) damage only — ATTACK.PAS's own <c>Details</c> is
    /// <c>FillChar</c>'d at the top of every <c>GroupEngage</c> call (ATTCOMM.PAS:326), so the real
    /// 'D'etails command only ever shows the most recent round, never a cumulative total.
    /// </summary>
    public CombatDetails LastRoundDetails { get; private set; } = new();

    public AttackResultType Result { get; private set; } = AttackResultType.None;
    public bool IsOver => Result != AttackResultType.None;

    public InteractiveCombatState(List<GroupRecord> groups, EnemyForces enemy, CombatDataRecord combatData)
    {
        Groups = groups;
        Enemy = enemy;
        CombatData = combatData;
    }

    /// <summary>Engage ('E'): one full round at whatever Pos/Trg every group already has. Throws once the engagement is already over — every caller must check <see cref="IsOver"/> first.</summary>
    public void Engage(Random random)
    {
        if (IsOver) {
            throw new InvalidOperationException("InteractiveCombatState.Engage: the engagement is already over.");
        }
        RunRound(random);
    }

    /// <summary>
    /// Retreat ('R', ATTCOMM.PAS:463-507, the "Are you sure" confirm is the caller's job): forces
    /// <see cref="Result"/> to <see cref="AttackResultType.AttackerRetreats"/> *before* the round runs
    /// — this is load-bearing, not incidental: GroupEngage's own surrender check is gated on
    /// <c>Result&lt;&gt;AttRetreatsART</c>, so pre-setting it here is what suppresses that check for
    /// this one round, while <see cref="AttackResultType.AttackerDestroyed"/> can still override it if
    /// this round wipes every group out. Ends the engagement unconditionally either way — real Pascal
    /// never gives the retreating round a chance to just continue the battle.
    /// </summary>
    public void Retreat(Random random)
    {
        if (IsOver) {
            throw new InvalidOperationException("InteractiveCombatState.Retreat: the engagement is already over.");
        }
        Result = AttackResultType.AttackerRetreats;
        RunRound(random);
    }

    /// <summary>GroupEngage (ATTCOMM.PAS:247-350 / ATTNPE.PAS:135-158): one pass of Battle across every shell, then AdvanceGroups, then the end-of-round checks. Unguarded by <see cref="IsOver"/> — <see cref="Retreat"/> needs Result already set going in.</summary>
    private void RunRound(Random random)
    {
        var details = new CombatDetails();
        foreach (var pos in _allShellPositions) {
            CombatEngine.Battle(Groups, Enemy, pos, CombatData, details, Casualties, Killed, random);
        }
        LastRoundDetails = details;

        CombatResolution.AdvanceGroups(Groups);

        if (Groups.All(g => g.Sta == GroupStatus.Destroyed)) {
            Result = AttackResultType.AttackerDestroyed;
        } else if (Result != AttackResultType.AttackerRetreats && CombatEngine.EnemySurrenders(Groups, Enemy, Casualties, Killed, CombatData)) {
            Result = AttackResultType.DefenderConquered;
        }
    }

    /// <summary>Target ('T', ATTCOMM.PAS:509-546): pure manual re-aim, consumes no round. <paramref name="target"/> null clears Trg (Pascal's '-'/NoRes).</summary>
    public void SetTarget(GroupRecord group, Types.AttackType? target) => group.Trg = target;

    /// <summary>
    /// Advance legality, GroupMove's own per-group gate (ATTCOMM.PAS:352-461's
    /// <c>(Pos&lt;&gt;Grnd) AND ((DTyp&lt;&gt;Flt) OR (Pos&lt;&gt;Orbit)) AND ((Pos&lt;&gt;SbOrb) OR
    /// (Typ IN [fgt,trn,jtn]))</c> verbatim): can't advance from Ground; a group fighting a Fleet target
    /// can't advance past Orbit (fleets have no SubOrbit/Ground shell); from SubOrbit only fighters and
    /// transports/jumptransports may advance to Ground.
    /// </summary>
    public bool CanAdvance(GroupRecord g) =>
        g.Sta != GroupStatus.Destroyed
        && g.Pos != Types.ShellPosition.Ground
        && !(CombatData.Target is Fleet && g.Pos == Types.ShellPosition.Orbit)
        && !(g.Pos == Types.ShellPosition.SubOrbit && g.Typ is not (Types.AttackType.Fighter or Types.AttackType.Transport or Types.AttackType.Jumptransport));

    /// <summary>
    /// Retreat legality, GroupMove's own per-group gate (ATTCOMM.PAS:352-461's
    /// <c>NOT (((Pos=Grnd) AND (Typ&lt;&gt;fgt)) OR (Pos=DpSpc))</c> verbatim): at Ground, only
    /// fighters can retreat; at DeepSpace (the outermost shell), nothing can retreat further.
    /// </summary>
    public bool CanRetreat(GroupRecord g) =>
        g.Sta != GroupStatus.Destroyed
        && !(g.Pos == Types.ShellPosition.Ground && g.Typ != Types.AttackType.Fighter)
        && g.Pos != Types.ShellPosition.DeepSpace;

    /// <summary>Queues an advance for the next round's AdvanceGroups. Caller must check <see cref="CanAdvance"/> first — matches GroupMove only ever prompting a group when at least one of Advance/Retreat is legal.</summary>
    public void QueueAdvance(GroupRecord g) => g.Sta = GroupStatus.Advancing;

    /// <summary>Queues a retreat for the next round's AdvanceGroups. Caller must check <see cref="CanRetreat"/> first.</summary>
    public void QueueRetreat(GroupRecord g) => g.Sta = GroupStatus.Retreating;

    /// <summary>CancelAdvance (ATTCOMM.PAS:367-377): reverts every queued Advance/Retreat back to Ready, leaving Destroyed groups untouched — GroupMove's own "Maneuver (y/N)" decline path, or its Esc-mid-loop unwind.</summary>
    public void CancelAllQueuedMoves()
    {
        foreach (var g in Groups) {
            if (g.Sta is GroupStatus.Advancing or GroupStatus.Retreating) {
                g.Sta = GroupStatus.Ready;
            }
        }
    }
}

/// <summary>Entry point for the interactive engine — GameShell.Attack()'s own counterpart to CombatResolution.NPEAttack.</summary>
public static class InteractiveCombat
{
    /// <summary>Mirrors AttackCommand's own setup (ATTCOMM.PAS:1621-1622): CalculateCombatData then GetEnemy, against whatever <paramref name="groups"/> Fleet Group Configuration (or DefaultDistribution, for "Standard battle configuration") produced.</summary>
    public static InteractiveCombatState BeginEngagement(Empire attacker, IShipCargoHolder target, List<GroupRecord> groups)
    {
        var combatData = CombatEngine.CalculateCombatData(attacker, target);
        var enemy = CombatEngine.GetEnemy(target);
        return new InteractiveCombatState(groups, enemy, combatData);
    }
}
