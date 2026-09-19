using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// Throwaway comparison: does per-resource, per-turn ISSP self-management (test 2) or perfect,
/// frictionless paired-world logistics (test 3) do more to close the gap between a Capital world's
/// default-ISSP baseline (test 1) and its material-unconstrained potential? Each world is seeded via
/// GetOptimumIndustry (never a bare zero-start Planet -- see ZZZIsspShipTraceTest's own doc comment
/// for why that traps production permanently at zero).
/// </summary>
public class ZZZIsspSelfManagementVsLogisticsTest
{
    private const int Target = 5000;
    private const int Years = 60;
    private const int SampleYears = 5;

    /// <summary>
    /// The r-world's own held-inventory target for test 4, deliberately lower than the C-world's
    /// Target=5000: a well-tuned logistics setup keeps the SOURCE world's buffer low (export
    /// aggressively) rather than let it accumulate. This also fixes a real measurement distortion --
    /// at Target=5000 the r-world's own cargo was pegging out at the 9999 cap, which shrinks the
    /// before/after production delta toward zero as its stock plateaus, so the C-world was receiving
    /// far less than the "perfect logistics" premise intends. A lower, achievable target keeps the
    /// r-world's stock oscillating well below the cap, so the same delta-based transfer actually
    /// tracks true annual production instead of being truncated by an already-full hold.
    /// </summary>
    private const int RSourceTarget = 2500;

    [Test, Explicit]
    public async Task CompareSelfManagementAndLogistics()
    {
        RunBaseline();
        RunSelfManaged();
        RunPairedLogistics();
        RunSelfManagedPairedLogistics();
        await Task.CompletedTask;
    }

    /// <summary>
    /// Settles whether self-managed ISSP alone ever leaves ShipyardGeneral (or its raw-material
    /// feeders) genuinely shortage-throttled at any point across the full run -- not just the
    /// steady-state tail -- using ProductionDiagnostics.IndustryGrowthLostToMetalsShortage as an
    /// outside observer (this reads the game's own internal metrics for OUR verification; the
    /// SelfManageIssp policy itself still only ever reacts to Cargo stock levels, exactly as a real
    /// controller would). If this is zero everywhere, no amount of imported material could have
    /// increased ship output here, by construction of UpdateIndustry's own rate-cap formula.
    /// </summary>
    [Test, Explicit]
    public async Task TraceSelfManagedShortage()
    {
        var random = new Random(1);
        var c = SeedWorld(WorldType.Capital, WorldClass.EarthLike, 4055, random);
        var handler = new AnnualTickHandler(random);
        var reported = new HashSet<CargoType>();

        ProductionDiagnostics.FilterEmpire = null;
        var anyShortageAnyIndustry = false;
        var totalSygLost = 0L;

        for (var year = 0; year < 70; year++) {
            SelfManageIssp(c);

            ProductionDiagnostics.Reset();
            ProductionDiagnostics.Enabled = true;
            reported.Clear();
            handler.RunProductionPipeline(c, reported);
            ProductionDiagnostics.Enabled = false;

            var lost = ProductionDiagnostics.IndustryGrowthLostToMetalsShortage;
            if (lost.Count > 0) {
                anyShortageAnyIndustry = true;
                totalSygLost += lost.GetValueOrDefault(IndustryType.ShipyardGeneral);
                Console.WriteLine($"Year {year}: shortage-throttled industries this tick: " +
                    string.Join(", ", lost.Select(kv => $"{kv.Key}={kv.Value}")) +
                    $" | ISSP(Che/Met/Tri)={c.SelfSufficiency.Chemical}/{c.SelfSufficiency.Metal}/{c.SelfSufficiency.Trillum}" +
                    $" | Cargo(Met/Che/Tri)={c.Cargo.Metals}/{c.Cargo.Chemicals}/{c.Cargo.Trillum}");
            }
        }

        Console.WriteLine($"--- Verdict ---");
        Console.WriteLine($"Any industry ever shortage-throttled across 70 years: {anyShortageAnyIndustry}");
        Console.WriteLine($"Total ShipyardGeneral growth lost to metals shortage across 70 years: {totalSygLost}");

        await Task.CompletedTask;
    }

