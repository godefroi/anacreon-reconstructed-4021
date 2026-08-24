using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Turns;

/// <summary>
/// Runs the annual economy tick (UPDATE.PAS:UpdateUniverse). One type, split across four files by
/// concern — there's no cross-file coupling to track beyond the constructor-injected
/// <c>random</c> and the shared primitives declared here:
/// <list type="bullet">
/// <item>AnnualTickHandler.cs (this file) — orchestration (<see cref="RunAnnualTick"/>/
/// <see cref="UpdateWorld"/>/<see cref="UpdateStarbase"/>) and shared primitives (Rnd/Jitter/
/// ClampResource/PascalRound/ChangeRevIndex).</item>
/// <item>AnnualTickHandler.Production.cs — the production pipeline (raw materials, industry,
/// ships/cargo) and SupplyLink/SurplusLink.</item>
/// <item>AnnualTickHandler.Population.cs — efficiency, tech level, population, food, ambrosia.</item>
/// <item>AnnualTickHandler.Revolution.cs — military buildup, revolution/rebellion, hostile life.</item>
/// <item>AnnualTickHandler.Empire.cs — empire-level tech research (NewTechLevel/GetChanceForNewTech).</item>
/// <item>AnnualTickHandler.Construction.cs — construction-site countdown/completion (UpdateConstruction),
/// creating Starbases/Stargates/minefields on completion.</item>
/// </list>
/// Covers planets (Commits 1-3: population/efficiency/revolution, production, tech level),
/// industrial-complex starbases (Commit 4: SupplyLink/SurplusLink, the rest of the pipeline gated on
/// Kind==IndustrialComplex), per-empire tech research (Commit 5a), and construction (Commit 5b).
/// </summary>
public sealed partial class AnnualTickHandler(Random random) : IAnnualTickHandler
{
    public void RunAnnualTick(Game game)
    {
        game.Year++;

        // Per-tick scratch accumulator for TotalRevolutionIndex. Pascal zeroes NewTotalRevIndex at
        // the top of UpdateUniverse (UPDATE.PAS:1445); only Rebellion writes to it (:657,671); and
        // UpdateEmpire SETS (not adds to) Empire.TotalRevIndex from it at the end of the tick (:432)
        // — so the value is replaced each year from that year's rebellion activity alone, not
        // accumulated across years. Committing it here (rather than waiting for the full UpdateEmpire
        // in a later commit) is what makes UpdateRevolution's TotalRevIndex(Emp) feedback read
        // (UPDATE.PAS:699) testable already.
        var newTotalRevIndex = new Dictionary<Empire, int>();

        foreach (var planet in game.Galaxy.Planets) {
            UpdateWorld(planet, game, newTotalRevIndex);
        }

        // Starbases run after every planet has completed its own full tick (UPDATE.PAS:1450-1466's
        // planet loop, then starbase loop) — SupplyLink pulls from neighbouring planets' this-year
        // cargo, not last year's.
        foreach (var starbase in game.Galaxy.Starbases) {
            UpdateStarbase(starbase, game, newTotalRevIndex);
        }

        // Snapshot: UpdateConstruction removes a completed site from this same list mid-iteration.
        foreach (var site in game.Galaxy.ConstructionSites.ToList()) {
            UpdateConstruction(site, game);
        }

        foreach (var empire in game.Empires) {
            empire.TotalRevolutionIndex = newTotalRevIndex.GetValueOrDefault(empire, 0);
            NewTechLevel(empire, game);
        }
    }

    /// <summary>
    /// UPDATE.PAS:1355-1391, planet branch.
    /// <code>
    /// Production pipeline (ProduceRawMaterial/GetIndustrialDistribution/UpdateIndustry/Production)
    /// UpdateEfficiency
    /// UpdateTechLevel
    /// UpdatePopulation
    /// UseUpFood
    /// UseUpAmbrosia
    /// UpdateMilitary
    /// [deferred: UpdateDefenses]                                                           &lt;- between
    /// UpdateRevolution
    /// HostileLife (if Class == Hostile)
    /// </code>
    /// </summary>
    private void UpdateWorld(Planet planet, Game game, Dictionary<Empire, int> newTotalRevIndex)
    {
        RunProductionPipeline(planet);
        UpdateEfficiency(planet);
        UpdateTechLevel(planet);
        UpdatePopulation(planet);
        UseUpFood(planet);
        UseUpAmbrosia(planet);
        UpdateMilitary(planet);
        UpdateRevolution(planet, game, newTotalRevIndex);

        if (planet.Class == WorldClass.Hostile) {
            HostileLife(planet);
        }
    }

    /// <summary>
    /// UPDATE.PAS:1392-1430, starbase branch. UpdateEfficiency/UpdateTechLevel run unconditionally for
    /// every starbase (deferred: UpdateDefenses, UPDATE.PAS:1429 — lands with the combat phase, which
    /// needs it as baseline defensive state); the rest of the economy pipeline — production
    /// (SupplyLink/SurplusLink-bracketed) and population/food/ambrosia/military/revolution — runs only
    /// for industrial complexes (STyp=cmp), gating the *entire* pipeline on being a complex, not just
    /// production (UPDATE.PAS:1420-1427).
    /// </summary>
    private void UpdateStarbase(Starbase starbase, Game game, Dictionary<Empire, int> newTotalRevIndex)
    {
        var isComplex = starbase.Kind == StarbaseKind.IndustrialComplex;

        if (isComplex) {
            RunProductionPipeline(starbase, () => SupplyLink(starbase, game.Galaxy), () => SurplusLink(starbase, game.Galaxy));
        }

        UpdateEfficiency(starbase);
        UpdateTechLevel(starbase);

        if (isComplex) {
            UpdatePopulation(starbase);
            UseUpFood(starbase);
            UseUpAmbrosia(starbase);
            UpdateMilitary(starbase);
            UpdateRevolution(starbase, game, newTotalRevIndex);
        }
    }

    /// <summary>Clamps a world's revolution index to [0,100] (PRIMINTR.PAS:ChangeRevIndex).</summary>
    private static void ChangeRevIndex(IEconomicWorld world, int change) =>
        world.RevolutionIndex = Math.Clamp(world.RevolutionIndex + change, 0, 100);

    /// <summary>Clamps a produced/consumed quantity to [0,MaxResources], truncating (Pascal source: MISC.PAS's ThgLmt).</summary>
    private static int ClampResource(double x) => PascalMath.ClampResource(x);

    /// <summary>Pascal's Round: nearest integer, halves away from zero (not banker's rounding).</summary>
    private static int PascalRound(double x) => PascalMath.PascalRound(x);

    /// <summary>Random integer in [min,max] inclusive; returns min if the range is empty or inverted (INT.PAS:Rnd).</summary>
    private int Rnd(int min, int max) => PascalMath.Rnd(random, min, max);

    /// <summary>Randomly varies a value by up to variation% in either direction (Pascal source: INT.PAS's RndVar).</summary>
    private int Jitter(int value, int variation) => PascalMath.Jitter(random, value, variation);
}
