using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>FLTCOMM.PAS's InputNewDistribution transfer math (GetChange/FillFleet/EmptyFleet) -- see ResourceDistribution's own doc comment.</summary>
public class ResourceDistributionTests
{
    private static readonly ResourceColumn Fighters = ResourceColumn.All[0];
    private static readonly ResourceColumn Transports = ResourceColumn.All[6];
    private static readonly ResourceColumn Jumptransports = ResourceColumn.All[3];
    private static readonly ResourceColumn Metals = ResourceColumn.All[11];

    [Test]
    public async Task TryTransfer_PositiveAmount_MovesGroundToFleetWhenPlayerOwnsGround()
    {
        var fltSh = new ShipCounts();
        var fltCr = new CargoHold();
        var grnSh = new ShipCounts { Fighters = 100 };
        var grnCr = new CargoHold();

        var ok = ResourceDistribution.TryTransfer(Fighters, fltSh, fltCr, grnSh, grnCr, groundIsPlayerOwned: true, amount: 40, out var error);

        await Assert.That(ok).IsTrue();
        await Assert.That(error).IsEmpty();
        await Assert.That(fltSh.Fighters).IsEqualTo(40);
        await Assert.That(grnSh.Fighters).IsEqualTo(60);
    }

    [Test]
    public async Task TryTransfer_PositiveAmount_FailsWhenGroundIsNotPlayersOwn()
    {
        var fltSh = new ShipCounts();
        var grnSh = new ShipCounts { Fighters = 100 };

        var ok = ResourceDistribution.TryTransfer(Fighters, fltSh, new CargoHold(), grnSh, new CargoHold(), groundIsPlayerOwned: false, amount: 10, out var error);

        await Assert.That(ok).IsFalse();
        await Assert.That(error).IsEqualTo("This is not your territory.");
        await Assert.That(fltSh.Fighters).IsEqualTo(0); // unchanged on failure
    }

    [Test]
    public async Task TryTransfer_NonPositiveAmount_MovesFleetToGroundRegardlessOfOwnership()
    {
        var fltSh = new ShipCounts { Fighters = 50 };
        var grnSh = new ShipCounts();

        var ok = ResourceDistribution.TryTransfer(Fighters, fltSh, new CargoHold(), grnSh, new CargoHold(), groundIsPlayerOwned: false, amount: -20, out _);

        await Assert.That(ok).IsTrue();
        await Assert.That(fltSh.Fighters).IsEqualTo(30);
        await Assert.That(grnSh.Fighters).IsEqualTo(20);
    }

    [Test]
    public async Task TryTransfer_FailsWhenNotEnoughOnWhicheverSideIsGivingResources()
    {
        var fltSh = new ShipCounts { Fighters = 5 };
        var grnSh = new ShipCounts { Fighters = 5 };

        var short_ = ResourceDistribution.TryTransfer(Fighters, fltSh, new CargoHold(), grnSh, new CargoHold(), true, amount: 10, out var groundError);
        var alsoShort = ResourceDistribution.TryTransfer(Fighters, fltSh, new CargoHold(), grnSh, new CargoHold(), true, amount: -10, out var fleetError);

        await Assert.That(short_).IsFalse();
        await Assert.That(groundError).IsEqualTo("Not enough fighter squadrons on ground.");
        await Assert.That(alsoShort).IsFalse();
        await Assert.That(fleetError).IsEqualTo("Not enough fighter squadrons in fleet.");
    }

    [Test]
    public async Task TryTransfer_FailsWhenBeyondTheMaxResourcesBound()
    {
        var ok = ResourceDistribution.TryTransfer(Fighters, new ShipCounts(), new CargoHold(), new ShipCounts { Fighters = ResourceDistribution.MaxResources * 2 }, new CargoHold(), true, amount: ResourceDistribution.MaxResources + 1, out var error);

        await Assert.That(ok).IsFalse();
        await Assert.That(error).IsEqualTo($"numbers from -{ResourceDistribution.MaxResources} to {ResourceDistribution.MaxResources}.");
    }

    [Test]
    public async Task FillFleet_CombatShipColumn_MovesEverythingRegardlessOfCargoSpace()
    {
        var fltSh = new ShipCounts();
        var grnSh = new ShipCounts { Fighters = 300 };

        ResourceDistribution.FillFleet(Fighters, fltSh, new CargoHold(), grnSh, new CargoHold(), groundIsPlayerOwned: true);

        await Assert.That(fltSh.Fighters).IsEqualTo(300);
        await Assert.That(grnSh.Fighters).IsEqualTo(0);
    }

    [Test]
    public async Task FillFleet_TransportColumn_MovesEverythingWhenCargoSpaceIsNotAlreadyNegative()
    {
        // One transport already aboard carrying nothing -- 1 unit of free space (FleetCargoSpace = 1).
        // Transports/jumptransports are only capped when CargoSpaceAvail is already negative *before*
        // adding them (FLTCOMM.PAS:371-374) -- so with 1 free unit already, every transport still moves,
        // even though taking all 5 leaves the fleet at -4. The capped case is the next test down.
        var fltSh = new ShipCounts { Transports = 1 };
        var grnSh = new ShipCounts { Transports = 5 };

        ResourceDistribution.FillFleet(Transports, fltSh, new CargoHold(), grnSh, new CargoHold(), groundIsPlayerOwned: true);

        await Assert.That(fltSh.Transports).IsEqualTo(6);
        await Assert.That(grnSh.Transports).IsEqualTo(0);
    }

