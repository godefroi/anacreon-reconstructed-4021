using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;
using Reconstructed4021.LegacyNpe;

namespace Reconstructed4021.Tests;

/// <summary>
/// Second pass of a genetic algorithm evolving Kingdom's persona genes (<see cref="NpeCharacter"/>)
/// against a fixed baseline Kingdom1 opponent, reusing <see cref="ScenarioPlayoutTests"/>'s
/// load/loop/elimination-check pattern. Scaled up from the first pass (population 16/generations
/// 6/seeds 3, 7.7s total) once that pass showed the per-playout cost was cheap enough to afford more,
/// and extended with explicit elimination outcome logging -- the first pass only inferred eliminations
/// from the fitness math (the bonus/penalty term dominating the score), never confirmed directly.
/// <c>[Explicit]</c> for the same reason as <see cref="ScenarioPlayoutTests"/> -- run the built DLL
/// directly, not <c>dotnet test -- --treenode-filter</c> (discovers zero tests against this repo's
/// SDK/TUnit version). From src/Reconstructed4021.Tests: <c>dotnet build -c Release</c>, then
/// <c>dotnet bin/Release/net10.0/Reconstructed4021.Tests.dll --treenode-filter "/*/*/GeneticAlgorithmTests/*" --report-trx --report-trx-filename ga.trx</c>,
/// then read <c>Console.WriteLine</c> output back from <c>ga.trx</c>'s <c>&lt;StdOut&gt;</c> blocks.
///
/// Evolves the six genes that actually differ between Kingdom1's and Kingdom2's own persona rolls
/// (<see cref="NpeCharacter.ImperialistGene"/>/<see cref="NpeCharacter.DefensiveGene"/>/
/// <see cref="NpeCharacter.OffensiveGene"/>/<see cref="NpeCharacter.FactorGene"/>/
/// <see cref="NpeCharacter.Provoke"/>/<see cref="NpeCharacter.SphereX"/> -- see
/// <see cref="KingdomTurnHandler"/>'s own constructor). <see cref="NpeCharacter.WorldPower"/>/
/// <see cref="NpeCharacter.Offset"/> (randomized identically for both personas) and
/// <see cref="NpeCharacter.RandomGene"/>/<see cref="NpeCharacter.Techno"/>/
/// <see cref="NpeCharacter.Honorable"/> (fixed constants for both) are held at representative fixed
/// values for every candidate here, not evolved -- keeps the search space to what actually
/// differentiates behavior. Bounds per gene are the union of Kingdom1's and Kingdom2's own roll
/// ranges (real, balance-tested ranges from the original design), not invented ones.
///
/// Needs LegacyNpe's <c>InternalsVisibleTo</c> (see its own <c>AssemblyInfo.cs</c>) to reach
/// <see cref="KingdomTurnHandler"/>'s internal <c>(NpeEmpireType, NpeCharacter, ...)</c> constructor
/// -- the only way to hand it an arbitrary gene vector instead of a random roll.
///
/// Fitness = mean over <see cref="SeedsPerCandidate"/> seeded East-vs-West playouts of a
/// time-integrated "damage inflicted on the baseline opponent" score, with a large fixed bonus/penalty
/// if either side is actually eliminated, so any real elimination always outranks any non-elimination
/// outcome. Third design, not the first: a first pass scored (evolved empire's final population -
/// baseline empire's final population), which turned out to reward the evolved side for out-growing
/// its rival by conquering *independent* worlds -- confirmed elsewhere this session to be most of
/// where population growth actually comes from -- rather than for weakening the baseline specifically.
/// That candidate scored 38% win rate on its training seeds and 0% on held-out seeds: a real
/// overfitting signal, not noise, because the fitness was climbing a hill (economic growth) that
/// merely correlates with winning rather than measuring it.
///
/// The fix: every <see cref="CheckpointYears"/> years, snapshot the BASELINE empire's own population/
/// ships/industry/planet-count and compare each to its own starting value -- <c>(start-now)/start</c>,
/// clamped to [0,1] per metric so the baseline's own growth is never counted as negative damage -- and
/// sum the four fractions into that checkpoint's damage. Accumulating this across the horizon rewards
/// candidates for actually hurting the specific opponent over time, not for being economically bigger,
/// and gives dense per-checkpoint signal instead of one end-of-run snapshot (the same area-under-curve
/// idea used for the redesignation-timing simulations earlier this session, for the same reason: a
/// single endpoint reading is noisier and throws away every candidate's mid-game trajectory). Each
/// playout's <see cref="Outcome"/> (evolved win / baseline win / neither) is tracked alongside the
/// score, so win rate is a real measured number, not inferred from fitness magnitude.
///
/// Once evolution finishes, the best-ever genome is re-evaluated against a batch of seeds that were
/// never used during the search (<see cref="HeldOutSeedBase"/> onward -- past any seed the training
/// loop's own <c>generation*100_000 + candidateIndex*1_000 + seedIndex</c> scheme could reach) to
/// check whether the search found something that generalizes or just overfit its small training seed
/// set.
/// </summary>
public class GeneticAlgorithmTests
{
    private const int PopulationSize = 24;
    private const int Generations = 12;
    // Raised from 8: at the ~5% true win rate this search operates near, 8 seeds/candidate gives a
    // binomial standard error (~7.7%) bigger than the effect size being searched for, so the GA
    // couldn't reliably tell a genuinely-better genome from a lucky one. 30 halves that noise floor.
    private const int SeedsPerCandidate = 30;
    private const int YearCap = 120;
    private const int CheckpointYears = 10; // 12 checkpoints over the 120-year horizon.
    // Max possible non-elimination damage is 4 metrics x 1.0 x 12 checkpoints = 48; comfortably below
    // this so any real elimination still dominates the score, without the absurd 1,000,000:1 ratio the
    // old population-margin fitness needed (that scale was calibrated to a much bigger raw number).
    private const double EliminationBonus = 500;
    private const int HeldOutSeedBase = 10_000_000;
    private const int HeldOutSeedCount = 50;

