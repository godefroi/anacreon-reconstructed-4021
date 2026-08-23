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
public static class TechCatalog
{
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

    /// <summary>See <see cref="MinTechForCargo"/>. No defense-tech gate exists in the production pipeline itself — only NewTechLevel reads this.</summary>
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
    private static readonly (TechLevel MinTech, Func<UnlockedTechnology, bool> IsUnlocked, Action<UnlockedTechnology> Unlock)[] _catalog = BuildCatalog();

    private static (TechLevel, Func<UnlockedTechnology, bool>, Action<UnlockedTechnology>)[] BuildCatalog()
    {
        var entries = new List<(TechLevel, Func<UnlockedTechnology, bool>, Action<UnlockedTechnology>)>();

        foreach (var type in Enum.GetValues<DefenseType>())
            entries.Add((MinTechForDefense[type], t => t.Defenses.Contains(type), t => t.Defenses.Add(type)));
        foreach (var type in Enum.GetValues<ShipType>())
            entries.Add((MinTechForShip[type], t => t.Ships.Contains(type), t => t.Ships.Add(type)));
        foreach (var type in Enum.GetValues<CargoType>())
            entries.Add((MinTechForCargo[type], t => t.Resources.Contains(type), t => t.Resources.Add(type)));
        foreach (var type in Enum.GetValues<ConstructionType>())
            entries.Add((MinTechForConstruction[type], t => t.Constructions.Contains(type), t => t.Constructions.Add(type)));

        return [.. entries];
    }

    /// <summary>Every catalog item unlocked by <paramref name="tech"/> but not yet in <paramref name="owned"/>, in catalog order (GetNewTech's PossibleTechSet-TechSet, UPDATE.PAS:370).</summary>
    public static List<Action<UnlockedTechnology>> MissingTechAt(UnlockedTechnology owned, TechLevel tech) =>
        [.. _catalog.Where(e => e.MinTech <= tech && !e.IsUnlocked(owned)).Select(e => e.Unlock)];
}
