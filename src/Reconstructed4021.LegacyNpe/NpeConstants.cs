using System.Collections.Frozen;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.LegacyNpe;

/// <summary>
/// The pure balance data <see cref="NpeToolkit"/>'s targeting/designation/defense-roll logic reads,
/// transcribed directly from source since it's isolated, parameter-only data with no real-state
/// dependency (same as <see cref="Combat.CombatConstants"/>). <see cref="WorldType"/>
/// and <see cref="WorldClass"/>'s declaration order already matches Pascal's WorldTypes/WorldClass
/// order 1:1 (verified directly against TYPES.PAS, not assumed from the name lists alone — WorldType
/// in particular has non-obvious 0-valued gaps at every *Starbase variant that a naive reordering
/// could silently shift), so every table below reads off the cited source lines without remapping.
/// </summary>
internal static class NpeConstants
{
    /// <summary>MaxNoOfRaiders (NPEINTR.PAS:38) — how many RaidTrnMSN fleets an empire keeps out against one enemy at once.</summary>
    public const int MaxNoOfRaiders = 3;

    /// <summary>Base chance (before adjustment) a world becomes each type: NPETypeDefault (NPEINTR.PAS:157-178).</summary>
    public static readonly FrozenDictionary<WorldType, int> TypeDefault = new Dictionary<WorldType, int> {
        [WorldType.Agricultural] = 10, [WorldType.Ambrosia] = 0, [WorldType.Base] = 75, [WorldType.BaseStarbase] = 0,
        [WorldType.Capital] = 0, [WorldType.Chemical] = 30, [WorldType.Independent] = 0, [WorldType.JumpshipBase] = 100,
        [WorldType.JumpshipBaseStarbase] = 0, [WorldType.Mine] = 50, [WorldType.NinjaWorld] = 0, [WorldType.Outpost] = 0,
        [WorldType.RawMaterialMine] = 50, [WorldType.RawMaterialMineStarbase] = 0, [WorldType.StarshipBase] = 100,
        [WorldType.StarshipBaseStarbase] = 0, [WorldType.TransportBase] = 20, [WorldType.TransportBaseStarbase] = 0,
        [WorldType.University] = 50, [WorldType.Terraform] = 0, [WorldType.TrillumMine] = 40,
    }.ToFrozenDictionary();

    /// <summary>How valuable a world of each type is as an attack target: NPETypeValue (NPEINTR.PAS:180-201).</summary>
    public static readonly FrozenDictionary<WorldType, int> TypeValue = new Dictionary<WorldType, int> {
        [WorldType.Agricultural] = 30, [WorldType.Ambrosia] = 100, [WorldType.Base] = 10, [WorldType.BaseStarbase] = 0,
        [WorldType.Capital] = 100, [WorldType.Chemical] = 50, [WorldType.Independent] = 25, [WorldType.JumpshipBase] = 25,
        [WorldType.JumpshipBaseStarbase] = 0, [WorldType.Mine] = 50, [WorldType.NinjaWorld] = 100, [WorldType.Outpost] = 100,
        [WorldType.RawMaterialMine] = 50, [WorldType.RawMaterialMineStarbase] = 0, [WorldType.StarshipBase] = 25,
        [WorldType.StarshipBaseStarbase] = 0, [WorldType.TransportBase] = 60, [WorldType.TransportBaseStarbase] = 0,
        [WorldType.University] = 50, [WorldType.Terraform] = 0, [WorldType.TrillumMine] = 50,
    }.ToFrozenDictionary();

    /// <summary>How valuable a world of each environment class is as an attack target: NPEClassValue (NPEINTR.PAS:203-224).</summary>
    public static readonly FrozenDictionary<WorldClass, int> ClassValue = new Dictionary<WorldClass, int> {
        [WorldClass.Ambrosia] = 100, [WorldClass.Arid] = 35, [WorldClass.Artificial] = 100, [WorldClass.Barren] = 50,
        [WorldClass.ClassJ] = 65, [WorldClass.ClassK] = 65, [WorldClass.ClassL] = 65, [WorldClass.ClassM] = 65,
        [WorldClass.Desert] = 50, [WorldClass.EarthLike] = 65, [WorldClass.Forest] = 75, [WorldClass.GasGiant] = 60,
        [WorldClass.Hostile] = 80, [WorldClass.Ice] = 25, [WorldClass.Jungle] = 75, [WorldClass.Ocean] = 50,
        [WorldClass.Paradise] = 100, [WorldClass.Poisonous] = 50, [WorldClass.Ruins] = 100, [WorldClass.Underground] = 60,
        [WorldClass.Volcanic] = 75,
    }.ToFrozenDictionary();