    private enum Outcome { EvolvedWin, BaselineWin, Neither }

    private sealed record GeneBounds(double Min, double Max)
    {
        public double Clamp(double v) => Math.Clamp(v, Min, Max);
        public double Span => Max - Min;
    }

    private sealed record GeneVector(
        double Imperialist, double Defensive, double Offensive, double Factor, double Provoke, double SphereX)
    {
        public NpeCharacter ToPersona()
        {
            var imp = (int)Math.Round(Imperialist);
            var def = (int)Math.Round(Defensive);
            var off = (int)Math.Round(Offensive);
            return new NpeCharacter {
                ImperialistGene = imp,
                DefensiveGene = def,
                OffensiveGene = off,
                FactorGene = (int)Math.Round(Factor),
                Provoke = (int)Math.Round(Provoke),
                SphereX = (int)Math.Round(SphereX),
                RandomGene = 50,
                Techno = 50,
                Honorable = 50,
                WorldPower = 50,
                Offset = 5,
                Clock = 0,
                Defensive = def,
                Offensive = off,
                Imperialist = imp,
            };
        }
    }

    // Union of Kingdom1's and Kingdom2's own roll ranges (KingdomTurnHandler.cs's constructor).
    private static readonly Dictionary<string, GeneBounds> Bounds = new() {
        ["Imperialist"] = new GeneBounds(1, 100),
        ["Defensive"] = new GeneBounds(5, 75),
        ["Offensive"] = new GeneBounds(1, 100),
        ["Factor"] = new GeneBounds(15, 25),
        ["Provoke"] = new GeneBounds(50, 100),
        ["SphereX"] = new GeneBounds(25, 100),
    };

