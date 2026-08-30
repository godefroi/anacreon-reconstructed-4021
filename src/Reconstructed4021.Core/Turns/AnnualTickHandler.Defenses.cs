using System.Collections.Frozen;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Turns;

/// <summary>
/// Planetary/starbase defense buildup (UPDATE.PAS:1278-1351, UpdateDefenses) — grows
/// <see cref="IEconomicWorld.Defenses"/> toward an optimum sized off troop strength, gated by
/// researched <see cref="UnlockedTechnology.Defenses"/>. Runs after UpdateMilitary for a planet
/// (UPDATE.PAS:1387); unconditionally for a starbase, even a non-industrial-complex one
/// (UPDATE.PAS:1429, outside the STyp=cmp guard that brackets the rest of a starbase's economy).
/// See AnnualTickHandler.cs for the type's overall file layout.
/// </summary>
public sealed partial class AnnualTickHandler
{
    /// <summary>Optimum defenses for 100 men, 20 billion population, 50% efficiency (DATACNST.PAS:551-552).</summary>
    private static readonly FrozenDictionary<DefenseType, int> _defenseAdjustment = new Dictionary<DefenseType, int> {
        [DefenseType.Lam] = 145,
        [DefenseType.DefenseSatellite] = 76,
        [DefenseType.Gdm] = 215,
        [DefenseType.IonCannon] = 83,
    }.ToFrozenDictionary();

    /// <summary>DefBuildRate (DATACNST.PAS:553-554).</summary>
    private static readonly FrozenDictionary<DefenseType, int> _defenseBuildRate = new Dictionary<DefenseType, int> {
        [DefenseType.Lam] = 220,
        [DefenseType.DefenseSatellite] = 50,
        [DefenseType.Gdm] = 250,
        [DefenseType.IonCannon] = 95,
    }.ToFrozenDictionary();

    /// <summary>
    /// RawM[LAM..ion,CargoTypes] (DATACNST.PAS:429-432) — units of raw material per 100 units built.
    /// Same table shape as <see cref="_rawMaterialForShips"/>; defenses get their own
    /// dictionary rather than a shared one spanning ships+defenses, matching how this port already
    /// keeps ships/cargo-products/construction each in their own table instead of one array indexed
    /// by Pascal's single flat ResourceTypes.
    /// </summary>
    private static readonly FrozenDictionary<DefenseType, FrozenDictionary<CargoType, int>> _rawMaterialForDefenses = new Dictionary<DefenseType, FrozenDictionary<CargoType, int>> {
        [DefenseType.Lam] = RawMaterialRow((CargoType.Chemicals, 25), (CargoType.Metals, 20), (CargoType.Trillum, 5)),
        [DefenseType.DefenseSatellite] = RawMaterialRow((CargoType.Chemicals, 30), (CargoType.Metals, 140), (CargoType.Trillum, 20)),
        [DefenseType.Gdm] = RawMaterialRow((CargoType.Chemicals, 10), (CargoType.Metals, 20), (CargoType.Trillum, 2)),
        [DefenseType.IonCannon] = RawMaterialRow((CargoType.Chemicals, 25), (CargoType.Metals, 150), (CargoType.Trillum, 10)),
    }.ToFrozenDictionary();

