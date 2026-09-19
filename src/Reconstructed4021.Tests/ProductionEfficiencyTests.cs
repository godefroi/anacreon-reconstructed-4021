using Reconstructed4021.Core;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;
using Reconstructed4021.LegacyNpe;

namespace Reconstructed4021.Tests;

/// <summary>
/// Quantifies how much production the real, unmodified <see cref="KingdomTurnHandler"/> AI actually
/// loses to (1) storage-cap overflow -- cargo produced mid-tick above
/// <see cref="PascalMath.MaxResources"/>, clamped away at the end of
/// <see cref="AnnualTickHandler.RunProductionPipeline"/> -- and (2) raw-material shortage throttling,
/// both in <c>UpdateIndustry</c>'s metals check (which slows industry's own growth) and in
/// <c>ApplyRawMaterialConstraint</c> (which directly cuts ship/cargo output this tick). A magnitude
/// question, not a fitness-correlation one -- a handful of seeds is enough, no GA-scale rigor needed.
/// <c>[Explicit]</c>, same reasoning as <see cref="ScenarioPlayoutTests"/>: a real multi-hundred-turn
/// playout, not a unit test. Run via the built DLL directly (`dotnet build -c Release` then
/// `dotnet bin/Release/net10.0/Reconstructed4021.Tests.dll --treenode-filter "/*/*/ProductionEfficiencyTests/*" --report-trx --report-trx-filename out.trx`),
/// reading <c>Console.WriteLine</c> output back from the TRX report, same as every other Explicit
/// harness this session.
/// </summary>
public class ProductionEfficiencyTests
{
    private const int Horizon = 120;
    private const int SeedCount = 5;

    public sealed record SeedTotals(
        Dictionary<CargoType, long> Overflow,
        Dictionary<IndustryType, long> IndustryGrowthLost,
        Dictionary<ShipType, long> ShipsLost,
        Dictionary<CargoType, long> CargoLost,
        Dictionary<CargoType, long> GrossCargo,
        Dictionary<ShipType, long> GrossShips,
        Dictionary<(WorldType, CargoType), long> OverflowByWorldType,
        Dictionary<WorldType, long> ShortageByWorldType,
        Dictionary<int, long> ShortageByEfficiencyBucket,
        Dictionary<WorldType, (long EfficiencySum, long PopulationSum, int Count)> OverflowWorldStats,
        Dictionary<WorldType, (long EfficiencySum, long PopulationSum, int Count)> ShortageWorldStats);

    [Test, Explicit]
    public async Task IntroKingdom1_ProductionLossTotals()
    {
        var perSeed = new List<SeedTotals>();
        for (var seed = 0; seed < SeedCount; seed++) {
            perSeed.Add(RunOneSeed(seed));
        }

        void Report<TKey>(string label, IEnumerable<Dictionary<TKey, long>> perSeedDicts) where TKey : notnull
        {
            var totals = new Dictionary<TKey, long>();
            foreach (var d in perSeedDicts)
                foreach (var (k, v) in d)
                    totals[k] = totals.GetValueOrDefault(k) + v;

            Console.WriteLine($"{label}:");
            foreach (var (k, v) in totals.OrderByDescending(kv => kv.Value))
                Console.WriteLine($"  {k}: {v}");
            Console.WriteLine($"  TOTAL: {totals.Values.Sum()}");
        }

        Console.WriteLine($"INTRO.SCN, Kingdom1, {SeedCount} seeds, {Horizon}-year horizon:");
        Report("Overflow lost (cargo produced above the 9999 storage cap, clamped away)", perSeed.Select(s => s.Overflow));
        Report("Gross cargo produced (raw materials + cargo products, before any loss)", perSeed.Select(s => s.GrossCargo));
        Report("Industry growth lost to metals shortage (UpdateIndustry)", perSeed.Select(s => s.IndustryGrowthLost));
        Report("Ships lost to raw-material shortage (ApplyRawMaterialConstraint)", perSeed.Select(s => s.ShipsLost));
        Report("Gross ships produced (before any shortage loss)", perSeed.Select(s => s.GrossShips));
        Report("Cargo products lost to raw-material shortage (legions/ninjas/ambrosia)", perSeed.Select(s => s.CargoLost));

        Console.WriteLine();
        Console.WriteLine("-- World-type / efficiency breakdown --");

        var overflowByWorldType = Merge(perSeed.Select(s => s.OverflowByWorldType));
        Console.WriteLine("Overflow lost by (WorldType, CargoType):");
        foreach (var (k, v) in overflowByWorldType.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"  {k.Item1} / {k.Item2}: {v}");

        var overflowStats = MergeStats(perSeed.Select(s => s.OverflowWorldStats));
        Console.WriteLine("Overflow world stats (avg efficiency, avg population, event count) by WorldType:");
        foreach (var (worldType, stats) in overflowStats.OrderByDescending(kv => kv.Value.Count))
            Console.WriteLine($"  {worldType}: avgEff={stats.EfficiencySum / (double)stats.Count:0.0} avgPop={stats.PopulationSum / (double)stats.Count:0.0} count={stats.Count}");

        var shortageByWorldType = Merge(perSeed.Select(s => s.ShortageByWorldType));
        Console.WriteLine("Industry growth lost to metals shortage by WorldType:");
        foreach (var (k, v) in shortageByWorldType.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"  {k}: {v}");

        var shortageStats = MergeStats(perSeed.Select(s => s.ShortageWorldStats));
        Console.WriteLine("Shortage world stats (avg efficiency, avg population, event count) by WorldType:");
        foreach (var (worldType, stats) in shortageStats.OrderByDescending(kv => kv.Value.Count))
            Console.WriteLine($"  {worldType}: avgEff={stats.EfficiencySum / (double)stats.Count:0.0} avgPop={stats.PopulationSum / (double)stats.Count:0.0} count={stats.Count}");

        var shortageByEfficiencyBucket = Merge(perSeed.Select(s => s.ShortageByEfficiencyBucket));
        Console.WriteLine("Industry growth lost to metals shortage by efficiency bucket (rounded down to nearest 10):");
        foreach (var (bucket, v) in shortageByEfficiencyBucket.OrderBy(kv => kv.Key))
            Console.WriteLine($"  {bucket}-{bucket + 9}: {v}");

        await Task.CompletedTask;
    }

