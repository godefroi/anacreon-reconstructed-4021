using System.Collections.Frozen;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Turns;

/// <summary>
/// Efficiency, tech-level advancement, population growth, food, and ambrosia addiction
/// (UPDATE.PAS:1381-1385 planet / :1417,1422-1424 starbase). See AnnualTickHandler.cs for the type's
/// overall file layout.
/// </summary>
public sealed partial class AnnualTickHandler
{
    private const int SuppliesPerBillion = 25;

    // Ambrosia addiction constants (DATACNST.PAS:41-45).
    private const double DrugsPerBillion = 11.5;
    private const int ChanceToAddict = 25;
    private const double AddictDeathCoeff = 0.12;
    private const double AddictEffCoeff = 0.9;
    private const double AddictRevICoeff = 0.55;

    /// <summary>% chance a world advances toward its empire's capital tech level each tick (DATACNST.PAS:61).</summary>
    private const int TechLevelIncreaseChance = 16;

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
    /// UseUpFood in Pascal — distinct from the global TechAdj used by TotalProd).
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

    private void UpdateEfficiency(IEconomicWorld world)
    {
        var inc = world.Owner.IsIndependent
            ? Rnd(0, 1)
            : world.Efficiency switch {
                <= 25 => Rnd(5, 12),
                <= 50 => Rnd(3, 8),
                <= 75 => Rnd(2, 5),
                <= 90 => Rnd(0, 3),
                <= 99 => Rnd(0, 1),
                _ => 0,
            };

        world.Efficiency = Math.Min(100, world.Efficiency + inc);
    }

    /// <summary>
    /// UPDATE.PAS:1032-1072. Skips AddNews (no news subsystem yet, same precedent as every other
    /// UpdateWorld step). Independent worlds drift upward on their own (1-in-50 chance per tick);
    /// owned worlds instead chase their empire's capital tech level up or down. A world with no
    /// capital to compare against (Owner.Capital is null) is a state Pascal's GetCapital can't
    /// produce for a real empire — every empire is founded with one — so this is purely a defensive
    /// no-op for incomplete test/setup state, not a modeled game rule.
    /// </summary>
    private void UpdateTechLevel(IEconomicWorld world)
    {
        if (world.TechLevel == TechLevel.Gate)
            return;

        if (world.Owner.IsIndependent) {
            if (Rnd(1, 50) == 1)
                world.TechLevel++;
            return;
        }

        var capitalTech = world.Owner.Capital?.TechLevel;
        if (capitalTech is null)
            return;

        if (capitalTech > world.TechLevel) {
            if (Rnd(1, 100) <= TechLevelIncreaseChance)
                world.TechLevel++;
        } else if (capitalTech < world.TechLevel) {
            if (Rnd(1, 15) == 1)
                world.TechLevel--;
        }
    }

    private void UpdatePopulation(IEconomicWorld world)
    {
        var maxPop = _maxPopulationByClass[world.EffectiveClass];
        var basePop = _basePopulationByTech[world.TechLevel];

        double increase;
        if (world.Population > maxPop)
            increase = Rnd(-10, 10);
        else if (world.Population < 75)
            increase = Rnd(2, 5);
        else if (world.Population > basePop)
            increase = basePop / 100.0;
        else
            increase = 128.0 * world.Population / maxPop;

        world.Population += PascalRound(increase);
    }

    private void UseUpFood(IEconomicWorld world)
    {
        var foodNeeded = ClampResource((world.Population / 100.0) * SuppliesPerBillion);

        if (foodNeeded > world.Cargo.Supplies) {
            var lack = foodNeeded - world.Cargo.Supplies;
            world.Cargo.Supplies = 0;

            var starve = Math.Min(lack / 6, world.Population / 10);
            world.Population -= starve;

            if (starve > 0) {
                var revInc = Math.Min(
                    (int)(_starvationRevoltAdjustmentByTech[world.TechLevel] * (starve / 10.0)),
                    45);
                ChangeRevIndex(world, revInc);
            }
        } else {
            world.Cargo.Supplies -= foodNeeded;
        }
    }

    /// <summary>
    /// UPDATE.PAS:1163-1276. Skips AddNews — no news subsystem yet (same precedent as UseUpFood and
    /// UpdateRevolution) — but every state effect (population, efficiency, revolution index, tech
    /// level, industry, addiction flag) is kept.
    /// </summary>
    private void UseUpAmbrosia(IEconomicWorld world)
    {
        var ambNeeded = ClampResource((world.Population / 100.0) * DrugsPerBillion);

        if (world.IsAddictedToAmbrosia) {
            if (ambNeeded <= world.Cargo.Ambrosia) {
                world.Cargo.Ambrosia -= ambNeeded;
                return;
            }

            // Not enough ambrosia: people die, efficiency and revolution index suffer, and one of
            // four random side effects (nothing / riots / industrial sabotage / tech regression) fires.
            var lack = ambNeeded - world.Cargo.Ambrosia;
            world.Cargo.Ambrosia = 0;

            var die = Math.Min(ClampResource(AddictDeathCoeff * lack), world.Population / 7);
            world.Population -= die;

            var effChange = Math.Min((int)(AddictEffCoeff * die), world.Efficiency);
            world.Efficiency -= effChange;

            ChangeRevIndex(world, (int)(AddictRevICoeff * die));

            switch (Rnd(1, 10)) {
                case >= 5 and <= 7:
                    world.Population -= ClampResource((Rnd(50, 120) / 100.0) * die);
                    break;
                case 8 or 9:
                    foreach (var industry in Enum.GetValues<IndustryType>())
                        world.Industry[industry] -= (int)(world.Industry[industry] * Rnd(0, 20) / 100.0);
                    break;
                case 10:
                    if (world.TechLevel > TechLevel.PreTech)
                        world.TechLevel--;
                    break;
            }

            if (Rnd(1, 100) <= ChanceToAddict)
                world.IsAddictedToAmbrosia = false;
        } else if (world.Cargo.Ambrosia > 0) {
            if (ambNeeded <= world.Cargo.Ambrosia && Rnd(1, 100) < ChanceToAddict)
                world.IsAddictedToAmbrosia = true;

            ambNeeded /= 2;
            world.Cargo.Ambrosia = Math.Max(0, world.Cargo.Ambrosia - ambNeeded);
        }
    }
}