    [Test, Explicit]
    public async Task TraceSelfManagedIssp()
    {
        var random = new Random(1);
        var c = SeedWorld(WorldType.Capital, WorldClass.EarthLike, 4055, random);
        var handler = new AnnualTickHandler(random);
        var reported = new HashSet<CargoType>();
        int[] checkpoints = [0, 1, 2, 3, 5, 10, 20, 40, 59];

        for (var year = 0; year < Years; year++) {
            SelfManageIssp(c);
            var dist = AnnualTickHandler.GetIndustrialDistribution(c);
            if (checkpoints.Contains(year)) {
                Console.WriteLine($"Year {year}: ISSP(Che/Met/Tri)={c.SelfSufficiency.Chemical}/{c.SelfSufficiency.Metal}/{c.SelfSufficiency.Trillum} " +
                    $"dist(SYG/Che/Min/Tri)={dist[IndustryType.ShipyardGeneral]:F1}/{dist[IndustryType.Chemical]:F1}/{dist[IndustryType.Mining]:F1}/{dist[IndustryType.TrillumMining]:F1} " +
                    $"Industry(SYG/Che/Min)={c.Industry[IndustryType.ShipyardGeneral]}/{c.Industry[IndustryType.Chemical]}/{c.Industry[IndustryType.Mining]} " +
                    $"Cargo(Met/Che/Tri)={c.Cargo.Metals}/{c.Cargo.Chemicals}/{c.Cargo.Trillum}");
            }
            reported.Clear();
            handler.RunProductionPipeline(c, reported);
        }

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
        planet.Cargo.Metals = 1000;
        planet.Cargo.Chemicals = 1000;
        planet.Cargo.Trillum = 1000;
        planet.Cargo.Supplies = 1000;

        return planet;
    }

    private static int TotalShips(int sampleYears) =>
        (int)Math.Round(Enum.GetValues<ShipType>().Sum(s => ProductionDiagnostics.GrossShipsProduced.GetValueOrDefault(s)) / (double)sampleYears);

    private static void PrintResult(string label, Planet c)
    {
        Console.WriteLine($"--- {label} ---");
        Console.WriteLine($"ISSP now: Chemical={c.SelfSufficiency.Chemical}, Metal={c.SelfSufficiency.Metal}, Trillum={c.SelfSufficiency.Trillum}");
        Console.WriteLine($"Ships/year (avg of last {SampleYears}): {TotalShips(SampleYears)}");
        Console.WriteLine($"Cargo: Metals={c.Cargo.Metals}, Chemicals={c.Cargo.Chemicals}, Trillum={c.Cargo.Trillum}, Supplies={c.Cargo.Supplies}");
    }

    private static void RunBaseline()
    {
        var random = new Random(1);
        var c = SeedWorld(WorldType.Capital, WorldClass.EarthLike, 4055, random);
        var handler = new AnnualTickHandler(random);
        var reported = new HashSet<CargoType>();

        for (var year = 0; year < Years; year++) {
            reported.Clear();
            handler.RunProductionPipeline(c, reported);
        }

        ProductionDiagnostics.FilterEmpire = null;
        ProductionDiagnostics.Reset();
        ProductionDiagnostics.Enabled = true;
        for (var year = 0; year < SampleYears; year++) {
            reported.Clear();
            handler.RunProductionPipeline(c, reported);
        }
        ProductionDiagnostics.Enabled = false;

        PrintResult("Test 1: Baseline (default ISSP=5 throughout, no intervention)", c);
    }

    /// <summary>Steps each of Chemical/Metal/Trillum ISSP one index toward 0 if cargo &gt; target, one index toward 10 if cargo &lt; target, clamped 0-10.</summary>
    private static void SelfManageIssp(Planet c)
    {
        void Step(Func<int> get, Action<int> set, int cargo)
        {
            var current = get();
            if (cargo > Target && current > 0)
                set(current - 1);
            else if (cargo < Target && current < 10)
                set(current + 1);
        }

        Step(() => c.SelfSufficiency.Chemical, v => c.SelfSufficiency.Chemical = v, c.Cargo.Chemicals);
        Step(() => c.SelfSufficiency.Metal, v => c.SelfSufficiency.Metal = v, c.Cargo.Metals);
        Step(() => c.SelfSufficiency.Trillum, v => c.SelfSufficiency.Trillum = v, c.Cargo.Trillum);
    }

