using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// Throwaway: does self-managed ISSP or paired logistics matter for a YOUNG, low-population
/// Independent world (population 464, matching the real shortage-population average found in an
/// earlier diagnostic round), unlike the mature Capital world tested in
/// ZZZIsspSelfManagementVsLogisticsTest, which was never material-constrained once self-managed.
///
/// Target is 500, not the Capital test's 5000: a baseline trace showed this world's cargo grows
/// roughly linearly and never gets near the 9999 cap in 60 years (Metal ~1280, Chemical ~620,
/// Trillum ~440), so 5000 would never be crossed -- 500 is reachable for Metal/Chemical within the
/// horizon while leaving Trillum genuinely tight throughout, which is itself informative.
/// </summary>
public class ZZZIsspYoungWorldTest
{
    private const int Population = 464;
    private const int Target = 500;
    private const int Years = 60;
    private const int SampleYears = 5;
    private const int RWorldPopulation = 1000;
    private const int RSourceTarget = 250;

    [Test, Explicit]
    public async Task CompareYoungWorld()
    {
        RunBaseline();
        RunSelfManagedWithShortageTrace();
        RunSelfManagedPairedLogistics();
        await Task.CompletedTask;
    }

    private static Planet SeedWorld(WorldType type, WorldClass cls, int population, Random random)
    {
        var owner = new Empire { Name = "Test-" + Guid.NewGuid() };
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
        planet.Cargo.Metals = 200;
        planet.Cargo.Chemicals = 200;
        planet.Cargo.Trillum = 200;
        planet.Cargo.Supplies = 200;

        return planet;
    }

    private static int TotalShips(int sampleYears) =>
        (int)Math.Round(Enum.GetValues<ShipType>().Sum(s => ProductionDiagnostics.GrossShipsProduced.GetValueOrDefault(s)) / (double)sampleYears);

    private static void PrintResult(string label, Planet w)
    {
        Console.WriteLine($"--- {label} ---");
        Console.WriteLine($"ISSP now: Chemical={w.SelfSufficiency.Chemical}, Metal={w.SelfSufficiency.Metal}, Trillum={w.SelfSufficiency.Trillum}");
        Console.WriteLine($"Ships/year (avg of last {SampleYears}): {TotalShips(SampleYears)}");
        Console.WriteLine($"Cargo: Metals={w.Cargo.Metals}, Chemicals={w.Cargo.Chemicals}, Trillum={w.Cargo.Trillum}, Supplies={w.Cargo.Supplies}");
    }

    private static void RunBaseline()
    {
        var random = new Random(1);
        var w = SeedWorld(WorldType.Independent, WorldClass.EarthLike, Population, random);
        var handler = new AnnualTickHandler(random);
        var reported = new HashSet<CargoType>();

        for (var year = 0; year < Years; year++) {
            reported.Clear();
            handler.RunProductionPipeline(w, reported);
        }

        ProductionDiagnostics.FilterEmpire = null;
        ProductionDiagnostics.Reset();
        ProductionDiagnostics.Enabled = true;
        for (var year = 0; year < SampleYears; year++) {
            reported.Clear();
            handler.RunProductionPipeline(w, reported);
        }
        ProductionDiagnostics.Enabled = false;

        PrintResult("Test 1: Baseline (default ISSP=5 throughout, no intervention)", w);
    }

    /// <summary>Steps each of Chemical/Metal/Trillum ISSP one index toward 0 if cargo &gt; target, one index toward 10 if cargo &lt; target, clamped 0-10. Same rule as the Capital-world test.</summary>
    private static void SelfManageIssp(Planet w, int target)
    {
        void Step(Func<int> get, Action<int> set, int cargo)
        {
            var current = get();
            if (cargo > target && current > 0)
                set(current - 1);
            else if (cargo < target && current < 10)
                set(current + 1);
        }

        Step(() => w.SelfSufficiency.Chemical, v => w.SelfSufficiency.Chemical = v, w.Cargo.Chemicals);
        Step(() => w.SelfSufficiency.Metal, v => w.SelfSufficiency.Metal = v, w.Cargo.Metals);
        Step(() => w.SelfSufficiency.Trillum, v => w.SelfSufficiency.Trillum = v, w.Cargo.Trillum);
    }