    [Test]
    public async Task FillFleet_TransportColumn_CappedWhenCargoSpaceAlreadyNegative()
    {
        // Fleet already overloaded with 300 metal on 0 transports: FleetCargoSpace = -100 (300/3).
        // Ground offers 200 transports -- only enough to close that 100-unit deficit should move,
        // matching Round(-CargoSpaceAvail/TrnAdj[trn]) = Round(100/1.0) = 100.
        var fltSh = new ShipCounts();
        var fltCr = new CargoHold { Metals = 300 };
        var grnSh = new ShipCounts { Transports = 200 };

        ResourceDistribution.FillFleet(Transports, fltSh, fltCr, grnSh, new CargoHold(), groundIsPlayerOwned: true);

        await Assert.That(fltSh.Transports).IsEqualTo(100);
        await Assert.That(grnSh.Transports).IsEqualTo(100);
    }

    [Test]
    public async Task FillFleet_JumptransportColumn_UsesItsOwnCargoAdjustment()
    {
        // Same -100 deficit as above, but jumptransports only carry 0.2x a transport's load, so it
        // takes 5x as many (Round(100/0.2)=500) to close the same gap.
        var fltSh = new ShipCounts();
        var fltCr = new CargoHold { Metals = 300 };
        var grnSh = new ShipCounts { Jumptransports = 1000 };

        ResourceDistribution.FillFleet(Jumptransports, fltSh, fltCr, grnSh, new CargoHold(), groundIsPlayerOwned: true);

        await Assert.That(fltSh.Jumptransports).IsEqualTo(500);
        await Assert.That(grnSh.Jumptransports).IsEqualTo(500);
    }

    [Test]
    public async Task FillFleet_CargoColumn_CappedByWhateverSpaceIsCurrentlyFree()
    {
        // 10 transports = 10 units of free space; metals cost 3 tons of space per unit
        // (CargoSpace[met]), so up to space*CargoSpace[met] = 10*3 = 30 metals can move.
        var fltSh = new ShipCounts { Transports = 10 };
        var fltCr = new CargoHold();
        var grnCr = new CargoHold { Metals = 100 };

        ResourceDistribution.FillFleet(Metals, fltSh, fltCr, new ShipCounts(), grnCr, groundIsPlayerOwned: true);

        await Assert.That(fltCr.Metals).IsEqualTo(30);
        await Assert.That(grnCr.Metals).IsEqualTo(70);
    }

    [Test]
    public async Task FillFleet_IsANoOpWhenGroundIsNotThePlayers()
    {
        var fltSh = new ShipCounts();
        var grnSh = new ShipCounts { Fighters = 100 };

        ResourceDistribution.FillFleet(Fighters, fltSh, new CargoHold(), grnSh, new CargoHold(), groundIsPlayerOwned: false);

        await Assert.That(fltSh.Fighters).IsEqualTo(0);
        await Assert.That(grnSh.Fighters).IsEqualTo(100);
    }

    [Test]
    public async Task EmptyFleet_ShipColumn_AlwaysMovesEverythingBackToGround()
    {
        var fltSh = new ShipCounts { Fighters = 75 };
        var grnSh = new ShipCounts();

        ResourceDistribution.EmptyFleet(Fighters, fltSh, new CargoHold(), grnSh, new CargoHold(), groundIsAFleet: true);

        await Assert.That(fltSh.Fighters).IsEqualTo(0);
        await Assert.That(grnSh.Fighters).IsEqualTo(75);
    }

    [Test]
    public async Task EmptyFleet_CargoColumn_MovesEverythingWhenGroundIsAWorldNotAFleet()
    {
        var fltCr = new CargoHold { Metals = 500 };
        var grnCr = new CargoHold();

        ResourceDistribution.EmptyFleet(Metals, new ShipCounts(), fltCr, new ShipCounts(), grnCr, groundIsAFleet: false);

        await Assert.That(fltCr.Metals).IsEqualTo(0);
        await Assert.That(grnCr.Metals).IsEqualTo(500);
    }

    [Test]
    public async Task EmptyFleet_CargoColumn_CappedByTheReceivingFleetsOwnSpaceWhenGroundIsAFleet()
    {
        // Receiving fleet has 2 free transport-units of space (2 transports, nothing aboard) -> can
        // only take 2*3=6 metals even though the source fleet is offering 500.
        var fltCr = new CargoHold { Metals = 500 };
        var grnSh = new ShipCounts { Transports = 2 };
        var grnCr = new CargoHold();

        ResourceDistribution.EmptyFleet(Metals, new ShipCounts(), fltCr, grnSh, grnCr, groundIsAFleet: true);

        await Assert.That(fltCr.Metals).IsEqualTo(494);
        await Assert.That(grnCr.Metals).IsEqualTo(6);
    }
}
