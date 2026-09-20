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
/// <see cref="KingdomTurnHandler"/>'s own constructor), plus four more that no real persona rolls
/// either (<see cref="NpeCharacter.ProximityGene"/>, <see cref="NpeCharacter.TrendWeightGene"/>,
/// <see cref="NpeCharacter.CenterOfGravityGene"/>, <see cref="NpeCharacter.FocusGene"/> -- see each
/// field's own doc comment). <see cref="NpeCharacter.WorldPower"/>/<see cref="NpeCharacter.Offset"/>
/// (randomized identically for both personas) and <see cref="NpeCharacter.RandomGene"/>/
/// <see cref="NpeCharacter.Techno"/>/<see cref="NpeCharacter.Honorable"/> (fixed constants for both)
/// are held at representative fixed values for every candidate here, not evolved -- keeps the search
/// space to what actually differentiates behavior. Bounds per real-persona gene are the union of
/// Kingdom1's and Kingdom2's own roll ranges (real, balance-tested ranges from the original design);
/// the four invented genes have no such precedent and use an arbitrary but reasonable 0-100.
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
///
/// A second test, <see cref="EvolveAgainstFourKingdom1BaselinesOnIntro"/>, runs the same evolved
/// persona against four fixed Kingdom1 opponents on the Intro scenario (5 empires total) instead of
/// one on East-vs-West -- the multi-enemy features (<c>TrendWeightGene</c>/<c>CenterOfGravityGene</c>/
/// <c>FocusGene</c>) have nothing to differentiate between in a 2-empire game, by construction, so
/// they can only be meaningfully tested here.
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
        double Imperialist, double Defensive, double Offensive, double Factor, double Provoke, double SphereX,
        double Proximity, double TrendWeight, double CenterOfGravity, double Focus, double Composition, double HeavyRange,
        double AttackSize = 0, double IsspTarget = 0)
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
                ProximityGene = (int)Math.Round(Proximity),
                TrendWeightGene = (int)Math.Round(TrendWeight),
                CenterOfGravityGene = (int)Math.Round(CenterOfGravity),
                FocusGene = (int)Math.Round(Focus),
                CompositionGene = (int)Math.Round(Composition),
                HeavyRangeGene = (int)Math.Round(HeavyRange),
                AttackSizeGene = (int)Math.Round(AttackSize),
                IsspTargetGene = (int)Math.Round(IsspTarget),
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

    // Union of Kingdom1's and Kingdom2's own roll ranges (KingdomTurnHandler.cs's constructor) for the
    // six real genes. The four invented genes (Proximity/TrendWeight/CenterOfGravity/Focus -- see each
    // NpeCharacter field's own doc comment) have no such precedent -- both real personas fix them at 0,
    // reproducing today's distance-blind/trend-blind/geography-blind/independent-per-enemy behavior
    // exactly -- so 0-100 is an arbitrary but reasonable bound to search for all four.
    private static readonly Dictionary<string, GeneBounds> Bounds = new() {
        ["Imperialist"] = new GeneBounds(1, 100),
        ["Defensive"] = new GeneBounds(5, 75),
        ["Offensive"] = new GeneBounds(1, 100),
        ["Factor"] = new GeneBounds(15, 25),
        ["Provoke"] = new GeneBounds(50, 100),
        ["SphereX"] = new GeneBounds(25, 100),
        ["Proximity"] = new GeneBounds(0, 100),
        ["TrendWeight"] = new GeneBounds(0, 100),
        ["CenterOfGravity"] = new GeneBounds(0, 100),
        ["Focus"] = new GeneBounds(0, 100),
        ["Composition"] = new GeneBounds(0, 100),
        // 30 comfortably exceeds the max Chebyshev distance on every 21x21 scenario in use (max 20),
        // so the top of this range means "always fire regardless of target distance" -- paired with
        // the now-monotonic cutoff in NpeToolkit.DeployBattleFleet (0 = never, 30 = always).
        ["HeavyRange"] = new GeneBounds(0, 30),
        // Signed: positive shifts WarCabinet's fixed JumpAttack/SlowAttack split toward JumpAttack
        // (frequent, small), negative toward SlowAttack (rare, large). 0 reproduces the original fixed
        // splits (75/25 Conflict, 50/50 War) exactly.
        ["AttackSize"] = new GeneBounds(-100, 100),
        // 0 disables self-management entirely (NpeToolkit.ManageSelfSufficiency's own no-op gate).
        // 108-123 is the range already found to reproduce known-good ISSP targets for two very
        // differently-sized reference worlds (a population-4055 Capital and a population-464
        // Independent world) from the same Population*IsspTargetGene/100 formula. A prior GA round
        // (bound 0-150) converged its best genome to the ceiling (150), so this is widened to 400 --
        // sanity-checked against Orion's Belt's largest world (population 2200, the capital): at 400
        // its target is 8800, still meaningfully under the 9999 cargo cap, leaving real headroom for
        // the controller to read "above target" and ease ISSP down. 500 would already be degenerate
        // for that same world (target 11000 exceeds the cap entirely, so it could never read "above
        // target" and would permanently ratchet ISSP up, collapsing toward default-like behavior) --
        // 400 is the widest sensible bound before hitting that failure mode.
        ["IsspTarget"] = new GeneBounds(0, 400),
    };

    [Test, Explicit]
    public async Task EvolveAgainstKingdom1Baseline()
    {
        var gaRandom = new Random(12345);
        var population = InitialPopulation(gaRandom, PopulationSize);
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
            Console.WriteLine($"  best genes: Imp={b.Imperialist:0} Def={b.Defensive:0} Off={b.Offensive:0} Fac={b.Factor:0} Prv={b.Provoke:0} Sph={b.SphereX:0} Prx={b.Proximity:0} Trd={b.TrendWeight:0} Cog={b.CenterOfGravity:0} Foc={b.Focus:0} Atk={b.AttackSize:0}");

            if (best.Fitness > bestEverFitness) {
                bestEverFitness = best.Fitness;
                bestEver = best.Genome;
            }

            var survivors = scored.Take(Math.Max(2, PopulationSize / 3)).Select(x => x.Genome).ToList();
            population = NextGeneration(survivors, gaRandom, PopulationSize);
        }

        overallStopwatch.Stop();
        var totalEvals = totalWins + totalLosses + totalNeither;
        Console.WriteLine($"total wall clock: {overallStopwatch.Elapsed.TotalSeconds:0.#}s ({totalEvals} playouts, {overallStopwatch.Elapsed.TotalMilliseconds / totalEvals:0.#}ms/playout)");
        Console.WriteLine($"eliminations across entire search: evolved-wins={totalWins} baseline-wins={totalLosses} neither={totalNeither} (of {totalEvals} total playouts)");
        Console.WriteLine($"best ever: fitness={bestEverFitness:0.#} genes: Imp={bestEver!.Imperialist:0} Def={bestEver.Defensive:0} Off={bestEver.Offensive:0} Fac={bestEver.Factor:0} Prv={bestEver.Provoke:0} Sph={bestEver.SphereX:0} Prx={bestEver.Proximity:0} Trd={bestEver.TrendWeight:0} Cog={bestEver.CenterOfGravity:0} Foc={bestEver.Focus:0} Atk={bestEver.AttackSize:0}");
        Console.WriteLine("Kingdom1 baseline ranges: Imp=1-5 Def=50-75 Off=1-2 Fac=15 Prv=75 Sph=25-75 Prx=Trd=Cog=Foc=0 (fixed, real games never set these)");
        Console.WriteLine("Kingdom2 baseline ranges: Imp=50-100 Def=5-10 Off=50-100 Fac=25 Prv=50-100 Sph=25-100 Prx=Trd=Cog=Foc=0 (fixed, real games never set these)");

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

    private static List<GeneVector> InitialPopulation(Random gaRandom, int populationSize)
    {
        var population = new List<GeneVector>();
        for (var i = 0; i < populationSize; i++) {
            var useKingdom2Style = gaRandom.Next(2) == 0;
            // The four invented genes have no Kingdom1/Kingdom2 precedent to seed from either style
            // with -- start every candidate at a uniform random roll across its own full bound,
            // independent of style.
            var proximity = gaRandom.Next(0, 101);
            var trendWeight = gaRandom.Next(0, 101);
            var centerOfGravity = gaRandom.Next(0, 101);
            var focus = gaRandom.Next(0, 101);
            var composition = gaRandom.Next(0, 101);
            var heavyRange = gaRandom.Next(0, 101);
            var attackSize = gaRandom.Next(-100, 101);
            var isspTarget = gaRandom.Next(0, 151);
            population.Add(useKingdom2Style
                ? new GeneVector(
                    Imperialist: gaRandom.Next(50, 101), Defensive: gaRandom.Next(5, 11),
                    Offensive: gaRandom.Next(50, 101), Factor: 25,
                    Provoke: gaRandom.Next(50, 101), SphereX: gaRandom.Next(25, 101),
                    Proximity: proximity, TrendWeight: trendWeight, CenterOfGravity: centerOfGravity, Focus: focus,
                    Composition: composition, HeavyRange: heavyRange, AttackSize: attackSize, IsspTarget: isspTarget)
                : new GeneVector(
                    Imperialist: gaRandom.Next(1, 6), Defensive: gaRandom.Next(50, 76),
                    Offensive: gaRandom.Next(1, 3), Factor: 15,
                    Provoke: 75, SphereX: gaRandom.Next(25, 76),
                    Proximity: proximity, TrendWeight: trendWeight, CenterOfGravity: centerOfGravity, Focus: focus,
                    Composition: composition, HeavyRange: heavyRange, AttackSize: attackSize, IsspTarget: isspTarget));
        }
        return population;
    }

    private static List<GeneVector> NextGeneration(List<GeneVector> survivors, Random gaRandom, int populationSize)
    {
        var next = new List<GeneVector>(survivors);
        while (next.Count < populationSize) {
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
        (a.Provoke + b.Provoke) / 2, (a.SphereX + b.SphereX) / 2, (a.Proximity + b.Proximity) / 2,
        (a.TrendWeight + b.TrendWeight) / 2, (a.CenterOfGravity + b.CenterOfGravity) / 2, (a.Focus + b.Focus) / 2,
        (a.Composition + b.Composition) / 2, (a.HeavyRange + b.HeavyRange) / 2, (a.AttackSize + b.AttackSize) / 2,
        (a.IsspTarget + b.IsspTarget) / 2);

    private static GeneVector Mutate(GeneVector g, Random gaRandom) => new(
        Perturb(g.Imperialist, Bounds["Imperialist"], gaRandom),
        Perturb(g.Defensive, Bounds["Defensive"], gaRandom),
        Perturb(g.Offensive, Bounds["Offensive"], gaRandom),
        Perturb(g.Factor, Bounds["Factor"], gaRandom),
        Perturb(g.Provoke, Bounds["Provoke"], gaRandom),
        Perturb(g.SphereX, Bounds["SphereX"], gaRandom),
        Perturb(g.Proximity, Bounds["Proximity"], gaRandom),
        Perturb(g.TrendWeight, Bounds["TrendWeight"], gaRandom),
        Perturb(g.CenterOfGravity, Bounds["CenterOfGravity"], gaRandom),
        Perturb(g.Focus, Bounds["Focus"], gaRandom),
        Perturb(g.Composition, Bounds["Composition"], gaRandom),
        Perturb(g.HeavyRange, Bounds["HeavyRange"], gaRandom),
        Perturb(g.AttackSize, Bounds["AttackSize"], gaRandom),
        Perturb(g.IsspTarget, Bounds["IsspTarget"], gaRandom));

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
        // the evolved side goes through Kingdom2ModernTurnHandler instead, which doesn't.
        NpeToolkit.SetEmpireDefenses(evolved, random);
        game.TurnHandlers[evolved] = new Kingdom2ModernTurnHandler(genome.ToPersona(), PolicyType.Harass, random);
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

    // ---- Intro (5 empires): 1 evolved vs. 4 fixed Kingdom1 baselines ----
    //
    // Sized down from the East-vs-West constants above after measuring real per-playout cost on this
    // scenario (5 empires means 5 empire-turns per simulated year instead of 2, so each playout costs
    // more) -- see this test's own report for the measured number and the reasoning behind the final
    // values chosen here.

    // Measured 716.5ms/playout at the 500-year horizon (probe: population 2/generations 1/seeds 2,
    // 2.9s/4 playouts) -- ~15.6x the ~46ms/playout measured at 120 years, a super-linear jump from
    // population/ship counts growing much larger over a longer horizon. Sized to land around 12-13
    // minutes total: 16 x 8 x 8 = 1,024 training playouts + 30 held-out = 1,054 x 0.7165s ~= 755s.
    private const int IntroPopulationSize = 16;
    private const int IntroGenerations = 8;
    private const int IntroSeedsPerCandidate = 8;
    // Extended from 120: at 120 years, real combat damage was measurable (5-7 world conquests/seed for
    // the best genome) but zero eliminations occurred across the entire search, in either direction --
    // not enough time for any campaign, focused or not, to actually finish an opponent off. 500 matches
    // the cap used by this session's very first all-Kingdom playout harness (ScenarioPlayoutTests.cs).
    private const int IntroYearCap = 500;
    // Points per world the evolved side actually conquers from a fixed opponent (see
    // RunOneFitnessPlayoutIntro's own doc comment for why this replaced the checkpoint-damage metric).
    // 20 worlds taken without finishing anyone off (2000/100) still scores below one real elimination --
    // preserves "finish one opponent" outranking "scatter damage across all four" for the normal case,
    // while still letting a genuinely dominant sweep across many worlds outscore a single elimination in
    // the extreme case, which is a real outcome that should score higher, not a design flaw.
    private const double ConquestWeight = 100;
    // One opponent's worth of "took every world" is comfortably above what ConquestWeight alone could
    // plausibly reach for a single opponent before they'd already be eliminated -- so an elimination
    // still dominates equivalent partial damage against that same opponent.
    private const double IntroEliminationBonus = 2000;
    private const int IntroHeldOutSeedBase = 20_000_000;
    private const int IntroHeldOutSeedCount = 30;

    [Test, Explicit]
    public async Task EvolveAgainstFourKingdom1BaselinesOnIntro()
    {
        var introPath = Path.Combine(PascalGroundTruth.PascalHarness.RepoRoot, "reference", "scenarios", "dos_131", "INTRO.SCN");
        var introText = File.ReadAllText(introPath);

        var gaRandom = new Random(67890);
        var population = InitialPopulation(gaRandom, IntroPopulationSize);
        var overallStopwatch = System.Diagnostics.Stopwatch.StartNew();

        GeneVector? bestEver = null;
        var bestEverFitness = double.NegativeInfinity;
        var totalAnyWins = 0;
        var totalAllWins = 0;
        var totalLosses = 0;
        var totalSeeds = 0;
        // Every candidate evaluated across the whole search (not just each generation's winner) -- the
        // GA's own convergence path can be misleading (it might converge on high Focus for reasons
        // unrelated to whether Focus actually helps, or fail to converge there even if it does, given
        // how sparse the elimination signal was at the shorter horizon) -- a direct correlation between
        // each gene's value and outcome across every real candidate is the actual empirical answer.
        var allEvaluated = new List<IntroCandidateResult>();

        for (var gen = 0; gen < IntroGenerations; gen++) {
            var genStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var scored = population
                .Select((genome, i) => EvaluateIntro(genome, gen, i, introText))
                .OrderByDescending(x => x.Fitness)
                .ToList();
            genStopwatch.Stop();
            allEvaluated.AddRange(scored);

            foreach (var c in scored) {
                totalAnyWins += c.AnyWins;
                totalAllWins += c.AllWins;
                totalLosses += c.Losses;
                totalSeeds += IntroSeedsPerCandidate;
            }

            var best = scored[0];
            var worst = scored[^1];
            Console.WriteLine($"gen {gen}: best={best.Fitness:0.#} (winRateAny={best.WinRateAny:P0} winRateAll={best.WinRateAll:P0}) worst={worst.Fitness:0.#} mean={scored.Average(x => x.Fitness):0.#} meanWinRateAny={scored.Average(x => x.WinRateAny):P0} ({genStopwatch.Elapsed.TotalSeconds:0.#}s, {IntroPopulationSize * IntroSeedsPerCandidate} evals)");
            var b = best.Genome;
            Console.WriteLine($"  best genes: Imp={b.Imperialist:0} Def={b.Defensive:0} Off={b.Offensive:0} Fac={b.Factor:0} Prv={b.Provoke:0} Sph={b.SphereX:0} Prx={b.Proximity:0} Trd={b.TrendWeight:0} Cog={b.CenterOfGravity:0} Foc={b.Focus:0} Atk={b.AttackSize:0}");

            if (best.Fitness > bestEverFitness) {
                bestEverFitness = best.Fitness;
                bestEver = best.Genome;
            }

            var survivors = scored.Take(Math.Max(2, IntroPopulationSize / 3)).Select(x => x.Genome).ToList();
            population = NextGeneration(survivors, gaRandom, IntroPopulationSize);
        }

        overallStopwatch.Stop();
        Console.WriteLine($"total wall clock: {overallStopwatch.Elapsed.TotalSeconds:0.#}s ({totalSeeds} playouts, {overallStopwatch.Elapsed.TotalMilliseconds / totalSeeds:0.#}ms/playout)");
        Console.WriteLine($"across entire search: evolved-eliminated-at-least-one={totalAnyWins} evolved-eliminated-all-four={totalAllWins} evolved-itself-eliminated={totalLosses} (of {totalSeeds} total playouts)");
        Console.WriteLine($"best ever: fitness={bestEverFitness:0.#} genes: Imp={bestEver!.Imperialist:0} Def={bestEver.Defensive:0} Off={bestEver.Offensive:0} Fac={bestEver.Factor:0} Prv={bestEver.Provoke:0} Sph={bestEver.SphereX:0} Prx={bestEver.Proximity:0} Trd={bestEver.TrendWeight:0} Cog={bestEver.CenterOfGravity:0} Foc={bestEver.Focus:0} Atk={bestEver.AttackSize:0}");

        var heldOutStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var heldOutAnyWins = 0;
        var heldOutAllWins = 0;
        var heldOutLosses = 0;
        double heldOutTotal = 0;
        for (var i = 0; i < IntroHeldOutSeedCount; i++) {
            var seed = IntroHeldOutSeedBase + i;
            var (score, eliminatedCount, opponentCount, evolvedEliminated, _) = RunOneFitnessPlayoutIntro(introText, bestEver, seed, IntroPlayers, IntroYearCap);
            heldOutTotal += score;
            if (eliminatedCount >= 1) heldOutAnyWins++;
            if (eliminatedCount == opponentCount) heldOutAllWins++;
            if (evolvedEliminated) heldOutLosses++;
        }
        heldOutStopwatch.Stop();
        Console.WriteLine($"held-out validation ({IntroHeldOutSeedCount} fresh seeds, never used in training, {heldOutStopwatch.Elapsed.TotalSeconds:0.#}s): mean fitness={heldOutTotal / IntroHeldOutSeedCount:0.#} winRateAny={(double)heldOutAnyWins / IntroHeldOutSeedCount:P0} winRateAll={(double)heldOutAllWins / IntroHeldOutSeedCount:P0} lossRate={(double)heldOutLosses / IntroHeldOutSeedCount:P0}");

        // Direct empirical check: does each gene's value actually predict outcome across every real
        // candidate evaluated, independent of what the GA itself happened to converge on? Pearson
        // correlation plus a top-third-vs-bottom-third bucketed comparison against fitness, elimination
        // rate (winRateAny), and raw conquest count.
        Console.WriteLine($"--- gene-vs-outcome analysis over {allEvaluated.Count} total evaluated candidates ---");
        ReportGeneCorrelation("Focus", allEvaluated, c => c.Genome.Focus);
        ReportGeneCorrelation("TrendWeight", allEvaluated, c => c.Genome.TrendWeight);
        ReportGeneCorrelation("CenterOfGravity", allEvaluated, c => c.Genome.CenterOfGravity);
        ReportGeneCorrelation("AttackSize", allEvaluated, c => c.Genome.AttackSize);

        await Task.CompletedTask;
    }

    private static void ReportGeneCorrelation(string name, List<IntroCandidateResult> all, Func<IntroCandidateResult, double> geneValue)
    {
        var withGene = all.Select(c => (Gene: geneValue(c), c.Fitness, c.WinRateAny, c.MeanWorldsConquered)).ToList();

        var corrFitness = Correlation(withGene.Select(x => (x.Gene, x.Fitness)));
        var corrWinRate = Correlation(withGene.Select(x => (x.Gene, x.WinRateAny)));
        var corrConquest = Correlation(withGene.Select(x => (x.Gene, x.MeanWorldsConquered)));

        var ordered = withGene.OrderBy(x => x.Gene).ToList();
        var third = Math.Max(1, ordered.Count / 3);
        var bottom = ordered.Take(third).ToList();
        var top = ordered.Skip(ordered.Count - third).ToList();

        Console.WriteLine($"{name}: corr(fitness)={corrFitness:0.00} corr(winRateAny)={corrWinRate:0.00} corr(conquests)={corrConquest:0.00}");
        Console.WriteLine($"  bottom third (n={bottom.Count}, gene {bottom.Min(x => x.Gene):0}-{bottom.Max(x => x.Gene):0}): meanFitness={bottom.Average(x => x.Fitness):0.#} meanWinRateAny={bottom.Average(x => x.WinRateAny):P0} meanConquests={bottom.Average(x => x.MeanWorldsConquered):0.#}");
        Console.WriteLine($"  top third    (n={top.Count}, gene {top.Min(x => x.Gene):0}-{top.Max(x => x.Gene):0}): meanFitness={top.Average(x => x.Fitness):0.#} meanWinRateAny={top.Average(x => x.WinRateAny):P0} meanConquests={top.Average(x => x.MeanWorldsConquered):0.#}");
    }

    /// <summary>Plain Pearson correlation coefficient -- no library needed for a pairwise check this small.</summary>
    private static double Correlation(IEnumerable<(double X, double Y)> pairs)
    {
        var list = pairs.ToList();
        var n = list.Count;
        if (n < 2) return 0;

        var meanX = list.Average(p => p.X);
        var meanY = list.Average(p => p.Y);
        var cov = list.Sum(p => (p.X - meanX) * (p.Y - meanY));
        var varX = list.Sum(p => (p.X - meanX) * (p.X - meanX));
        var varY = list.Sum(p => (p.Y - meanY) * (p.Y - meanY));
        var denom = Math.Sqrt(varX * varY);
        return denom == 0 ? 0 : cov / denom;
    }

    private sealed record IntroCandidateResult(
        GeneVector Genome, double Fitness, double WinRateAny, double WinRateAll, int AnyWins, int AllWins, int Losses,
        double MeanWorldsConquered);

    private static IntroCandidateResult EvaluateIntro(GeneVector genome, int generation, int candidateIndex, string introText)
        => EvaluateMultiOpponent(genome, generation, candidateIndex, introText, IntroPlayers, IntroYearCap, IntroSeedsPerCandidate);

    /// <summary>
    /// Generalized over <see cref="EvaluateIntro"/> so the same fitness/elimination-tracking logic can
    /// drive any multi-opponent scenario, not just Intro -- <see cref="EvolveOnOrionsBelt"/> reuses this
    /// unchanged for the 3-empire near/far fixture. <paramref name="players"/>'s first entry must be the
    /// evolved side (matched by name below, same as <see cref="RunOneFitnessPlayoutIntro"/> always did
    /// for Intro's "evolved").
    /// </summary>
    private static IntroCandidateResult EvaluateMultiOpponent(
        GeneVector genome, int generation, int candidateIndex, string scenarioText,
        IReadOnlyList<ScenarioLoader.PlayerInfo> players, int yearCap, int seedsPerCandidate)
    {
        double total = 0;
        var anyWins = 0;
        var allWins = 0;
        var losses = 0;
        var worldsConqueredTotal = 0;
        for (var seedIndex = 0; seedIndex < seedsPerCandidate; seedIndex++) {
            var seed = generation * 100_000 + candidateIndex * 1_000 + seedIndex;
            var (score, eliminatedCount, opponentCount, evolvedEliminated, worldsConquered) = RunOneFitnessPlayoutIntro(scenarioText, genome, seed, players, yearCap);
            total += score;
            worldsConqueredTotal += worldsConquered;
            if (eliminatedCount >= 1) anyWins++;
            if (eliminatedCount == opponentCount) allWins++;
            if (evolvedEliminated) losses++;
        }
        return new IntroCandidateResult(genome, total / seedsPerCandidate,
            (double)anyWins / seedsPerCandidate, (double)allWins / seedsPerCandidate, anyWins, allWins, losses,
            (double)worldsConqueredTotal / seedsPerCandidate);
    }

    /// <summary>
    /// One evolved empire vs. every other empire the Intro scenario creates (3 player slots, one of
    /// which is "evolved" -- the rest given placeholder names and left as fixed baselines -- plus
    /// whatever NPE-declared empires the scenario itself adds independent of the player list). Found
    /// by name, not by <c>game.Empires</c> index: <see cref="ScenarioLoader"/> appends empires in
    /// whatever order CREATEPLAYEREMPIRE/CREATENPEMPIRE tokens appear in the .SCN text, not
    /// players-first -- indexing would silently pick the wrong empire as "evolved" if that order ever
    /// isn't what it looks like from the player list alone.
    ///
    /// Fitness here is NOT the East-vs-West "decline from the opponent's own starting stock" metric
    /// (<see cref="Damage"/>/<see cref="MeasureBaseline"/>, still used by the East-vs-West path above,
    /// unchanged). That metric went completely flat on Intro -- 2,880/2,880 training playouts and
    /// 30/30 held-out playouts scored exactly 0.0 in the first Intro round, confirmed by direct
    /// diagnostic sampling: all four fixed opponents grow 3-9x population and 60-200x ships over 120
    /// years purely from conquering independent worlds, regardless of what the evolved side does, and
    /// with force now split across up to four simultaneous targets instead of one, nothing ever dented
    /// any opponent's own trajectory enough to go net-negative relative to its start. The metric was
    /// measuring net stock change, which their own unrelated growth swamps -- not combat outcomes.
    ///
    /// The fix: count real, directly-attributable combat outcomes instead of net trajectories.
    /// <see cref="Types.NewsType.WorldConqueredByEnemy"/> fires unconditionally (no scouting gate,
    /// confirmed by reading <c>Empire.AddNews</c>'s own guard clause) on the DEFENDER's own
    /// <see cref="Empire.News"/> list whenever a world changes hands, tagged with
    /// <see cref="Entities.NewsItem.OtherEmpire"/> = the attacker (<c>CombatOutcome.cs</c>'s
    /// <c>ConquerWorld</c>) -- exactly the attribution needed to count "worlds the evolved side
    /// specifically took from this specific fixed opponent," sidestepping the opponent's own unrelated
    /// growth entirely. (The <c>Global</c> variants of this headline,
    /// <c>EnemyConqueredWorldGlobal</c>/<c>EnemyConqueredCapitalGlobal</c>, looked promising at first
    /// but turned out to broadcast to every THIRD-PARTY empire that scouted the event and explicitly
    /// exclude the attacker/defender themselves from that broadcast -- the wrong list to read.)
    ///
    /// News gets erased on an empire's own turn (real Pascal's EraseNews, <c>ANACREON.PAS</c>'s
    /// per-empire NPE sequence) -- polling every opponent's <c>News.Count</c> after every single
    /// <see cref="TurnEngine.AdvanceOneTurn"/> call (not just at checkpoints) and only scanning the
    /// slice since the last poll catches every new item before any later call can erase it: addition
    /// always happens during the ATTACKER's own turn call (a world can only change hands while its new
    /// owner is acting), while erasure only ever happens during that SAME opponent's own turn call --
    /// two different calls can never both add-to and erase-from the same list in one step, so a add
    /// always gets polled at least once before any later erase reaches it. A list shrinking between
    /// polls means it was just erased; the cursor resets to 0 rather than reading stale/absent items
    /// (there's nothing left to recover -- the point above is that this can't happen to an unpolled
    /// item).
    /// </summary>
    /// <summary>The 3 player slots Intro's own scenario text declares -- see <see cref="RunOneFitnessPlayoutIntro"/>'s own doc comment for the two NPE-declared empires Intro adds independent of this list.</summary>
    private static readonly ScenarioLoader.PlayerInfo[] IntroPlayers = [
        new("evolved", null, IsEmpress: false),
        new("baseline-p1", null, IsEmpress: false),
        new("baseline-p2", null, IsEmpress: false),
    ];

    private static (double Score, int EliminatedCount, int OpponentCount, bool EvolvedEliminated, int WorldsConquered) RunOneFitnessPlayoutIntro(
        string scenarioText, GeneVector genome, int seed, IReadOnlyList<ScenarioLoader.PlayerInfo> players, int yearCap)
    {
        var random = new Random(seed);
        var setup = new GalaxySetup(random);
        var loader = new ScenarioLoader(setup, random);

        var game = loader.Load(scenarioText, players);
        var startYear = game.Year;
        var evolved = game.Empires.First(e => e.Name == "evolved");
        var opponents = game.Empires.Where(e => e != evolved).ToList();

        NpeToolkit.SetEmpireDefenses(evolved, random);
        game.TurnHandlers[evolved] = new Kingdom2ModernTurnHandler(genome.ToPersona(), PolicyType.Harass, random);
        foreach (var opponent in opponents) {
            game.TurnHandlers[opponent] = new KingdomTurnHandler(opponent, NpeEmpireType.Kingdom1, random);
        }

        var turnEngine = new TurnEngine(new VisibilityHandler(random), new FleetMovementHandler(random), new AnnualTickHandler(random));

        var worldsConquered = new int[opponents.Count];
        var lastNewsCount = new int[opponents.Count];

        var maxCalls = yearCap * game.Empires.Count * 2;
        for (var call = 0; call < maxCalls; call++) {
            turnEngine.AdvanceOneTurn(game);

            for (var i = 0; i < opponents.Count; i++) {
                var news = opponents[i].News;
                if (news.Count < lastNewsCount[i]) {
                    lastNewsCount[i] = 0; // erased on this opponent's own turn -- nothing to recover, see doc comment above.
                }
                for (var j = lastNewsCount[i]; j < news.Count; j++) {
                    if (news[j].Headline == NewsType.WorldConqueredByEnemy && news[j].OtherEmpire == evolved) {
                        worldsConquered[i]++;
                    }
                }
                lastNewsCount[i] = news.Count;
            }

            var evolvedAlive = evolved.Status != EmpireStatus.Eliminated;
            var opponentsRemaining = opponents.Count(o => o.Status != EmpireStatus.Eliminated);
            if (!evolvedAlive || opponentsRemaining == 0 || game.Year - startYear >= yearCap) {
                return FinalizeScoreIntro(worldsConquered.Sum(), opponents, evolvedAlive);
            }
        }

        return FinalizeScoreIntro(worldsConquered.Sum(), opponents, evolved.Status != EmpireStatus.Eliminated);
    }

    private static (double Score, int EliminatedCount, int OpponentCount, bool EvolvedEliminated, int WorldsConquered) FinalizeScoreIntro(
        int totalWorldsConquered, List<Empire> opponents, bool evolvedAlive)
    {
        var eliminatedCount = opponents.Count(o => o.Status == EmpireStatus.Eliminated);
        var bonus = (IntroEliminationBonus * eliminatedCount) - (evolvedAlive ? 0 : IntroEliminationBonus);
        return (totalWorldsConquered * ConquestWeight + bonus, eliminatedCount, opponents.Count, !evolvedAlive, totalWorldsConquered);
    }

    // ---- Orion's Belt (3 empires, collinear): 1 evolved vs. 2 fixed Kingdom1 baselines ----
    //
    // Purpose-built fixture (Fixtures/OrionsBelt.scn -- kept, reusable, not thrown away) to isolate two
    // open questions the Intro round (1-vs-4, zero eliminations even at 500 years) couldn't answer: is
    // 1-vs-2 winnable at all (intermediate difficulty between East-vs-West's working 1-vs-1 and Intro's
    // failing 1-vs-4), and does CenterOfGravityGene show a real signal now that there's an unambiguous
    // near opponent (Chebyshev distance 7) and far opponent (distance 19, ~2.7x farther) instead of
    // Intro's four roughly-equidistant-and-scattered opponents. No independent worlds exist in this
    // scenario at all, so growth can only come from conquering a rival -- removes the "expand into empty
    // space" confound entirely rather than just outrunning it with a bigger fitness number.
    //
    // Reuses EvaluateMultiOpponent/RunOneFitnessPlayoutIntro/FinalizeScoreIntro unchanged (all already
    // generalized over opponent count and scenario text) -- only the scenario, player list, and horizon
    // differ from the Intro path.

    private static readonly ScenarioLoader.PlayerInfo[] OrionsBeltPlayers = [
        new("evolved", null, IsEmpress: false),
        new("near", null, IsEmpress: false),
        new("far", null, IsEmpress: false),
    ];

    // Started at the same 120-year horizon East-vs-West's working 1-vs-1 case used, per the user's own
    // instruction to isolate opponent count as the only changed variable from that known-working
    // baseline, before considering whether to escalate the way the Intro round did.
    private const int OrionsBeltYearCap = 120;
    // Measured 11.9ms/playout in the CompositionGene round's actual run (population 16/generations
    // 10/seeds 16, 30.6s/2,560 playouts) -- cheaper than East-vs-West despite one more empire, since
    // Orion's Belt has far fewer total worlds (9 vs. 14). At a true win rate this small (0.2%-0.5%
    // observed across the last two rounds), 16 seeds/candidate gives a binomial standard error well
    // above the effect size being searched for -- the exact problem already diagnosed and fixed once
    // for East-vs-West by raising seeds 8->30 (held-out 5%->8%). Raised to 50 here (held-out
    // proportionally to 60) rather than just matching East-vs-West's 30, since Orion's Belt's true win
    // rate is smaller still; at ~12ms/playout this is still only ~90s projected, not a real cost
    // tradeoff worth trimming for.
    // Population x generations raised from 16x10 (160 evaluated candidates) to 48x24 (1,152) after
    // FocusGene/TrendWeightGene's correlations flipped sign twice between rounds at the smaller pool
    // despite a reliable per-candidate seed count (50) -- too few DISTINCT candidates for a stable
    // correlation reading, a different problem than per-candidate noise. Measured cost at the old
    // scale (50 seeds/candidate) was ~10-11ms/playout; sized against that below, re-measure the first
    // generation's real cost before trusting this comment's projection.
    private const int OrionsBeltPopulationSize = 48;
    private const int OrionsBeltGenerations = 24;
    private const int OrionsBeltSeedsPerCandidate = 50;
    private const int OrionsBeltHeldOutSeedBase = 30_000_000;
    private const int OrionsBeltHeldOutSeedCount = 80;

    [Test, Explicit]
    public async Task EvolveOnOrionsBelt()
    {
        var path = Path.Combine(PascalGroundTruth.PascalHarness.RepoRoot, "src", "Reconstructed4021.Tests", "Fixtures", "OrionsBelt.scn");
        var text = File.ReadAllText(path);

        var gaRandom = new Random(24680);
        var population = InitialPopulation(gaRandom, OrionsBeltPopulationSize);
        var overallStopwatch = System.Diagnostics.Stopwatch.StartNew();

        GeneVector? bestEver = null;
        var bestEverFitness = double.NegativeInfinity;
        var totalAnyWins = 0;
        var totalAllWins = 0;
        var totalLosses = 0;
        var totalSeeds = 0;
        var allEvaluated = new List<IntroCandidateResult>();

        for (var gen = 0; gen < OrionsBeltGenerations; gen++) {
            var genStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var scored = population
                .Select((genome, i) => EvaluateMultiOpponent(genome, gen, i, text, OrionsBeltPlayers, OrionsBeltYearCap, OrionsBeltSeedsPerCandidate))
                .OrderByDescending(x => x.Fitness)
                .ToList();
            genStopwatch.Stop();
            allEvaluated.AddRange(scored);

            foreach (var c in scored) {
                totalAnyWins += c.AnyWins;
                totalAllWins += c.AllWins;
                totalLosses += c.Losses;
                totalSeeds += OrionsBeltSeedsPerCandidate;
            }

            var best = scored[0];
            var worst = scored[^1];
            Console.WriteLine($"gen {gen}: best={best.Fitness:0.#} (winRateAny={best.WinRateAny:P0} winRateAll={best.WinRateAll:P0}) worst={worst.Fitness:0.#} mean={scored.Average(x => x.Fitness):0.#} meanWinRateAny={scored.Average(x => x.WinRateAny):P0} ({genStopwatch.Elapsed.TotalSeconds:0.#}s, {OrionsBeltPopulationSize * OrionsBeltSeedsPerCandidate} evals)");
            var b = best.Genome;
            Console.WriteLine($"  best genes: Imp={b.Imperialist:0} Def={b.Defensive:0} Off={b.Offensive:0} Fac={b.Factor:0} Prv={b.Provoke:0} Sph={b.SphereX:0} Prx={b.Proximity:0} Trd={b.TrendWeight:0} Cog={b.CenterOfGravity:0} Foc={b.Focus:0} Cmp={b.Composition:0} Hrg={b.HeavyRange:0} Atk={b.AttackSize:0} Isp={b.IsspTarget:0}");

            if (best.Fitness > bestEverFitness) {
                bestEverFitness = best.Fitness;
                bestEver = best.Genome;
            }

            var survivors = scored.Take(Math.Max(2, OrionsBeltPopulationSize / 3)).Select(x => x.Genome).ToList();
            population = NextGeneration(survivors, gaRandom, OrionsBeltPopulationSize);
        }

        overallStopwatch.Stop();
        Console.WriteLine($"total wall clock: {overallStopwatch.Elapsed.TotalSeconds:0.#}s ({totalSeeds} playouts, {overallStopwatch.Elapsed.TotalMilliseconds / totalSeeds:0.#}ms/playout)");
        Console.WriteLine($"across entire search: evolved-eliminated-at-least-one={totalAnyWins} evolved-eliminated-both={totalAllWins} evolved-itself-eliminated={totalLosses} (of {totalSeeds} total playouts)");
        Console.WriteLine($"best ever: fitness={bestEverFitness:0.#} genes: Imp={bestEver!.Imperialist:0} Def={bestEver.Defensive:0} Off={bestEver.Offensive:0} Fac={bestEver.Factor:0} Prv={bestEver.Provoke:0} Sph={bestEver.SphereX:0} Prx={bestEver.Proximity:0} Trd={bestEver.TrendWeight:0} Cog={bestEver.CenterOfGravity:0} Foc={bestEver.Focus:0} Cmp={bestEver.Composition:0} Hrg={bestEver.HeavyRange:0} Atk={bestEver.AttackSize:0} Isp={bestEver.IsspTarget:0}");

        var heldOutStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var heldOutAnyWins = 0;
        var heldOutAllWins = 0;
        var heldOutLosses = 0;
        double heldOutTotal = 0;
        for (var i = 0; i < OrionsBeltHeldOutSeedCount; i++) {
            var seed = OrionsBeltHeldOutSeedBase + i;
            var (score, eliminatedCount, opponentCount, evolvedEliminated, _) = RunOneFitnessPlayoutIntro(text, bestEver, seed, OrionsBeltPlayers, OrionsBeltYearCap);
            heldOutTotal += score;
            if (eliminatedCount >= 1) heldOutAnyWins++;
            if (eliminatedCount == opponentCount) heldOutAllWins++;
            if (evolvedEliminated) heldOutLosses++;
        }
        heldOutStopwatch.Stop();
        Console.WriteLine($"held-out validation ({OrionsBeltHeldOutSeedCount} fresh seeds, never used in training, {heldOutStopwatch.Elapsed.TotalSeconds:0.#}s): mean fitness={heldOutTotal / OrionsBeltHeldOutSeedCount:0.#} winRateAny={(double)heldOutAnyWins / OrionsBeltHeldOutSeedCount:P0} winRateAll={(double)heldOutAllWins / OrionsBeltHeldOutSeedCount:P0} lossRate={(double)heldOutLosses / OrionsBeltHeldOutSeedCount:P0}");

        Console.WriteLine($"--- gene-vs-outcome analysis over {allEvaluated.Count} total evaluated candidates ---");
        ReportGeneCorrelation("Proximity", allEvaluated, c => c.Genome.Proximity);
        ReportGeneCorrelation("Focus", allEvaluated, c => c.Genome.Focus);
        ReportGeneCorrelation("TrendWeight", allEvaluated, c => c.Genome.TrendWeight);
        ReportGeneCorrelation("CenterOfGravity", allEvaluated, c => c.Genome.CenterOfGravity);
        ReportGeneCorrelation("Composition", allEvaluated, c => c.Genome.Composition);
        ReportGeneCorrelation("HeavyRange", allEvaluated, c => c.Genome.HeavyRange);
        ReportGeneCorrelation("AttackSize", allEvaluated, c => c.Genome.AttackSize);
        ReportGeneCorrelation("IsspTarget", allEvaluated, c => c.Genome.IsspTarget);

        await Task.CompletedTask;
    }

    // ---- Diagnostic-only, temporary: fleet dispatch instrumentation ----
    // Not part of the GA -- a one-off tool to look at real attack-dispatch data (fleet power/
    // composition, target defense, home-capital defense at dispatch time) from actual playouts, to
    // find out mechanically why 2+ simultaneous opponents collapses win rate so much harder than the
    // opponent-count alone would suggest. Set NPE_DIAG=1 to get per-dispatch DIAG lines from
    // NpeToolkit.DeployBattleFleet (temporary instrumentation there too).
    [Test, Explicit]
    public async Task DiagnoseFleetDispatch()
    {
        // A representative "aggressive" genome -- Off high/Def low, matching the best-genome shape
        // every round this session converged toward. New genes held at 0 (no-op) since this is about
        // baseline dispatch mechanics, not testing those genes.
        var genome = new GeneVector(
            Imperialist: 60, Defensive: 7, Offensive: 88, Factor: 25, Provoke: 70, SphereX: 60,
            Proximity: 0, TrendWeight: 0, CenterOfGravity: 0, Focus: 0, Composition: 0, HeavyRange: 0);

        Environment.SetEnvironmentVariable("NPE_DIAG", "1");
        try {
            Console.WriteLine("=== East-vs-West (1v1), 3 seeds ===");
            var ewPath = Path.Combine(PascalGroundTruth.PascalHarness.RepoRoot, "reference", "scenarios", "dos_131", "EASTWEST.SCN");
            var ewText = File.ReadAllText(ewPath);
            for (var seed = 0; seed < 3; seed++) {
                Console.WriteLine($"--- seed {seed} ---");
                var (score, outcome) = RunOneFitnessPlayout(ewText, genome, seed);
                Console.WriteLine($"RESULT seed={seed} outcome={outcome} score={score:0.#}");
            }

            Console.WriteLine("=== Orion's Belt (1v2), 3 seeds ===");
            var obPath = Path.Combine(PascalGroundTruth.PascalHarness.RepoRoot, "src", "Reconstructed4021.Tests", "Fixtures", "OrionsBelt.scn");
            var obText = File.ReadAllText(obPath);
            for (var seed = 0; seed < 3; seed++) {
                Console.WriteLine($"--- seed {seed} ---");
                var (score, eliminatedCount, opponentCount, evolvedEliminated, worldsConquered) =
                    RunOneFitnessPlayoutIntro(obText, genome, seed, OrionsBeltPlayers, OrionsBeltYearCap);
                Console.WriteLine($"RESULT seed={seed} eliminated={eliminatedCount}/{opponentCount} evolvedEliminated={evolvedEliminated} worldsConquered={worldsConquered} score={score:0.#}");
            }
        } finally {
            Environment.SetEnvironmentVariable("NPE_DIAG", null);
        }

        await Task.CompletedTask;
    }

    // ---- Diagnostic-only, temporary: verifies HeavyRangeGene's fixed monotonic cutoff ----
    // A direct call to DeployBattleFleet against two explicit, fixed-distance targets, bypassing
    // GetBestTarget's own target selection entirely -- a full-playout version of this check (tried
    // first) was inconclusive because the AI's target selection never chose the far world at all in
    // the seeds tried, for either the cheap or heavy dispatch, which is a fact about target selection
    // and defense/value scoring, not about this gate. Calling DeployBattleFleet directly with an
    // explicit target removes that confound. Not kept as a permanent regression test.
    [Test, Explicit]
    public async Task DiagnoseHeavyRangeGating()
    {
        // Checks whether the heavy wave actually gets dispatched (fleetStates gains a Starship-bearing
        // entry). "far" is deliberately 15, not farther: a pure-Starship fleet's own fuel range is
        // ~20 years at 1 sector/year (28.54 fuel capacity / 1.427 consumption per Starship,
        // FleetLogistics.cs), so a target right at that boundary conflates a real, separate fuel/range
        // abort with the withinHeavyRange gate this test exists to check -- 15 stays comfortably clear
        // of that so only the gate itself is under test.
        foreach (var heavyRange in new[] { 0, 10, 30 }) {
            foreach (var (label, distance) in new[] { ("near", 5), ("far", 15) }) {
                var attacker = new Empire { Name = "Attacker" };
                var defender = new Empire { Name = "Defender" };
                var fromWorld = new Planet {
                    Location = new Core.Galaxy.Coordinate(0, 0),
                    Owner = attacker,
                    Class = WorldClass.EarthLike,
                    TechLevel = TechLevel.Jump,
                    Efficiency = 100,
                    Type = WorldType.Capital,
                    Ships = new ShipCounts { Starships = 2000, Penetrators = 2000, Jumpships = 2000, HunterKillers = 2000 },
                };
                fromWorld.Cargo.Trillum = 9999;
                var target = new Planet { Location = new Core.Galaxy.Coordinate(distance, 0), Owner = defender, Class = WorldClass.EarthLike, TechLevel = TechLevel.Jump };

                var game = new Game(new Core.Galaxy.Galaxy(size: 21));
                game.Galaxy.Planets.AddRange([fromWorld, target]);
                var random = new Random(1);
                var fleetStates = new Dictionary<Fleet, KingdomFleetState>();

                NpeToolkit.DeployBattleFleet(attacker, fleetStates, fromWorld, 100_000, 0, NpeMissionType.JumpAttack, target, game, random, compositionGene: 80, heavyRangeGene: heavyRange);

                var heavyDispatched = fleetStates.Keys.Any(f => f.Ships.Starships > 0 || f.Ships.Penetrators > 0);
                Console.WriteLine($"HeavyRange={heavyRange,2} target={label,4} (distance={distance}): heavy wave dispatched={heavyDispatched}");
            }
        }

        await Task.CompletedTask;
    }

    // ---- Diagnostic-only, temporary: verifies AttackSizeGene actually shifts real WarCabinet dispatch ----
    // Calls WarCabinet directly (Conflict and War tiers) with AttackChance pinned to 100 and Balance to
    // 0 so the attack always fires, over many trials per AttackSizeGene value, and tallies how many
    // KingdomFleetState entries land as JumpAttack vs. SlowAttack. Not kept as a permanent regression
    // test -- a one-off check that the gene's shift actually reaches real dispatch behavior, not just
    // that the code compiles. Confirmed monotonic and matching the original fixed splits at 0 (within
    // sampling noise over 200 trials): Conflict 0=78% jump (vs. the real 75% baseline), War 0=49.5%
    // (vs. 50%); Conflict/War both hit 0% at AttackSize=-100 and 100% at AttackSize=100, the clamped
    // ends of the +/-50-point shift. persona.WorldPower must be set to a nonzero value for GetBestTarget
    // to ever pick a target at all (its own scoring factor multiplies by WorldPower/20) -- a real trap
    // for any future direct WarCabinet/DeployJumpAttack call in a test, not obvious from either
    // method's own signature.
    [Test, Explicit]
    public async Task DiagnoseAttackSizeGating()
    {
        const int trials = 200;
        foreach (var tier in new[] { PolicyType.Conflict, PolicyType.War }) {
            foreach (var attackSize in new[] { -100, 0, 100 }) {
                int jumpCount = 0, slowCount = 0;
                for (var trial = 0; trial < trials; trial++) {
                    var attacker = new Empire { Name = "Attacker" };
                    var defender = new Empire { Name = "Defender" };
                    var fromWorld = new Planet {
                        Location = new Core.Galaxy.Coordinate(0, 0),
                        Owner = attacker,
                        Class = WorldClass.EarthLike,
                        TechLevel = TechLevel.Jump,
                        Efficiency = 100,
                        Type = WorldType.Capital,
                        Ships = new ShipCounts { Starships = 2000, Penetrators = 2000, Jumpships = 2000, HunterKillers = 2000 },
                    };
                    fromWorld.Cargo.Trillum = 9999;
                    var target = new Planet { Location = new Core.Galaxy.Coordinate(5, 0), Owner = defender, Class = WorldClass.EarthLike, TechLevel = TechLevel.Jump, Population = 1000 };

                    var game = new Game(new Core.Galaxy.Galaxy(size: 21));
                    game.Galaxy.Planets.AddRange([fromWorld, target]);
                    game.Empires.AddRange([attacker, defender]);
                    attacker.Planets.MarkKnown(target);
                    var random = new Random(trial);
                    var fleetStates = new Dictionary<Fleet, KingdomFleetState>();
                    var state = new Dictionary<Empire, StateDeptRecord> {
                        [defender] = new StateDeptRecord { Policy = tier, AttackChance = 100, Balance = 0 },
                    };
                    var persona = new NpeCharacter { AttackSizeGene = attackSize, WorldPower = 50 };

                    NpeToolkit.WarCabinet(attacker, [fromWorld], fleetStates, persona, state, PolicyType.Neutral, game, random);

                    jumpCount += fleetStates.Values.Count(s => s.Mission == NpeMissionType.JumpAttack);
                    slowCount += fleetStates.Values.Count(s => s.Mission == NpeMissionType.SlowAttack);
                }
                var total = jumpCount + slowCount;
                var jumpPct = total == 0 ? 0 : 100.0 * jumpCount / total;
                Console.WriteLine($"tier={tier,-8} AttackSize={attackSize,4}: jump={jumpCount,3} slow={slowCount,3} (jump%={jumpPct:0.#})");
            }
        }

        await Task.CompletedTask;
    }

    // ---- Diagnostic-only, temporary: does fuel/trillum exhaustion actually happen in real runs? ----
    // Checks two things full playouts can reveal that the direct-call tests above can't: (1) real
    // pre-flight aborts (DeployBattleFleet's EDA>Range check) and mid-journey FleetOutOfFuel events,
    // via the new NPE_DIAG-FUEL/-FUELOUT lines added to NpeToolkit.DeployBattleFleet and
    // FleetMovementHandler.ConsumeFuel this round; (2) whether any world's TrillumReserve actually
    // approaches zero within a 120-year horizon. Runs a "heavy" genome (Composition=80, HeavyRange=30
    // -- unlimited on Orion's Belt's own map, so nothing gates dispatch) against a "cheap" control
    // (Composition=0) for a direct comparison.
    [Test, Explicit]
    public async Task DiagnoseFuelAndTrillumExhaustion()
    {
        var obPath = Path.Combine(PascalGroundTruth.PascalHarness.RepoRoot, "src", "Reconstructed4021.Tests", "Fixtures", "OrionsBelt.scn");
        var obText = File.ReadAllText(obPath);

        var heavyGenome = new GeneVector(
            Imperialist: 60, Defensive: 7, Offensive: 88, Factor: 25, Provoke: 70, SphereX: 60,
            Proximity: 0, TrendWeight: 0, CenterOfGravity: 0, Focus: 0, Composition: 80, HeavyRange: 30);
        var cheapGenome = heavyGenome with { Composition = 0, HeavyRange = 0 };

        Environment.SetEnvironmentVariable("NPE_DIAG", "1");
        try {
            foreach (var (label, genome) in new[] { ("HEAVY", heavyGenome), ("CHEAP", cheapGenome) }) {
                Console.WriteLine($"=== {label} genome (Composition={genome.Composition}, HeavyRange={genome.HeavyRange}), 5 seeds, 120-year horizon ===");
                for (var seed = 0; seed < 5; seed++) {
                    var random = new Random(seed);
                    var setup = new GalaxySetup(random);
                    var loader = new ScenarioLoader(setup, random);
                    var game = loader.Load(obText, OrionsBeltPlayers);
                    var startYear = game.Year;
                    var evolved = game.Empires.First(e => e.Name == "evolved");
                    var opponents = game.Empires.Where(e => e != evolved).ToList();

                    NpeToolkit.SetEmpireDefenses(evolved, random);
                    game.TurnHandlers[evolved] = new Kingdom2ModernTurnHandler(genome.ToPersona(), PolicyType.Harass, random);
                    foreach (var opponent in opponents) {
                        game.TurnHandlers[opponent] = new KingdomTurnHandler(opponent, NpeEmpireType.Kingdom1, random);
                    }

                    var turnEngine = new TurnEngine(new VisibilityHandler(random), new FleetMovementHandler(random), new AnnualTickHandler(random));
                    var maxCalls = OrionsBeltYearCap * game.Empires.Count * 2;
                    for (var call = 0; call < maxCalls; call++) {
                        turnEngine.AdvanceOneTurn(game);
                        var evolvedAlive = evolved.Status != EmpireStatus.Eliminated;
                        var opponentsRemaining = opponents.Count(o => o.Status != EmpireStatus.Eliminated);
                        if (!evolvedAlive || opponentsRemaining == 0 || game.Year - startYear >= OrionsBeltYearCap) {
                            break;
                        }
                    }

                    var evolvedWorlds = game.Galaxy.Planets.Where(p => p.Owner == evolved).ToList();
                    var minReserve = evolvedWorlds.Count == 0 ? -1 : evolvedWorlds.Min(p => p.TrillumReserve);
                    var maxReserve = evolvedWorlds.Count == 0 ? -1 : evolvedWorlds.Max(p => p.TrillumReserve);
                    Console.WriteLine($"seed={seed} finalYear={game.Year} evolvedWorlds={evolvedWorlds.Count} evolvedTrillumReserve=[min={minReserve} max={maxReserve}]");
                }
            }
        } finally {
            Environment.SetEnvironmentVariable("NPE_DIAG", null);
        }

        await Task.CompletedTask;
    }

    // ---- Diagnostic-only: does our own gene work make production waste/shortage worse? ----
    // Compares the slot-0 empire's own ProductionDiagnostics totals (storage-cap overflow, industry
    // growth lost to metals shortage, ships lost to shortage) between two conditions on the same
    // scenario/seeds: an ordinary Kingdom1 baseline in that seat, versus Kingdom2ModernTurnHandler
    // with a representative aggressive+composition genome in that same seat. ProductionDiagnostics has
    // no per-empire breakdown by default (a whole-game accumulator, fine for the earlier Intro-baseline
    // round, which only needed a single empire's own scenario anyway) -- ProductionDiagnostics.Tracks
    // was added specifically for this comparison, gated by a nullable FilterEmpire so the original
    // whole-game behavior (FilterEmpire=null) is unchanged for every existing caller.
    //
    // No persisted "best-ever" genome vector exists on disk from the Composition/HeavyRange/AttackSize
    // rounds (their console output wasn't saved) -- this reuses DiagnoseFleetDispatch's own
    // representative-aggressive shape (Off high/Def low, the shape every round converged toward) and
    // adds the two gene values those specific rounds' reports did record explicitly: Composition=44
    // (HeavyRangeGene round's best-ever) and AttackSize=19 (AttackSizeGene round's best-ever).
    // TrendWeight/CenterOfGravity/Focus/HeavyRange have no single recorded "best" value to replay (they
    // showed weak or inconsistent signals across rounds) -- held at representative mid-range values
    // rather than 0, so this genome exercises the same decision paths a real evolved candidate would,
    // not just the two genes this round cares about in isolation.
    [Test, Explicit]
    public async Task CompareProductionWasteBaselineVsEvolved()
    {
        var obPath = Path.Combine(PascalGroundTruth.PascalHarness.RepoRoot, "src", "Reconstructed4021.Tests", "Fixtures", "OrionsBelt.scn");
        var obText = File.ReadAllText(obPath);

        var evolvedGenome = new GeneVector(
            Imperialist: 60, Defensive: 7, Offensive: 88, Factor: 25, Provoke: 70, SphereX: 60,
            Proximity: 0, TrendWeight: 20, CenterOfGravity: 20, Focus: 30, Composition: 44, HeavyRange: 15, AttackSize: 19);

        const int seedCount = 5;

        void RunCondition(string label, bool evolvedSlotUsesGene)
        {
            ProductionDiagnostics.Reset();
            ProductionDiagnostics.Enabled = true;
            try {
                for (var seed = 0; seed < seedCount; seed++) {
                    var random = new Random(seed);
                    var setup = new GalaxySetup(random);
                    var loader = new ScenarioLoader(setup, random);
                    var game = loader.Load(obText, OrionsBeltPlayers);
                    var startYear = game.Year;
                    var evolved = game.Empires.First(e => e.Name == "evolved");
                    var opponents = game.Empires.Where(e => e != evolved).ToList();

                    ProductionDiagnostics.FilterEmpire = evolved;

                    if (evolvedSlotUsesGene) {
                        NpeToolkit.SetEmpireDefenses(evolved, random);
                        game.TurnHandlers[evolved] = new Kingdom2ModernTurnHandler(evolvedGenome.ToPersona(), PolicyType.Harass, random);
                    } else {
                        game.TurnHandlers[evolved] = new KingdomTurnHandler(evolved, NpeEmpireType.Kingdom1, random);
                    }
                    foreach (var opponent in opponents) {
                        game.TurnHandlers[opponent] = new KingdomTurnHandler(opponent, NpeEmpireType.Kingdom1, random);
                    }

                    var turnEngine = new TurnEngine(new VisibilityHandler(random), new FleetMovementHandler(random), new AnnualTickHandler(random));
                    var maxCalls = OrionsBeltYearCap * game.Empires.Count * 2;
                    for (var call = 0; call < maxCalls; call++) {
                        turnEngine.AdvanceOneTurn(game);
                        var evolvedAlive = evolved.Status != EmpireStatus.Eliminated;
                        var opponentsRemaining = opponents.Count(o => o.Status != EmpireStatus.Eliminated);
                        if (!evolvedAlive || opponentsRemaining == 0 || game.Year - startYear >= OrionsBeltYearCap) {
                            break;
                        }
                    }
                }

                Console.WriteLine($"=== {label}: slot-0 ({(evolvedSlotUsesGene ? "Kingdom2Modern, evolved genome" : "Kingdom1 baseline")}) totals across {seedCount} seeds, {OrionsBeltYearCap}-year horizon ===");
                Console.WriteLine($"  overflow lost (per cargo type): {string.Join(", ", ProductionDiagnostics.OverflowLost.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}"))}");
                Console.WriteLine($"  gross cargo produced (per cargo type): {string.Join(", ", ProductionDiagnostics.GrossCargoProduced.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}"))}");
                Console.WriteLine($"  industry growth lost to metals shortage (per industry): {string.Join(", ", ProductionDiagnostics.IndustryGrowthLostToMetalsShortage.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}"))}");
                Console.WriteLine($"  ships lost to shortage (per ship type): {string.Join(", ", ProductionDiagnostics.ShipsLostToShortage.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}"))}");
                Console.WriteLine($"  gross ships produced (per ship type): {string.Join(", ", ProductionDiagnostics.GrossShipsProduced.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}"))}");
                Console.WriteLine($"  cargo lost to shortage (per cargo type): {string.Join(", ", ProductionDiagnostics.CargoLostToShortage.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}"))}");
            } finally {
                ProductionDiagnostics.Enabled = false;
                ProductionDiagnostics.FilterEmpire = null;
            }
        }

        RunCondition("BASELINE", evolvedSlotUsesGene: false);
        RunCondition("EVOLVED", evolvedSlotUsesGene: true);

        await Task.CompletedTask;
    }

    // ---- Diagnostic-only: 2x2 factorial isolating Composition vs. AttackSize's contribution to the
    // extra overflow found in CompareProductionWasteBaselineVsEvolved. Every other new gene
    // (Proximity/TrendWeight/CenterOfGravity/Focus) is held at its true no-op default (0) in every
    // cell, so only Composition and AttackSize vary. The base six behavioral genes (Imperialist/
    // Defensive/Offensive/Factor/Provoke/SphereX) are held fixed at the same aggressive shape used in
    // the prior round's combined genome, in every cell -- they define "who this empire is" and
    // changing them would be a third confound, not a no-op.
    //
    // HeavyRangeGene is the one gene NOT held at 0 despite not being one of the two under test: it
    // gates whether a heavy wave fires at all (DeployBattleFleet's own `compositionGene > 0 &&
    // withinHeavyRange` check, distance <= heavyRangeGene) -- at 0 it only permits distance-0 targets,
    // which makes Composition inert regardless of its own value. Held at 30 (safely above Orion's
    // Belt's 19-max Chebyshev distance, i.e. "permissive") in all four cells so Composition's own
    // effect is actually testable; this is harmless in the Composition=0 cells since compositionGene>0
    // already gates the whole branch off there.
    [Test, Explicit]
    public async Task CompareProductionWasteFactorial()
    {
        var obPath = Path.Combine(PascalGroundTruth.PascalHarness.RepoRoot, "src", "Reconstructed4021.Tests", "Fixtures", "OrionsBelt.scn");
        var obText = File.ReadAllText(obPath);

        const int seedCount = 5;

        GeneVector Genome(double composition, double attackSize) => new(
            Imperialist: 60, Defensive: 7, Offensive: 88, Factor: 25, Provoke: 70, SphereX: 60,
            Proximity: 0, TrendWeight: 0, CenterOfGravity: 0, Focus: 0, Composition: composition, HeavyRange: 30, AttackSize: attackSize);

        void RunCell(string label, GeneVector genome)
        {
            ProductionDiagnostics.Reset();
            ProductionDiagnostics.Enabled = true;
            try {
                for (var seed = 0; seed < seedCount; seed++) {
                    var random = new Random(seed);
                    var setup = new GalaxySetup(random);
                    var loader = new ScenarioLoader(setup, random);
                    var game = loader.Load(obText, OrionsBeltPlayers);
                    var startYear = game.Year;
                    var evolved = game.Empires.First(e => e.Name == "evolved");
                    var opponents = game.Empires.Where(e => e != evolved).ToList();

                    ProductionDiagnostics.FilterEmpire = evolved;

                    NpeToolkit.SetEmpireDefenses(evolved, random);
                    game.TurnHandlers[evolved] = new Kingdom2ModernTurnHandler(genome.ToPersona(), PolicyType.Harass, random);
                    foreach (var opponent in opponents) {
                        game.TurnHandlers[opponent] = new KingdomTurnHandler(opponent, NpeEmpireType.Kingdom1, random);
                    }

                    var turnEngine = new TurnEngine(new VisibilityHandler(random), new FleetMovementHandler(random), new AnnualTickHandler(random));
                    var maxCalls = OrionsBeltYearCap * game.Empires.Count * 2;
                    for (var call = 0; call < maxCalls; call++) {
                        turnEngine.AdvanceOneTurn(game);
                        var evolvedAlive = evolved.Status != EmpireStatus.Eliminated;
                        var opponentsRemaining = opponents.Count(o => o.Status != EmpireStatus.Eliminated);
                        if (!evolvedAlive || opponentsRemaining == 0 || game.Year - startYear >= OrionsBeltYearCap) {
                            break;
                        }
                    }
                }

                var metalOverflow = ProductionDiagnostics.OverflowLost.GetValueOrDefault(CargoType.Metals);
                var metalGross = ProductionDiagnostics.GrossCargoProduced.GetValueOrDefault(CargoType.Metals);
                var cheOverflow = ProductionDiagnostics.OverflowLost.GetValueOrDefault(CargoType.Chemicals);
                var cheGross = ProductionDiagnostics.GrossCargoProduced.GetValueOrDefault(CargoType.Chemicals);
                var metalShortage = ProductionDiagnostics.IndustryGrowthLostToMetalsShortage.Values.Sum();
                var shipsLost = ProductionDiagnostics.ShipsLostToShortage.Values.Sum();
                var shipsGross = ProductionDiagnostics.GrossShipsProduced.Values.Sum();

                Console.WriteLine($"=== {label} (Composition={genome.Composition}, AttackSize={genome.AttackSize}) across {seedCount} seeds ===");
                Console.WriteLine($"  metal overflow: {metalOverflow}/{metalGross} ({(metalGross == 0 ? 0 : 100.0 * metalOverflow / metalGross):0.0}%)");
                Console.WriteLine($"  chemical overflow: {cheOverflow}/{cheGross} ({(cheGross == 0 ? 0 : 100.0 * cheOverflow / cheGross):0.0}%)");
                Console.WriteLine($"  industry growth lost to metals shortage: {metalShortage}");
                Console.WriteLine($"  ships lost to shortage: {shipsLost}/{shipsGross} ({(shipsGross == 0 ? 0 : 100.0 * shipsLost / shipsGross):0.00}%)");
            } finally {
                ProductionDiagnostics.Enabled = false;
                ProductionDiagnostics.FilterEmpire = null;
            }
        }

        RunCell("Composition=0,  AttackSize=0 ", Genome(0, 0));
        RunCell("Composition=44, AttackSize=0 ", Genome(44, 0));
        RunCell("Composition=0,  AttackSize=19", Genome(0, 19));
        RunCell("Composition=44, AttackSize=19", Genome(44, 19));

        await Task.CompletedTask;
    }

    // ---- Diagnostic-only: 2x2x2 factorial isolating TrendWeight/CenterOfGravity/Focus's contribution
    // to the extra overflow found in CompareProductionWasteBaselineVsEvolved, now that
    // CompareProductionWasteFactorial ruled out Composition/AttackSize (both held at 0 here, along
    // with Proximity). HeavyRangeGene held at 30 for consistency with the prior factorial even though
    // it's moot with Composition=0. Base six genes fixed at the same aggressive shape as every prior
    // round in this file.
    [Test, Explicit]
    public async Task CompareProductionWasteFactorialTrend()
    {
        var obPath = Path.Combine(PascalGroundTruth.PascalHarness.RepoRoot, "src", "Reconstructed4021.Tests", "Fixtures", "OrionsBelt.scn");
        var obText = File.ReadAllText(obPath);

        const int seedCount = 5;

        GeneVector Genome(double trend, double cog, double focus) => new(
            Imperialist: 60, Defensive: 7, Offensive: 88, Factor: 25, Provoke: 70, SphereX: 60,
            Proximity: 0, TrendWeight: trend, CenterOfGravity: cog, Focus: focus, Composition: 0, HeavyRange: 30, AttackSize: 0);

        void RunCell(string label, GeneVector genome)
        {
            ProductionDiagnostics.Reset();
            ProductionDiagnostics.Enabled = true;
            try {
                for (var seed = 0; seed < seedCount; seed++) {
                    var random = new Random(seed);
                    var setup = new GalaxySetup(random);
                    var loader = new ScenarioLoader(setup, random);
                    var game = loader.Load(obText, OrionsBeltPlayers);
                    var startYear = game.Year;
                    var evolved = game.Empires.First(e => e.Name == "evolved");
                    var opponents = game.Empires.Where(e => e != evolved).ToList();

                    ProductionDiagnostics.FilterEmpire = evolved;

                    NpeToolkit.SetEmpireDefenses(evolved, random);
                    game.TurnHandlers[evolved] = new Kingdom2ModernTurnHandler(genome.ToPersona(), PolicyType.Harass, random);
                    foreach (var opponent in opponents) {
                        game.TurnHandlers[opponent] = new KingdomTurnHandler(opponent, NpeEmpireType.Kingdom1, random);
                    }

                    var turnEngine = new TurnEngine(new VisibilityHandler(random), new FleetMovementHandler(random), new AnnualTickHandler(random));
                    var maxCalls = OrionsBeltYearCap * game.Empires.Count * 2;
                    for (var call = 0; call < maxCalls; call++) {
                        turnEngine.AdvanceOneTurn(game);
                        var evolvedAlive = evolved.Status != EmpireStatus.Eliminated;
                        var opponentsRemaining = opponents.Count(o => o.Status != EmpireStatus.Eliminated);
                        if (!evolvedAlive || opponentsRemaining == 0 || game.Year - startYear >= OrionsBeltYearCap) {
                            break;
                        }
                    }
                }

                var metalOverflow = ProductionDiagnostics.OverflowLost.GetValueOrDefault(CargoType.Metals);
                var metalGross = ProductionDiagnostics.GrossCargoProduced.GetValueOrDefault(CargoType.Metals);
                var cheOverflow = ProductionDiagnostics.OverflowLost.GetValueOrDefault(CargoType.Chemicals);
                var cheGross = ProductionDiagnostics.GrossCargoProduced.GetValueOrDefault(CargoType.Chemicals);
                var metalShortage = ProductionDiagnostics.IndustryGrowthLostToMetalsShortage.Values.Sum();
                var shipsLost = ProductionDiagnostics.ShipsLostToShortage.Values.Sum();
                var shipsGross = ProductionDiagnostics.GrossShipsProduced.Values.Sum();

                Console.WriteLine($"=== {label} (Trend={genome.TrendWeight}, CoG={genome.CenterOfGravity}, Focus={genome.Focus}) across {seedCount} seeds ===");
                Console.WriteLine($"  metal overflow: {metalOverflow}/{metalGross} ({(metalGross == 0 ? 0 : 100.0 * metalOverflow / metalGross):0.0}%)");
                Console.WriteLine($"  chemical overflow: {cheOverflow}/{cheGross} ({(cheGross == 0 ? 0 : 100.0 * cheOverflow / cheGross):0.0}%)");
                Console.WriteLine($"  industry growth lost to metals shortage: {metalShortage}");
                Console.WriteLine($"  ships lost to shortage: {shipsLost}/{shipsGross} ({(shipsGross == 0 ? 0 : 100.0 * shipsLost / shipsGross):0.00}%)");
            } finally {
                ProductionDiagnostics.Enabled = false;
                ProductionDiagnostics.FilterEmpire = null;
            }
        }

        RunCell("Trend=0,  CoG=0,  Focus=0 ", Genome(0, 0, 0));
        RunCell("Trend=20, CoG=0,  Focus=0 ", Genome(20, 0, 0));
        RunCell("Trend=0,  CoG=20, Focus=0 ", Genome(0, 20, 0));
        RunCell("Trend=0,  CoG=0,  Focus=30", Genome(0, 0, 30));
        RunCell("Trend=20, CoG=20, Focus=0 ", Genome(20, 20, 0));
        RunCell("Trend=20, CoG=0,  Focus=30", Genome(20, 0, 30));
        RunCell("Trend=0,  CoG=20, Focus=30", Genome(0, 20, 30));
        RunCell("Trend=20, CoG=20, Focus=30", Genome(20, 20, 30));

        await Task.CompletedTask;
    }
}
