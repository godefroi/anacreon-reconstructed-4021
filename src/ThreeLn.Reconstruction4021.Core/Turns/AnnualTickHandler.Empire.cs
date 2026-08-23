using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Turns;

public sealed partial class AnnualTickHandler
{
    private const int TechIncCap = 12;      // % chance for capital (DATACNST.PAS:56)
    private const int TechIncUnv = 15;      // % chance per university world (:57)
    private const int TechIncRns = 5;       // % chance per ruins world (:58)
    private const int TechIncUnvRns = 17;   // % chance for university on ruins world (:59)

    /// <summary>
    /// One "unlockable item" across all 4 of Empire.Technology's category buckets, ordered to match
    /// Pascal's single TechnologyTypes enum (LAM..dis) exactly — GetNewTech's Rnd(1,TechNumber) picks
    /// an index into that combined ordering, so golden-file RNG parity depends on this order:
    /// Defenses, then Ships, then Resources (CargoType), then Constructions.
    ///
    /// Lazy, not a plain field initializer: this class's static fields are split across partial
    /// files (this one and AnnualTickHandler.Production.cs, which declares _minTechForDefense/
    /// _minTechForShip/_minTechForCargo/_minTechForConstruction), and all of a type's partial-file
    /// field initializers run in one shared static constructor in file-compilation order — not
    /// dependency order. A plain initializer here read those tables as still-null on the first build,
    /// since this file happens to compile first alphabetically. Lazy defers BuildTechCatalog until
    /// first access, by which point the whole static constructor (every partial file's fields) has
    /// already finished.
    /// </summary>
    private static readonly Lazy<(TechLevel MinTech, Func<UnlockedTechnology, bool> IsUnlocked, Action<UnlockedTechnology> Unlock)[]> _techCatalog =
        new(BuildTechCatalog);

    private static (TechLevel, Func<UnlockedTechnology, bool>, Action<UnlockedTechnology>)[] BuildTechCatalog()
    {
        var entries = new List<(TechLevel, Func<UnlockedTechnology, bool>, Action<UnlockedTechnology>)>();

        foreach (var type in Enum.GetValues<DefenseType>())
            entries.Add((_minTechForDefense[type], t => t.Defenses.Contains(type), t => t.Defenses.Add(type)));
        foreach (var type in Enum.GetValues<ShipType>())
            entries.Add((_minTechForShip[type], t => t.Ships.Contains(type), t => t.Ships.Add(type)));
        foreach (var type in Enum.GetValues<CargoType>())
            entries.Add((_minTechForCargo[type], t => t.Resources.Contains(type), t => t.Resources.Add(type)));
        foreach (var type in Enum.GetValues<ConstructionType>())
            entries.Add((_minTechForConstruction[type], t => t.Constructions.Contains(type), t => t.Constructions.Add(type)));

        return [.. entries];
    }

    /// <summary>Every catalog item unlocked by <paramref name="tech"/> but not yet in <paramref name="owned"/>, in catalog order (GetNewTech's PossibleTechSet-TechSet, UPDATE.PAS:370).</summary>
    private static List<Action<UnlockedTechnology>> MissingTechAt(UnlockedTechnology owned, TechLevel tech) =>
        [.. _techCatalog.Value.Where(e => e.MinTech <= tech && !e.IsUnlocked(owned)).Select(e => e.Unlock)];

    /// <summary>
    /// UPDATE.PAS:224-428 (NewTechLevel). UpdateEmpire's other line (committing NewTotalRevIndex) is
    /// already handled in <see cref="RunAnnualTick"/>; this is UpdateEmpire's only other behavior.
    /// Skips AddNews — no news subsystem yet, same precedent as every other UpdateWorld step.
    /// </summary>
    private void NewTechLevel(Empire emp, Game game)
    {
        var tech = emp.TechnologyLevel;

        // TechSet=TechDev[GteTchLvl]: every category fully researched, nothing left to ever roll for.
        if (MissingTechAt(emp.Technology, TechLevel.Gate).Count == 0)
            return;

        var missingAtCurrentLevel = MissingTechAt(emp.Technology, tech);
        var (chance, lab) = GetChanceForNewTech(emp, tech, game);

        if (Rnd(1, 100) > chance)
            return;

        if (missingAtCurrentLevel.Count > 0) {
            // "new technology" branch (UPDATE.PAS:388-401): TechSet<>TechDev[Tech] still.
            missingAtCurrentLevel[Rnd(1, missingAtCurrentLevel.Count) - 1](emp.Technology);
            return;
        }

        // "new tech level" branch (UPDATE.PAS:402-426): TechSet=TechDev[Tech], advance the level.
        // Does NOT grant the new level's techs — the next tick's MissingTechAt(tech) starts nonempty
        // again relative to the higher TechDev[Tech].
        var newTech = tech + 1;
        emp.TechnologyLevel = newTech;

        // A null lab only happens when GetChanceForNewTech found zero labs (chance=0), which already
        // returned above via the Rnd(1,100)>chance check — defensive, not a modeled game state, same
        // category as UpdateTechLevel's null-capital no-op.
        if (lab is not null)
            lab.TechLevel = newTech;

        // Make all other university/capital worlds at the old level go up too (UPDATE.PAS:412-421) —
        // planets only, matching Pascal exactly; starbases are excluded from this specific sweep.
        foreach (var planet in game.Galaxy.Planets) {
            if (planet.Owner == emp && planet.TechLevel == tech
                && (planet.Type == WorldType.Capital || planet.Type == WorldType.University)) {
                planet.TechLevel = newTech;
            }
        }
    }

