using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs shared between the golden-file generator (GoldenFileTests) and the always-on
/// CombatEngineTests.MatchesGoldenFile. Expected outputs live exclusively in
/// reference/verify/golden/combat.golden, computed patch-based: a real run of ATTACK.PAS's own
/// CalculateCombatData, GetEnemy, DefaultDistribution, and Battle against a hand-assembled Universe^
/// (reference/verify/runworld.pas's combat domain).
///
/// Every case attacks a Planet target (Empire2's own capital) with a fleet built from exactly five
/// ship types (fgt/hkr/jmp/pen/ssp, deliberately no jtn/trn) so DefaultDistribution always produces
/// five groups in the same fixed order — ground-troop groups only exist after the not-yet-ported
/// AdvanceGroups swap (Phase 5 commit 5e), so this domain stays at the DpSpc shell and never reaches
/// Ground. Empire2's DefenseSettings is always the same InitDefenseRecord distribution
/// EmpireFactory.SeedDefenseSettings seeds on the C# side, so GetEnemy's per-shell ship split is
/// directly comparable. FighterGroupTarget optionally aims group 1 (fighters) at a real AttackType
/// (rather than leaving Trg unset, DefaultDistribution's own default) so GroupAttack's actual-damage
/// path is exercised too, not just the defender's counterattack via EnemyAttack/GetTargetArray.
/// </summary>
public sealed record CombatCase(
    string Name, TechLevel AttackerCapTech, TechLevel DefenderTech, WorldClass DefenderClass, int DefenderRevIndex,
    int AttackerFgt, int AttackerHkr, int AttackerJmp, int AttackerPen, int AttackerSsp,
    int DefenderFgt, int DefenderHkr, int DefenderJmp, int DefenderJtn, int DefenderPen, int DefenderSsp, int DefenderTrn,
    int DefenderLam, int DefenderDef, int DefenderGdm, int DefenderIon,
    AttackType? FighterGroupTarget, int RngFixedValue) : INamedCase;

internal static class CombatCases
{
    public static readonly IReadOnlyList<CombatCase> All = [
        // Every group's Trg stays unset (real DefaultDistribution default) -- only the defender's own
        // counterattack (EnemyAttack, via GetTargetArray's full priority system) does real damage;
        // GroupAttack's own output is 0 for every group (CombatTable[*,NoRes]=0), matching real Pascal.
        // Same tech level both sides keeps CombatTechAdj at its neutral 100 baseline.
        new(Name: "DefaultTargetingRealDefenderCounterattack", AttackerCapTech: TechLevel.Jump, DefenderTech: TechLevel.Jump,
            DefenderClass: WorldClass.EarthLike, DefenderRevIndex: 0,
            AttackerFgt: 100, AttackerHkr: 100, AttackerJmp: 100, AttackerPen: 100, AttackerSsp: 100,
            DefenderFgt: 50, DefenderHkr: 50, DefenderJmp: 50, DefenderJtn: 0, DefenderPen: 50, DefenderSsp: 50, DefenderTrn: 0,
            DefenderLam: 0, DefenderDef: 0, DefenderGdm: 0, DefenderIon: 0,
            FighterGroupTarget: null, RngFixedValue: 0),

        // Group 1 (fighters) aimed at the defender's hunter-killers -- exercises GroupAttack's real,
        // nonzero-damage path (InRangeOfDefense's ship-vs-ship fallthrough is unconditionally true,
        // regardless of shell) alongside the same defender counterattack as the baseline case above.
        new(Name: "FighterGroupTargetsHunterKiller", AttackerCapTech: TechLevel.Jump, DefenderTech: TechLevel.Jump,
            DefenderClass: WorldClass.EarthLike, DefenderRevIndex: 0,
            AttackerFgt: 100, AttackerHkr: 100, AttackerJmp: 100, AttackerPen: 100, AttackerSsp: 100,
            DefenderFgt: 50, DefenderHkr: 50, DefenderJmp: 50, DefenderJtn: 0, DefenderPen: 50, DefenderSsp: 50, DefenderTrn: 0,
            DefenderLam: 0, DefenderDef: 0, DefenderGdm: 0, DefenderIon: 0,
            FighterGroupTarget: AttackType.HunterKiller, RngFixedValue: 0),

        // A large attacker/defender tech gap (Gate vs. PreTech) drives CombatTechAdj's own asymmetry
        // (AShipAdj/DShipAdj computed from opposite corners of the table) into real, nonzero effect on
        // both sides' damage output, unlike the neutral-100 baseline above.
        new(Name: "LargeTechGapFavorsAttacker", AttackerCapTech: TechLevel.Gate, DefenderTech: TechLevel.PreTech,
            DefenderClass: WorldClass.Barren, DefenderRevIndex: 20,
            AttackerFgt: 100, AttackerHkr: 100, AttackerJmp: 100, AttackerPen: 100, AttackerSsp: 100,
            DefenderFgt: 50, DefenderHkr: 50, DefenderJmp: 50, DefenderJtn: 0, DefenderPen: 50, DefenderSsp: 50, DefenderTrn: 0,
            DefenderLam: 0, DefenderDef: 0, DefenderGdm: 0, DefenderIon: 0,
            FighterGroupTarget: AttackType.HunterKiller, RngFixedValue: 0),
    ];

    /// <summary>MethodDataSource shape for CombatEngineTests.MatchesGoldenFile.</summary>
    public static IEnumerable<Func<CombatCase>> AsDataSource() => All.Select(c => (Func<CombatCase>)(() => c));
}
