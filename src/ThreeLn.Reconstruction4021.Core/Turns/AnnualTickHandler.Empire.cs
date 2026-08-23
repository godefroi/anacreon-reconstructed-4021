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
    /// UPDATE.PAS:224-428 (NewTechLevel). UpdateEmpire's other line (committing NewTotalRevIndex) is
    /// already handled in <see cref="RunAnnualTick"/>; this is UpdateEmpire's only other behavior.
    /// Skips AddNews — no news subsystem yet, same precedent as every other UpdateWorld step.
    /// </summary>
    private void NewTechLevel(Empire emp, Game game)
    {
        var tech = emp.TechnologyLevel;

        // TechSet=TechDev[GteTchLvl]: every category fully researched, nothing left to ever roll for.
        if (TechCatalog.MissingTechAt(emp.Technology, TechLevel.Gate).Count == 0) {
            return;
        }

        var missingAtCurrentLevel = TechCatalog.MissingTechAt(emp.Technology, tech);
        var (chance, lab) = GetChanceForNewTech(emp, tech, game);

        if (Rnd(1, 100) > chance) {
            return;
        }

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
        lab?.TechLevel = newTech;

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
