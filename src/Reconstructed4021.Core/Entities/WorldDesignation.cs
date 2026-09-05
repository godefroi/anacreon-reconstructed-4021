using System.Collections.Frozen;
using Reconstructed4021.Core.Types;
using static Reconstructed4021.Core.PascalMath;

namespace Reconstructed4021.Core.Entities;

/// <summary>
/// DesignateWorld (INTRFACE.PAS:189-223) and the balance tables its own caller, DesignateCommand
/// (DESIGN.PAS:656-861), needs to build its menu. <see cref="PrincipalIndustry"/> lives here rather
/// than staying a private copy inside <see cref="Turns.AnnualTickHandler"/> (its only other reader)
/// so both share the one real table.
/// </summary>
public static class WorldDesignation
{
    /// <summary>
    /// Minimum tech level to designate a world to a given type (DATACNST.PAS:279-300) -- the WORLD's
    /// own <see cref="IEconomicWorld.TechLevel"/>, not the empire's unlocked-tech catalog (a
    /// deliberately different gate from <see cref="TechCatalog"/>'s own MinTechForX tables).
    /// </summary>
    public static readonly FrozenDictionary<WorldType, TechLevel> MinTechForType = new Dictionary<WorldType, TechLevel> {
        [WorldType.Agricultural] = TechLevel.PreTech,
        [WorldType.Ambrosia] = TechLevel.Bio,
        [WorldType.Base] = TechLevel.PreWarp,
        [WorldType.BaseStarbase] = TechLevel.Gate,
        [WorldType.Capital] = TechLevel.Jump,
        [WorldType.Chemical] = TechLevel.PreAtomic,
        [WorldType.Independent] = TechLevel.PreTech,
        [WorldType.JumpshipBase] = TechLevel.Jump,
        [WorldType.JumpshipBaseStarbase] = TechLevel.Gate,
        [WorldType.Mine] = TechLevel.Primitive,
        [WorldType.NinjaWorld] = TechLevel.Starship,
        [WorldType.Outpost] = TechLevel.Gate,
        [WorldType.RawMaterialMine] = TechLevel.Primitive,
        [WorldType.RawMaterialMineStarbase] = TechLevel.Gate,
        [WorldType.StarshipBase] = TechLevel.Bio,
        [WorldType.StarshipBaseStarbase] = TechLevel.Gate,
        [WorldType.TransportBase] = TechLevel.PreWarp,
        [WorldType.TransportBaseStarbase] = TechLevel.Gate,
        [WorldType.University] = TechLevel.Jump,
        [WorldType.Terraform] = TechLevel.Warp,
        [WorldType.TrillumMine] = TechLevel.Atomic,
    }.ToFrozenDictionary();

    /// <summary>Principal (population-scaling) industry per world type: PrincipalIndustry (DATACNST.PAS:493-514).</summary>
    public static readonly FrozenDictionary<WorldType, IndustryType> PrincipalIndustry = new Dictionary<WorldType, IndustryType> {
        [WorldType.Agricultural] = IndustryType.Supply,
        [WorldType.Ambrosia] = IndustryType.Bioindustry,
        [WorldType.Base] = IndustryType.ShipyardGeneral,
        [WorldType.BaseStarbase] = IndustryType.ShipyardGeneral,
        [WorldType.Capital] = IndustryType.ShipyardGeneral,
        [WorldType.Chemical] = IndustryType.Chemical,
        [WorldType.Independent] = IndustryType.ShipyardGeneral,
        [WorldType.JumpshipBase] = IndustryType.ShipyardJump,
        [WorldType.JumpshipBaseStarbase] = IndustryType.ShipyardJump,
        [WorldType.Mine] = IndustryType.Mining,
        [WorldType.NinjaWorld] = IndustryType.Bioindustry,
        [WorldType.Outpost] = IndustryType.ShipyardGeneral,
        [WorldType.RawMaterialMine] = IndustryType.Mining,
        [WorldType.RawMaterialMineStarbase] = IndustryType.Mining,
        [WorldType.StarshipBase] = IndustryType.ShipyardStarship,
        [WorldType.StarshipBaseStarbase] = IndustryType.ShipyardStarship,
        [WorldType.TransportBase] = IndustryType.ShipyardTransport,
        [WorldType.TransportBaseStarbase] = IndustryType.ShipyardTransport,
        [WorldType.University] = IndustryType.Mining,
        [WorldType.Terraform] = IndustryType.Mining,
        [WorldType.TrillumMine] = IndustryType.TrillumMining,
    }.ToFrozenDictionary();

