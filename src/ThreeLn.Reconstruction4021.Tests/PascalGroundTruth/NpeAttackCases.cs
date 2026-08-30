using ThreeLn.Reconstruction4021.Core.Combat;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs shared between the golden-file generator (GoldenFileTests) and the always-on
/// NpeAttackTests.MatchesGoldenFile. Expected outputs live exclusively in
/// reference/verify/golden/npeattack.golden, computed patch-based: a real run of ATTNPE.PAS's own
/// NPEAttack (the full body, including RestoreCombatant/ResolveAttack — see
/// ATTNPE.PAS.patch/ATTACK.PAS.patch) against a hand-assembled Universe^ (reference/verify/runworld.pas's
/// npeattack domain).
///
/// The attacker is always Empire1's fleet: 200 fighters, 200 hunter-killers, plus (when
/// AttackerCarriesTroops) a 20-ship jumptransport group carrying 1000 ninja-legion cargo —
/// DefaultDistribution picks ninjas over plain legions whenever both are present, so this is the one
/// real way to get a troop-carrying group without a second harness path. Empire2's DefenseSettings is
/// always the same InitDefenseRecord distribution EmpireFactory.SeedDefenseSettings seeds on the C#
/// side. Empire1's capital sits at (0,0), Empire2's (the usual Planet target) at (50,50) — fixed by
/// runworld.pas itself — so a case that also sets Planet3Present drives ConquerEmpire's own per-planet
/// cascade (once Empire2's capital falls) via a second, caller-positioned/populated Empire2 world.
/// </summary>
public sealed record NpeAttackCase(
    string Name, TechLevel DefenderTech, WorldClass DefenderClass, bool AttackerCarriesTroops,
    int DefenderFgt, int DefenderHkr, int DefenderMen, AttackIntentionType Intent, bool TargetIsFleet, int RngFixedValue,
    bool Planet3Present = false, int Planet3X = 0, int Planet3Y = 0, int Planet3Population = 0,
    int Planet3RevIndex = 0, TechLevel Planet3Tech = TechLevel.PreTech) : INamedCase;

