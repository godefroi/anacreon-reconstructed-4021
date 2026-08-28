using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Types;
using static ThreeLn.Reconstruction4021.Core.PascalMath;

namespace ThreeLn.Reconstruction4021.Core.Combat;

/// <summary>Casualties/Killed accumulated across a whole engagement, plus how it ended.</summary>
public sealed record CombatEngagementResult(AttackResultType Result, AttackTally Casualties, AttackTally Killed);

/// <summary>
/// The multi-round, multi-shell resolution loop (ATTNPE.PAS) plus outcome application (ATTACK.PAS,
/// Phase 5 commit 5f): repeatedly runs CombatEngine.Battle across every shell, advances/retreats
/// groups between rounds, decides when an engagement is over, then hands the result to
/// Combat/CombatOutcome.cs to actually apply it — culminating in <see cref="NPEAttack"/>, the entry
/// point Phase 6 (NPE AI) and Phase 8 (human auto-resolve) will both call. <see cref="NPEAttack"/>'s
/// real Pascal body also calls DestroyConstructionOrGate for a construction-site/stargate target —
/// wired in below as an early branch, matching Pascal's own <c>Target.ObjTyp IN [Con,Gate]</c> check,
/// which entirely bypasses CalculateCombatData/GetEnemy/the group-engage loop (see
/// Combat/CombatStandalone.cs, Phase 5 commit 5g).
///
/// <see cref="RetrIndex"/> (Pascal's <c>RetrIndex</c>/<c>RetreatIndex</c> parameter, threaded through
/// NPEAttack/FleetEngage/WorldEngage/FleetRetreats in real Pascal) is dropped entirely — confirmed by
/// reading every one of those bodies, none of them ever reads it; FleetRetreats' own retreat rule is a
/// hardcoded 30-round timeout, not parametrized by it. A real, if incomplete, feature in 1.31 itself,
/// not a gap this port introduced.
/// </summary>
public static class CombatResolution
{
    private static readonly ShellPosition[] _allShellPositions = Enum.GetValues<ShellPosition>();

    /// <summary>
    /// NPEAttack (ATTNPE.PAS:386-424): the round-robin resolution loop (FleetEngage/WorldEngage),
    /// then RestoreCombatant against both combatants and ResolveAttack to apply the outcome —
    /// <paramref name="target"/> must be an <see cref="IEconomicWorld"/> (dispatches to
    /// <see cref="WorldEngage"/>) or a <see cref="Fleet"/> (dispatches to <see cref="FleetEngage"/>),
    /// matching real Pascal's own <c>Target.ObjTyp=Flt</c> check. <c>Capture</c> is real Pascal's own
    /// <c>IF Intent=DestTrnAIT THEN Capture:=False ELSE Capture:=True</c> — every intent except
    /// DestroyTransports captures a conquered fleet's remains rather than letting them scatter.
    /// </summary>
    public static CombatEngagementResult NPEAttack(Empire attacker, Fleet attackerFleet, object target, AttackIntentionType intent, Game game, Random random)
    {
        var targetOwner = target switch {
            IEconomicWorld w => w.Owner,
            Fleet f => f.Owner,
            ConstructionSite c => c.Owner,
            Stargate g => g.Owner,
            _ => throw new ArgumentException($"NPEAttack: unexpected target type {target.GetType()}.", nameof(target)),
        };
        var hkSurprise = CombatEngine.ForcesUnknown(attackerFleet, targetOwner);

        if (target is ConstructionSite or Stargate) {
            CombatStandalone.DestroyConstructionOrGate(attacker, hkSurprise, target, game, random);
            return new CombatEngagementResult(AttackResultType.DefenderConquered, new AttackTally(), new AttackTally());
        }

        // target is Fleet or IEconomicWorld here — the ConstructionSite/Stargate cases already
        // returned above; the cast makes that explicit for CalculateCombatData/GetEnemy's own
        // narrower IFleetSource contract.
        var fleetSourceTarget = (IShipCargoHolder)target;
        var combatData = CombatEngine.CalculateCombatData(attacker, fleetSourceTarget);
        var groups = CombatEngine.DefaultDistribution(attackerFleet);
        var enemy = CombatEngine.GetEnemy(fleetSourceTarget);

        var engagement = target is Fleet
            ? FleetEngage(groups, enemy, combatData, intent, random)
            : WorldEngage(groups, enemy, combatData, intent, random);

        CombatOutcome.RestoreCombatant(attackerFleet, engagement.Casualties);
        CombatOutcome.RestoreCombatant(target, engagement.Killed);

        var capture = intent != AttackIntentionType.DestroyTransports;
        CombatOutcome.ResolveAttack(engagement.Result, attackerFleet, target, hkSurprise, capture, engagement.Casualties, engagement.Killed, game, random);

        return engagement;
    }

