using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Turns;

/// <summary>
/// Diagnostic-only instrumentation for measuring production actually lost to storage-cap overflow
/// (cargo produced mid-tick, above <see cref="PascalMath.MaxResources"/>, clamped away by
/// <c>ClampCargo</c> at the end of <see cref="AnnualTickHandler.RunProductionPipeline"/>) and to
/// raw-material shortage throttling (industry growth cut short by <c>UpdateIndustry</c>'s metals
/// check, and ship/cargo output cut short by <c>ApplyRawMaterialConstraint</c>). Gated behind
/// <c>PROD_DIAG=1</c> so every call site is a single boolean check, no dictionary access, when
/// disabled -- not wired into any shipped behavior.
/// </summary>
public static class ProductionDiagnostics
{
    /// <summary>
    /// Off by default (a single boolean check, no further cost, on every production tick). A test
    /// harness flips this on directly rather than via an environment variable, since the totals are
    /// read back in-process from the dictionaries below, not parsed out of console/TRX output.
    /// </summary>
    public static bool Enabled;

    /// <summary>
    /// When set, only a world owned by this empire is tracked (a same-scenario, per-seat comparison
    /// -- e.g. "did swapping this one empire's turn handler change its own waste/shortage numbers" --
    /// added for exactly that comparison rather than every empire's combined totals). Null (the
    /// default) tracks every world, matching this class's original whole-game behavior.
    /// </summary>
    public static Empire? FilterEmpire;

    public static readonly Dictionary<CargoType, long> OverflowLost = new();
    public static readonly Dictionary<IndustryType, long> IndustryGrowthLostToMetalsShortage = new();
    public static readonly Dictionary<ShipType, long> ShipsLostToShortage = new();
    public static readonly Dictionary<CargoType, long> CargoLostToShortage = new();
    public static readonly Dictionary<CargoType, long> GrossCargoProduced = new();
    public static readonly Dictionary<ShipType, long> GrossShipsProduced = new();

    /// <summary>
    /// Who's overflowing and who's shortage-throttled, added to characterize the two effects above
    /// (a world-type/efficiency/population breakdown) rather than just their aggregate magnitude --
    /// distinct dictionaries from the ones above so existing callers/reports are untouched.
    /// </summary>
    public static readonly Dictionary<(WorldType WorldType, CargoType Cargo), long> OverflowByWorldType = new();
    public static readonly Dictionary<WorldType, long> IndustryGrowthLostByWorldType = new();
    public static readonly Dictionary<int, long> IndustryGrowthLostByEfficiencyBucket = new();
    public static readonly Dictionary<WorldType, (long EfficiencySum, long PopulationSum, int Count)> OverflowWorldStats = new();
    public static readonly Dictionary<WorldType, (long EfficiencySum, long PopulationSum, int Count)> ShortageWorldStats = new();

    public static bool Tracks(IEconomicWorld world) => FilterEmpire is null || world.Owner == FilterEmpire;

    public static void Reset()
    {
        OverflowLost.Clear();
        IndustryGrowthLostToMetalsShortage.Clear();
        ShipsLostToShortage.Clear();
        CargoLostToShortage.Clear();
        GrossCargoProduced.Clear();
        GrossShipsProduced.Clear();
        OverflowByWorldType.Clear();
        IndustryGrowthLostByWorldType.Clear();
        IndustryGrowthLostByEfficiencyBucket.Clear();
        OverflowWorldStats.Clear();
        ShortageWorldStats.Clear();
    }

    public static void AddOverflow(CargoType type, long amount) =>
        OverflowLost[type] = OverflowLost.GetValueOrDefault(type) + amount;

    public static void AddIndustryGrowthLost(IndustryType type, long amount) =>
        IndustryGrowthLostToMetalsShortage[type] = IndustryGrowthLostToMetalsShortage.GetValueOrDefault(type) + amount;

    public static void AddShipsLost(ShipType type, long amount) =>
        ShipsLostToShortage[type] = ShipsLostToShortage.GetValueOrDefault(type) + amount;

    public static void AddCargoLost(CargoType type, long amount) =>
        CargoLostToShortage[type] = CargoLostToShortage.GetValueOrDefault(type) + amount;

    public static void AddGrossCargo(CargoType type, long amount) =>
        GrossCargoProduced[type] = GrossCargoProduced.GetValueOrDefault(type) + amount;

    public static void AddGrossShips(ShipType type, long amount) =>
        GrossShipsProduced[type] = GrossShipsProduced.GetValueOrDefault(type) + amount;

    public static void AddOverflowByWorld(WorldType worldType, int efficiency, int population, CargoType cargo, long amount)
    {
        OverflowByWorldType[(worldType, cargo)] = OverflowByWorldType.GetValueOrDefault((worldType, cargo)) + amount;
        var stats = OverflowWorldStats.GetValueOrDefault(worldType);
        OverflowWorldStats[worldType] = (stats.EfficiencySum + efficiency, stats.PopulationSum + population, stats.Count + 1);
    }

    public static void AddShortageByWorld(WorldType worldType, int efficiency, int population, long amount)
    {
        IndustryGrowthLostByWorldType[worldType] = IndustryGrowthLostByWorldType.GetValueOrDefault(worldType) + amount;
        var bucket = (efficiency / 10) * 10;
        IndustryGrowthLostByEfficiencyBucket[bucket] = IndustryGrowthLostByEfficiencyBucket.GetValueOrDefault(bucket) + amount;
        var stats = ShortageWorldStats.GetValueOrDefault(worldType);
        ShortageWorldStats[worldType] = (stats.EfficiencySum + efficiency, stats.PopulationSum + population, stats.Count + 1);
    }
}
