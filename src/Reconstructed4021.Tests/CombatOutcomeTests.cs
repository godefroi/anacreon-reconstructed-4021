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
    /// ConquerEmpire's own <c>Booty</c> (ATTACK.PAS:1028,1048,1060): a world close to the conqueror's
    /// own capital, far from the enemy's, and below the population roll joins the conqueror outright
    /// (ATTACK.PAS:1044-1049) -- <see cref="CombatOutcome.ConquerEmpire"/> returns it so
    /// <c>TacticalBattleScreen</c> can show the human attacker the same "the following worlds have
    /// joined our empire" report <c>ATTCOMM.PAS</c>'s <c>EmpireConquestReport</c> shows there.
    /// </summary>
    [Test]
    public async Task ConquerEmpire_WorldJoinsConqueror_ReturnsItAsBooty()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);

        var conqueror = EmpireFactory.CreateEmpire("Conqueror", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        conqueror.NpeType = NpeEmpireType.Pirate;
        var conquerorCapital = new Planet { Location = new Coordinate(0, 0), Owner = conqueror, Class = WorldClass.EarthLike, Type = WorldType.Capital, TechLevel = TechLevel.Jump };
        conqueror.Capital = conquerorCapital;
        galaxy.Planets.Add(conquerorCapital);

        var enemy = EmpireFactory.CreateEmpire("Enemy", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        enemy.NpeType = NpeEmpireType.Pirate;
        var enemyCapital = new Planet { Location = new Coordinate(90, 90), Owner = conqueror, Class = WorldClass.EarthLike, Type = WorldType.Independent, TechLevel = TechLevel.Jump };
        enemy.Capital = enemyCapital;

        // Close to the conqueror (distToConq<10), far from the enemy capital (dist>10), population
        // below the Rnd(900,1100) roll (900 with FixedRandom(0)) -- ATTACK.PAS:1044's join branch.
        var joiningWorld = new Planet { Location = new Coordinate(1, 1), Owner = enemy, Class = WorldClass.EarthLike, Type = WorldType.Agricultural, TechLevel = TechLevel.PreAtomic, Population = 100 };
        galaxy.Planets.Add(joiningWorld);

        game.Empires.Add(conqueror);
        game.Empires.Add(enemy);
        game.TurnHandlers[conqueror] = new NonHumanTurnHandler();
        game.TurnHandlers[enemy] = new NonHumanTurnHandler();

        var joined = CombatOutcome.ConquerEmpire(conqueror, enemy, game, new FixedRandom(0));

        await Assert.That(joined).Contains(joiningWorld);
        await Assert.That(joiningWorld.Owner).IsEqualTo(conqueror);
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

    /// <summary>
    /// GitHub #51: AbortFleet's ships/cargo merge clamps to [0,MaxResources] at the merge site,
    /// matching Pascal's AddThings (MISC.PAS:281,284 via ThgLmt) -- a world can't come out of a merge
    /// already over the cap.
    /// </summary>
    [Test]
    public async Task AbortFleet_ClampsShipsAndCargoToMaxResources()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);

        var attacker = EmpireFactory.CreateEmpire("Attacker", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        var attackerFleet = new Fleet { Location = new Coordinate(0, 0), Owner = attacker };
        attackerFleet.Ships[ShipType.Fighter] = 9000;
        attackerFleet.Cargo.Trillum = 9000;
        galaxy.Fleets.Add(attackerFleet);

        var defender = EmpireFactory.CreateEmpire("Defender", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        var targetFleet = new Fleet { Location = new Coordinate(0, 0), Owner = defender };
        targetFleet.Ships[ShipType.Fighter] = 9000;
        targetFleet.Cargo.Trillum = 9000;
        galaxy.Fleets.Add(targetFleet);

        CombatOutcome.ResolveAttack(
            AttackResultType.DefenderConquered, attackerFleet, targetFleet,
            hkAttack: false, capture: true, new AttackTally(), new AttackTally(), game, new FixedRandom(0));

        await Assert.That(attackerFleet.Ships[ShipType.Fighter]).IsEqualTo(PascalMath.MaxResources);
        await Assert.That(attackerFleet.Cargo.Trillum).IsEqualTo(PascalMath.MaxResources);
    }

    /// <summary>
    /// GitHub #43: a conquered Fleet is removed from Galaxy.Fleets (DestroyFleet) before
    /// ResolveAttack's later AddNews calls fire. Those calls must report the fleet's last-known
    /// location, not the fleet object itself -- otherwise the news survives this turn holding a
    /// reference to something no longer in the galaxy, and renders as "(unknown location)" once
    /// the object can no longer be resolved (e.g. after a save round-trip).
    /// </summary>
    [Test]
    public async Task ResolveAttack_HkAttackFleetConquered_ReportsLastKnownLocationNotDanglingFleet()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);

        var attacker = EmpireFactory.CreateEmpire("Attacker", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        var attackerFleet = new Fleet { Location = new Coordinate(3, 4), Owner = attacker };
        galaxy.Fleets.Add(attackerFleet);

        var defender = EmpireFactory.CreateEmpire("Defender", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        var targetLocation = new Coordinate(3, 4);
        var targetFleet = new Fleet { Location = targetLocation, Owner = defender };
        targetFleet.Ships.Fighters = 5;
        galaxy.Fleets.Add(targetFleet);

        CombatOutcome.ResolveAttack(
            AttackResultType.DefenderConquered, attackerFleet, targetFleet,
            hkAttack: true, capture: false, new AttackTally(), new AttackTally(), game, new FixedRandom(0));

        await Assert.That(game.Galaxy.Fleets).DoesNotContain(targetFleet);

        var destroyedNews = defender.News.Single(n => n.Headline == NewsType.FleetDestroyedByUnknown);
        await Assert.That(destroyedNews.Subject).IsNull();
        await Assert.That(destroyedNews.Position).IsEqualTo(targetLocation);

        var detailNews = defender.News.Single(n => n.Headline == NewsType.DestructionDetail);
        await Assert.That(detailNews.Subject).IsNull();
        await Assert.That(detailNews.Position).IsEqualTo(targetLocation);
    }

    /// <summary>Same GitHub #43 fix, the non-hkAttack branch (WorldConqueredByEnemy).</summary>
    [Test]
    public async Task ResolveAttack_PlainFleetConquered_ReportsLastKnownLocationNotDanglingFleet()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);

        var attacker = EmpireFactory.CreateEmpire("Attacker", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        var attackerFleet = new Fleet { Location = new Coordinate(7, 8), Owner = attacker };
        galaxy.Fleets.Add(attackerFleet);

        var defender = EmpireFactory.CreateEmpire("Defender", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        var targetLocation = new Coordinate(7, 8);
        var targetFleet = new Fleet { Location = targetLocation, Owner = defender };
        galaxy.Fleets.Add(targetFleet);

        CombatOutcome.ResolveAttack(
            AttackResultType.DefenderConquered, attackerFleet, targetFleet,
            hkAttack: false, capture: false, new AttackTally(), new AttackTally(), game, new FixedRandom(0));

        var conqueredNews = defender.News.Single(n => n.Headline == NewsType.WorldConqueredByEnemy);
        await Assert.That(conqueredNews.Subject).IsNull();
        await Assert.That(conqueredNews.Position).IsEqualTo(targetLocation);
        await Assert.That(conqueredNews.OtherEmpire).IsEqualTo(attacker);
    }

    /// <summary>
    /// GitHub #65: RestoreCombatant's Fleet branch reruns ATTACK.PAS:1141-1181's own post-casualty
    /// checks. Ship and legion casualties come from independent tallies, so losing 2 of 50 transports
    /// (5 legions/transport, exactly at capacity beforehand) while losing only 4 of 250 legions leaves
    /// cargo over the new, smaller capacity -- BalanceFleet must trim it back down. The same casualty
    /// also has to reclamp Fuel, left at the old (50-transport) capacity, down to the new one.
    /// </summary>
    [Test]
    public async Task RestoreCombatant_FleetOverCapacityAfterCasualties_RebalancesCargoAndClampsFuel()
    {
        var owner = EmpireFactory.CreateEmpire("Owner", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = owner, Fuel = FleetLogistics.FuelCapacity(new ShipCounts { Transports = 50 }) };
        fleet.Ships.Transports = 50;
        fleet.Cargo.Legions = 250; // Exactly at capacity: 50 transports * 5 legions/transport.

        var casualties = new AttackTally { [AttackType.Transport] = 2, [AttackType.Legion] = 4 };
        CombatOutcome.RestoreCombatant(fleet, casualties);

        await Assert.That(fleet.Ships.Transports).IsEqualTo(48);
        await Assert.That(fleet.Cargo.Legions).IsEqualTo(240); // BalanceFleet trims back to the new capacity.
        await Assert.That(FleetLogistics.FleetCargoSpace(fleet.Ships, fleet.Cargo)).IsEqualTo(0);
        await Assert.That(fleet.Fuel).IsEqualTo(FleetLogistics.FuelCapacity(fleet.Ships)); // Reclamped, not left at the 50-transport figure.
    }
}
