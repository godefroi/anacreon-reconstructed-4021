using ThreeLn.Reconstruction4021.Core;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.NewGame;
using ThreeLn.Reconstruction4021.Core.Npe;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests;

/// <summary>
/// Phase 6e (Core/Npe/NpeToolkit.cs's ReviewNews enemy-attack case arm/StateDepartment/StateDeptReport/
/// WarCabinet). Hardcoded, same rationale as NpeToolkitTests.cs/NpeToolkitDeployImplementTests.cs —
/// exercised through ReviewNews's public entry point since AttackSeverity/RespondToEnemyAttack are
/// private nested helpers in real Pascal too.
/// </summary>
public class NpeDiplomacyTests
{
    private static Empire NewEmpire(string name) =>
        EmpireFactory.CreateEmpire(name, null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);

    private static (Game Game, Galaxy Galaxy) NewGame()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Game(galaxy);
        return (game, galaxy);
    }

    [Test]
    public async Task ReviewNews_AttackSeverity_DoesNotLeakPastTheNextHeadline()
    {
        // AttackSeverity's forward scan must stop at the next non-DestructionDetail entry. Two attack
        // reports back to back: AttackerA's own damage (1 Fighter, MPower=1) computes a low severity
        // (10) against a basePower of 100000 (capital's 1000 Starships * MPower[Starship]=100); if the
        // scan incorrectly kept reading into AttackerB's own detail entries (900 Starships, MPower=100,
        // severity 55), AttackerA would wrongly land at the same Preempt outcome as AttackerB instead
        // of Harass.
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var attackerA = NewEmpire("AttackerA");
        var attackerB = NewEmpire("AttackerB");
        game.Empires.Add(owner);

        var capital = new Planet { Location = new Coordinate(50, 50), Owner = owner, Type = WorldType.Capital, Class = WorldClass.EarthLike, TechLevel = TechLevel.Warp };
        capital.Ships.Starships = 1000;
        galaxy.Planets.Add(capital);
        var regionCapitals = new List<IEconomicWorld> { capital };

        var attackedFleet = new Fleet { Location = capital.Location, Owner = owner };

        owner.AddNews(NewsType.EnemyEmpireDestroyed, attackedFleet, otherEmpire: attackerA);
        owner.AddNews(NewsType.DestructionDetail, attackedFleet, p1: 1, p2: (int)AttackType.Fighter + 1);

        owner.AddNews(NewsType.EnemyEmpireDestroyed, attackedFleet, otherEmpire: attackerB);
        owner.AddNews(NewsType.DestructionDetail, attackedFleet, p1: 900, p2: (int)AttackType.Starship + 1);

        var persona = new NpeCharacter { Provoke = 75 };
        var state = new Dictionary<Empire, StateDeptRecord>();
        var random = new FixedRandom(0); // Rnd(1,100) always returns 1.
        var fleetStates = new Dictionary<Fleet, KingdomFleetState>();

        NpeToolkit.ReviewNews(owner, regionCapitals, fleetStates, persona, state, PolicyType.Neutral, game, random);

        await Assert.That(state[attackerA].Policy).IsEqualTo(PolicyType.Harass);
        await Assert.That(state[attackerB].Policy).IsEqualTo(PolicyType.Preempt);
    }

    [Test]
    public async Task ReviewNews_RespondToEnemyAttack_NeutralAlwaysEscalates()
    {
        // RespondToEnemyAttack's NeutralPLT arm (NPE00.PAS:94-97) is an unconditional either/or —
        // Policy always moves to PreemptPLT or HarassPLT, never stays NeutralPLT. Severity forced low
        // (10, via a single minor DestructionDetail) so this exercises the ELSE (HarassPLT) arm
        // specifically, not the IF (PreemptPLT) one already covered by the test above.
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var attacker = NewEmpire("Attacker");
        game.Empires.Add(owner);

        var capital = new Planet { Location = new Coordinate(50, 50), Owner = owner, Type = WorldType.Capital, Class = WorldClass.EarthLike, TechLevel = TechLevel.Warp };
        capital.Ships.Starships = 1000;
        galaxy.Planets.Add(capital);
        var regionCapitals = new List<IEconomicWorld> { capital };

        var attackedFleet = new Fleet { Location = capital.Location, Owner = owner };
        owner.AddNews(NewsType.EnemyEmpireDestroyed, attackedFleet, otherEmpire: attacker);
        owner.AddNews(NewsType.DestructionDetail, attackedFleet, p1: 1, p2: (int)AttackType.Fighter + 1);

        var persona = new NpeCharacter { Provoke = 75 };
        var state = new Dictionary<Empire, StateDeptRecord>();
        var random = new FixedRandom(0);
        var fleetStates = new Dictionary<Fleet, KingdomFleetState>();

        NpeToolkit.ReviewNews(owner, regionCapitals, fleetStates, persona, state, PolicyType.Neutral, game, random);

        await Assert.That(state[attacker].Policy).IsNotEqualTo(PolicyType.Neutral);
        await Assert.That(state[attacker].Policy).IsEqualTo(PolicyType.Harass);
    }
}