    /// <summary>FleetEngage (ATTNPE.PAS:221-286): the round-robin loop for a fleet-vs-fleet engagement.</summary>
    public static CombatEngagementResult FleetEngage(List<GroupRecord> groups, EnemyForces enemy, CombatDataRecord combatData, AttackIntentionType intent, Random random)
    {
        var casualties = new AttackTally();
        var killed = new AttackTally();
        var result = AttackResultType.None;
        var engageRound = 0;

        do {
            engageRound++;
            FleetRetreats(engageRound, groups, ref result);
            FleetEngageTargetting(groups, enemy, intent);
            GroupEngage(groups, enemy, combatData, casualties, killed, random, ref result);
        } while (result == AttackResultType.None);

        return new CombatEngagementResult(result, casualties, killed);
    }

    /// <summary>WorldEngage (ATTNPE.PAS:288-384): the round-robin loop for a fleet-vs-world engagement.</summary>
    public static CombatEngagementResult WorldEngage(List<GroupRecord> groups, EnemyForces enemy, CombatDataRecord combatData, AttackIntentionType intent, Random random)
    {
        var casualties = new AttackTally();
        var killed = new AttackTally();
        var result = AttackResultType.None;
        var engageRound = 0;

        do {
            engageRound++;
            FleetRetreats(engageRound, groups, ref result);
            WorldEngageTargetting(groups, enemy, intent);
            GroupEngage(groups, enemy, combatData, casualties, killed, random, ref result);
        } while (result == AttackResultType.None);

        return new CombatEngagementResult(result, casualties, killed);
    }

    /// <summary>
    /// GroupEngage (ATTNPE.PAS:135-158): one full round — Battle at every shell in
    /// order, then AdvanceGroups, then decides whether the engagement is over. CombatDetails is fresh
    /// every round here (matching real Pascal's own local, FillChar-zeroed VAR) and discarded — nothing
    /// reads it past this call in real Pascal either.
    /// </summary>
    private static void GroupEngage(
        List<GroupRecord> groups, EnemyForces enemy, CombatDataRecord combatData,
        AttackTally casualties, AttackTally killed, Random random, ref AttackResultType result)
    {
        var details = new CombatDetails();
        foreach (var pos in _allShellPositions) {
            CombatEngine.Battle(groups, enemy, pos, combatData, details, casualties, killed, random);
        }

        AdvanceGroups(groups);

        if (groups.All(g => g.Sta == GroupStatus.Destroyed)) {
            result = AttackResultType.AttackerDestroyed;
        } else if (result != AttackResultType.AttackerRetreats && CombatEngine.EnemySurrenders(groups, enemy, casualties, killed, combatData)) {
            result = AttackResultType.DefenderConquered;
        }
    }

    /// <summary>FleetRetreats (ATTNPE.PAS:160-192): a fixed 30-round timeout, once no live group is anything but a ship (no troops left to press the attack).</summary>
    private static void FleetRetreats(int engageRound, IReadOnlyList<GroupRecord> groups, ref AttackResultType result)
    {
        if (engageRound > 30 && NoMenLeft(groups)) {
            result = AttackResultType.AttackerRetreats;
        }
    }

    private static bool NoMenLeft(IReadOnlyList<GroupRecord> groups) =>
        groups.All(g => g.Sta == GroupStatus.Destroyed || g.Typ is not (AttackType.Legion or AttackType.NinjaLegion));

    /// <summary>TransportsLeft (ATTNPE.PAS:194-207): whether every still-live group is a transport (i.e. every non-transport combat ship is gone).</summary>
    private static bool TransportsLeft(IReadOnlyList<GroupRecord> groups) =>
        groups.All(g => g.Sta == GroupStatus.Destroyed || g.Typ is AttackType.Jumptransport or AttackType.Transport);