    [Test, Explicit]
    public async Task EvolveAgainstKingdom1Baseline()
    {
        var gaRandom = new Random(12345);
        var population = InitialPopulation(gaRandom);
        var overallStopwatch = System.Diagnostics.Stopwatch.StartNew();

        GeneVector? bestEver = null;
        var bestEverFitness = double.NegativeInfinity;
        var totalWins = 0;
        var totalLosses = 0;
        var totalNeither = 0;

        for (var gen = 0; gen < Generations; gen++) {
            var genStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var scored = population
                .Select((genome, i) => Evaluate(genome, gen, i))
                .OrderByDescending(x => x.Fitness)
                .ToList();
            genStopwatch.Stop();

            foreach (var c in scored) {
                totalWins += c.Wins;
                totalLosses += c.Losses;
                totalNeither += c.Neither;
            }

            var best = scored[0];
            var worst = scored[^1];
            Console.WriteLine($"gen {gen}: best={best.Fitness:0.#} (winrate={best.WinRate:P0}) worst={worst.Fitness:0.#} (winrate={worst.WinRate:P0}) mean={scored.Average(x => x.Fitness):0.#} meanWinRate={scored.Average(x => x.WinRate):P0} ({genStopwatch.Elapsed.TotalSeconds:0.#}s, {PopulationSize * SeedsPerCandidate} evals)");
            var b = best.Genome;
            Console.WriteLine($"  best genes: Imp={b.Imperialist:0} Def={b.Defensive:0} Off={b.Offensive:0} Fac={b.Factor:0} Prv={b.Provoke:0} Sph={b.SphereX:0}");

            if (best.Fitness > bestEverFitness) {
                bestEverFitness = best.Fitness;
                bestEver = best.Genome;
            }

            var survivors = scored.Take(Math.Max(2, PopulationSize / 3)).Select(x => x.Genome).ToList();
            population = NextGeneration(survivors, gaRandom);
        }

        overallStopwatch.Stop();
        var totalEvals = totalWins + totalLosses + totalNeither;
        Console.WriteLine($"total wall clock: {overallStopwatch.Elapsed.TotalSeconds:0.#}s ({totalEvals} playouts, {overallStopwatch.Elapsed.TotalMilliseconds / totalEvals:0.#}ms/playout)");
        Console.WriteLine($"eliminations across entire search: evolved-wins={totalWins} baseline-wins={totalLosses} neither={totalNeither} (of {totalEvals} total playouts)");
        Console.WriteLine($"best ever: fitness={bestEverFitness:0.#} genes: Imp={bestEver!.Imperialist:0} Def={bestEver.Defensive:0} Off={bestEver.Offensive:0} Fac={bestEver.Factor:0} Prv={bestEver.Provoke:0} Sph={bestEver.SphereX:0}");
        Console.WriteLine("Kingdom1 baseline ranges: Imp=1-5 Def=50-75 Off=1-2 Fac=15 Prv=75 Sph=25-75");
        Console.WriteLine("Kingdom2 baseline ranges: Imp=50-100 Def=5-10 Off=50-100 Fac=25 Prv=50-100 Sph=25-100");

        // Held-out validation: re-score the best-ever genome against seeds the search never trained
        // on, to check whether it generalizes or just overfit its small training seed set.
        var heldOutStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var heldOutWins = 0;
        var heldOutLosses = 0;
        var heldOutNeither = 0;
        double heldOutTotal = 0;
        for (var i = 0; i < HeldOutSeedCount; i++) {
            var seed = HeldOutSeedBase + i;
            var path = Path.Combine(PascalGroundTruth.PascalHarness.RepoRoot, "reference", "scenarios", "dos_131", "EASTWEST.SCN");
            var text = File.ReadAllText(path);
            var (score, outcome) = RunOneFitnessPlayout(text, bestEver, seed);
            heldOutTotal += score;
            switch (outcome) {
                case Outcome.EvolvedWin: heldOutWins++; break;
                case Outcome.BaselineWin: heldOutLosses++; break;
                default: heldOutNeither++; break;
            }
        }
        heldOutStopwatch.Stop();
        Console.WriteLine($"held-out validation ({HeldOutSeedCount} fresh seeds, never used in training, {heldOutStopwatch.Elapsed.TotalSeconds:0.#}s): mean fitness={heldOutTotal / HeldOutSeedCount:0.#} win rate={(double)heldOutWins / HeldOutSeedCount:P0} (wins={heldOutWins} losses={heldOutLosses} neither={heldOutNeither})");

        await Task.CompletedTask;
    }