    /// <summary>
    /// UPDATE.PAS:234-356. Returns the empire-wide per cent chance of gaining new technology this
    /// year (the sum of every eligible lab's individual chance) and which world gets credited as the
    /// lab (chosen by weighted random walk over the lab list — cosmetic bookkeeping for the world
    /// whose TechLevel gets bumped in the "new tech level" branch, since the chance itself is already
    /// summed across every lab regardless of which one wins the walk).
    /// </summary>
    private (int Chance, IEconomicWorld? Lab) GetChanceForNewTech(Empire emp, TechLevel empTech, Game game)
    {
        var labs = new List<(IEconomicWorld World, int Chance)>();

        foreach (var planet in game.Galaxy.Planets) {
            if (planet.Owner != emp)
                continue;
            if (labs.Count >= 20)
                break;
            var chance = LabChance(planet, empTech, includePlanetOnlyBranches: true);
            if (chance is not null)
                labs.Add((planet, chance.Value));
        }

        foreach (var starbase in game.Galaxy.Starbases) {
            if (starbase.Owner != emp)
                continue;
            if (labs.Count >= 20)
                break;
            var chance = LabChance(starbase, empTech, includePlanetOnlyBranches: false);
            if (chance is not null)
                labs.Add((starbase, chance.Value));
        }

        var totalChance = labs.Sum(l => l.Chance);
        var roll = Rnd(1, totalChance);
        foreach (var (world, chance) in labs) {
            if (roll <= chance)
                return (totalChance, world);
            roll -= chance;
        }

        // Loop should never exit here (source comment, UPDATE.PAS:351), but nothing bad happens if it
        // does — only reachable when there are zero labs, in which case totalChance is 0 and the
        // caller's Rnd(1,100)<=chance gate can never pass regardless of what Lab is.
        return (totalChance, emp.Capital);
    }

    /// <summary>
    /// Per-lab chance cascade (UPDATE.PAS:275-306/317-328). Starbases only get the first two branches
    /// — <paramref name="includePlanetOnlyBranches"/>=false skips the "any higher-tech world" and
    /// "ruins world" fallbacks, matching Pascal's separate, shorter starbase cascade exactly. The
    /// ruins branch is naturally unreachable for a starbase even without the flag (EffectiveClass is
    /// always Artificial), but the flag also mirrors the source's structural omission of the
    /// higher-tech fallback, which isn't otherwise implied by EffectiveClass alone.
    /// </summary>
    private static int? LabChance(IEconomicWorld world, TechLevel empTech, bool includePlanetOnlyBranches)
    {
        if (world.Type == WorldType.Capital)
            return Trunc(TechIncCap, world.Efficiency);

        if (world.Type == WorldType.University && world.TechLevel == empTech) {
            var pct = includePlanetOnlyBranches && world.EffectiveClass == WorldClass.Ruins ? TechIncUnvRns : TechIncUnv;
            return Trunc(pct, world.Efficiency);
        }

        if (includePlanetOnlyBranches) {
            if (world.TechLevel > empTech)
                return Trunc(TechIncUnv, world.Efficiency);
            if (world.EffectiveClass == WorldClass.Ruins)
                return Trunc(TechIncRns, world.Efficiency);
        }

        return null;
    }

    /// <summary>Trunc(percent*eff/100) — a true real-division truncation (UPDATE.PAS's Trunc calls), not integer division.</summary>
    private static int Trunc(int percent, int eff) => (int)(percent * eff / 100.0);
}
