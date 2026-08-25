using System.Collections.Frozen;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Entities;

/// <summary>
/// Every "unlockable item" across <see cref="UnlockedTechnology"/>'s 4 category buckets, with the
/// minimum <see cref="TechLevel"/> it first appears at in Pascal's TechDev (DATACNST.PAS:359-370).
/// TechDev is monotonically increasing in TechLevel — every level's set is a superset of the previous
/// one's — so "first tech level it appears at" is equivalent to full set membership at every later
/// level, and much smaller to state than replicating all 11 raw sets. Shared between the annual-tick
/// handler (NewTechLevel's per-tick research rolls; ShipTechAvailable/CargoTechAvailable's production
/// gate) and new-game empire creation (seeding TechDev[Pred(Tech)] for a starting empire) — both need
/// the exact same "what's unlockable at tech level X" answer.
/// </summary>
/// <summary>Which of <see cref="UnlockedTechnology"/>'s 4 buckets a <see cref="TechCatalog.TechGrantIdentity"/> names.</summary>
public enum TechCategory
{
    Defense,
    Ship,
    Cargo,
    Construction,
}

public static class TechCatalog
{
    /// <summary>
    /// Identifies one catalog entry without boxing its concrete enum value: <see cref="Ordinal"/> is
    /// that value cast to <c>int</c>, disambiguated by <see cref="Category"/> since Pascal's single
    /// flat <c>TechnologyTypes</c> ordinal was already deliberately split into four typed enums when
    /// this port's data model was built — a bare <c>int</c> alone would be ambiguous across them.
    /// </summary>
    public readonly record struct TechGrantIdentity(TechCategory Category, int Ordinal);

    public static readonly FrozenDictionary<CargoType, TechLevel> MinTechForCargo = new Dictionary<CargoType, TechLevel> {
        [CargoType.Supplies] = TechLevel.PreTech,
        [CargoType.Legion] = TechLevel.Primitive,
        [CargoType.Metals] = TechLevel.Primitive,
        [CargoType.Chemicals] = TechLevel.PreAtomic,
        [CargoType.Trillum] = TechLevel.Atomic,
        [CargoType.Ambrosia] = TechLevel.Bio,
        [CargoType.NinjaLegion] = TechLevel.Starship,
    }.ToFrozenDictionary();

    /// <summary>See <see cref="MinTechForCargo"/>.</summary>
    public static readonly FrozenDictionary<ShipType, TechLevel> MinTechForShip = new Dictionary<ShipType, TechLevel> {
        [ShipType.Fighter] = TechLevel.PreWarp,
        [ShipType.Transport] = TechLevel.Warp,
        [ShipType.Jumpship] = TechLevel.Jump,
        [ShipType.Jumptransport] = TechLevel.Jump,
        [ShipType.HunterKiller] = TechLevel.Bio,
        [ShipType.Penetrator] = TechLevel.Bio,
        [ShipType.Starship] = TechLevel.Starship,
    }.ToFrozenDictionary();

    /// <summary>See <see cref="MinTechForCargo"/>. Read by NewTechLevel (research rolls) and by AnnualTickHandler.Defenses.cs's DefenseTechAvailable (UpdateDefenses' own per-world TechDev[Tech] gate).</summary>
    public static readonly FrozenDictionary<DefenseType, TechLevel> MinTechForDefense = new Dictionary<DefenseType, TechLevel> {
        [DefenseType.Gdm] = TechLevel.Atomic,
        [DefenseType.IonCannon] = TechLevel.Jump,
        [DefenseType.DefenseSatellite] = TechLevel.Bio,
        [DefenseType.Lam] = TechLevel.Starship,
    }.ToFrozenDictionary();

    /// <summary>See <see cref="MinTechForCargo"/>.</summary>
    public static readonly FrozenDictionary<ConstructionType, TechLevel> MinTechForConstruction = new Dictionary<ConstructionType, TechLevel> {
        [ConstructionType.Outpost] = TechLevel.Bio,
        [ConstructionType.Minefield] = TechLevel.Starship,
        [ConstructionType.CommandBase] = TechLevel.Starship,
        [ConstructionType.IndustrialComplex] = TechLevel.Starship,
        [ConstructionType.Fortress] = TechLevel.PreGate,
        [ConstructionType.WarpLink] = TechLevel.PreGate,
        [ConstructionType.Disrupter] = TechLevel.PreGate,
        [ConstructionType.Gate] = TechLevel.Gate,
    }.ToFrozenDictionary();