    /// <summary>
    /// ClassName (CLSCOMM.PAS:38-61, read by ProductionCom's own Class: field) -- not
    /// designation-specific, but this is the one place a display name for <see cref="WorldClass"/>
    /// already lives, and Designate's own suitability hint (<see cref="SuitabilityHint"/>) is its
    /// first non-Production reader.
    /// </summary>
    public static string ClassName(WorldClass cls) => cls switch {
        WorldClass.Ambrosia => "Ambrosia",
        WorldClass.Arid => "Arid",
        WorldClass.Artificial => "Artificial",
        WorldClass.Barren => "Barren",
        WorldClass.ClassJ => "Class j",
        WorldClass.ClassK => "Class k",
        WorldClass.ClassL => "Class l",
        WorldClass.ClassM => "Class m",
        WorldClass.Desert => "Desert",
        WorldClass.EarthLike => "Earth-like",
        WorldClass.Forest => "Forest world",
        WorldClass.GasGiant => "Gas Giant",
        WorldClass.Hostile => "Hostile life",
        WorldClass.Ice => "Ice world",
        WorldClass.Jungle => "Jungle world",
        WorldClass.Ocean => "Ocean world",
        WorldClass.Paradise => "Paradise",
        WorldClass.Poisonous => "Poisonous",
        WorldClass.Ruins => "Ancient ruins",
        WorldClass.Underground => "Underground",
        WorldClass.Volcanic => "Volcanic",
        _ => throw new ArgumentOutOfRangeException(nameof(cls)),
    };

    /// <summary>
    /// TypeData's own Che/Min/Tri columns (DATACNST.PAS:469-491), for the world types whose industry
    /// mix is a fixed percentage split of "whatever's left after Supply" rather than solved per-tick
    /// from ISSP/class/tech (<see cref="Turns.AnnualTickHandler"/>'s own <c>_rawMaterialOnlyTypes</c>/
    /// <c>_typeData</c> carry the full 9-column balance row, including the Sup/PI columns those types
    /// don't use for this; this is deliberately just the 3 columns Designate's own hint needs, not a
    /// second copy of that whole table). University and Terraform's own three numbers don't sum to
    /// 100 in the shipped data -- confirmed against source, not a transcription slip -- the remainder
    /// simply isn't allocated to production for those two (their real value lies elsewhere: tech
    /// catch-up for University, nothing this port implements yet for Terraform).
    /// </summary>
    public static readonly FrozenDictionary<WorldType, (int Chemical, int Mining, int Trillum)> RawMaterialSplit =
        new Dictionary<WorldType, (int, int, int)> {
            [WorldType.Agricultural] = (40, 40, 20),
            [WorldType.Chemical] = (80, 10, 10),
            [WorldType.Mine] = (10, 80, 10),
            [WorldType.RawMaterialMine] = (40, 40, 20),
            [WorldType.RawMaterialMineStarbase] = (40, 40, 20),
            [WorldType.University] = (10, 10, 5),
            [WorldType.Terraform] = (30, 10, 20),
            [WorldType.TrillumMine] = (10, 10, 80),
        }.ToFrozenDictionary();

    /// <summary>
    /// Not real Pascal: DesignateCommand's own menu gives no hint at all about what a designation
    /// actually changes -- optimally choosing meant cross-referencing manual tables by hand. The
    /// class-suitability percentage (<see cref="Turns.AnnualTickHandler.ClassIndustryAdjustment"/>,
    /// 100% = average) is its own column in the picker's own list (one line per candidate, so every
    /// choice's number is visible at once); this is the rest, for whichever candidate is currently
    /// highlighted -- one of two real, honest facts, not a full production forecast (tech level and
    /// population still shape the actual numbers either way): for types in
    /// <see cref="RawMaterialSplit"/>, that type's own fixed Che/Min/Tri split (confirmed from
    /// source: this world's own ISSP dials for those three industries genuinely play no part in that
    /// split -- only Supply's ISSP sizes the remainder pool being split); for every other type, this
    /// world's own CURRENT Chemical/Mining/Trillum ISSP dials, which do feed directly into how the
    /// Gamma/Beta system solves that type's principal industry (INTRFACE.PAS's own
    /// GetIndustrialDistribution) -- real numbers already known, not invented ones.
    /// </summary>
    public static string DesignationHint(IEconomicWorld world, WorldType candidateType)
    {
        // No hand-picked \n line breaks -- wording changes shouldn't have to keep them lined up with
        // the picker's own column width. The Tui side gives this enough rows and lets Label wrap it.
        if (RawMaterialSplit.TryGetValue(candidateType, out var split)) {
            var total = split.Chemical + split.Mining + split.Trillum;
            var unused = total < 100 ? $" The remaining {100 - total}% goes unused here." : "";
            return $"Fixed split of non-food industry: {split.Chemical}% chemical, {split.Mining}% mining, {split.Trillum}% trillum.{unused} " +
                "Food/supply production is set separately (population, ISSP) -- your own ISSP for these three doesn't change this split.";
        }

        var cheIssp = SelfSufficiencySettings.DisplayPercent(world.SelfSufficiencyIndex(IndustryType.Chemical));
        var minIssp = SelfSufficiencySettings.DisplayPercent(world.SelfSufficiencyIndex(IndustryType.Mining));
        var triIssp = SelfSufficiencySettings.DisplayPercent(world.SelfSufficiencyIndex(IndustryType.TrillumMining));
        return $"Your ISSP here: chemical {cheIssp}, mining {minIssp}, trillum {triIssp}. These feed directly into how much " +
            "of each actually gets produced (higher generally means more).";
    }