internal static class NpeAttackCases
{
    public static readonly IReadOnlyList<NpeAttackCase> All = [
        // No troops of its own means the attacker's fleet trivially satisfies FleetRetreats'
        // NoMenLeft check (no Legion/NinjaLegion group ever exists to make it false) -- so a weak
        // defender that never quite surrenders or gets fully destroyed just runs out the 30-round
        // clock and the attacker retreats.
        new(Name: "WeakDefenderNoAttackerTroopsRetreats", DefenderTech: TechLevel.Jump, DefenderClass: WorldClass.EarthLike,
            AttackerCarriesTroops: false, DefenderFgt: 10, DefenderHkr: 10, DefenderMen: 50,
            Intent: AttackIntentionType.Conquer, TargetIsFleet: false, RngFixedValue: 0),

        // Same weak defender, but the attacker's own jtn group (carrying 1000 nnj) means NoMenLeft is
        // no longer trivially true -- and once it lands, its troops overwhelm the defending 50 men,
        // exercising AdvanceGroups' Ground-landing swap, WorldEngage's own Trg:=men override, and (once
        // the capital falls) ResolveAttack's DefenderConquered/ConquerWorld/ConquerEmpire path -- with
        // no other Empire2 world to consider, ConquerEmpire's own loop finds nothing and the empire is
        // totally destroyed (DestroyEmpire).
        new(Name: "WeakDefenderWithAttackerTroopsConquers", DefenderTech: TechLevel.Jump, DefenderClass: WorldClass.EarthLike,
            AttackerCarriesTroops: true, DefenderFgt: 10, DefenderHkr: 10, DefenderMen: 50,
            Intent: AttackIntentionType.Conquer, TargetIsFleet: false, RngFixedValue: 0),

        // A much stronger defender, still against a troopless attacker -- still retreats after 30
        // rounds (real Pascal: the timeout doesn't care how the fight is going, only whether the
        // attacker has any live troops left to press with), but with real, larger casualties on both
        // sides from many more rounds of ship-vs-ship combat first.
        new(Name: "StrongDefenderNoAttackerTroopsRetreats", DefenderTech: TechLevel.Jump, DefenderClass: WorldClass.EarthLike,
            AttackerCarriesTroops: false, DefenderFgt: 200, DefenderHkr: 200, DefenderMen: 500,
            Intent: AttackIntentionType.Conquer, TargetIsFleet: false, RngFixedValue: 0),

        // TargetIsFleet routes through FleetEngage instead of WorldEngage -- EnemySurrenders' own
        // Target-is-Fleet branch (a different set of surrender conditions than a world's) is what
        // actually ends this one; DefenderConquered is real Pascal's shared "the enemy gave up"
        // result for both target kinds, not a world-only outcome. ResolveAttack's own Target-is-Fleet
        // branch adds the target's surviving ships back into Killed (GetShips(Target,Sh) reads them
        // post-RestoreCombatant) before DestroyFleet -- this is the one case where Killed moves between
        // the 5e-only Casualties/Killed tally and the full 5f-restored outcome.
        new(Name: "FleetTargetWithAttackerTroopsSurrenders", DefenderTech: TechLevel.Jump, DefenderClass: WorldClass.EarthLike,
            AttackerCarriesTroops: true, DefenderFgt: 5, DefenderHkr: 5, DefenderMen: 20,
            Intent: AttackIntentionType.Conquer, TargetIsFleet: true, RngFixedValue: 0),

        // DestroyTransports intent exercises PrioritizeTargetCandidates' own intent-driven branch
        // (biasing every non-transport candidate's priority down to 1) even though this particular
        // defender has no transports of its own to actually destroy. Also exercises ResolveAttack's own
        // Capture:=False path (Intent=DestTrnAIT), so a conquered fleet's remains are never captured.
        new(Name: "DestroyTransportsIntent", DefenderTech: TechLevel.Jump, DefenderClass: WorldClass.EarthLike,
            AttackerCarriesTroops: true, DefenderFgt: 10, DefenderHkr: 10, DefenderMen: 50,
            Intent: AttackIntentionType.DestroyTransports, TargetIsFleet: false, RngFixedValue: 0),

        // ConquerEmpire's per-planet cascade, branch 1 (ATTACK.PAS:1044-1049): once the weak capital
        // falls, Planet3 -- far from Empire2's own capital (50,50), close to Empire1's (0,0), low
        // population -- immediately joins the conqueror outright. With both Empire2 worlds gone, the
        // empire ends up totally destroyed regardless (DestroyEmpire) -- this case's own point is
        // Planet3's ConquerWorld side effects (owner/efficiency/RevIndex), not the empire-level outcome.
        new(Name: "ConquerEmpireImmediateConquestOfSecondWorld", DefenderTech: TechLevel.Jump, DefenderClass: WorldClass.EarthLike,
            AttackerCarriesTroops: true, DefenderFgt: 10, DefenderHkr: 10, DefenderMen: 50,
            Intent: AttackIntentionType.Conquer, TargetIsFleet: false, RngFixedValue: 0,
            Planet3Present: true, Planet3X: 5, Planet3Y: 5, Planet3Population: 500, Planet3RevIndex: 5, Planet3Tech: TechLevel.Jump),

        // ConquerEmpire's new-capital branch (ATTACK.PAS:1063-1096,1132-1138): Planet3's high population
        // (>900, so branch 1's Pop<Rnd(900,1100) fails) and moderate RevIndex (<=50 and <=35, so neither
        // branch 2 nor 3 fires) fall through to the new-capital candidate check; Bio-level tech
        // (>JmpTchLvl) makes it a real candidate, so it becomes Empire2's new capital via NewCapital's
        // own SetEmpireTechnology/SetCapital/SetType/ChangeRevIndex/SetEfficiency sequence, and the
        // empire survives with one world instead of being destroyed.
        new(Name: "ConquerEmpireNewCapitalChosen", DefenderTech: TechLevel.Jump, DefenderClass: WorldClass.EarthLike,
            AttackerCarriesTroops: true, DefenderFgt: 10, DefenderHkr: 10, DefenderMen: 50,
            Intent: AttackIntentionType.Conquer, TargetIsFleet: false, RngFixedValue: 0,
            Planet3Present: true, Planet3X: 5, Planet3Y: 5, Planet3Population: 2000, Planet3RevIndex: 20, Planet3Tech: TechLevel.Bio),
    ];

    /// <summary>MethodDataSource shape for NpeAttackTests.MatchesGoldenFile.</summary>
    public static IEnumerable<Func<NpeAttackCase>> AsDataSource() => All.Select(c => (Func<NpeAttackCase>)(() => c));
}
