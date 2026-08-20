using System.Collections.Frozen;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Turns;

/// <summary>
/// Runs the annual economy tick (UPDATE.PAS:UpdateUniverse). This commit covers population growth,
/// efficiency, food consumption, and revolution/rebellion for planets only — industry/production,
/// tech advancement, starbase economy, and construction/empire-level updates are later commits (see
/// docs/ROADMAP.md and the insertion-point map on <see cref="UpdateWorld"/>). HostileLife shipped
/// early with this commit (it's cheap and sits right after UpdateRevolution in Pascal) even though
/// it isn't its own roadmap line.
/// </summary>
public sealed class AnnualTickHandler(Random random) : IAnnualTickHandler
{
    private const int MaxResources = 9999;
    private const int SuppliesPerBillion = 25;

    private static readonly FrozenDictionary<WorldClass, int> _maxPopulationByClass = new Dictionary<WorldClass, int> {
        [WorldClass.Ambrosia] = 4830,
        [WorldClass.Arid] = 4100,
        [WorldClass.Artificial] = 3100,
        [WorldClass.Barren] = 2340,
        [WorldClass.ClassJ] = 4610,
        [WorldClass.ClassK] = 4600,
        [WorldClass.ClassL] = 4220,
        [WorldClass.ClassM] = 4800,
        [WorldClass.Desert] = 3580,
        [WorldClass.EarthLike] = 4500,
        [WorldClass.Forest] = 4710,
        [WorldClass.GasGiant] = 2010,
        [WorldClass.Hostile] = 4010,
        [WorldClass.Ice] = 1920,
        [WorldClass.Jungle] = 4720,
        [WorldClass.Ocean] = 3520,
        [WorldClass.Paradise] = 5000,
        [WorldClass.Poisonous] = 2100,
        [WorldClass.Ruins] = 4590,
        [WorldClass.Underground] = 4500,
        [WorldClass.Volcanic] = 3950,
    }.ToFrozenDictionary();

    /// <summary>Average population at 50% efficiency, by tech level (DATACNST.PAS:221-223).</summary>
    private static readonly FrozenDictionary<TechLevel, int> _basePopulationByTech = new Dictionary<TechLevel, int> {
        [TechLevel.PreTech] = 3,
        [TechLevel.Primitive] = 10,
        [TechLevel.PreAtomic] = 100,
        [TechLevel.Atomic] = 250,
        [TechLevel.PreWarp] = 500,
        [TechLevel.Warp] = 700,
        [TechLevel.Jump] = 1100,
        [TechLevel.Bio] = 1700,
        [TechLevel.Starship] = 2000,
        [TechLevel.PreGate] = 2500,
        [TechLevel.Gate] = 3000,
    }.ToFrozenDictionary();

    /// <summary>
    /// Starvation-driven revolution-index sensitivity by tech level: low-tech worlds are used to
    /// starving and revolt less readily than high-tech ones (UPDATE.PAS:1126-1128, a table local to
    /// UseUpFood in Pascal — distinct from the global TechAdj used by Commit 2's production formula).
    /// </summary>
    private static readonly FrozenDictionary<TechLevel, int> _starvationRevoltAdjustmentByTech = new Dictionary<TechLevel, int> {
        [TechLevel.PreTech] = 100,
        [TechLevel.Primitive] = 100,
        [TechLevel.PreAtomic] = 30,
        [TechLevel.Atomic] = 20,
        [TechLevel.PreWarp] = 15,
        [TechLevel.Warp] = 13,
        [TechLevel.Jump] = 12,
        [TechLevel.Bio] = 13,
        [TechLevel.Starship] = 14,
        [TechLevel.PreGate] = 14,
        [TechLevel.Gate] = 15,
    }.ToFrozenDictionary();

    /// <summary>% of population in military at 50 military index, by world type (DATACNST.PAS:351-356).</summary>
    private static readonly FrozenDictionary<WorldType, int> _optimumMilitaryByType = new Dictionary<WorldType, int> {
        [WorldType.Agricultural] = 5,
        [WorldType.Ambrosia] = 100,
        [WorldType.Base] = 200,
        [WorldType.BaseStarbase] = 200,
        [WorldType.Capital] = 200,
        [WorldType.Chemical] = 10,
        [WorldType.Independent] = 80,
        [WorldType.JumpshipBase] = 100,
        [WorldType.JumpshipBaseStarbase] = 100,
        [WorldType.Mine] = 20,
        [WorldType.NinjaWorld] = 150,
        [WorldType.Outpost] = 0,
        [WorldType.RawMaterialMine] = 20,
        [WorldType.RawMaterialMineStarbase] = 20,
        [WorldType.StarshipBase] = 150,
        [WorldType.StarshipBaseStarbase] = 150,
        [WorldType.TransportBase] = 80,
        [WorldType.TransportBaseStarbase] = 80,
        [WorldType.University] = 1,
        [WorldType.Terraform] = 10,
        [WorldType.TrillumMine] = 30,
    }.ToFrozenDictionary();

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

