using System.Collections.Frozen;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Entities;

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

    /// <summary>IndusNames (DATACNST.PAS:151-160) -- display name for one industry, read by Designate's own menu (PrincipalIndustry text) and Production.</summary>
    public static string Name(IndustryType type) => type switch {
        IndustryType.Bioindustry => "bio-tech labs",
        IndustryType.Chemical => "chemical plants",
        IndustryType.Mining => "metal mines",
        IndustryType.ShipyardGeneral => "ship yards",
        IndustryType.ShipyardJump => "jumpship yards",
        IndustryType.ShipyardStarship => "starship yards",
        IndustryType.ShipyardTransport => "transport yards",
        IndustryType.Supply => "food factories",
        IndustryType.TrillumMining => "trillum mines",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