    /// <summary>AllAdvance (ATTACK.PAS-adjacent, ATTNPE.PAS:113-122): sends every live combat-ship group (not transports, not troops) forward one shell, short of SubOrbit.</summary>
    private static void AllAdvance(IReadOnlyList<GroupRecord> groups)
    {
        foreach (var g in groups) {
            if (g.Sta != GroupStatus.Destroyed && g.Pos < ShellPosition.SubOrbit
                && g.Typ is AttackType.Fighter or AttackType.HunterKiller or AttackType.Jumpship or AttackType.Penetrator or AttackType.Starship) {
                g.Sta = GroupStatus.Advancing;
            }
        }
    }

    /// <summary>TrnAdvance (ATTNPE.PAS:124-133): sends transports (and their fighter escort) forward one shell, including from SubOrbit down to Ground.</summary>
    private static void TrnAdvance(IReadOnlyList<GroupRecord> groups)
    {
        foreach (var g in groups) {
            if (g.Sta != GroupStatus.Destroyed && g.Pos < ShellPosition.Ground
                && g.Typ is AttackType.Fighter or AttackType.Jumptransport or AttackType.Transport) {
                g.Sta = GroupStatus.Advancing;
            }
        }
    }

    /// <summary>
    /// AdvanceGroups (ATTACK.PAS:183-215): moves every Advancing group one shell forward and every
    /// Retreating group one shell back, resetting to Ready either way. A transport/jumptransport
    /// reaching Ground with troops aboard swaps Num/Gat and becomes a troop group (Typ:=GatTyp) — and
    /// its Trg is unconditionally set to Legion regardless of whether the cargo was actually
    /// NinjaLegion, a real Pascal quirk (ATTACK.PAS:205) preserved verbatim, not fixed.
    /// </summary>
    private static void AdvanceGroups(IReadOnlyList<GroupRecord> groups)
    {
        foreach (var g in groups) {
            if (g.Sta == GroupStatus.Advancing) {
                g.Pos++;
                g.Sta = GroupStatus.Ready;

                if (g.Pos == ShellPosition.Ground && g.Gat != 0) {
                    (g.Num, g.Gat) = (g.Gat, g.Num);
                    g.TrnTyp = g.Typ.AsShipType();
                    g.Typ = g.GatTyp!.Value;
                    g.Trg = AttackType.Legion;
                }
            } else if (g.Sta == GroupStatus.Retreating) {
                g.Pos--;
                g.Sta = GroupStatus.Ready;
            }
        }
    }

    /// <summary>
    /// Targetting, FleetEngage's own nested copy (ATTNPE.PAS:230-271): assigns each live group's Trg to
    /// its best available target at its own shell, or sends the whole fleet advancing if nothing at any
    /// shell has a target yet (and no group has reached SubOrbit — fleet combat never proceeds past it,
    /// see AllAdvance).
    /// </summary>
    private static void FleetEngageTargetting(List<GroupRecord> groups, EnemyForces enemy, AttackIntentionType intent)
    {
        var targetAvailable = false;
        ShellPosition? generalPos = null;

        foreach (var g in groups) {
            if (g.Sta == GroupStatus.Destroyed) {
                continue;
            }
            generalPos ??= g.Pos;

            var candidates = GetTargetCandidates(g.Pos, enemy);
            if (candidates.Count > 0) {
                targetAvailable = true;
                PrioritizeTargetCandidates(g.Typ, g.Num, intent, candidates);
                g.Trg = GetBestTarget(candidates);
            }
        }

        if (!targetAvailable && generalPos != ShellPosition.SubOrbit) {
            AllAdvance(groups);
        }
    }