        foreach (var planet in game.Galaxy.Planets)
            UpdateWorld(planet, newTotalRevIndex);

        foreach (var empire in game.Empires)
            empire.TotalRevolutionIndex = newTotalRevIndex.GetValueOrDefault(empire, 0);
    }

    /// <summary>
    /// Full Pascal sequence, for when later commits land (UPDATE.PAS:1371-1390), with insertion
    /// points relative to what's implemented here:
    /// <code>
    /// [Commit 2: ProduceRawMaterial/GetIndustrialDistribution/UpdateIndustry/Production]  &lt;- before
    /// UpdateEfficiency
    /// [Commit 3: UpdateTechLevel]                                                          &lt;- between
    /// UpdatePopulation
    /// UseUpFood
    /// [Commit 2b: UseUpAmbrosia]                                                           &lt;- between
    /// [deferred: UpdateMilitary, UpdateDefenses]                                            &lt;- between
    /// UpdateRevolution
    /// HostileLife (if Class == Hostile)
    /// </code>
    /// </summary>
    private void UpdateWorld(Planet planet, Dictionary<Empire, int> newTotalRevIndex)
    {
        UpdateEfficiency(planet);
        UpdatePopulation(planet);
        UseUpFood(planet);
        UpdateRevolution(planet, newTotalRevIndex);

        if (planet.Class == WorldClass.Hostile)
            HostileLife(planet);
    }

    private void UpdateEfficiency(Planet planet)
    {
        var inc = planet.Owner.IsIndependent
            ? Rnd(0, 1)
            : planet.Efficiency switch {
                <= 25 => Rnd(5, 12),
                <= 50 => Rnd(3, 8),
                <= 75 => Rnd(2, 5),
                <= 90 => Rnd(0, 3),
                <= 99 => Rnd(0, 1),
                _ => 0,
            };

        planet.Efficiency = Math.Min(100, planet.Efficiency + inc);
    }

    private void UpdatePopulation(Planet planet)
    {
        var maxPop = _maxPopulationByClass[planet.Class];
        var basePop = _basePopulationByTech[planet.TechLevel];

        double increase;
        if (planet.Population > maxPop)
            increase = Rnd(-10, 10);
        else if (planet.Population < 75)
            increase = Rnd(2, 5);
        else if (planet.Population > basePop)
            increase = basePop / 100.0;
        else
            increase = 128.0 * planet.Population / maxPop;

        planet.Population += PascalRound(increase);
    }

    private void UseUpFood(Planet planet)
    {
        var foodNeeded = ThgLmt((planet.Population / 100.0) * SuppliesPerBillion);

        if (foodNeeded > planet.Cargo.Supplies) {
            var lack = foodNeeded - planet.Cargo.Supplies;
            planet.Cargo.Supplies = 0;

            var starve = Math.Min(lack / 6, planet.Population / 10);
            planet.Population -= starve;

            if (starve > 0) {
                var revInc = Math.Min(
                    (int)(_starvationRevoltAdjustmentByTech[planet.TechLevel] * (starve / 10.0)),
                    45);
                ChangeRevIndex(planet, revInc);
            }
        } else {
            planet.Cargo.Supplies -= foodNeeded;
        }
    }

    private void UpdateRevolution(Planet planet, Dictionary<Empire, int> newTotalRevIndex)
    {
        var owner = planet.Owner;

        // Decrease revolution index (UPDATE.PAS:698-703).
        var empRevAdj = RndVar(TotalRevIndex(owner), 50);
        if (planet.Type == WorldType.Capital)
            ChangeRevIndex(planet, -Rnd(20, 30));
        else
            ChangeRevIndex(planet, empRevAdj + Rnd(-5, 2));

        if (owner.IsIndependent)
            return;

        // Military presence affects revolution (UPDATE.PAS:705-735).
        var optimumMilitary = ThgLmt(RndVar(
            PascalRound(planet.Population / 150.0 * _optimumMilitaryByType[planet.Type]), 10));
        var military = ThgLmt(planet.Cargo.Legions + 5.0 * planet.Cargo.NinjaLegions);

        if (military > optimumMilitary) {
            if (planet.RevolutionIndex > 30) {
                var factor = Rnd(1, (military - optimumMilitary) / 100);
                ChangeRevIndex(planet, -factor);
            } else if (planet.Type != WorldType.Capital && planet.Type != WorldType.Base) {
                if (Rnd(1, 5) == 1)
                    ChangeRevIndex(planet, Rnd(5, 15));
            }
        }

        // Rebellion trigger (UPDATE.PAS:737-753). The news-only threshold tiers below 75 (RebelW1-4)
        // have no state effect and are skipped — no news subsystem yet.
        if (planet.RevolutionIndex > 75 && Rnd(1, 100) < planet.RevolutionIndex && planet.Type != WorldType.Capital)
            Rebellion(planet, military, newTotalRevIndex);
    }

    private void Rebellion(Planet planet, int military, Dictionary<Empire, int> newTotalRevIndex)
    {
        var owner = planet.Owner;
        var rebels = Math.Max(1, ThgLmt(Math.Sqrt(planet.Population) * 65));
        var menLost = rebels / 5;
        var chanceToEndRebel = military / Math.Sqrt(rebels) * 1.414213;

        var lost = Math.Min(planet.Cargo.Legions, menLost);
        planet.Cargo.Legions -= lost;
        menLost -= lost;
        lost = Math.Min(planet.Cargo.NinjaLegions, menLost / 5);
        planet.Cargo.NinjaLegions -= lost;

        if (Rnd(1, 100) < chanceToEndRebel) {
            // Empire puts down the rebellion.
            ChangeRevIndex(planet, Rnd(-15, 5));
            newTotalRevIndex[owner] = newTotalRevIndex.GetValueOrDefault(owner) - Rnd(1, 5);
        } else {
            // World rebels and goes independent.
            planet.Owner = Empire.Independent;
            planet.Type = WorldType.Independent;
            // InitializeISSP resets self-sufficiency to DefaultISSP ($5555 — all four dials at the
            // midpoint of the 0-10 range, DATACNST.PAS:516).
            planet.SelfSufficiency.Chemical = 5;
            planet.SelfSufficiency.Metal = 5;
            planet.SelfSufficiency.Supply = 5;
            planet.SelfSufficiency.Trillum = 5;
            ChangeRevIndex(planet, -Rnd(40, 50));
            planet.Cargo.Legions = rebels;
            newTotalRevIndex[owner] = newTotalRevIndex.GetValueOrDefault(owner) + Rnd(5, 10);
        }

        planet.Population = ThgLmt(planet.Population - military / 1000.0);
        planet.Efficiency = Math.Max(0, planet.Efficiency - Rnd(5, 15));
    }

    private void HostileLife(Planet planet)
    {
        var menAdj = (planet.Efficiency + 50) * ((planet.Cargo.Legions + 5 * planet.Cargo.NinjaLegions) / 100.0);
        var chanceOfAttack = Math.Max(0, 25 - PascalRound((menAdj - 2000) / 100.0));

        if (Rnd(1, 100) <= chanceOfAttack) {
            if (Rnd(1, 100) <= 25) {
                var popKilled = Math.Min(planet.Population, Rnd(10, 50));
                planet.Population -= popKilled;
                ChangeRevIndex(planet, Rnd(5, 15));
            } else {
                var menKilled = Math.Min(planet.Cargo.Legions, Rnd(200, 300));
                var nnjKilled = Math.Min(planet.Cargo.NinjaLegions, Rnd(20, 50));
                planet.Cargo.Legions -= menKilled;
                planet.Cargo.NinjaLegions -= nnjKilled;
            }
        } else if (!planet.Owner.IsIndependent && Rnd(1, 100) <= 20) {
            planet.Cargo.NinjaLegions += Rnd(50, 150);
        }
    }

    /// <summary>Empire's revolution-index feedback into per-world changes (PRIMINTR.PAS:1097-1105).</summary>
    private static int TotalRevIndex(Empire empire) =>
        empire.IsIndependent ? 0 : empire.TotalRevolutionIndex + empire.RevolutionFactor;

    /// <summary>Clamps a world's revolution index to [0,100] (PRIMINTR.PAS:ChangeRevIndex).</summary>
    private static void ChangeRevIndex(Planet planet, int change) =>
        planet.RevolutionIndex = Math.Clamp(planet.RevolutionIndex + change, 0, 100);

    /// <summary>Clamps a produced/consumed quantity to [0,MaxResources], truncating (MISC.PAS:ThgLmt).</summary>
    private static int ThgLmt(double x) => x > MaxResources ? MaxResources : x < 0 ? 0 : (int)x;

    /// <summary>Pascal's Round: nearest integer, halves away from zero (not banker's rounding).</summary>
    private static int PascalRound(double x) => x >= 0 ? (int)(x + 0.5) : (int)(x - 0.5);

    /// <summary>Random integer in [min,max] inclusive; returns min if the range is empty or inverted (INT.PAS:Rnd).</summary>
    private int Rnd(int min, int max) => max <= min ? min : random.Next(max - min + 1) + min;

    /// <summary>Value +/- variation% of itself (INT.PAS:RndVar).</summary>
    private int RndVar(int value, int variation)
    {
        var spread = (int)(value * (variation / 100.0));
        return Rnd(value - spread, value + spread);
    }
}
