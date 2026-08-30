using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

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
        // UseUpFuel (FLEET.PAS:660-703) never touches Destination -- a fleet that runs dry still
        // remembers where it was headed, so it can resume once refueled.
        await Assert.That(fleet.Destination).IsEqualTo(new Coordinate(10, 0));
        await Assert.That(human.News).Contains(n => n.Headline == NewsType.FleetOutOfFuel);
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
        starbase.Cargo.Trillum = 200;
        game.Galaxy.Starbases.Add(starbase);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceStarbases(game, empire);

        await Assert.That(starbase.Location).IsEqualTo(new Coordinate(1, 0));
        await Assert.That(starbase.Destination).IsNull();
        await Assert.That(starbase.Status).IsEqualTo(FleetStatus.Ready);
        await Assert.That(starbase.Cargo.Trillum).IsEqualTo(100);
    }

    [Test]
    public async Task StarbaseWithoutEnoughTrillum_ReportsOutOfFuelAndStays()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var starbase = new Starbase {
            Owner = empire,
            Location = new Coordinate(0, 0),
            Destination = new Coordinate(5, 0),
            Kind = StarbaseKind.Fortress,
            Status = FleetStatus.Ready,
        };
        starbase.Cargo.Trillum = 50;
        game.Galaxy.Starbases.Add(starbase);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceStarbases(game, empire);

        await Assert.That(starbase.Location).IsEqualTo(new Coordinate(0, 0));
        await Assert.That(starbase.Cargo.Trillum).IsEqualTo(50);
        await Assert.That(empire.News).Contains(n => n.Headline == NewsType.StarbaseOutOfFuel);
    }

    [Test]
    public async Task StarbaseBlockedByOccupiedCell_ReportsBlockedAndStaysAfterPayingFuel()
    {
        var empire = new Empire { Name = "Human" };
        var blocker = new Empire { Name = "Blocker" };
        var game = new Game(new Galaxy(size: 20));
        // Occupies (1,0), (1,-1) and (1,1) so every candidate the direct/alternate-direction search
        // could reach toward (5,0) is blocked -- GetObjectAt is owner-blind, so a foreign starbase
        // blocks just as a planet or gate would.
        game.Galaxy.Starbases.Add(new Starbase { Owner = blocker, Location = new Coordinate(1, 0), Kind = StarbaseKind.IndustrialComplex });
        game.Galaxy.Starbases.Add(new Starbase { Owner = blocker, Location = new Coordinate(1, -1), Kind = StarbaseKind.IndustrialComplex });
        game.Galaxy.Starbases.Add(new Starbase { Owner = blocker, Location = new Coordinate(1, 1), Kind = StarbaseKind.IndustrialComplex });

        var starbase = new Starbase {
            Owner = empire,
            Location = new Coordinate(0, 0),
            Destination = new Coordinate(5, 0),
            Kind = StarbaseKind.CommandBase,
            Status = FleetStatus.Ready,
        };
        starbase.Cargo.Trillum = 200;
        game.Galaxy.Starbases.Add(starbase);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceStarbases(game, empire);

        await Assert.That(starbase.Location).IsEqualTo(new Coordinate(0, 0));
        // Blocked moves still cost fuel (SBASE.PAS:249-250 deducts FuelCon unconditionally).
        await Assert.That(starbase.Cargo.Trillum).IsEqualTo(100);
        await Assert.That(empire.News).Contains(n => n.Headline == NewsType.StarbaseBlocked);
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

    [Test]
    public async Task StandardFleet_HitsDenseNebula_RevertsToLastCellAndReportsBlocked()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        game.Galaxy.SetNebula(new Coordinate(1, 0), NebulaType.DenseNebula);

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Destination = new Coordinate(5, 0), Fuel = 1000, Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceFleets(game, new Empire { Name = "AI" }, empire);

        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(0, 0));
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.InTransit);
        await Assert.That(empire.News).Contains(n => n.Headline == NewsType.FleetBlocked);
    }

    [Test]
    public async Task FleetAtPublicGate_TeleportsDirectlyToDestination()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        game.Galaxy.Stargates.Add(new Stargate { Owner = empire, Location = new Coordinate(0, 0), Kind = StargateKind.Gate });

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Destination = new Coordinate(15, 15), Fuel = 1000, Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        // Standard fleets advance as the next empire's fleets, and a fleet sitting at any gate
        // (owner-blind occupancy check) also advances in the next-empire slot (FLEET.PAS:892-903).
        handler.AdvanceFleets(game, new Empire { Name = "AI" }, empire);

        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(15, 15));
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.Ready);
        await Assert.That(fleet.Destination).IsNull();
    }

    [Test]
    public async Task FleetAtPublicGate_DestinationInDenseNebula_CannotGateThereAndStays()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        game.Galaxy.Stargates.Add(new Stargate { Owner = empire, Location = new Coordinate(0, 0), Kind = StargateKind.Gate });
        game.Galaxy.SetNebula(new Coordinate(15, 15), NebulaType.DenseNebula);

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Destination = new Coordinate(15, 15), Fuel = 1000, Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceFleets(game, new Empire { Name = "AI" }, empire);

        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(0, 0));
        await Assert.That(fleet.Destination).IsEqualTo(new Coordinate(15, 15));
        await Assert.That(empire.News).Contains(n => n.Headline == NewsType.CannotGateToDenseNebula);
    }

    [Test]
    public async Task FleetAtWarpLink_DestinationIsMatchingKnownLink_Teleports()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var originLink = new Stargate { Owner = empire, Location = new Coordinate(0, 0), Kind = StargateKind.WarpLink };
        var destLink = new Stargate { Owner = empire, Location = new Coordinate(12, 12), Kind = StargateKind.WarpLink };
        game.Galaxy.Stargates.Add(originLink);
        game.Galaxy.Stargates.Add(destLink);
        empire.Stargates.MarkKnown(destLink);

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Destination = new Coordinate(12, 12), Fuel = 1000, Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceFleets(game, new Empire { Name = "AI" }, empire);

        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(12, 12));
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.Ready);
    }

    [Test]
    public async Task FleetAtWarpLink_DestinationLinkUnknown_DoesNotTeleport()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        game.Galaxy.Stargates.Add(new Stargate { Owner = empire, Location = new Coordinate(0, 0), Kind = StargateKind.WarpLink });
        // Destination gate exists but the fleet's owner has never scouted/been told about it.
        game.Galaxy.Stargates.Add(new Stargate { Owner = empire, Location = new Coordinate(12, 12), Kind = StargateKind.WarpLink });

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Destination = new Coordinate(12, 12), Fuel = 1000, Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceFleets(game, new Empire { Name = "AI" }, empire);

        // Falls through to ordinary movement instead: 1 step toward (12,12) from (0,0).
        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(1, 1));
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.InTransit);
    }

    [Test]
    public async Task FleetAtFortress_WithinFiveOfDestination_Teleports()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        game.Galaxy.Starbases.Add(new Starbase { Owner = empire, Location = new Coordinate(0, 0), Kind = StarbaseKind.Fortress });

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Destination = new Coordinate(3, 3), Fuel = 1000, Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceFleets(game, new Empire { Name = "AI" }, empire);

        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(3, 3));
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.Ready);
    }

    [Test]
    public async Task FleetAtFortress_FarFromDestination_HopsFiveThenContinuesWithOrdinaryMovementSameTurn()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        game.Galaxy.Starbases.Add(new Starbase { Owner = empire, Location = new Coordinate(0, 0), Kind = StarbaseKind.Fortress });

        // Standard fleet: FltMovementRate=1, so a plain fleet would only reach (1,1) this turn.
        // A fortress boosts it 5 cells first (to (5,5)), then the ordinary 1-cell allotment applies
        // on top, landing at (6,6) -- both apply in the same turn (FLEET.PAS:768-838).
        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Destination = new Coordinate(15, 15), Fuel = 1000, Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceFleets(game, new Empire { Name = "AI" }, empire);

        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(6, 6));
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.InTransit);
    }
}
