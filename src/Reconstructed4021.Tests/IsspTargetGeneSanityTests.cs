using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;
using Reconstructed4021.LegacyNpe;

namespace Reconstructed4021.Tests;

/// <summary>
/// Sanity checks for NpeToolkit.ManageSelfSufficiency, gated on NpeCharacter.IsspTargetGene: does it
/// reproduce (or beat) the known-good mature-world result (997 ships/year, ZZZIsspSelfManagement
/// VsLogisticsTest) and avoid the known-bad young-world regression (37 ships/year,
/// ZZZIsspYoungWorldTest) that a fixed absolute target and naive step-by-one convergence produced.
/// </summary>
public class IsspTargetGeneSanityTests
{
    private static Planet SeedWorld(WorldType type, WorldClass cls, int population, out Empire owner, out Game game)
    {
        owner = new Empire { Name = "Test-" + Guid.NewGuid() };
        foreach (var ship in Enum.GetValues<ShipType>())
            owner.Technology.Ships.Add(ship);

        var planet = new Planet {
            Location = new Coordinate(0, 0),
            Owner = owner,
            Class = cls,
            Type = type,
            TechLevel = TechLevel.Bio,
            Population = population,
            Efficiency = 100,
            TrillumReserve = 999_999,
        };

        var dist0 = AnnualTickHandler.GetIndustrialDistribution(planet);
        var tip0 = AnnualTickHandler.TotalProd(planet.Population, planet.TechLevel);
        foreach (var industry in Enum.GetValues<IndustryType>()) {
            var clsAdj = AnnualTickHandler.ClassIndustryAdjustment[(planet.Class, industry)];
            planet.Industry[industry] = (int)Math.Round(tip0 * (dist0[industry] / 100.0) * (clsAdj / 100.0));
        }
        planet.Cargo.Metals = population > 1000 ? 1000 : 200;
        planet.Cargo.Chemicals = planet.Cargo.Metals;
        planet.Cargo.Trillum = planet.Cargo.Metals;
        planet.Cargo.Supplies = planet.Cargo.Metals;

        game = new Game(new Reconstructed4021.Core.Galaxy.Galaxy(size: 21));
        game.Galaxy.Planets.Add(planet);
        return planet;
    }

    private static int RunAndMeasure(WorldType type, int population, int isspTargetGene, int years, int sampleYears)
    {
        var random = new Random(1);
        var planet = SeedWorld(type, WorldClass.EarthLike, population, out var owner, out var game);
        var handler = new AnnualTickHandler(random);
        var persona = new NpeCharacter { IsspTargetGene = isspTargetGene };
        var reported = new HashSet<CargoType>();

        for (var year = 0; year < years; year++) {
            NpeToolkit.ManageSelfSufficiency(owner, persona, game);
            reported.Clear();
            handler.RunProductionPipeline(planet, reported);
        }

        ProductionDiagnostics.FilterEmpire = null;
        ProductionDiagnostics.Reset();
        ProductionDiagnostics.Enabled = true;
        for (var year = 0; year < sampleYears; year++) {
            NpeToolkit.ManageSelfSufficiency(owner, persona, game);
            reported.Clear();
            handler.RunProductionPipeline(planet, reported);
        }
        ProductionDiagnostics.Enabled = false;

        var shipsPerYear = (int)Math.Round(Enum.GetValues<ShipType>().Sum(s => ProductionDiagnostics.GrossShipsProduced.GetValueOrDefault(s)) / (double)sampleYears);
        Console.WriteLine($"pop={population} target%={isspTargetGene} -> ships/year={shipsPerYear}, " +
            $"ISSP(Che/Met/Tri)={planet.SelfSufficiency.Chemical}/{planet.SelfSufficiency.Metal}/{planet.SelfSufficiency.Trillum}, " +
            $"Cargo(Met/Che/Tri)={planet.Cargo.Metals}/{planet.Cargo.Chemicals}/{planet.Cargo.Trillum}");
        return shipsPerYear;
    }

    /// <summary>Traces convergence speed: how many years until ISSP stops moving, on the mature world.</summary>
    [Test, Explicit]
    public async Task TraceConvergenceSpeed()
    {
        var random = new Random(1);
        var planet = SeedWorld(WorldType.Capital, WorldClass.EarthLike, 4055, out var owner, out var game);
        var handler = new AnnualTickHandler(random);
        var persona = new NpeCharacter { IsspTargetGene = 123 }; // target ~ 4055*1.23 = 4988, matching the known-good 5000
        var reported = new HashSet<CargoType>();
        var last = (-1, -1, -1);
        var settledAtYear = -1;

        for (var year = 0; year < 70; year++) {
            NpeToolkit.ManageSelfSufficiency(owner, persona, game);
            var now = (planet.SelfSufficiency.Chemical, planet.SelfSufficiency.Metal, planet.SelfSufficiency.Trillum);
            if (now == last && settledAtYear < 0) {
                settledAtYear = year;
            } else if (now != last) {
                settledAtYear = -1;
            }
            last = now;
            reported.Clear();
            handler.RunProductionPipeline(planet, reported);
        }

        Console.WriteLine($"Final ISSP(Che/Met/Tri)={last}, first settled (2 consecutive identical readings) at year {settledAtYear}");
        await Task.CompletedTask;
    }

    [Test, Explicit]
    public async Task MatureWorld_ReproducesOrBeatsKnownGood()
    {
        // Known-good from ZZZIsspSelfManagementVsLogisticsTest: 997 ships/year, naive step-by-one, ~65yr settle.
        var shipsPerYear = RunAndMeasure(WorldType.Capital, 4055, isspTargetGene: 123, years: 60, sampleYears: 5);
        Console.WriteLine($"Known-good baseline: 997 ships/year (naive step-by-one). This run: {shipsPerYear}.");
        await Task.CompletedTask;
    }

    [Test, Explicit]
    public async Task YoungWorld_AvoidsKnownRegression()
    {
        // Known-bad from ZZZIsspYoungWorldTest: 37 ships/year (worse than baseline's 44), naive step-by-one
        // with a fixed target of 500 misread this small economy as chronically short.
        var shipsPerYear = RunAndMeasure(WorldType.Independent, 464, isspTargetGene: 108, years: 60, sampleYears: 5);
        Console.WriteLine($"Known-bad regression: 37 ships/year (worse than baseline 44). This run: {shipsPerYear}.");
        await Task.CompletedTask;
    }
}
