using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;
using Reconstructed4021.LegacyNpe;

namespace Reconstructed4021.Tests;

/// <summary>
/// Plays a real scenario to resolution with every empire driven by the same ported NPE AI
/// (<see cref="KingdomTurnHandler"/>), to get a real "how many years does this scenario actually take
/// to resolve" number instead of a suggested-length guess from the .SCN file's own (unenforced)
/// MinLen/MaxLen. Parameterized by persona (Kingdom1 = passive/neutral, Kingdom2 = aggressive/Harass —
/// see the constructor's own doc comment) since an all-Kingdom1 galaxy turned out to essentially never
/// resolve (every empire has a near-zero <c>OffensiveGene</c>), which says something real about that
/// persona but nothing about whether the harness itself drives real combat — Kingdom2 is the control
/// for that. A game is over when at most one empire's <see cref="Types.EmpireStatus"/> is not
/// <see cref="EmpireStatus.Eliminated"/>, checked once per <see cref="TurnEngine.AdvanceOneTurn"/> — a
/// full round can leave a defeated empire at <see cref="EmpireStatus.PendingElimination"/> for up to
/// one more empire-turn before <see cref="TurnEngine.BeginTurn"/> finalizes it, so "resolved" here can
/// lag the real elimination by a turn or two; acceptable slop for this exploratory harness. Also tracks
/// every individual elimination (not just the game-ending one) and start/end aggregate state
/// (planets/population/ships/industry per empire, plus galaxy-wide independent-planet count) so a
/// run that never fully resolves can still show whether the galaxy is actually active or frozen.
/// <c>[Explicit]</c> keeps this out of the default <c>dotnet test</c> run (each case is a real,
/// possibly-hundreds-of-turns playout, not a unit test) — the <c>dotnet test -- --treenode-filter</c>
/// form silently discovers zero tests against this repo's SDK/TUnit version; run the built test DLL
/// directly instead, e.g. from <c>src/Reconstructed4021.Tests</c>:
/// <c>dotnet bin/Release/net10.0/Reconstructed4021.Tests.dll --treenode-filter "/*/*/ScenarioPlayoutTests/*"</c>
/// (build with <c>dotnet build -c Release</c> first). Per-seed results print via <c>Console.WriteLine</c>,
/// which this harness's test-platform version doesn't surface on the console or in the HTML report —
/// read them back from the TRX report instead: add <c>--report-trx --report-trx-filename out.trx</c> to
/// the run above, then grep <c>out.trx</c> for "seed ".
///
/// Every empire, including scenario-declared player slots (which <see cref="ScenarioLoader"/> assigns
/// a <c>HumanTurnHandler</c> to) and any NPE type other than Kingdom1/2, gets its
/// <see cref="Game.TurnHandlers"/> entry overwritten with a fresh handler of the requested persona after
/// load — this deliberately ignores the scenario's own declared personalities/player slots to get a
/// uniform baseline. Note this only replaces the turn handler, not whatever else distinguishes a
/// player slot from an NPE slot internally (e.g. <see cref="EmpireStatus.PendingElimination"/> is only
/// ever assigned to a human-declared empire per that enum's own doc comment) — a player-slot empire
/// here can still show up as PendingElimination rather than going straight to Eliminated like a real
/// NPE would. <see cref="KingdomTurnHandler"/>'s constructor calls <c>NpeToolkit.SetEmpireDefenses</c>,
/// which already ran once for a real NPE slot during <see cref="ScenarioLoader.Load"/> — an overwritten
/// player or non-Kingdom NPE slot ends up with its starting defenses rolled twice against the same RNG
/// stream. Harmless for "does this resolve and how fast" but means starting defense counts here aren't
/// a faithful single scenario-load draw; flag before using this harness for anything that cares about
/// exact starting state.
/// </summary>
public class ScenarioPlayoutTests
{
    private const int YearCap = 500;
    private const int SeedCount = 10;

    public sealed record EmpireSnapshot(string Name, int Planets, int Population, int Ships, int Industry);
    public sealed record EliminationEvent(int Year, string EmpireName);

    public sealed record PlayoutResult(
        int Seed, int YearsElapsed, int TurnCalls, string Outcome, int EmpireCountAtStart,
        List<EliminationEvent> Eliminations,
        List<EmpireSnapshot> StartSnapshots, List<EmpireSnapshot> EndSnapshots,
        int IndependentPlanetsStart, int IndependentPlanetsEnd);

