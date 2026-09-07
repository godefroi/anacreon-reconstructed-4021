using System.Collections.Frozen;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Entities;

/// <summary>
/// Construction-site data shared between the annual tick (<see cref="Turns.AnnualTickHandler"/>'s own
/// <c>UseUpRawMaterial</c>) and the Build menu's own New/Site Status commands -- one source instead of
/// each reading or re-deriving its own copy.
/// </summary>
public static class ConstructionCatalog
{
    /// <summary>ConsName (CONSTR.PAS:39-47).</summary>
    public static string DisplayName(ConstructionType type) => type switch {
        ConstructionType.Minefield => "SRM field",
        ConstructionType.CommandBase => "command base",
        ConstructionType.Fortress => "fortress",
        ConstructionType.IndustrialComplex => "industrial complex",
        ConstructionType.Outpost => "outpost",
        ConstructionType.Gate => "stargate",
        ConstructionType.WarpLink => "warp link",
        ConstructionType.Disrupter => "jumpspace disrupter",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>YearsToBuild (DATACNST.PAS:528-536) -- TimeToCompletion's starting value.</summary>
    public static readonly FrozenDictionary<ConstructionType, int> YearsToBuild = new Dictionary<ConstructionType, int> {
        [ConstructionType.Minefield] = 2,
        [ConstructionType.CommandBase] = 6,
        [ConstructionType.Fortress] = 12,
        [ConstructionType.IndustrialComplex] = 10,
        [ConstructionType.Outpost] = 3,
        [ConstructionType.Gate] = 15,
        [ConstructionType.WarpLink] = 5,
        [ConstructionType.Disrupter] = 8,
    }.ToFrozenDictionary();

    /// <summary>
    /// ConsCargoNeeded (DATACNST.PAS:539-548) -- per-year raw material draw, che/met/tri only (men/nnj/
    /// amb/sup are never drawn on by construction). Was <c>AnnualTickHandler.Production.cs</c>'s own
    /// private <c>_constructionCargoNeeded</c>; relocated here so the Build menu's cost-preview screen
    /// reads the same table the tick actually draws down, not a second hand-copied one.
    /// </summary>
    public static readonly FrozenDictionary<ConstructionType, FrozenDictionary<CargoType, int>> RawMaterialPerYear = new Dictionary<ConstructionType, FrozenDictionary<CargoType, int>> {
        [ConstructionType.Minefield] = RawMaterialRow((CargoType.Chemicals, 110), (CargoType.Metals, 500), (CargoType.Trillum, 80)),
        [ConstructionType.CommandBase] = RawMaterialRow((CargoType.Chemicals, 460), (CargoType.Metals, 2300), (CargoType.Trillum, 180)),
        [ConstructionType.Fortress] = RawMaterialRow((CargoType.Chemicals, 840), (CargoType.Metals, 2870), (CargoType.Trillum, 250)),
        [ConstructionType.IndustrialComplex] = RawMaterialRow((CargoType.Chemicals, 590), (CargoType.Metals, 2600), (CargoType.Trillum, 150)),
        [ConstructionType.Outpost] = RawMaterialRow((CargoType.Chemicals, 350), (CargoType.Metals, 1120), (CargoType.Trillum, 150)),
        [ConstructionType.Gate] = RawMaterialRow((CargoType.Chemicals, 2530), (CargoType.Metals, 3920), (CargoType.Trillum, 1450)),
        [ConstructionType.WarpLink] = RawMaterialRow((CargoType.Chemicals, 1560), (CargoType.Metals, 2550), (CargoType.Trillum, 290)),
        [ConstructionType.Disrupter] = RawMaterialRow((CargoType.Chemicals, 1110), (CargoType.Metals, 1180), (CargoType.Trillum, 1120)),
    }.ToFrozenDictionary();

    private static FrozenDictionary<CargoType, int> RawMaterialRow(params (CargoType Type, int Amount)[] entries) =>
        entries.ToDictionary(e => e.Type, e => e.Amount).ToFrozenDictionary();
}