    private static List<GeneVector> InitialPopulation(Random gaRandom)
    {
        var population = new List<GeneVector>();
        for (var i = 0; i < PopulationSize; i++) {
            var useKingdom2Style = gaRandom.Next(2) == 0;
            population.Add(useKingdom2Style
                ? new GeneVector(
                    Imperialist: gaRandom.Next(50, 101), Defensive: gaRandom.Next(5, 11),
                    Offensive: gaRandom.Next(50, 101), Factor: 25,
                    Provoke: gaRandom.Next(50, 101), SphereX: gaRandom.Next(25, 101))
                : new GeneVector(
                    Imperialist: gaRandom.Next(1, 6), Defensive: gaRandom.Next(50, 76),
                    Offensive: gaRandom.Next(1, 3), Factor: 15,
                    Provoke: 75, SphereX: gaRandom.Next(25, 76)));
        }
        return population;
    }

    private static List<GeneVector> NextGeneration(List<GeneVector> survivors, Random gaRandom)
    {
        var next = new List<GeneVector>(survivors);
        while (next.Count < PopulationSize) {
            var parentA = survivors[gaRandom.Next(survivors.Count)];
            if (survivors.Count > 1 && gaRandom.NextDouble() < 0.3) {
                var parentB = survivors[gaRandom.Next(survivors.Count)];
                next.Add(Mutate(Crossover(parentA, parentB), gaRandom));
            } else {
                next.Add(Mutate(parentA, gaRandom));
            }
        }
        return next;
    }

    private static GeneVector Crossover(GeneVector a, GeneVector b) => new(
        (a.Imperialist + b.Imperialist) / 2, (a.Defensive + b.Defensive) / 2,
        (a.Offensive + b.Offensive) / 2, (a.Factor + b.Factor) / 2,
        (a.Provoke + b.Provoke) / 2, (a.SphereX + b.SphereX) / 2);

    private static GeneVector Mutate(GeneVector g, Random gaRandom) => new(
        Perturb(g.Imperialist, Bounds["Imperialist"], gaRandom),
        Perturb(g.Defensive, Bounds["Defensive"], gaRandom),
        Perturb(g.Offensive, Bounds["Offensive"], gaRandom),
        Perturb(g.Factor, Bounds["Factor"], gaRandom),
        Perturb(g.Provoke, Bounds["Provoke"], gaRandom),
        Perturb(g.SphereX, Bounds["SphereX"], gaRandom));

    /// <summary>Box-Muller Gaussian step, sigma = 10% of the gene's real (union) range.</summary>
    private static double Perturb(double value, GeneBounds bounds, Random gaRandom)
    {
        var u1 = 1.0 - gaRandom.NextDouble();
        var u2 = gaRandom.NextDouble();
        var gaussian = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return bounds.Clamp(value + gaussian * bounds.Span * 0.1);
    }

    private sealed record CandidateResult(GeneVector Genome, double Fitness, double WinRate, int Wins, int Losses, int Neither);

    private static CandidateResult Evaluate(GeneVector genome, int generation, int candidateIndex)
    {
        var path = Path.Combine(PascalGroundTruth.PascalHarness.RepoRoot, "reference", "scenarios", "dos_131", "EASTWEST.SCN");
        var text = File.ReadAllText(path);

        double total = 0;
        var wins = 0;
        var losses = 0;
        var neither = 0;
        for (var seedIndex = 0; seedIndex < SeedsPerCandidate; seedIndex++) {
            // Deterministic but distinct per (generation, candidate, seed) -- reproducible reruns.
            // Well below HeldOutSeedBase, so training and held-out seeds never collide.
            var seed = generation * 100_000 + candidateIndex * 1_000 + seedIndex;
            var (score, outcome) = RunOneFitnessPlayout(text, genome, seed);
            total += score;
            switch (outcome) {
                case Outcome.EvolvedWin: wins++; break;
                case Outcome.BaselineWin: losses++; break;
                default: neither++; break;
            }
        }
        return new CandidateResult(genome, total / SeedsPerCandidate, (double)wins / SeedsPerCandidate, wins, losses, neither);
    }

