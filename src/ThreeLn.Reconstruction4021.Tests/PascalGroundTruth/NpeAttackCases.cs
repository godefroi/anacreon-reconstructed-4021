using ThreeLn.Reconstruction4021.Core.Combat;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs shared between the golden-file generator (GoldenFileTests) and the always-on
/// NpeAttackTests.MatchesGoldenFile. Expected outputs live exclusively in
/// reference/verify/golden/npeattack.golden, computed patch-based: a real run of ATTNPE.PAS's own
/// NPEAttack (patched to stop at Result/Casualties/Killed — see ATTNPE.PAS.patch) against a
/// hand-assembled Universe^ (reference/verify/runworld.pas's npeattack domain).
///
/// Unlike CombatCases (which isolates one round of Battle at one shell), this domain exercises the
/// whole multi-round resolution loop — FleetRetreats/Targetting/GroupEngage/AdvanceGroups — end to
/// end through NPEAttack's own real entry point. The attacker is always Empire1's fleet: 200
/// fighters, 200 hunter-killers, plus (when AttackerCarriesTroops) a 20-ship jumptransport group
/// carrying 1000 ninja-legion cargo — DefaultDistribution picks ninjas over plain legions whenever
/// both are present, so this is the one real way to get a troop-carrying group without a second
/// harness path. Empire2's DefenseSettings is always the same InitDefenseRecord distribution
/// EmpireFactory.SeedDefenseSettings seeds on the C# side.
/// </summary>
public sealed record NpeAttackCase(
    string Name, TechLevel DefenderTech, WorldClass DefenderClass, bool AttackerCarriesTroops,
    int DefenderFgt, int DefenderHkr, int DefenderMen, AttackIntentionType Intent, bool TargetIsFleet, int RngFixedValue) : INamedCase;

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
        // exercising AdvanceGroups' Ground-landing swap and WorldEngage's own Trg:=men override.
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
        // result for both target kinds, not a world-only outcome.
        new(Name: "FleetTargetWithAttackerTroopsSurrenders", DefenderTech: TechLevel.Jump, DefenderClass: WorldClass.EarthLike,
            AttackerCarriesTroops: true, DefenderFgt: 5, DefenderHkr: 5, DefenderMen: 20,
            Intent: AttackIntentionType.Conquer, TargetIsFleet: true, RngFixedValue: 0),

        // DestroyTransports intent exercises PrioritizeTargetCandidates' own intent-driven branch
        // (biasing every non-transport candidate's priority down to 1) even though this particular
        // defender has no transports of its own to actually destroy.
        new(Name: "DestroyTransportsIntent", DefenderTech: TechLevel.Jump, DefenderClass: WorldClass.EarthLike,
            AttackerCarriesTroops: true, DefenderFgt: 10, DefenderHkr: 10, DefenderMen: 50,
            Intent: AttackIntentionType.DestroyTransports, TargetIsFleet: false, RngFixedValue: 0),
    ];

    /// <summary>MethodDataSource shape for NpeAttackTests.MatchesGoldenFile.</summary>
    public static IEnumerable<Func<NpeAttackCase>> AsDataSource() => All.Select(c => (Func<NpeAttackCase>)(() => c));
}
