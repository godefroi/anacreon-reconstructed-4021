using ThreeLn.Reconstruction4021.Core;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Turns;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests;

public class FleetMovementHandlerTests
{
    [Test]
    public async Task SequentialMovement_ActingEmpireJumpFleetAndNextEmpireWarpFleetAdvanceTogether()
    {
        var human = new Empire { Name = "Human" };
        var ai = new Empire { Name = "AI" };
        var game = new Game(new Galaxy(size: 20));
        game.Empires.Add(human);
        game.Empires.Add(ai);

        var jumpFleet = new Fleet {
            Owner = human,
            Location = new Coordinate(0, 0),
            Destination = new Coordinate(10, 0),
            Fuel = 100,
            Status = FleetStatus.Ready,
        };
        jumpFleet.Ships.Jumpships = 2;

        var warpFleet = new Fleet {
            Owner = ai,
            Location = new Coordinate(0, 0),
            Destination = new Coordinate(2, 0),
            Fuel = 100,
            Status = FleetStatus.Ready,
        };
        warpFleet.Ships.Fighters = 2;

        game.Galaxy.Fleets.Add(jumpFleet);
        game.Galaxy.Fleets.Add(warpFleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceFleets(game, human, ai);

        await Assert.That(jumpFleet.Location).IsEqualTo(new Coordinate(10, 0));
        await Assert.That(warpFleet.Location).IsEqualTo(new Coordinate(1, 0));
        await Assert.That(jumpFleet.Status).IsEqualTo(FleetStatus.Ready);
        await Assert.That(warpFleet.Status).IsEqualTo(FleetStatus.InTransit);
    }

    [Test]
    public async Task FleetWithInsufficientFuel_DeactivatesInsteadOfMoving()
    {
        var human = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var fleet = new Fleet {
            Owner = human,
            Location = new Coordinate(0, 0),
            Destination = new Coordinate(10, 0),
            Fuel = 0,
            Status = FleetStatus.Ready,
        };
        fleet.Ships.Fighters = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        // Standard-type fleets only advance as the *next* empire's fleets (FLEET.PAS:892-903),
        // so human must be passed as nextEmpire here for this fleet to be evaluated at all.
        handler.AdvanceFleets(game, new Empire { Name = "AI" }, human);

        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.Inactive);
        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(0, 0));
        await Assert.That(fleet.Destination).IsNull();
    }

    [Test]
    public async Task StarbaseMovesOneCellPerTurnTowardDestination()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var starbase = new Starbase {
            Owner = empire,
            Location = new Coordinate(0, 0),
            Destination = new Coordinate(1, 0),
            Kind = StarbaseKind.CommandBase,
            Status = FleetStatus.Ready,
        };
        game.Galaxy.Starbases.Add(starbase);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceStarbases(game, empire);

        await Assert.That(starbase.Location).IsEqualTo(new Coordinate(1, 0));
        await Assert.That(starbase.Destination).IsNull();
        await Assert.That(starbase.Status).IsEqualTo(FleetStatus.Ready);
    }

    [Test]
    public async Task JumpFleet_HitsEnemyMinefield_SurvivesDamagedAndLandsOnTheMinedCell()
    {
        var victim = new Empire { Name = "Victim" };
        var mineOwner = new Empire { Name = "MineOwner" };
        var game = new Game(new Galaxy(size: 20));
        game.Galaxy.SetMine(new Coordinate(1, 0), mineOwner);

        var fleet = new Fleet { Owner = victim, Location = new Coordinate(0, 0), Destination = new Coordinate(5, 0), Fuel = 1000, Status = FleetStatus.Ready };
        fleet.Ships.Jumpships = 10;
        game.Galaxy.Fleets.Add(fleet);

        // Rnd(1,100) with FixedRandom(0) = 1; ProtecNeeded[Jumpship]=20, so destroyed = min(10, 1 + PascalRound(10*0.4)) = 5.
        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceFleets(game, victim, new Empire { Name = "Other" });

        await Assert.That(game.Galaxy.Fleets).Contains(fleet);
        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(1, 0));
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.InTransit);
        await Assert.That(fleet.Ships.Jumpships).IsEqualTo(5);
        await Assert.That(game.Galaxy.IsMineScoutedBy(victim, new Coordinate(1, 0))).IsTrue();
        await Assert.That(victim.News).Contains(n => n.Headline == NewsType.FleetDamagedByMines && n.OtherEmpire == mineOwner);
        await Assert.That(victim.News).Contains(n => n.Headline == NewsType.DestructionDetail && n.Parm1 == 5);
        await Assert.That(mineOwner.News).Contains(n => n.Headline == NewsType.EnemyFleetDamagedInMinefield && n.OtherEmpire == victim && n.Position == new Coordinate(1, 0));
    }

    [Test]
    public async Task JumpFleet_HitsEnemyMinefield_DestroyedWhenNoShipsSurvive()
    {
        var victim = new Empire { Name = "Victim" };
        var mineOwner = new Empire { Name = "MineOwner" };
        var game = new Game(new Galaxy(size: 20));
        game.Galaxy.SetMine(new Coordinate(1, 0), mineOwner);

        var fleet = new Fleet { Owner = victim, Location = new Coordinate(0, 0), Destination = new Coordinate(5, 0), Fuel = 1000, Status = FleetStatus.Ready };
        fleet.Ships.Jumpships = 1;
        game.Galaxy.Fleets.Add(fleet);

        // Rnd(1,100) with FixedRandom(0) = 1; destroyed = min(1, 1 + PascalRound(1*0.4)) = 1 — total loss.
        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceFleets(game, victim, new Empire { Name = "Other" });

        await Assert.That(game.Galaxy.Fleets).DoesNotContain(fleet);
        await Assert.That(victim.News).Contains(n => n.Headline == NewsType.FleetDestroyedByMines && n.OtherEmpire == mineOwner);
        await Assert.That(victim.News).Contains(n => n.Headline == NewsType.DestructionDetail && n.Parm1 == 1);
    }

    [Test]
    public async Task JumpFleet_PassesNearEnemyDisrupterGate_StopsAndLandsOnTheBlockedCell()
    {
        var victim = new Empire { Name = "Victim" };
        var gateOwner = new Empire { Name = "GateOwner" };
        var game = new Game(new Galaxy(size: 20));

        var gate = new Stargate { Owner = gateOwner, Location = new Coordinate(2, 0), Kind = StargateKind.Disrupter };
        game.Galaxy.Stargates.Add(gate);

        var fleet = new Fleet { Owner = victim, Location = new Coordinate(0, 0), Destination = new Coordinate(10, 0), Fuel = 1000, Status = FleetStatus.Ready };
        fleet.Ships.Jumpships = 5;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceFleets(game, victim, new Empire { Name = "Other" });

        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(1, 0));
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.InTransit);
        await Assert.That(fleet.Ships.Jumpships).IsEqualTo(5);
        await Assert.That(victim.News).Contains(n => n.Headline == NewsType.FleetStoppedByDisrupter && n.OtherEmpire == gateOwner);
    }
}