    /// <summary>
    /// Targetting, WorldEngage's own nested copy (ATTNPE.PAS:300-352): same as
    /// <see cref="FleetEngageTargetting"/>, except a group that has reached Ground always targets
    /// Legion (defending troops), overriding whatever GetBestTarget picked — real Pascal fires this
    /// unconditionally for any live Ground group, targets-available or not — and "no target anywhere"
    /// sends transports forward (<see cref="TrnAdvance"/>) once the deepest shell reached is SubOrbit
    /// or no non-transport combat ship remains, rather than advancing everything
    /// (<see cref="AllAdvance"/>).
    /// </summary>
    private static void WorldEngageTargetting(List<GroupRecord> groups, EnemyForces enemy, AttackIntentionType intent)
    {
        var targetAvailable = false;
        ShellPosition? deepestPos = null; // Pascal calls this LowestPos despite tracking the maximum Pos seen

        foreach (var g in groups) {
            if (g.Sta == GroupStatus.Destroyed) {
                continue;
            }
            deepestPos = deepestPos is { } dp && g.Pos > dp ? g.Pos : deepestPos ?? g.Pos;

            var candidates = GetTargetCandidates(g.Pos, enemy);
            if (candidates.Count > 0) {
                targetAvailable = true;
                PrioritizeTargetCandidates(g.Typ, g.Num, intent, candidates);
                g.Trg = GetBestTarget(candidates);
            }

            if (g.Pos == ShellPosition.Ground) {
                g.Trg = AttackType.Legion;
            }
        }

        if (!targetAvailable) {
            if (deepestPos == ShellPosition.SubOrbit || TransportsLeft(groups)) {
                TrnAdvance(groups);
            } else {
                AllAdvance(groups);
            }
        }
    }

    /// <summary>One candidate an attacking group could aim at, with its computed priority (ATTNPE.PAS:38-43's TargetRecord — a different shape from ATTACK.PAS's own, unrelated, TargetArray).</summary>
    private sealed class TargetCandidate(AttackType targTyp, int targNum)
    {
        public AttackType TargTyp { get; } = targTyp;
        public int TargNum { get; } = targNum;
        public int Priority { get; set; }
    }

    /// <summary>GetTargetArray (ATTNPE.PAS:45-65): every AttackType present at this shell, in AttackType's own LAM..nnj order.</summary>
    private static List<TargetCandidate> GetTargetCandidates(ShellPosition pos, EnemyForces enemy)
    {
        var candidates = new List<TargetCandidate>();
        foreach (var type in Enum.GetValues<AttackType>()) {
            if (enemy[pos, type] > 0) {
                candidates.Add(new TargetCandidate(type, enemy[pos, type]));
            }
        }
        return candidates;
    }

    /// <summary>PrioritizeTargetArray (ATTNPE.PAS:67-90): scores each candidate by how much damage the attacker would do to it, weighted by its remaining military value — biased toward or away from transports by <paramref name="intent"/>.</summary>
    private static void PrioritizeTargetCandidates(AttackType attackerType, int attackerNum, AttackIntentionType intent, List<TargetCandidate> candidates)
    {
        foreach (var c in candidates) {
            var total = (c.TargNum / 1000.0) * (CombatConstants.CombatTable[(attackerType, c.TargTyp)] / 100.0) * CombatConstants.CombatPower[c.TargTyp];
            if (total < 2) {
                total = 2;
            }
            if (intent == AttackIntentionType.CaptureTransports && c.TargTyp is AttackType.Transport or AttackType.Jumptransport) {
                total = 1;
            } else if (intent == AttackIntentionType.DestroyTransports && c.TargTyp is not (AttackType.Transport or AttackType.Jumptransport)) {
                total = 1;
            }
            c.Priority = IntLmt(total);
        }
    }

    /// <summary>
    /// GetBestTarget (ATTNPE.PAS:92-107): the candidate with the highest priority — on a tie, the
    /// *last* one scanned in list order wins (Pascal's own <c>&gt;=</c> comparison keeps overwriting),
    /// so this walks the list in the same order <see cref="GetTargetCandidates"/> built it in
    /// (AttackType's own LAM..nnj order) to match exactly. Only ever called with a nonempty list —
    /// real Pascal's own only call site is itself guarded by <c>NoOfTargets&gt;0</c>.
    /// </summary>
    private static AttackType GetBestTarget(IReadOnlyList<TargetCandidate> candidates)
    {
        if (candidates.Count == 0) {
            throw new ArgumentException("GetBestTarget: no candidates — real Pascal only ever calls this when NoOfTargets>0.", nameof(candidates));
        }

        var highestPriority = 0;
        var best = candidates[0].TargTyp;
        foreach (var c in candidates) {
            if (c.Priority >= highestPriority) {
                highestPriority = c.Priority;
                best = c.TargTyp;
            }
        }
        return best;
    }
}