    /// <summary>
    /// UPDATE.PAS:1278-1351. TroopStrength (PRIMINTR.PAS:400-411, Cargo.Legions+2*Cargo.NinjaLegions)
    /// is inlined — it's a one-line read with no other caller in this port yet. The three Pln-vs-Base
    /// differences: a planet's BuildRate gets an extra Population/2000 factor; an Outpost starbase
    /// quarters Optimum and never builds DefenseSatellites; a CommandBase/Fortress starbase
    /// quadruples both Optimum and BuildRate. Lams are only ever built on a Base- or Capital-type
    /// world, planet or starbase alike, independent of the ObjTyp branch above.
    /// <paramref name="reportedShortfalls"/> is the same per-tick set <see cref="RunProductionPipeline"/>
    /// threads through UpdateIndustry/Production (UPDATE.PAS's OtherReports, shared across the whole
    /// per-world tick) — unlike Production's own raw-material shortfall, DefensesLackResources fires
    /// for a starbase too, with no IsPlanet guard (UPDATE.PAS:1336 has none, unlike UPDATE.PAS:904's
    /// planet-only ReportPlanetLack call).
    /// </summary>
    private void UpdateDefenses(IEconomicWorld world, HashSet<CargoType> reportedShortfalls)
    {
        var troopStrength = world.Cargo.Legions + 2 * world.Cargo.NinjaLegions;
        var buildRate = (troopStrength / 2000.0) * (1 + (world.Efficiency - 50) / 100.0);
        var optimum = troopStrength / 100.0;
        var isOutpost = false;
        var effectiveTech = EffectiveTechnologyLevel(world);

        if (world.IsPlanet) {
            buildRate *= world.Population / 2000.0;
        } else if (world is Starbase { Kind: StarbaseKind.Outpost }) {
            optimum /= 4;
            isOutpost = true;
        } else if (world is Starbase { Kind: StarbaseKind.CommandBase or StarbaseKind.Fortress }) {
            optimum *= 4;
            buildRate *= 4;
        }

        foreach (var defenseType in Enum.GetValues<DefenseType>()) {
            if (!DefenseTechAvailable(world, defenseType, effectiveTech))
                continue;

            var optimumDef = ClampResource(optimum * _defenseAdjustment[defenseType]);
            if (isOutpost && defenseType == DefenseType.DefenseSatellite)
                optimumDef = 0;
            if (defenseType == DefenseType.Lam && world.Type is not (WorldType.Base or WorldType.Capital))
                optimumDef = 0;

            if (world.Defenses[defenseType] >= optimumDef)
                continue;

            var maxBuild = Math.Max(ClampResource(buildRate * _defenseBuildRate[defenseType]), 1);
            var build = Math.Min(optimumDef - world.Defenses[defenseType], maxBuild);
            var rawMaterialCost = _rawMaterialForDefenses[defenseType];
            var rawNeeded = new Dictionary<CargoType, int>();

            foreach (var cargoType in _rawMaterialCargoTypes) {
                if (!rawMaterialCost.TryGetValue(cargoType, out var costPer100)) {
                    rawNeeded[cargoType] = 0;
                    continue;
                }

                var needed = ClampResource(build * (costPer100 / 100.0));
                if (needed > world.Cargo[cargoType]) {
                    build = ClampResource(world.Cargo[cargoType] / (double)costPer100 * 100);
                    needed = ClampResource(build * (costPer100 / 100.0));
                    ReportResourceShortfall(world, cargoType, NewsType.DefensesLackResources, reportedShortfalls);
                }
                rawNeeded[cargoType] = needed;
            }

            foreach (var cargoType in _rawMaterialCargoTypes) {
                var used = Math.Min(world.Cargo[cargoType], rawNeeded[cargoType]);
                world.Cargo[cargoType] -= used;
            }

            world.Defenses[defenseType] = ClampResource(world.Defenses[defenseType] + build);
        }
    }

    /// <summary>
    /// UPDATE.PAS:1367-1369's "Technology:=Technology*TechDev[Tech]" intersection, applied to
    /// defenses specifically — same per-world tech-level gate as <see cref="ShipTechAvailable"/>,
    /// reusing its own <see cref="EffectiveTechnologyLevel"/> helper: an owned world
    /// needs both the empire's research (<see cref="UnlockedTechnology.Defenses"/>) and its own
    /// TechLevel to have reached <see cref="TechCatalog.MinTechForDefense"/>; an independent world has
    /// no empire research to check, only the tech-level gate (at one level below its own, per
    /// EffectiveTechnologyLevel).
    /// </summary>
    private static bool DefenseTechAvailable(IEconomicWorld world, DefenseType defenseType, TechLevel effectiveTech)
    {
        if (effectiveTech < TechCatalog.MinTechForDefense[defenseType])
            return false;

        return world.Owner.IsIndependent || world.Owner.Technology.Defenses.Contains(defenseType);
    }
}
