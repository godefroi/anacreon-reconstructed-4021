using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// <see cref="Core.Entities.FleetLogistics"/>: FuelCapacity/FuelConsumption/FleetCargoSpace/
/// BalanceFleet against reference/verify/golden/fleetlogistics.golden — real MISC.PAS/INTRFACE.PAS
/// arithmetic, no RNG. See FleetLogisticsCases' own doc comment for what each case exercises.
/// </summary>
public class FleetLogisticsTests
{
    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles))]
    [MethodDataSource(typeof(PascalGroundTruth.FleetLogisticsCases), nameof(PascalGroundTruth.FleetLogisticsCases.AsDataSource))]
    public async Task MatchesGoldenFile(PascalGroundTruth.FleetLogisticsCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("fleetlogistics.golden");
        var expected = golden[c.Name];

        var ships = new ShipCounts {
            Fighters = c.Fgt, HunterKillers = c.Hkr, Jumpships = c.Jmp, Jumptransports = c.Jtn,
            Penetrators = c.Pen, Starships = c.Ssp, Transports = c.Trn,
        };
        var cargo = new CargoHold {
            Legions = c.Men, NinjaLegions = c.Nnj, Ambrosia = c.Amb, Chemicals = c.Che,
            Metals = c.Met, Supplies = c.Sup, Trillum = c.Tri,
        };

        var fuelCapacity = FleetLogistics.FuelCapacity(ships);
        var fuelConsumption = FleetLogistics.FuelConsumption(ships, cargo);
        var cargoSpace = FleetLogistics.FleetCargoSpace(ships, cargo);
        FleetLogistics.BalanceFleet(ships, cargo);

        await Assert.That(fuelCapacity).IsEqualTo(double.Parse(expected["fuelcap"])).Within(0.000001);
        await Assert.That(fuelConsumption).IsEqualTo(double.Parse(expected["fuelcons"])).Within(0.000001);
        await Assert.That(cargoSpace).IsEqualTo(int.Parse(expected["cargospace"]));
        await Assert.That(cargo.Legions).IsEqualTo(int.Parse(expected["balanced_men"]));
        await Assert.That(cargo.NinjaLegions).IsEqualTo(int.Parse(expected["balanced_nnj"]));
        await Assert.That(cargo.Ambrosia).IsEqualTo(int.Parse(expected["balanced_amb"]));
        await Assert.That(cargo.Chemicals).IsEqualTo(int.Parse(expected["balanced_che"]));
        await Assert.That(cargo.Metals).IsEqualTo(int.Parse(expected["balanced_met"]));
        await Assert.That(cargo.Supplies).IsEqualTo(int.Parse(expected["balanced_sup"]));
        await Assert.That(cargo.Trillum).IsEqualTo(int.Parse(expected["balanced_tri"]));
    }

    [Test]
    public async Task FleetCargoSpaceFor_ScalesFreeSpaceByTheCargoTypesOwnPerUnitCost()
    {
        var ships = new ShipCounts { Transports = 10 };
        var cargo = new CargoHold();

        // 10 tons of free transport space; Metals costs 3 space per ton (CargoSpacePerUnit), so up to
        // 30 tons of metals fit -- the same product ExecuteTransCOM's own pickup clamp computes inline.
        await Assert.That(FleetLogistics.FleetCargoSpaceFor(CargoType.Metals, ships, cargo)).IsEqualTo(30);
    }
}
