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
        Dictionary<ShipType, long> GrossShips);

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

        await Task.CompletedTask;
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
            new Dictionary<ShipType, long>(ProductionDiagnostics.GrossShipsProduced));
    }
}
