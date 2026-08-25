using ThreeLn.Reconstruction4021.Core.Combat;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.NewGame;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests;

/// <summary>
/// Phase 5 commit 5e: the multi-round resolution loop (Core/Combat/CombatResolution.cs) — FleetEngage/
/// WorldEngage/NPEAttack. MatchesGoldenFile checks the whole round-robin loop (not one round in
/// isolation, already covered by CombatEngineTests) against reference/verify/golden/npeattack.golden;
/// see NpeAttackCases' own doc comment for the fixed attacker fleet every case shares.
/// </summary>
public class NpeAttackTests
{
    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles))]
    [MethodDataSource(typeof(PascalGroundTruth.NpeAttackCases), nameof(PascalGroundTruth.NpeAttackCases.AsDataSource))]
    public async Task MatchesGoldenFile(PascalGroundTruth.NpeAttackCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("npeattack.golden");
        var expected = golden[c.Name];

        var attacker = new Empire { Name = "Attacker" };
        attacker.Capital = new Planet { Location = new Coordinate(0, 0), Owner = attacker, Class = WorldClass.EarthLike, Type = WorldType.Capital, TechLevel = TechLevel.Jump };

        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = attacker };
        fleet.Ships.Fighters = 200;
        fleet.Ships.HunterKillers = 200;
        if (c.AttackerCarriesTroops) {
            fleet.Ships.Jumptransports = 20;
            fleet.Cargo.NinjaLegions = 1000;
        }

        var defender = EmpireFactory.CreateEmpire("Defender", null, isEmpress: false, TechLevel.PreTech, restlessness: 0, centralModifier: false, foundingYear: 0);

        object target;
        if (c.TargetIsFleet) {
            var targetFleet = new Fleet { Location = new Coordinate(0, 0), Owner = defender };
            targetFleet.Ships.Fighters = c.DefenderFgt;
            targetFleet.Ships.HunterKillers = c.DefenderHkr;
            target = targetFleet;
        } else {
            var targetPlanet = new Planet { Location = new Coordinate(0, 0), Owner = defender, Class = c.DefenderClass, Type = WorldType.Capital, TechLevel = c.DefenderTech };
            targetPlanet.Ships.Fighters = c.DefenderFgt;
            targetPlanet.Ships.HunterKillers = c.DefenderHkr;
            targetPlanet.Cargo.Legions = c.DefenderMen;
            defender.Capital = targetPlanet;
            target = targetPlanet;
        }

        var random = new FixedRandom(c.RngFixedValue);
        var outcome = CombatResolution.NPEAttack(attacker, fleet, target, c.Intent, random);

        await Assert.That((int)outcome.Result).IsEqualTo(int.Parse(expected["result"]));
        await Assert.That(outcome.Casualties[AttackType.Fighter]).IsEqualTo(int.Parse(expected["cas_fgt"]));
        await Assert.That(outcome.Casualties[AttackType.HunterKiller]).IsEqualTo(int.Parse(expected["cas_hkr"]));
        await Assert.That(outcome.Casualties[AttackType.Jumptransport]).IsEqualTo(int.Parse(expected["cas_jtn"]));
        await Assert.That(outcome.Casualties[AttackType.NinjaLegion]).IsEqualTo(int.Parse(expected["cas_nnj"]));
        await Assert.That(outcome.Killed[AttackType.Fighter]).IsEqualTo(int.Parse(expected["kill_fgt"]));
        await Assert.That(outcome.Killed[AttackType.HunterKiller]).IsEqualTo(int.Parse(expected["kill_hkr"]));
        await Assert.That(outcome.Killed[AttackType.Legion]).IsEqualTo(int.Parse(expected["kill_men"]));
    }

    // None of NpeAttackCases' scenarios ever wipe out the attacker outright (AttackerDestroyed) --
    // every case pits a much stronger fleet against a weak-to-moderate defender. A lone, unescorted
    // fighter against an overwhelming defender exercises that termination path directly instead.
    [Test]
    public async Task WorldEngage_AttackerFullyDestroyedEndsTheEngagement()
    {
        var attacker = new Empire { Name = "Attacker" };
        attacker.Capital = new Planet { Location = new Coordinate(0, 0), Owner = attacker, Class = WorldClass.EarthLike, Type = WorldType.Capital, TechLevel = TechLevel.Jump };
        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = attacker };
        fleet.Ships.Fighters = 1;

        var defender = EmpireFactory.CreateEmpire("Defender", null, isEmpress: false, TechLevel.PreTech, restlessness: 0, centralModifier: false, foundingYear: 0);
        var target = new Planet { Location = new Coordinate(0, 0), Owner = defender, Class = WorldClass.EarthLike, Type = WorldType.Capital, TechLevel = TechLevel.Jump };
        target.Ships.HunterKillers = 9999;
        defender.Capital = target;

        var groups = CombatEngine.DefaultDistribution(fleet);
        var enemy = CombatEngine.GetEnemy(target);
        var combatData = CombatEngine.CalculateCombatData(attacker, target);

        var outcome = CombatResolution.WorldEngage(groups, enemy, combatData, AttackIntentionType.Conquer, new FixedRandom(99));

        await Assert.That(outcome.Result).IsEqualTo(AttackResultType.AttackerDestroyed);
    }
}