    private static Dictionary<TKey, long> Merge<TKey>(IEnumerable<Dictionary<TKey, long>> perSeedDicts) where TKey : notnull
    {
        var totals = new Dictionary<TKey, long>();
        foreach (var d in perSeedDicts)
            foreach (var (k, v) in d)
                totals[k] = totals.GetValueOrDefault(k) + v;
        return totals;
    }

    private static Dictionary<TKey, (long EfficiencySum, long PopulationSum, int Count)> MergeStats<TKey>(
        IEnumerable<Dictionary<TKey, (long EfficiencySum, long PopulationSum, int Count)>> perSeedDicts) where TKey : notnull
    {
        var totals = new Dictionary<TKey, (long, long, int)>();
        foreach (var d in perSeedDicts) {
            foreach (var (k, v) in d) {
                var existing = totals.GetValueOrDefault(k);
                totals[k] = (existing.Item1 + v.EfficiencySum, existing.Item2 + v.PopulationSum, existing.Item3 + v.Count);
            }
        }
        return totals;
    }

    private static SeedTotals RunOneSeed(int seed)
    {
        var random = new Random(seed);
        var setup = new GalaxySetup(random);
        var loader = new ScenarioLoader(setup, random);
        var path = Path.Combine(PascalGroundTruth.PascalHarness.RepoRoot, "reference", "scenarios", "dos_131", "INTRO.SCN");
        var text = File.ReadAllText(path);
        var players = Enumerable.Range(1, 3)
            .Select(i => new ScenarioLoader.PlayerInfo($"p{i}", null, IsEmpress: false))
            .ToArray();

        var game = loader.Load(text, players);
        var startYear = game.Year;

        foreach (var empire in game.Empires) {
            game.TurnHandlers[empire] = new KingdomTurnHandler(empire, NpeEmpireType.Kingdom1, random);
        }

        var turnEngine = new TurnEngine(new VisibilityHandler(random), new FleetMovementHandler(random), new AnnualTickHandler(random));

        ProductionDiagnostics.Reset();
        ProductionDiagnostics.Enabled = true;

        var maxCalls = Horizon * Math.Max(1, game.Empires.Count) * 2;
        for (var call = 0; call < maxCalls && game.Year - startYear < Horizon; call++) {
            turnEngine.AdvanceOneTurn(game);
        }

        ProductionDiagnostics.Enabled = false;

        return new SeedTotals(
            new Dictionary<CargoType, long>(ProductionDiagnostics.OverflowLost),
            new Dictionary<IndustryType, long>(ProductionDiagnostics.IndustryGrowthLostToMetalsShortage),
            new Dictionary<ShipType, long>(ProductionDiagnostics.ShipsLostToShortage),
            new Dictionary<CargoType, long>(ProductionDiagnostics.CargoLostToShortage),
            new Dictionary<CargoType, long>(ProductionDiagnostics.GrossCargoProduced),
            new Dictionary<ShipType, long>(ProductionDiagnostics.GrossShipsProduced),
            new Dictionary<(WorldType, CargoType), long>(ProductionDiagnostics.OverflowByWorldType),
            new Dictionary<WorldType, long>(ProductionDiagnostics.IndustryGrowthLostByWorldType),
            new Dictionary<int, long>(ProductionDiagnostics.IndustryGrowthLostByEfficiencyBucket),
            new Dictionary<WorldType, (long, long, int)>(ProductionDiagnostics.OverflowWorldStats),
            new Dictionary<WorldType, (long, long, int)>(ProductionDiagnostics.ShortageWorldStats));
    }
}
