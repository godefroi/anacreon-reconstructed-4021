using ThreeLn.Reconstruction4021.Core.Combat;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.NewGame;
using ThreeLn.Reconstruction4021.Core.Turns;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests;

/// <summary>
/// <see cref="Core.Combat.CombatResolution"/>'s round-robin loop and <see cref="Core.Combat.CombatOutcome"/>'s
/// outcome application: FleetEngage/WorldEngage/NPEAttack, now including
/// RestoreCombatant/ResolveAttack/ConquerWorld/ConquerEmpire. MatchesGoldenFile checks the whole
/// engagement end to end (not one round in isolation, already covered by CombatEngineTests) against
/// reference/verify/golden/npeattack.golden, including the defender's (and, when a case drives
/// ConquerEmpire's per-planet cascade, a second Empire2 world's) post-attack ownership/efficiency/
/// revolution-index/type; see NpeAttackCases' own doc comment for the fixed attacker fleet every case
/// shares and what each case's own scenario is chosen to exercise.
/// </summary>
public class NpeAttackTests
{
    /// <summary>Never the interactive UI handler (IsHuman=false) — every NpeAttackCases scenario is an NPE-vs-NPE or NPE-vs-independent fight, matching the Pascal harness's own EmpirePlayer=False empires.</summary>
    private sealed class NonHumanTurnHandler : ITurnHandler
    {
        public bool IsHuman => false;
        public void PlayTurn(Empire empire, Core.Game game) { }
    }

    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles))]
    [MethodDataSource(typeof(PascalGroundTruth.NpeAttackCases), nameof(PascalGroundTruth.NpeAttackCases.AsDataSource))]
    public async Task MatchesGoldenFile(PascalGroundTruth.NpeAttackCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("npeattack.golden");
        var expected = golden[c.Name];

        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);

        var attacker = new Empire { Name = "Attacker" };
        var attackerCapital = new Planet { Location = new Coordinate(0, 0), Owner = attacker, Class = WorldClass.EarthLike, Type = WorldType.Capital, TechLevel = TechLevel.Jump };
        attacker.Capital = attackerCapital;
        galaxy.Planets.Add(attackerCapital);

        var fleet = new Fleet { Location = new Coordinate(5, 5), Owner = attacker };
        fleet.Ships.Fighters = 200;
        fleet.Ships.HunterKillers = 200;
        if (c.AttackerCarriesTroops) {
            fleet.Ships.Jumptransports = 20;
            fleet.Cargo.NinjaLegions = 1000;
        }
        galaxy.Fleets.Add(fleet);

        var defender = EmpireFactory.CreateEmpire("Defender", null, isEmpress: false, TechLevel.PreTech, restlessness: 0, centralModifier: false, foundingYear: 0);

        game.Empires.Add(attacker);
        game.Empires.Add(defender);
        game.TurnHandlers[attacker] = new NonHumanTurnHandler();
        game.TurnHandlers[defender] = new NonHumanTurnHandler();

        object target;
        Planet defenderPlanet2;
        if (c.TargetIsFleet) {
            var targetFleet = new Fleet { Location = new Coordinate(50, 50), Owner = defender };
            targetFleet.Ships.Fighters = c.DefenderFgt;
            targetFleet.Ships.HunterKillers = c.DefenderHkr;
            galaxy.Fleets.Add(targetFleet);
            target = targetFleet;

            // ConquerEmpire never runs for a Fleet target (only a conquered Capital world triggers it),
            // so the defender still needs a real capital of its own for GetCapital/GetCoord's sake --
            // matching this domain's own Pascal harness, which always creates Planet[2] as Empire2's
            // capital regardless of TargetIsFleet.
            defenderPlanet2 = new Planet { Location = new Coordinate(50, 50), Owner = defender, Class = c.DefenderClass, Type = WorldType.Capital, TechLevel = c.DefenderTech };
            defender.Capital = defenderPlanet2;
            galaxy.Planets.Add(defenderPlanet2);
        } else {
            defenderPlanet2 = new Planet { Location = new Coordinate(50, 50), Owner = defender, Class = c.DefenderClass, Type = WorldType.Capital, TechLevel = c.DefenderTech };
            defenderPlanet2.Ships.Fighters = c.DefenderFgt;
            defenderPlanet2.Ships.HunterKillers = c.DefenderHkr;
            defenderPlanet2.Cargo.Legions = c.DefenderMen;
            defender.Capital = defenderPlanet2;
            galaxy.Planets.Add(defenderPlanet2);
            target = defenderPlanet2;
        }

        Planet? planet3 = null;
        if (c.Planet3Present) {
            planet3 = new Planet {
                Location = new Coordinate(c.Planet3X, c.Planet3Y), Owner = defender,
                Class = WorldClass.EarthLike, Type = WorldType.Agricultural, TechLevel = c.Planet3Tech,
                Population = c.Planet3Population, RevolutionIndex = c.Planet3RevIndex,
            };
            galaxy.Planets.Add(planet3);
        }

        var random = new FixedRandom(c.RngFixedValue);
        var outcome = CombatResolution.NPEAttack(attacker, fleet, target, c.Intent, game, random);

        await Assert.That((int)outcome.Result).IsEqualTo(int.Parse(expected["result"]));
        await Assert.That(outcome.Casualties[AttackType.Fighter]).IsEqualTo(int.Parse(expected["cas_fgt"]));
        await Assert.That(outcome.Casualties[AttackType.HunterKiller]).IsEqualTo(int.Parse(expected["cas_hkr"]));
        await Assert.That(outcome.Casualties[AttackType.Jumptransport]).IsEqualTo(int.Parse(expected["cas_jtn"]));
        await Assert.That(outcome.Casualties[AttackType.NinjaLegion]).IsEqualTo(int.Parse(expected["cas_nnj"]));
        await Assert.That(outcome.Killed[AttackType.Fighter]).IsEqualTo(int.Parse(expected["kill_fgt"]));
        await Assert.That(outcome.Killed[AttackType.HunterKiller]).IsEqualTo(int.Parse(expected["kill_hkr"]));
        await Assert.That(outcome.Killed[AttackType.Legion]).IsEqualTo(int.Parse(expected["kill_men"]));

        // Planet[2]'s post-attack state, keyed off the object captured before the attack (not
        // defender.Capital, which ConquerEmpire's own new-capital branch can repoint at Planet3) --
        // real regardless of TargetIsFleet/Result, only meaningfully different from the pre-attack
        // input once ConquerWorld actually ran on it (DefenderConquered results).
        await Assert.That((int)DefenderOwnerOrdinal(defenderPlanet2.Owner, attacker, defender)).IsEqualTo(int.Parse(expected["def_owner"]));
        await Assert.That(defenderPlanet2.Efficiency).IsEqualTo(int.Parse(expected["def_eff"]));
        await Assert.That(defenderPlanet2.RevolutionIndex).IsEqualTo(int.Parse(expected["def_rev"]));
        await Assert.That((int)defenderPlanet2.Type).IsEqualTo(int.Parse(expected["def_type"]));

        if (planet3 is not null) {
            await Assert.That((int)DefenderOwnerOrdinal(planet3.Owner, attacker, defender)).IsEqualTo(int.Parse(expected["p3_owner"]));
            await Assert.That(planet3.Efficiency).IsEqualTo(int.Parse(expected["p3_eff"]));
            await Assert.That(planet3.RevolutionIndex).IsEqualTo(int.Parse(expected["p3_rev"]));
            await Assert.That((int)planet3.Type).IsEqualTo(int.Parse(expected["p3_type"]));
        }

        var newCapitalIndex = defender.Capital switch {
            null => 0,
            var cap when ReferenceEquals(cap, defenderPlanet2) => 2,
            var cap when ReferenceEquals(cap, planet3) => 3,
            _ => -1,
        };
        await Assert.That(newCapitalIndex).IsEqualTo(int.Parse(expected["newcap_idx"]));
    }

    /// <summary>Matches the Pascal harness's own Empire ordinal (Empire1=0,Empire2=1,...,Indep=8) for the two empires this domain ever uses as an owner.</summary>
    private static int DefenderOwnerOrdinal(Empire owner, Empire attacker, Empire defender) =>
        owner == attacker ? 0 : owner == defender ? 1 : 8;

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
