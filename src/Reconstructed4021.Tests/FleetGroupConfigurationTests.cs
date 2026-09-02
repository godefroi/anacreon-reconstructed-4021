using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>ATTCOMM.PAS's GetGroups bookkeeping (ChangeGroupType/LoadShips/LoadTransports/the tail compaction+auto-load pass) -- see FleetGroupConfiguration's own doc comment.</summary>
public class FleetGroupConfigurationTests
{
    [Test]
    public async Task ChangeGroupType_NoOp_WhenTypeAlreadyMatches()
    {
        var pool = new ShipCounts();
        var group = new GroupRecord { Typ = AttackType.Fighter, Num = 5 };

        FleetGroupConfiguration.ChangeGroupType(pool, new CargoHold(), group, AttackType.Fighter);

        await Assert.That(group.Num).IsEqualTo(5);
        await Assert.That(pool.Fighters).IsEqualTo(0);
    }

    [Test]
    public async Task ChangeGroupType_ReturnsShipsAndCarriedTroopsToPool_ThenSwitches()
    {
        var shipPool = new ShipCounts();
        var cargoPool = new CargoHold();
        var group = new GroupRecord { Typ = AttackType.Transport, Num = 10, Gat = 5, GatTyp = AttackType.Legion };

        FleetGroupConfiguration.ChangeGroupType(shipPool, cargoPool, group, AttackType.Fighter);

        await Assert.That(group.Typ).IsEqualTo(AttackType.Fighter);
        await Assert.That(group.Num).IsEqualTo(0);
        await Assert.That(group.Gat).IsEqualTo(0);
        await Assert.That(group.GatTyp).IsNull();
        await Assert.That(shipPool.Transports).IsEqualTo(10);
        await Assert.That(cargoPool.Legions).IsEqualTo(5);
    }

    [Test]
    public async Task LoadShips_PositiveAmount_ClampedToWhatsInThePool()
    {
        var pool = new ShipCounts { Fighters = 30 };
        var group = new GroupRecord { Typ = AttackType.Fighter, Num = 0 };

        FleetGroupConfiguration.LoadShips(pool, group, 100);

        await Assert.That(group.Num).IsEqualTo(30);
        await Assert.That(pool.Fighters).IsEqualTo(0);
    }

    [Test]
    public async Task LoadShips_NegativeAmount_ClampedToTheGroupsOwnCount_ReturnsToPool()
    {
        var pool = new ShipCounts { Fighters = 10 };
        var group = new GroupRecord { Typ = AttackType.Fighter, Num = 20 };

        FleetGroupConfiguration.LoadShips(pool, group, -100);

        await Assert.That(group.Num).IsEqualTo(0);
        await Assert.That(pool.Fighters).IsEqualTo(30);
    }