    /// <summary>
    /// One entry per unlockable item, ordered to match Pascal's single TechnologyTypes enum (LAM..dis)
    /// exactly — GetNewTech's Rnd(1,TechNumber) picks an index into that combined ordering, so
    /// golden-file RNG parity depends on this order: Defenses, then Ships, then Resources (CargoType),
    /// then Constructions. A plain field initializer (not Lazy) is safe here: everything this depends
    /// on is declared earlier in this same file, and C# runs one class's static field initializers in
    /// textual declaration order.
    /// </summary>
    private static readonly (TechLevel MinTech, Func<UnlockedTechnology, bool> IsUnlocked, Action<UnlockedTechnology> Unlock, TechGrantIdentity Identity)[] _catalog = BuildCatalog();

    private static (TechLevel, Func<UnlockedTechnology, bool>, Action<UnlockedTechnology>, TechGrantIdentity)[] BuildCatalog()
    {
        var entries = new List<(TechLevel, Func<UnlockedTechnology, bool>, Action<UnlockedTechnology>, TechGrantIdentity)>();

        foreach (var type in Enum.GetValues<DefenseType>())
            entries.Add((MinTechForDefense[type], t => t.Defenses.Contains(type), t => t.Defenses.Add(type),
                new TechGrantIdentity(TechCategory.Defense, (int)type)));
        foreach (var type in Enum.GetValues<ShipType>())
            entries.Add((MinTechForShip[type], t => t.Ships.Contains(type), t => t.Ships.Add(type),
                new TechGrantIdentity(TechCategory.Ship, (int)type)));
        foreach (var type in Enum.GetValues<CargoType>())
            entries.Add((MinTechForCargo[type], t => t.Resources.Contains(type), t => t.Resources.Add(type),
                new TechGrantIdentity(TechCategory.Cargo, (int)type)));
        foreach (var type in Enum.GetValues<ConstructionType>())
            entries.Add((MinTechForConstruction[type], t => t.Constructions.Contains(type), t => t.Constructions.Add(type),
                new TechGrantIdentity(TechCategory.Construction, (int)type)));

        return [.. entries];
    }

    /// <summary>
    /// Every catalog item unlocked by <paramref name="tech"/> but not yet in <paramref name="owned"/>,
    /// in catalog order (GetNewTech's PossibleTechSet-TechSet, UPDATE.PAS:370). Pairs each grant with
    /// its <see cref="TechGrantIdentity"/> so a caller that picks one (NewTechLevel, for its
    /// <c>NCapTech</c> news) knows which one it granted, not just how to grant it.
    /// </summary>
    public static List<(TechGrantIdentity Identity, Action<UnlockedTechnology> Unlock)> MissingTechAt(UnlockedTechnology owned, TechLevel tech) =>
        [.. _catalog.Where(e => e.MinTech <= tech && !e.IsUnlocked(owned)).Select(e => (e.Identity, e.Unlock))];

    /// <summary>
    /// A fresh, fully-unlocked-at-<paramref name="tech"/> set (Pascal's <c>TechDev[tech]</c> constant
    /// table, computed rather than stored — see EmpireFactory.SeedTechnology's own doc comment for why).
    /// Shared by EmpireFactory (a new empire's starting set) and Combat/CombatOutcome.cs's ConquerEmpire
    /// (a surviving empire's tech reset when it gets a new, differently-teched capital, Phase 5 commit
    /// 5f) — both need the identical "everything TechDev grants by this level" answer.
    /// </summary>
    public static UnlockedTechnology FullSetAt(TechLevel tech)
    {
        var full = new UnlockedTechnology();
        foreach (var (_, unlock) in MissingTechAt(new UnlockedTechnology(), tech)) {
            unlock(full);
        }
        return full;
    }

    /// <summary>
    /// A single named grant, for callers building an explicit tech list by real type (e.g. empire
    /// creation's scenario-specified "extra techs") rather than walking the whole catalog. Kept here
    /// rather than callers writing <c>t => t.Ships.Add(type)</c> inline so every grant of a given
    /// bucket goes through one place. Deliberately keyed on this port's own enums, not any external
    /// file format's numbering — a decoder for e.g. a scenario file's own ordinal space belongs at
    /// that format's parsing boundary, translating into these, not the other way around.
    /// </summary>
    public static Action<UnlockedTechnology> Grant(DefenseType type) => t => t.Defenses.Add(type);

    /// <summary>See <see cref="Grant(DefenseType)"/>.</summary>
    public static Action<UnlockedTechnology> Grant(ShipType type) => t => t.Ships.Add(type);

    /// <summary>See <see cref="Grant(DefenseType)"/>.</summary>
    public static Action<UnlockedTechnology> Grant(CargoType type) => t => t.Resources.Add(type);

    /// <summary>See <see cref="Grant(DefenseType)"/>.</summary>
    public static Action<UnlockedTechnology> Grant(ConstructionType type) => t => t.Constructions.Add(type);
}
