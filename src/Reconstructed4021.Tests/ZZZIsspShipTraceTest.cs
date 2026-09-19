using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// Throwaway trace: why does a Capital world at ISSP=100% show 0 ships/year while at ISSP=1% it
/// shows a large number, given metal is abundant (overflowing) in both cases. Prints per-ship-type
/// gross production and industry-level/dist state at steady state for both settings.
/// </summary>
public class ZZZIsspShipTraceTest
{
    [Test, Explicit]
    public async Task TraceIsspShipOutput()
    {
        RunOneSetting(issp: 5, label: "ISSP=100% (index 5, default)");
        RunOneSetting(issp: 0, label: "ISSP=1% (index 0, floor)");
        await Task.CompletedTask;
    }

    private static void RunOneSetting(int issp, string label)
    {
        var random = new Random(42);
        var owner = new Empire { Name = "Test" };
        foreach (var ship in Enum.GetValues<ShipType>())
            owner.Technology.Ships.Add(ship); // unlock everything so tech-gating can't hide the real effect

        var planet = new Planet {
            Location = new Coordinate(0, 0),
            Owner = owner,
            Class = WorldClass.EarthLike,
            Type = WorldType.Capital,
            TechLevel = TechLevel.Bio,
            Population = 4055,
            Efficiency = 100,
            TrillumReserve = 999_999, // remove reserve depletion as a confound for this trace
        };
        planet.SelfSufficiency.Chemical = issp;
        planet.SelfSufficiency.Metal = issp;
        planet.SelfSufficiency.Trillum = issp;
        // Supply left at its own default (5) throughout -- not the thing under test.

        // A hand-built Planet starts at Industry=0/Cargo=0 for every field, which is NOT what a real
        // world looks like (GalaxySetup seeds Industry via GetOptimumIndustry and jitters Cargo from a
        // scenario's own base values) -- starting from true zero can never bootstrap at all, since
        // ProduceRawMaterial skips any industry at level<=0 and UpdateIndustry can't grow anything
        // without metal on hand already. Seed both realistically, same as a real new world would be.
        var dist0 = AnnualTickHandler.GetIndustrialDistribution(planet);
        var tip0 = AnnualTickHandler.TotalProd(planet.Population, planet.TechLevel);
        foreach (var industry in Enum.GetValues<IndustryType>()) {
            var clsAdj = AnnualTickHandler.ClassIndustryAdjustment[(planet.Class, industry)];
            planet.Industry[industry] = (int)Math.Round(tip0 * (dist0[industry] / 100.0) * (clsAdj / 100.0));
        }
        planet.Cargo.Metals = 1000;
        planet.Cargo.Chemicals = 1000;
        planet.Cargo.Trillum = 1000;
        planet.Cargo.Supplies = 1000;

        var handler = new AnnualTickHandler(random);
        var reported = new HashSet<CargoType>();

        // Run to steady state without tracking.
        for (var year = 0; year < 55; year++) {
            reported.Clear();
            handler.RunProductionPipeline(planet, reported);
        }

        // Capture a clean steady-state snapshot: reset diagnostics, run 5 more years, average.
        ProductionDiagnostics.FilterEmpire = null;
        ProductionDiagnostics.Reset();
        ProductionDiagnostics.Enabled = true;
        for (var year = 0; year < 5; year++) {
            reported.Clear();
            handler.RunProductionPipeline(planet, reported);
        }
        ProductionDiagnostics.Enabled = false;

        var dist = AnnualTickHandler.GetIndustrialDistribution(planet);

        Console.WriteLine($"--- {label} ---");
        Console.WriteLine($"TechLevel={planet.TechLevel}, dist[ShipyardGeneral]={dist[IndustryType.ShipyardGeneral]:0.00}%, dist[Chemical]={dist[IndustryType.Chemical]:0.00}%, dist[Mining]={dist[IndustryType.Mining]:0.00}%, dist[Trillum]={dist[IndustryType.TrillumMining]:0.00}%");
        Console.WriteLine($"Industry[ShipyardGeneral]={planet.Industry[IndustryType.ShipyardGeneral]}, Industry[Mining]={planet.Industry[IndustryType.Mining]}, Industry[Chemical]={planet.Industry[IndustryType.Chemical]}");
        Console.WriteLine($"Cargo: Metals={planet.Cargo.Metals}, Chemicals={planet.Cargo.Chemicals}, Trillum={planet.Cargo.Trillum}, Supplies={planet.Cargo.Supplies}");
        Console.WriteLine("Gross ships/year (avg of last 5 years), by ship type:");
        foreach (var ship in Enum.GetValues<ShipType>()) {
            var gross = ProductionDiagnostics.GrossShipsProduced.GetValueOrDefault(ship) / 5.0;
            var lost = ProductionDiagnostics.ShipsLostToShortage.GetValueOrDefault(ship) / 5.0;
            var minTech = TechCatalog.MinTechForShip[ship];
            var techGated = minTech > planet.TechLevel;
            Console.WriteLine($"  {ship}: gross/yr={gross:0.##}, lostToRawMaterialShortage/yr={lost:0.##}, minTech={minTech}, techGatedAtThisWorld={techGated}");
        }
    }
}
