using Reconstructed4021.Core;
using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// Outcome application (<see cref="Core.Combat.CombatOutcome"/>). NpeAttackTests.MatchesGoldenFile
/// already covers ConquerWorld/ConquerEmpire/RestoreCombatant/ResolveAttack end to end against real
/// Pascal output; the tests here cover the one real branch the ground-truth harness structurally can't
/// reach — a human empire's defeat (ATTACK.PAS:1120-1131's <c>EmpirePlayer(EnemyEmp)</c> branch) — since
/// the Pascal harness never marks an empire <c>IsAPlayer</c>.
/// </summary>
public class CombatOutcomeTests
{
    private sealed class HumanTurnHandler : ITurnHandler
    {
        public bool IsHuman => true;
        public void PlayTurn(Empire empire, Core.Game game) { }
    }

    private sealed class NonHumanTurnHandler : ITurnHandler
    {
        public bool IsHuman => false;
        public void PlayTurn(Empire empire, Core.Game game) { }
    }

    /// <summary>
    /// ConquerEmpire's human branch (ATTACK.PAS:1122-1128): a human empire whose capital just fell,
    /// with no other world to fall back to, is left in play with its Capital cleared and DefeatedBy set
    /// — not torn down via DestroyEmpire, and not removed from Game.Empires.
    /// </summary>
    [Test]
    public async Task ConquerEmpire_HumanEmpireWithNoOtherWorldIsDefeatedInPlace()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);

        var conqueror = EmpireFactory.CreateEmpire("Conqueror", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        conqueror.NpeType = NpeEmpireType.Pirate; // Registered with a NonHumanTurnHandler below -- NpeType is what AnyHumanPlayersRemain/ConquerEmpire now read to tell human from NPE.
        var conquerorCapital = new Planet { Location = new Coordinate(0, 0), Owner = conqueror, Class = WorldClass.EarthLike, Type = WorldType.Capital, TechLevel = TechLevel.Jump };
        conqueror.Capital = conquerorCapital;
        galaxy.Planets.Add(conquerorCapital);

        var human = EmpireFactory.CreateEmpire("Human", "pw", isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        var humanCapital = new Planet { Location = new Coordinate(50, 50), Owner = conqueror, Class = WorldClass.EarthLike, Type = WorldType.Independent, TechLevel = TechLevel.Jump };
        human.Capital = humanCapital;
        // Not in game.Galaxy.Planets under the human's own ownership -- ConquerEmpire's per-planet loop
        // finds nothing left to consider, matching a capital that was already ConquerWorld'd (reassigned)
        // by ResolveAttack before this call, the same real precondition ConquerEmpire always runs under.

        game.Empires.Add(conqueror);
        game.Empires.Add(human);
        game.TurnHandlers[conqueror] = new NonHumanTurnHandler();
        game.TurnHandlers[human] = new HumanTurnHandler();

        CombatOutcome.ConquerEmpire(conqueror, human, game, new FixedRandom(0));

        await Assert.That(human.Capital).IsNull();
        await Assert.That(human.Status).IsEqualTo(EmpireStatus.PendingElimination);
        await Assert.That(human.DefeatedBy).IsEqualTo(conqueror);
        await Assert.That(game.Empires).Contains(human);
        await Assert.That(game.AnyHumanPlayersRemain).IsFalse();
    }

    /// <summary>
    /// ConquerEmpire's non-human branch (ATTACK.PAS:1130): an NPE empire in the same no-worlds-left
    /// situation is torn down via DestroyEmpire instead of parking at PendingElimination — but stays
    /// a permanent Game.Empires member (Status.Eliminated), same as a human, since the roster never
    /// shrinks. Only Game.TurnHandlers loses its entry (no more AI decisions to make).
    /// </summary>
    [Test]
    public async Task ConquerEmpire_NonHumanEmpireWithNoOtherWorldIsDestroyed()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);

        var conqueror = EmpireFactory.CreateEmpire("Conqueror", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        conqueror.NpeType = NpeEmpireType.Pirate; // Registered with a NonHumanTurnHandler below -- NpeType is what AnyHumanPlayersRemain/ConquerEmpire now read to tell human from NPE.
        var conquerorCapital = new Planet { Location = new Coordinate(0, 0), Owner = conqueror, Class = WorldClass.EarthLike, Type = WorldType.Capital, TechLevel = TechLevel.Jump };
        conqueror.Capital = conquerorCapital;
        galaxy.Planets.Add(conquerorCapital);

        var npe = EmpireFactory.CreateEmpire("Npe", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        npe.NpeType = NpeEmpireType.Pirate; // ConquerEmpire now reads NpeType, not TurnHandlers/IsHuman, to tell a human from an NPE.
        var npeCapital = new Planet { Location = new Coordinate(50, 50), Owner = conqueror, Class = WorldClass.EarthLike, Type = WorldType.Independent, TechLevel = TechLevel.Jump };
        npe.Capital = npeCapital;

        game.Empires.Add(conqueror);
        game.Empires.Add(npe);
        game.TurnHandlers[conqueror] = new NonHumanTurnHandler();
        game.TurnHandlers[npe] = new NonHumanTurnHandler();

        CombatOutcome.ConquerEmpire(conqueror, npe, game, new FixedRandom(0));

        await Assert.That(game.Empires).Contains(npe);
        await Assert.That(game.TurnHandlers).DoesNotContainKey(npe);
        await Assert.That(npe.Status).IsEqualTo(EmpireStatus.Eliminated);
        await Assert.That(npe.DefeatedBy).IsEqualTo(conqueror);
    }

    /// <summary>
    /// AbortFleet (FLEET.PAS:150-209), reached via <see cref="CombatOutcome.DestroyEmpire"/> (itself
    /// reached via <see cref="CombatOutcome.ConquerEmpire"/>'s NPE-destroyed branch — AbortFleet is
    /// <c>internal</c>, so this test goes through the same public entry point a real conquest would):
    /// leftover fuel converts to trillum and lands on the ground planet's own cargo, clamped via ThgLmt
    /// (<see cref="PascalMath.ClampResource"/>) both on the ton conversion and on the add.
    /// </summary>
    [Test]
    public async Task AbortFleet_ConvertsLeftoverFuelToTrillumOnWorldGround()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);

        var conqueror = EmpireFactory.CreateEmpire("Conqueror", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        var conquerorCapital = new Planet { Location = new Coordinate(0, 0), Owner = conqueror, Class = WorldClass.EarthLike, Type = WorldType.Capital, TechLevel = TechLevel.Jump };
        conqueror.Capital = conquerorCapital;
        galaxy.Planets.Add(conquerorCapital);

        var npe = EmpireFactory.CreateEmpire("Npe", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        npe.NpeType = NpeEmpireType.Pirate;
        var npeCapital = new Planet { Location = new Coordinate(50, 50), Owner = conqueror, Class = WorldClass.EarthLike, Type = WorldType.Independent, TechLevel = TechLevel.Jump };
        npe.Capital = npeCapital;

        var fleet = new Fleet { Location = new Coordinate(10, 10), Owner = npe, Fuel = 530 }; // 530 / FuelPerTon(265) = 2 tons exactly
        galaxy.Fleets.Add(fleet);
        var ground = new Planet { Location = new Coordinate(10, 10), Owner = conqueror, Class = WorldClass.EarthLike, Type = WorldType.Agricultural, TechLevel = TechLevel.Jump };
        ground.Cargo.Trillum = 10;
        galaxy.Planets.Add(ground);

        game.Empires.Add(conqueror);
        game.Empires.Add(npe);
        game.TurnHandlers[conqueror] = new NonHumanTurnHandler();
        game.TurnHandlers[npe] = new NonHumanTurnHandler();

        CombatOutcome.ConquerEmpire(conqueror, npe, game, new FixedRandom(0));

        await Assert.That(ground.Cargo.Trillum).IsEqualTo(12);
    }

    /// <summary>
    /// AbortFleet's fleet-to-fleet branch (FLEET.PAS:178-183), reached via
    /// <see cref="CombatOutcome.ResolveAttack"/>'s Fleet/DefenderConquered/capture path: fuel merges
    /// directly into the ground fleet's own Fuel, no trillum/tons involved and no capacity clamp —
    /// matching Pascal's own unclamped SetFleetFuel.
    /// </summary>
    [Test]
    public async Task AbortFleet_MergesFuelDirectlyWhenGroundIsFleet()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);

        var attacker = EmpireFactory.CreateEmpire("Attacker", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        var attackerFleet = new Fleet { Location = new Coordinate(0, 0), Owner = attacker, Fuel = 15 };
        galaxy.Fleets.Add(attackerFleet);

        var defender = EmpireFactory.CreateEmpire("Defender", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        var targetFleet = new Fleet { Location = new Coordinate(0, 0), Owner = defender, Fuel = 40 };
        galaxy.Fleets.Add(targetFleet);

        CombatOutcome.ResolveAttack(
            AttackResultType.DefenderConquered, attackerFleet, targetFleet,
            hkAttack: false, capture: true, new AttackTally(), new AttackTally(), game, new FixedRandom(0));

        await Assert.That(attackerFleet.Fuel).IsEqualTo(55);
        await Assert.That(attackerFleet.Cargo.Trillum).IsEqualTo(0);
    }
}