    [Test]
    public async Task LoadTroops_ThrowsForANonTransportGroup()
    {
        var group = new GroupRecord { Typ = AttackType.Fighter, Num = 10 };

        await Assert.That(() => FleetGroupConfiguration.LoadTroops(new CargoHold(), group, CargoType.Legion))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task LoadTroops_CapsAtTrnAdjTimesNumTimesCargoSpace()
    {
        // Transport: TrnAdj=1, CargoSpace[Legion]=5 -- Num=10 caps at 50, but only 30 are available.
        var pool = new CargoHold { Legions = 30 };
        var group = new GroupRecord { Typ = AttackType.Transport, Num = 10 };

        FleetGroupConfiguration.LoadTroops(pool, group, CargoType.Legion);

        await Assert.That(group.Gat).IsEqualTo(30);
        await Assert.That(group.GatTyp).IsEqualTo(AttackType.Legion);
        await Assert.That(pool.Legions).IsEqualTo(0);
    }

    [Test]
    public async Task LoadTroops_CapacityLimitedWhenPoolHasPlenty()
    {
        var pool = new CargoHold { Legions = 9999 };
        var group = new GroupRecord { Typ = AttackType.Transport, Num = 10 }; // capacity 50

        FleetGroupConfiguration.LoadTroops(pool, group, CargoType.Legion);

        await Assert.That(group.Gat).IsEqualTo(50);
        await Assert.That(pool.Legions).IsEqualTo(9949);
    }

    [Test]
    public async Task LoadTroops_ReturnsAnyExistingGatToPoolFirst_ThenLoadsTheNewType()
    {
        var pool = new CargoHold { Legions = 10, NinjaLegions = 4 };
        var group = new GroupRecord { Typ = AttackType.Transport, Num = 10, Gat = 3, GatTyp = AttackType.NinjaLegion };

        FleetGroupConfiguration.LoadTroops(pool, group, CargoType.Legion);

        await Assert.That(pool.NinjaLegions).IsEqualTo(7); // the old 3 nnj came back before the switch
        await Assert.That(group.Gat).IsEqualTo(10); // capacity 50, but only 10 legions in the pool
        await Assert.That(group.GatTyp).IsEqualTo(AttackType.Legion);
        await Assert.That(pool.Legions).IsEqualTo(0);
    }

    [Test]
    public async Task Finalize_DropsGroupsWithZeroShips()
    {
        var empty = new GroupRecord { Typ = AttackType.Fighter, Num = 0 };
        var full = new GroupRecord { Typ = AttackType.HunterKiller, Num = 5 };

        var result = FleetGroupConfiguration.Finalize([empty, full], new CargoHold());

        await Assert.That(result).Count().IsEqualTo(1);
        await Assert.That(result[0]).IsSameReferenceAs(full);
    }

    [Test]
    public async Task Finalize_OrdersEveryNonTransportGroupBeforeAnyTransportGroup()
    {
        // Configured in the opposite order from the expected output, to prove this is a real
        // reordering pass (ATTCOMM.PAS's own two separate compaction loops), not just "keep input order".
        var transport = new GroupRecord { Typ = AttackType.Transport, Num = 10 };
        var jumptransport = new GroupRecord { Typ = AttackType.Jumptransport, Num = 5 };
        var fighter = new GroupRecord { Typ = AttackType.Fighter, Num = 20 };
        var hunterKiller = new GroupRecord { Typ = AttackType.HunterKiller, Num = 8 };

        var result = FleetGroupConfiguration.Finalize([transport, fighter, jumptransport, hunterKiller], new CargoHold());

        await Assert.That(result).Count().IsEqualTo(4);
        await Assert.That(result[0]).IsSameReferenceAs(fighter);
        await Assert.That(result[1]).IsSameReferenceAs(hunterKiller);
        await Assert.That(result[2]).IsSameReferenceAs(transport);
        await Assert.That(result[3]).IsSameReferenceAs(jumptransport);
    }

    [Test]
    public async Task Finalize_AutoLoadsRemainingTroops_PrefersMenOverNinja_OppositeOfDefaultGroup()
    {
        var pool = new CargoHold { Legions = 3, NinjaLegions = 3 };
        var group = new GroupRecord { Typ = AttackType.Transport, Num = 10 };

        var result = FleetGroupConfiguration.Finalize([group], pool);

        await Assert.That(result[0].GatTyp).IsEqualTo(AttackType.Legion);
        await Assert.That(result[0].Gat).IsEqualTo(3);
        await Assert.That(pool.Legions).IsEqualTo(0);
        await Assert.That(pool.NinjaLegions).IsEqualTo(3); // untouched -- men took priority
    }

    [Test]
    public async Task Finalize_FallsBackToNinjaWhenNoMenAreLeftInThePool()
    {
        var pool = new CargoHold { NinjaLegions = 4 };
        var group = new GroupRecord { Typ = AttackType.Transport, Num = 10 };

        var result = FleetGroupConfiguration.Finalize([group], pool);

        await Assert.That(result[0].GatTyp).IsEqualTo(AttackType.NinjaLegion);
        await Assert.That(result[0].Gat).IsEqualTo(4);
    }

    [Test]
    public async Task Finalize_DoesNotOverwriteAGroupThatAlreadyCarriesTroops()
    {
        var pool = new CargoHold { Legions = 100 };
        var group = new GroupRecord { Typ = AttackType.Transport, Num = 10, Gat = 7, GatTyp = AttackType.NinjaLegion };

        var result = FleetGroupConfiguration.Finalize([group], pool);

        await Assert.That(result[0].Gat).IsEqualTo(7);
        await Assert.That(result[0].GatTyp).IsEqualTo(AttackType.NinjaLegion);
        await Assert.That(pool.Legions).IsEqualTo(100); // untouched
    }
}