    [Test, Explicit]
    [Arguments("INTRO.SCN", 3, NpeEmpireType.Kingdom1)]
    [Arguments("INTRO.SCN", 3, NpeEmpireType.Kingdom2)]
    [Arguments("EASTWEST.SCN", 2, NpeEmpireType.Kingdom1)]
    [Arguments("EASTWEST.SCN", 2, NpeEmpireType.Kingdom2)]
    public async Task AllKingdomPlayout(string fileName, int playerCount, NpeEmpireType persona)
    {
        var path = Path.Combine(PascalGroundTruth.PascalHarness.RepoRoot, "reference", "scenarios", "dos_131", fileName);
        var text = File.ReadAllText(path);

        var results = new List<PlayoutResult>();
        for (var seed = 0; seed < SeedCount; seed++) {
            results.Add(RunOnePlayout(text, playerCount, persona, seed));
        }

        Console.WriteLine($"{fileName} ({playerCount} players, persona {persona}), {SeedCount} seeds, year cap {YearCap}, empires at start: {results[0].EmpireCountAtStart}:");
        foreach (var r in results) {
            Console.WriteLine($"  seed {r.Seed}: {r.Outcome} at year {r.YearsElapsed} ({r.TurnCalls} turn calls), eliminations: {(r.Eliminations.Count == 0 ? "none" : string.Join(", ", r.Eliminations.Select(e => $"{e.EmpireName}@y{e.Year}")))}");
            Console.WriteLine($"    indep planets {r.IndependentPlanetsStart}->{r.IndependentPlanetsEnd}");
            foreach (var s in r.StartSnapshots) {
                var e = r.EndSnapshots.FirstOrDefault(x => x.Name == s.Name);
                Console.WriteLine($"    {s.Name}: planets {s.Planets}->{e?.Planets.ToString() ?? "gone"}, pop {s.Population}->{e?.Population.ToString() ?? "gone"}, ships {s.Ships}->{e?.Ships.ToString() ?? "gone"}, industry {s.Industry}->{e?.Industry.ToString() ?? "gone"}");
            }
        }

        var resolved = results.Where(r => r.Outcome != "unresolved").ToList();
        Console.WriteLine(resolved.Count > 0
            ? $"  resolved {resolved.Count}/{SeedCount}, years-to-resolution: min {resolved.Min(r => r.YearsElapsed)}, max {resolved.Max(r => r.YearsElapsed)}, mean {resolved.Average(r => r.YearsElapsed):0.#}"
            : "  none resolved within the year cap");
        var anyElimination = results.Count(r => r.Eliminations.Count > 0);
        Console.WriteLine($"  runs with >=1 elimination (of any kind): {anyElimination}/{SeedCount}");

        await Task.CompletedTask;
    }

    private static PlayoutResult RunOnePlayout(string scenarioText, int playerCount, NpeEmpireType persona, int seed)
    {
        var random = new Random(seed);
        var setup = new GalaxySetup(random);
        var loader = new ScenarioLoader(setup, random);
        var players = Enumerable.Range(1, playerCount)
            .Select(i => new ScenarioLoader.PlayerInfo($"p{i}", null, IsEmpress: false))
            .ToArray();

        var game = loader.Load(scenarioText, players);
        var startYear = game.Year;
        var empireCountAtStart = game.Empires.Count;
        var startSnapshots = Snapshot(game);
        var independentPlanetsStart = IndependentPlanetCount(game);

        foreach (var empire in game.Empires) {
            game.TurnHandlers[empire] = new KingdomTurnHandler(empire, persona, random);
        }

        var turnEngine = new TurnEngine(new VisibilityHandler(random), new FleetMovementHandler(random), new AnnualTickHandler(random));

        var eliminations = new List<EliminationEvent>();
        var priorStatus = game.Empires.ToDictionary(e => e, e => e.Status);

        var maxCalls = YearCap * Math.Max(1, game.Empires.Count) * 2;
        for (var call = 0; call < maxCalls; call++) {
            turnEngine.AdvanceOneTurn(game);

            foreach (var empire in game.Empires) {
                if (priorStatus[empire] != EmpireStatus.Eliminated && empire.Status == EmpireStatus.Eliminated) {
                    eliminations.Add(new EliminationEvent(game.Year - startYear, empire.Name));
                }
                priorStatus[empire] = empire.Status;
            }

            var alive = game.Empires.Count(e => e.Status != EmpireStatus.Eliminated);
            if (alive <= 1) {
                var outcome = alive == 1 ? "resolved" : "mutual elimination";
                return new PlayoutResult(seed, game.Year - startYear, call + 1, outcome, empireCountAtStart,
                    eliminations, startSnapshots, Snapshot(game), independentPlanetsStart, IndependentPlanetCount(game));
            }

            if (game.Year - startYear >= YearCap) {
                return new PlayoutResult(seed, game.Year - startYear, call + 1, "unresolved", empireCountAtStart,
                    eliminations, startSnapshots, Snapshot(game), independentPlanetsStart, IndependentPlanetCount(game));
            }
        }

        return new PlayoutResult(seed, game.Year - startYear, maxCalls, "unresolved (call budget)", empireCountAtStart,
            eliminations, startSnapshots, Snapshot(game), independentPlanetsStart, IndependentPlanetCount(game));
    }

    private static int TotalShips(IShipCargoHolder holder) => Enum.GetValues<ShipType>().Sum(t => holder.Ships[t]);

    private static int TotalIndustry(IEconomicWorld world) => Enum.GetValues<IndustryType>().Sum(t => world.Industry[t]);

    private static int IndependentPlanetCount(Game game) => game.Galaxy.Planets.Count(p => p.Owner == Empire.Independent);

    private static List<EmpireSnapshot> Snapshot(Game game) =>
        game.Empires.Select(empire => {
            var planets = game.Galaxy.Planets.Where(p => p.Owner == empire).ToList();
            var starbases = game.Galaxy.Starbases.Where(s => s.Owner == empire).ToList();
            var fleets = game.Galaxy.Fleets.Where(f => f.Owner == empire).ToList();

            var ships = planets.Sum(TotalShips) + starbases.Sum(TotalShips) + fleets.Sum(TotalShips);
            var industry = planets.Sum(TotalIndustry) + starbases.Sum(TotalIndustry);
            var population = planets.Sum(p => p.Population);

            return new EmpireSnapshot(empire.Name, planets.Count, population, ships, industry);
        }).ToList();
}