    /// <summary>
    /// The decisive check: does this young world's main industry EVER get genuinely
    /// shortage-throttled across the full run, unlike the mature Capital (which never did across 70
    /// years)? Uses ProductionDiagnostics.IndustryGrowthLostToMetalsShortage as an outside observer
    /// for verification -- the SelfManageIssp policy itself still only ever reacts to Cargo stock.
    /// </summary>
    private static void RunSelfManagedWithShortageTrace()
    {
        var random = new Random(1);
        var w = SeedWorld(WorldType.Independent, WorldClass.EarthLike, Population, random);
        var handler = new AnnualTickHandler(random);
        var reported = new HashSet<CargoType>();

        ProductionDiagnostics.FilterEmpire = null;
        var anyShortageAnyIndustry = false;
        var totalSygLost = 0L;
        var totalAnyLost = 0L;

        for (var year = 0; year < Years; year++) {
            SelfManageIssp(w, Target);

            ProductionDiagnostics.Reset();
            ProductionDiagnostics.Enabled = true;
            reported.Clear();
            handler.RunProductionPipeline(w, reported);
            ProductionDiagnostics.Enabled = false;

            var lost = ProductionDiagnostics.IndustryGrowthLostToMetalsShortage;
            if (lost.Count > 0) {
                anyShortageAnyIndustry = true;
                totalSygLost += lost.GetValueOrDefault(IndustryType.ShipyardGeneral);
                totalAnyLost += lost.Values.Sum();
                Console.WriteLine($"Year {year}: shortage-throttled industries this tick: " +
                    string.Join(", ", lost.Select(kv => $"{kv.Key}={kv.Value}")) +
                    $" | ISSP(Che/Met/Tri)={w.SelfSufficiency.Chemical}/{w.SelfSufficiency.Metal}/{w.SelfSufficiency.Trillum}" +
                    $" | Cargo(Met/Che/Tri)={w.Cargo.Metals}/{w.Cargo.Chemicals}/{w.Cargo.Trillum}");
            }
        }

        Console.WriteLine($"--- Test 2 shortage verdict ---");
        Console.WriteLine($"Any industry ever shortage-throttled across {Years} years: {anyShortageAnyIndustry}");
        Console.WriteLine($"Total ShipyardGeneral growth lost to metals shortage: {totalSygLost}");
        Console.WriteLine($"Total (any industry) growth lost to metals shortage: {totalAnyLost}");

        // Re-run identically for the ships/year steady-state figure (same pattern as the Capital test).
        var random2 = new Random(1);
        var w2 = SeedWorld(WorldType.Independent, WorldClass.EarthLike, Population, random2);
        var handler2 = new AnnualTickHandler(random2);
        var reported2 = new HashSet<CargoType>();
        for (var year = 0; year < Years; year++) {
            SelfManageIssp(w2, Target);
            reported2.Clear();
            handler2.RunProductionPipeline(w2, reported2);
        }
        ProductionDiagnostics.FilterEmpire = null;
        ProductionDiagnostics.Reset();
        ProductionDiagnostics.Enabled = true;
        for (var year = 0; year < SampleYears; year++) {
            SelfManageIssp(w2, Target);
            reported2.Clear();
            handler2.RunProductionPipeline(w2, reported2);
        }
        ProductionDiagnostics.Enabled = false;
        PrintResult("Test 2: Per-turn self-managed ISSP (step toward target=500 each year)", w2);
    }

    private static void RunSelfManagedPairedLogistics()
    {
        var random = new Random(1);
        var c = SeedWorld(WorldType.Independent, WorldClass.EarthLike, Population, random);
        var r = SeedWorld(WorldType.RawMaterialMine, WorldClass.EarthLike, RWorldPopulation, random);
        var handler = new AnnualTickHandler(random);
        var reportedC = new HashSet<CargoType>();
        var reportedR = new HashSet<CargoType>();

        void RunYearAndTransfer()
        {
            SelfManageIssp(c, Target);

            reportedC.Clear();
            handler.RunProductionPipeline(c, reportedC);

            var before = (Che: r.Cargo.Chemicals, Met: r.Cargo.Metals, Tri: r.Cargo.Trillum);
            reportedR.Clear();
            handler.RunProductionPipeline(r, reportedR);
            var produced = (
                Che: r.Cargo.Chemicals - before.Che,
                Met: r.Cargo.Metals - before.Met,
                Tri: r.Cargo.Trillum - before.Tri);

            void Transfer(int producedThisYear, Func<int> getR, Action<int> setR, Func<int> getC, Action<int> setC)
            {
                if (producedThisYear <= RSourceTarget)
                    return;
                var excess = producedThisYear - RSourceTarget;
                setR(getR() - excess);
                setC(getC() + excess);
            }

            Transfer(produced.Che, () => r.Cargo.Chemicals, v => r.Cargo.Chemicals = v, () => c.Cargo.Chemicals, v => c.Cargo.Chemicals = v);
            Transfer(produced.Met, () => r.Cargo.Metals, v => r.Cargo.Metals = v, () => c.Cargo.Metals, v => c.Cargo.Metals = v);
            Transfer(produced.Tri, () => r.Cargo.Trillum, v => r.Cargo.Trillum = v, () => c.Cargo.Trillum, v => c.Cargo.Trillum = v);
        }

        for (var year = 0; year < Years; year++)
            RunYearAndTransfer();

        ProductionDiagnostics.FilterEmpire = null;
        ProductionDiagnostics.Reset();
        ProductionDiagnostics.Enabled = true;
        for (var year = 0; year < SampleYears; year++)
            RunYearAndTransfer();
        ProductionDiagnostics.Enabled = false;

        PrintResult("Test 3: Self-managed ISSP + paired perfect logistics (r-world pop=1000, held at 250)", c);
        Console.WriteLine($"  r-world final cargo: Metals={r.Cargo.Metals}, Chemicals={r.Cargo.Chemicals}, Trillum={r.Cargo.Trillum}");
    }
}