    private static (double Score, Outcome Outcome) RunOneFitnessPlayout(string scenarioText, GeneVector genome, int seed)
    {
        var random = new Random(seed);
        var setup = new GalaxySetup(random);
        var loader = new ScenarioLoader(setup, random);
        var players = new[] {
            new ScenarioLoader.PlayerInfo("evolved", null, IsEmpress: false),
            new ScenarioLoader.PlayerInfo("baseline", null, IsEmpress: false),
        };

        var game = loader.Load(scenarioText, players);
        var startYear = game.Year;
        var evolved = game.Empires[0];
        var baseline = game.Empires[1];

        // No npeProvider was passed to Load, so neither empire's defenses were seeded yet -- the
        // public KingdomTurnHandler constructor normally does this itself; replicate it here since
        // the evolved side goes through the internal (persona-injecting) constructor instead.
        NpeToolkit.SetEmpireDefenses(evolved, random);
        game.TurnHandlers[evolved] = new KingdomTurnHandler(
            NpeEmpireType.Kingdom2, genome.ToPersona(), new Dictionary<Empire, StateDeptRecord>(),
            new Dictionary<Fleet, KingdomFleetState>(), random);
        game.TurnHandlers[baseline] = new KingdomTurnHandler(baseline, NpeEmpireType.Kingdom1, random);

        var turnEngine = new TurnEngine(new VisibilityHandler(random), new FleetMovementHandler(random), new AnnualTickHandler(random));

        var baselineStart = MeasureBaseline(game, baseline);
        var damageAccum = 0.0;
        var nextCheckpointYear = startYear + CheckpointYears;

        var maxCalls = YearCap * 2 * 2;
        for (var call = 0; call < maxCalls; call++) {
            turnEngine.AdvanceOneTurn(game);

            while (game.Year - nextCheckpointYear >= 0 && game.Year - startYear <= YearCap) {
                damageAccum += Damage(baselineStart, MeasureBaseline(game, baseline));
                nextCheckpointYear += CheckpointYears;
            }

            var evolvedAlive = evolved.Status != EmpireStatus.Eliminated;
            var baselineAlive = baseline.Status != EmpireStatus.Eliminated;
            if (!evolvedAlive || !baselineAlive || game.Year - startYear >= YearCap) {
                return FinalizeScore(damageAccum, evolvedAlive, baselineAlive);
            }
        }

        return FinalizeScore(damageAccum,
            evolved.Status != EmpireStatus.Eliminated, baseline.Status != EmpireStatus.Eliminated);
    }

    private sealed record BaselineMetrics(double Population, double Ships, double Industry, double Planets);

    private static int TotalShips(IShipCargoHolder holder) => Enum.GetValues<ShipType>().Sum(t => holder.Ships[t]);

    private static int TotalIndustry(IEconomicWorld world) => Enum.GetValues<IndustryType>().Sum(t => world.Industry[t]);

    private static BaselineMetrics MeasureBaseline(Game game, Empire baseline)
    {
        var planets = game.Galaxy.Planets.Where(p => p.Owner == baseline).ToList();
        var starbases = game.Galaxy.Starbases.Where(s => s.Owner == baseline).ToList();
        var fleets = game.Galaxy.Fleets.Where(f => f.Owner == baseline).ToList();

        var ships = planets.Sum(TotalShips) + starbases.Sum(TotalShips) + fleets.Sum(TotalShips);
        var industry = planets.Sum(TotalIndustry) + starbases.Sum(TotalIndustry);
        var population = planets.Sum(p => p.Population);

        return new BaselineMetrics(population, ships, industry, planets.Count);
    }

    /// <summary>Fraction of each metric the baseline has lost since <paramref name="start"/>, summed -- growth never counts as negative damage.</summary>
    private static double Damage(BaselineMetrics start, BaselineMetrics now) =>
        Fraction(start.Population, now.Population) + Fraction(start.Ships, now.Ships) +
        Fraction(start.Industry, now.Industry) + Fraction(start.Planets, now.Planets);

    private static double Fraction(double start, double now) =>
        start > 0 ? Math.Clamp((start - now) / start, 0, 1) : 0;

    private static (double Score, Outcome Outcome) FinalizeScore(double damageAccum, bool evolvedAlive, bool baselineAlive)
    {
        var (bonus, outcome) = (evolvedAlive, baselineAlive) switch {
            (true, false) => (EliminationBonus, Outcome.EvolvedWin),
            (false, true) => (-EliminationBonus, Outcome.BaselineWin),
            _ => (0, Outcome.Neither),
        };
        return (damageAccum + bonus, outcome);
    }
}