    /// <summary>TypeName (DATACNST.PAS:71-92) -- bare noun phrase. DesignateCommand's own local "a/an X" narrative phrasing is Tui-side text (GameShell's own copy), not this table.</summary>
    public static string TypeName(WorldType type) => type switch {
        WorldType.Agricultural => "agricultural world",
        WorldType.Ambrosia => "ambrosia world",
        WorldType.Base => "base planet",
        WorldType.BaseStarbase => "base planet",
        WorldType.Capital => "capital",
        WorldType.Chemical => "chemical planet",
        WorldType.Independent => "independent world",
        WorldType.JumpshipBase => "jumpship base",
        WorldType.JumpshipBaseStarbase => "jumpship base",
        WorldType.Mine => "metal mine",
        WorldType.NinjaWorld => "ninja world",
        WorldType.Outpost => "outpost",
        WorldType.RawMaterialMine => "raw material mine",
        WorldType.RawMaterialMineStarbase => "raw material mine",
        WorldType.StarshipBase => "starship base",
        WorldType.StarshipBaseStarbase => "starship base",
        WorldType.TransportBase => "transport base",
        WorldType.TransportBaseStarbase => "transport base",
        WorldType.University => "university world",
        WorldType.Terraform => "terraforming",
        WorldType.TrillumMine => "trillum mine",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>
    /// DesignateWorld (INTRFACE.PAS:189-223). When <paramref name="newType"/> is Capital, first runs
    /// the capital-swap sequence: adjusts the empire's overall tech level if the new capital's own
    /// tech differs from the old one (<see cref="Combat.CombatOutcome.SetEmpireTechnology"/>, same
    /// formula <see cref="Combat.CombatOutcome.ConquerEmpire"/>'s own capital-swap uses), demotes the
    /// old capital to a plain Base, and bumps the empire's total unrest
    /// (<see cref="Combat.CombatOutcome.ChangeTotalRevIndex"/>) -- distinct from a conquered capital's
    /// own <c>ChangeRevIndex</c> (a single world's local index, different magnitude and sign; these
    /// are two separate real Pascal procedures, not one reused across both call sites). The efficiency
    /// penalty below (<c>Eff-Round(Eff/(1.5+Random))</c>) fires unconditionally, including on the old
    /// capital when swapping and on <paramref name="world"/> itself even when <paramref name="newType"/>
    /// equals its current type -- matching real Pascal exactly, which never special-cases a no-op
    /// redesignation.
    ///
    /// Generalizes NpeToolkit's own private RedesignateWorldType (ReDesignateEmpire never redesignates
    /// to Capital, so it never needed this branch) -- that method now calls this one directly instead
    /// of keeping a second copy.
    /// </summary>
    public static void Redesignate(IEconomicWorld world, WorldType newType, Random random)
    {
        if (newType == WorldType.Capital) {
            var emp = world.Owner;
            var oldCapital = emp.Capital!;
            var oldTech = oldCapital.TechLevel;
            var newTech = world.TechLevel;

            if (oldTech > newTech) {
                Combat.CombatOutcome.SetEmpireTechnology(emp, newTech, newTech);
            } else if (oldTech < newTech) {
                Combat.CombatOutcome.SetEmpireTechnology(emp, newTech, newTech - 1);
            }

            emp.Capital = world;
            oldCapital.Type = WorldType.Base;
            oldCapital.Efficiency -= PascalRound(oldCapital.Efficiency / (1.5 + random.NextDouble()));
            Combat.CombatOutcome.ChangeTotalRevIndex(emp, Rnd(random, 35, 45));
        }

        world.Efficiency -= PascalRound(world.Efficiency / (1.5 + random.NextDouble()));
        world.Type = newType;
    }
}