    /// <summary>Minimum tech level a world must have before it can even be considered for each designation: MinTechForType (DATACNST.PAS:249-270).</summary>
    public static readonly FrozenDictionary<WorldType, TechLevel> MinTechForType = new Dictionary<WorldType, TechLevel> {
        [WorldType.Agricultural] = TechLevel.PreTech, [WorldType.Ambrosia] = TechLevel.Bio, [WorldType.Base] = TechLevel.PreWarp,
        [WorldType.BaseStarbase] = TechLevel.Gate, [WorldType.Capital] = TechLevel.Jump, [WorldType.Chemical] = TechLevel.PreAtomic,
        [WorldType.Independent] = TechLevel.PreTech, [WorldType.JumpshipBase] = TechLevel.Jump, [WorldType.JumpshipBaseStarbase] = TechLevel.Gate,
        [WorldType.Mine] = TechLevel.Primitive, [WorldType.NinjaWorld] = TechLevel.Starship, [WorldType.Outpost] = TechLevel.Gate,
        [WorldType.RawMaterialMine] = TechLevel.Primitive, [WorldType.RawMaterialMineStarbase] = TechLevel.Gate,
        [WorldType.StarshipBase] = TechLevel.Bio, [WorldType.StarshipBaseStarbase] = TechLevel.Gate,
        [WorldType.TransportBase] = TechLevel.PreWarp, [WorldType.TransportBaseStarbase] = TechLevel.Gate,
        [WorldType.University] = TechLevel.Jump, [WorldType.Terraform] = TechLevel.Gate, [WorldType.TrillumMine] = TechLevel.Atomic,
    }.ToFrozenDictionary();

    // Shell-position rows (deep space, high orbit, orbit, sub-orbit, ground) x ship-type columns
    // (fgt, hkr, jmp, jtn, pen, ssp, trn) — same layout NPEINTR.PAS/DATACNST.PAS comment as.

    /// <summary>InitDefenseRecord's ShellDefDist (DATACNST.PAS:373-379) — the game's starting fleet-defense distribution.</summary>
    public static readonly ShellDefensePlan InitDefenseFleets = BuildPlan(new[,] {
        { 5, 50, 10, 0, 15, 0, 0 },
        { 10, 10, 20, 0, 30, 50, 0 },
        { 10, 10, 30, 0, 30, 30, 0 },
        { 55, 30, 40, 0, 25, 20, 0 },
        { 20, 0, 0, 100, 0, 0, 100 },
    });

    /// <summary>Defense1's ShellDefDist (NPEINTR.PAS:41-47).</summary>
    public static readonly ShellDefensePlan Defense1Fleets = BuildPlan(new[,] {
        { 0, 0, 0, 0, 0, 0, 0 },
        { 0, 0, 0, 0, 100, 100, 0 },
        { 50, 100, 100, 0, 0, 0, 0 },
        { 0, 0, 0, 0, 0, 0, 0 },
        { 50, 0, 0, 100, 0, 0, 100 },
    });

    /// <summary>Defense2's ShellDefDist (NPEINTR.PAS:49-55).</summary>
    public static readonly ShellDefensePlan Defense2Fleets = BuildPlan(new[,] {
        { 0, 0, 0, 0, 0, 0, 0 },
        { 0, 0, 0, 0, 0, 0, 0 },
        { 100, 100, 100, 0, 100, 100, 0 },
        { 0, 0, 0, 0, 0, 0, 0 },
        { 0, 0, 0, 100, 0, 0, 100 },
    });

    /// <summary>Defense3's ShellDefDist (NPEINTR.PAS:57-63).</summary>
    public static readonly ShellDefensePlan Defense3Fleets = BuildPlan(new[,] {
        { 0, 0, 0, 0, 0, 0, 0 },
        { 0, 25, 50, 0, 75, 75, 0 },
        { 0, 75, 50, 0, 25, 25, 0 },
        { 0, 0, 0, 0, 0, 0, 0 },
        { 100, 0, 0, 100, 0, 0, 100 },
    });

    private static ShellDefensePlan BuildPlan(int[,] rows)
    {
        var plan = new ShellDefensePlan();
        var positions = Enum.GetValues<ShellPosition>();
        var shipTypes = Enum.GetValues<ShipType>();
        for (var p = 0; p < positions.Length; p++) {
            for (var s = 0; s < shipTypes.Length; s++) {
                plan[positions[p]][shipTypes[s]] = rows[p, s];
            }
        }
        return plan;
    }
}
