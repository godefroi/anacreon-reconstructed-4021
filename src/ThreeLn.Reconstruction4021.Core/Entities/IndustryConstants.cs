using System.Collections.Frozen;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Entities;

/// <summary>
/// Production adjustment by tech level: TechAdj2 (DATACNST.PAS:231-233), used by
/// <see cref="Turns.AnnualTickHandler"/>'s own IP/Alpha production formulas and by NpeToolkit's
/// StateDeptReport port (NPEINTR.PAS's GetEmpireStatus reuses the same IP calculation for its
/// per-world shipyard-industry sum): shared here rather than duplicated.
/// </summary>
public static class IndustryConstants
{
    public static readonly FrozenDictionary<TechLevel, int> IndustrialProductionTechAdjustment = new Dictionary<TechLevel, int> {
        [TechLevel.PreTech] = 12,
        [TechLevel.Primitive] = 24,
        [TechLevel.PreAtomic] = 36,
        [TechLevel.Atomic] = 47,
        [TechLevel.PreWarp] = 58,
        [TechLevel.Warp] = 67,
        [TechLevel.Jump] = 76,
        [TechLevel.Bio] = 84,
        [TechLevel.Starship] = 90,
        [TechLevel.PreGate] = 95,
        [TechLevel.Gate] = 100,
    }.ToFrozenDictionary();
}
