using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.SaveFormat;
using Reconstructed4021.Core.Types;
using Reconstructed4021.LegacyNpe;

namespace Reconstructed4021.Tests;

/// <summary>
/// <see cref="PirateTurnHandler.PlayTurn"/> — a whole NPE turn end to end (KingdomTurnHandlerTests'
/// own style), since it exercises real dispatch a single procedure's own test can't reach: whether
/// DeployRaiders/DeployNewFleets' composed ships actually reduce the launch world's own stock before
/// the other Deploy* procedure reads it later in the same turn, and whether a freshly deployed fleet
/// already at its destination gets dispatched by UpdateFleets in that same PlayTurn call.
///
/// <see cref="FixedRandom"/> with a fixed low roll always resolves <c>Rnd(lo,hi)</c> to <c>lo</c> (see
/// its own doc comment) — every ship-count/coordinate roll below is computed from that, not guessed.
/// </summary>
public class PirateTurnHandlerTests
{
    private static (Game Game, Galaxy Galaxy) NewGame()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Game(galaxy);
        return (game, galaxy);
    }

    [Test]
    public async Task PlayTurn_FreshEmpire_NoOwnedWorlds_DoesNotThrow()
    {
        var (game, _) = NewGame();
        var owner = EmpireFactory.CreateEmpire("Owner", null, isEmpress: false, TechLevel.Warp, restlessness: 0, centralModifier: false, foundingYear: 0);
        game.Empires.Add(owner);

        var handler = new PirateTurnHandler(owner, new FixedRandom(0));

        handler.PlayTurn(owner, game);
    }

    [Test]
    public async Task PlayTurn_DeploysRaiderFleetAtBestScoringTarget()
    {
        // Raider gate (NPE01.PAS:324): hkr>1500, jmp>2500, jtn>4000. Stocked just over each threshold
        // so DeployRaiders' Rnd(lo,hi) picks (under FixedRandom, always lo) consume everything but 1
        // of each -- leaving DeployNewFleets' own gate unmet later in the same turn (see this file's
        // own class doc comment on why that matters).
        var (game, galaxy) = NewGame();
        var owner = EmpireFactory.CreateEmpire("Owner", null, isEmpress: false, TechLevel.Warp, restlessness: 0, centralModifier: false, foundingYear: 0);
        game.Empires.Add(owner);

        var home = new Planet { Location = new Coordinate(50, 50), Owner = owner, Type = WorldType.Capital, Class = WorldClass.EarthLike, TechLevel = TechLevel.Warp };
        home.Ships[ShipType.HunterKiller] = 1501;
        home.Ships[ShipType.Jumpship] = 2501;
        home.Ships[ShipType.Jumptransport] = 4001;
        home.Cargo[CargoType.Legion] = 2000;
        owner.Capital = home;
        galaxy.Planets.Add(home);

        // Only known candidate: Protect=0 (no defenses/ships) < fltPower, Gain=100+100+5*50+200/2=550,
        // Legion+2*NinjaLegion=0 < gat(2000) -- the one and only positive-score target.
        var target = new Planet { Location = new Coordinate(10, 10), Owner = Empire.Independent, Type = WorldType.Independent, Class = WorldClass.EarthLike, TechLevel = TechLevel.Atomic };
        target.Cargo[CargoType.Chemicals] = 100;
        target.Cargo[CargoType.Metals] = 100;
        target.Cargo[CargoType.Trillum] = 50;
        target.Cargo[CargoType.Supplies] = 200;
        galaxy.Planets.Add(target);

        var handler = new PirateTurnHandler(owner, new FixedRandom(0));
        handler.PlayTurn(owner, game);

        await Assert.That(galaxy.Fleets).Count().IsEqualTo(1);
        var fleet = galaxy.Fleets[0];
        await Assert.That(fleet.Owner).IsEqualTo(owner);
        await Assert.That(fleet.Destination).IsEqualTo(target.Location);
        await Assert.That(fleet.Ships[ShipType.HunterKiller]).IsEqualTo(1500);
        await Assert.That(fleet.Ships[ShipType.Jumpship]).IsEqualTo(2500);
        await Assert.That(fleet.Ships[ShipType.Jumptransport]).IsEqualTo(4000);
    }

    [Test]
    public async Task PlayTurn_RaiderReachesUndefendedWorld_ConquersAndPlundersInTheSameTurn()
    {
        // Home and target share a coordinate so the freshly deployed raider is already at its
        // destination (Ready) the instant DeployRaiders launches it -- UpdateFleets (later in this
        // same PlayTurn) dispatches its AttackWorld mission immediately. Target has zero ships/
        // defenses, so NPEAttack's CaptureTransports resolves to DefenderConquered against any
        // nonzero attacking force (no combat rounds needed) -- ConquerWorld reassigns it to the
        // pirate first, so PlunderWorld's fleet.Owner==target.Owner guard passes, loots it, then
        // hands it back to Independent (a raid, not a permanent conquest -- real Pascal's own
        // PlunderWorld semantics, see NpeToolkit.PlunderWorld's own doc comment).
        var (game, galaxy) = NewGame();
        var owner = EmpireFactory.CreateEmpire("Owner", null, isEmpress: false, TechLevel.Warp, restlessness: 0, centralModifier: false, foundingYear: 0);
        game.Empires.Add(owner);

        var home = new Planet { Location = new Coordinate(10, 10), Owner = owner, Type = WorldType.Capital, Class = WorldClass.EarthLike, TechLevel = TechLevel.Warp };
        home.Ships[ShipType.HunterKiller] = 1501;
        home.Ships[ShipType.Jumpship] = 2501;
        home.Ships[ShipType.Jumptransport] = 4001;
        home.Cargo[CargoType.Legion] = 2000;
        owner.Capital = home;
        galaxy.Planets.Add(home);

        var target = new Planet { Location = new Coordinate(10, 10), Owner = Empire.Independent, Type = WorldType.Independent, Class = WorldClass.EarthLike, TechLevel = TechLevel.Atomic };
        target.Cargo[CargoType.Chemicals] = 500;
        galaxy.Planets.Add(target);

        var handler = new PirateTurnHandler(owner, new FixedRandom(0));
        handler.PlayTurn(owner, game);

        await Assert.That(target.Owner.IsIndependent).IsTrue();
        await Assert.That(target.Cargo[CargoType.Chemicals]).IsEqualTo(0);
        var fleet = galaxy.Fleets.Single(f => f.Owner == owner);
        await Assert.That(fleet.Cargo[CargoType.Chemicals]).IsGreaterThan(0);
    }

    [Test]
    public async Task PlayTurn_DeploysPatrolFleet_ThenInterceptsNearbyEnemyTransport_InTheSameTurn()
    {
        // Patrol gate's first band (NPE01.PAS:365): jtn>4000 AND jmp>2000 -- hkr stays 0 so the raider
        // gate (needs hkr>1500) never fires from the same world. Placed at (1,1): GetPatrolDestination
        // under FixedRandom always resolves to block (1,1) -> XY (1,1) too (Rnd(1,5) picks the low
        // bound), so the freshly deployed fleet's Destination equals its own Location -- already
        // FleetStatus.Ready -- and UpdateFleets (which runs after DeployNewFleets in PlayTurn's own
        // real sequence) dispatches it in this same call.
        var (game, galaxy) = NewGame();
        var owner = EmpireFactory.CreateEmpire("Owner", null, isEmpress: false, TechLevel.Warp, restlessness: 0, centralModifier: false, foundingYear: 0);
        game.Empires.Add(owner);

        var home = new Planet { Location = new Coordinate(1, 1), Owner = owner, Type = WorldType.Capital, Class = WorldClass.EarthLike, TechLevel = TechLevel.Warp };
        home.Ships[ShipType.Jumptransport] = 4001;
        home.Ships[ShipType.Jumpship] = 2001;
        owner.Capital = home;
        galaxy.Planets.Add(home);

        // FindTarget (NPE01.PAS:37): needs jtn+trn>0, jmp+hkr <= our composed fleet's jmp+hkr
        // (Rnd(1900,9200)=1900 jmp, 0 hkr under FixedRandom), pen+ssp <= our hkr/2 (=0). All satisfied
        // by an all-transport fleet within 5 sectors with no jmp/hkr/pen/ssp of its own.
        var enemyOwner = EmpireFactory.CreateEmpire("Enemy", null, isEmpress: false, TechLevel.Warp, restlessness: 0, centralModifier: false, foundingYear: 0);
        game.Empires.Add(enemyOwner);
        var enemyFleet = new Fleet { Location = new Coordinate(2, 2), Owner = enemyOwner };
        enemyFleet.Ships[ShipType.Transport] = 100;
        galaxy.Fleets.Add(enemyFleet);

        var handler = new PirateTurnHandler(owner, new FixedRandom(0));
        handler.PlayTurn(owner, game);

        var patrolFleet = galaxy.Fleets.Single(f => f.Owner == owner);
        await Assert.That(patrolFleet.Destination).IsEqualTo(enemyFleet.Location);
    }

    [Test]
    public async Task PlayTurn_CatchesAndDestroysTransport_SaveDoesNotDangleOnTheDestroyedTarget()
    {
        // Continues the interception scenario above: once the patrol fleet physically arrives (this
        // test moves it there directly -- fleet movement itself is FleetMovementHandler's job, not
        // PlayTurn's), AttackTransports' catch-up branch attacks and destroys the (undefended,
        // transport-only) target. Before this fix, state.Target kept pointing at that now-destroyed
        // Fleet, and a save taken afterward threw resolving a reference to an object no longer in
        // Game.Galaxy.Fleets (ReturnHome now nulls Target before the attack runs -- see its own doc
        // comment).
        var (game, galaxy) = NewGame();
        var owner = EmpireFactory.CreateEmpire("Owner", null, isEmpress: false, TechLevel.Warp, restlessness: 0, centralModifier: false, foundingYear: 0);
        game.Empires.Add(owner);

        var home = new Planet { Location = new Coordinate(1, 1), Owner = owner, Type = WorldType.Capital, Class = WorldClass.EarthLike, TechLevel = TechLevel.Warp };
        home.Ships[ShipType.Jumptransport] = 4001;
        home.Ships[ShipType.Jumpship] = 2001;
        owner.Capital = home;
        galaxy.Planets.Add(home);

        var enemyOwner = EmpireFactory.CreateEmpire("Enemy", null, isEmpress: false, TechLevel.Warp, restlessness: 0, centralModifier: false, foundingYear: 0);
        game.Empires.Add(enemyOwner);
        var enemyFleet = new Fleet { Location = new Coordinate(2, 2), Owner = enemyOwner };
        enemyFleet.Ships[ShipType.Transport] = 100;
        galaxy.Fleets.Add(enemyFleet);

        var handler = new PirateTurnHandler(owner, new FixedRandom(0));
        handler.PlayTurn(owner, game); // deploy + intercept -> AttackTransports, Destination = enemyFleet.Location

        var patrolFleet = galaxy.Fleets.Single(f => f.Owner == owner);
        patrolFleet.Location = enemyFleet.Location; // simulates arrival
        patrolFleet.Status = FleetStatus.Ready;

        handler.PlayTurn(owner, game); // catch-up branch: attacks, destroys the transport-only target

        await Assert.That(galaxy.Fleets.Contains(enemyFleet)).IsFalse();

        var provider = new LegacyNpeProvider();
        GameJson.Serialize(game, provider); // must not throw resolving a reference to the destroyed fleet
        SavGameWriter.WriteGame(game, provider);
    }

    [Test]
    public async Task PlayTurn_PatrolFleetWithNoTarget_EventuallyGivesUpAndReturnsHome()
    {
        // No enemy anywhere -- FindTarget always returns null. Waiting starts at Rnd(2,5)=2 under
        // FixedRandom, so it takes two no-target turns to reach 0, a third to give up (destination ->
        // FindNearestBase, the same (1,1) world it's already at -- Ready again), and a fourth for
        // UpdateFleets' now-Return-mission dispatch to fire ImplementReturnMSN and dissolve it.
        var (game, galaxy) = NewGame();
        var owner = EmpireFactory.CreateEmpire("Owner", null, isEmpress: false, TechLevel.Warp, restlessness: 0, centralModifier: false, foundingYear: 0);
        game.Empires.Add(owner);

        var home = new Planet { Location = new Coordinate(1, 1), Owner = owner, Type = WorldType.Capital, Class = WorldClass.EarthLike, TechLevel = TechLevel.Warp };
        home.Ships[ShipType.Jumptransport] = 4001;
        home.Ships[ShipType.Jumpship] = 2001;
        owner.Capital = home;
        galaxy.Planets.Add(home);

        var handler = new PirateTurnHandler(owner, new FixedRandom(0));
        handler.PlayTurn(owner, game); // deploy + first dispatch (no target, Waiting 2->1)

        var patrolFleet = galaxy.Fleets.Single();

        handler.PlayTurn(owner, game); // Waiting 1->0
        handler.PlayTurn(owner, game); // give up -> Return, destination (1,1) (already there) -> Ready
        handler.PlayTurn(owner, game); // ImplementReturnMSN dissolves it

        await Assert.That(galaxy.Fleets.Contains(patrolFleet)).IsFalse();
    }

    [Test]
    public async Task ReviewNews_FleetOutOfFuel_DestroysFleet()
    {
        var (game, galaxy) = NewGame();
        var owner = EmpireFactory.CreateEmpire("Owner", null, isEmpress: false, TechLevel.Warp, restlessness: 0, centralModifier: false, foundingYear: 0);
        game.Empires.Add(owner);

        var fleet = new Fleet { Location = new Coordinate(5, 5), Owner = owner };
        galaxy.Fleets.Add(fleet);
        owner.AddNews(NewsType.FleetOutOfFuel, fleet);

        var handler = new PirateTurnHandler(owner, new FixedRandom(0));
        handler.PlayTurn(owner, game);

        await Assert.That(galaxy.Fleets.Contains(fleet)).IsFalse();
    }
}