    private static void RunSelfManaged()
    {
        var random = new Random(1);
        var c = SeedWorld(WorldType.Capital, WorldClass.EarthLike, 4055, random);
        var handler = new AnnualTickHandler(random);
        var reported = new HashSet<CargoType>();

        for (var year = 0; year < Years; year++) {
            SelfManageIssp(c);
            reported.Clear();
            handler.RunProductionPipeline(c, reported);
        }

        ProductionDiagnostics.FilterEmpire = null;
        ProductionDiagnostics.Reset();
        ProductionDiagnostics.Enabled = true;
        for (var year = 0; year < SampleYears; year++) {
            SelfManageIssp(c);
            reported.Clear();
            handler.RunProductionPipeline(c, reported);
        }
        ProductionDiagnostics.Enabled = false;

        PrintResult("Test 2: Per-turn self-managed ISSP (step toward target=5000 each year)", c);
    }

    private static void RunPairedLogistics()
    {
        var random = new Random(1);
        var c = SeedWorld(WorldType.Capital, WorldClass.EarthLike, 4055, random);
        var r = SeedWorld(WorldType.RawMaterialMine, WorldClass.EarthLike, 4055, random);
        var handler = new AnnualTickHandler(random);
        var reportedC = new HashSet<CargoType>();
        var reportedR = new HashSet<CargoType>();

        void RunYearAndTransfer()
        {
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
                if (producedThisYear <= Target)
                    return;
                var excess = producedThisYear - Target;
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

        PrintResult("Test 3: Paired perfect logistics (C-world ISSP stays at default=5, r-world exports annual surplus over 5000)", c);
        Console.WriteLine($"  r-world final cargo: Metals={r.Cargo.Metals}, Chemicals={r.Cargo.Chemicals}, Trillum={r.Cargo.Trillum}");
    }

    /// <summary>
    /// Fixes test 3's real flaw: holding the C-world's ISSP fixed at default meant it never reduced
    /// its own redundant local production in response to the r-world's import, so the import just hit
    /// an already-full cargo hold and got clamped away -- not a real test of whether logistics can
    /// help. Here the C-world runs the same per-resource SelfManageIssp step as test 2, called at the
    /// top of each year's loop -- since the prior year's transfer already landed in c.Cargo by then,
    /// this year's step reacts to a stock that already reflects the import, exactly the "sees its
    /// external supply and dials down local production accordingly" behavior the user asked for.
    /// </summary>
    /// <summary>
    /// 1,155 population, not an arbitrary guess: it's the actual population of a real "Base"-type
    /// planet in a real player save inspected earlier this session (saves/deleted/Ovaris-4021.json) --
    /// a reasonable stand-in for a world conquered or redesignated partway through a game, distinctly
    /// less mature than the 4,055-population C-world that's been growing since turn 1.
    /// </summary>
    private const int RWorldPopulation = 1155;

    private static void RunSelfManagedPairedLogistics()
    {
        var random = new Random(1);
        var c = SeedWorld(WorldType.Capital, WorldClass.EarthLike, 4055, random);
        var r = SeedWorld(WorldType.RawMaterialMine, WorldClass.EarthLike, RWorldPopulation, random);
        var handler = new AnnualTickHandler(random);
        var reportedC = new HashSet<CargoType>();
        var reportedR = new HashSet<CargoType>();

        void RunYearAndTransfer()
        {
            SelfManageIssp(c);

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

        PrintResult("Test 4: Self-managed ISSP + paired perfect logistics (C-world dials down as imports arrive, r-world held at 2500)", c);
        Console.WriteLine($"  r-world final cargo: Metals={r.Cargo.Metals}, Chemicals={r.Cargo.Chemicals}, Trillum={r.Cargo.Trillum}");
    }
}
