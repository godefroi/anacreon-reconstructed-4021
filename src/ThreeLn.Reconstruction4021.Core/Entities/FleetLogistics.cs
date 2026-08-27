using System.Collections.Frozen;
using ThreeLn.Reconstruction4021.Core.Types;
using static ThreeLn.Reconstruction4021.Core.PascalMath;

namespace ThreeLn.Reconstruction4021.Core.Entities;

/// <summary>
/// Fleet fuel/cargo formulas (MISC.PAS:168-222,394-401 &amp; DATACNST.PAS:382-401,556) — pure functions
/// over <see cref="ShipCounts"/>/<see cref="CargoHold"/>, shared by fleet movement
/// (<see cref="Turns.FleetMovementHandler"/>) and, later, deployment/cargo-transfer (Phase 6c's
/// <c>DeployFleet</c>/<c>ChangeCompositionOfFleet</c>), matching real Pascal's own single MISC.PAS
/// grouping rather than duplicating the formulas per consumer.
///
/// Every table here is real per-ship/per-cargo-type data, not invented — a prior version of
/// <c>FleetMovementHandler</c>'s fuel model used made-up constants (confirmed by comparing against
/// this file's real <c>FuelCons</c>/<c>FuelCap</c> source values while scoping Phase 6's 6a: e.g. the
/// old model made Jumpships cost more fuel than Starships per-ship, backwards from source, where
/// Starships are by far the most expensive at 1.427/year against a Jumpship's 0.659). Fixed as part
/// of 6a rather than left in place, since every later movement fix builds on this.
/// </summary>
public static class FleetLogistics
{
    /// <summary>Trillum-to-fuel conversion rate (DATACNST.PAS:556) — 1 ton of trillum yields this much fuel.</summary>
    public const int FuelPerTon = 265;

    /// <summary>Max fuel each ship type can hold, in hundredths of a fuel unit (DATACNST.PAS:399-401).</summary>
    private static readonly FrozenDictionary<ShipType, int> _fuelCapacityHundredths = new Dictionary<ShipType, int> {
        [ShipType.Fighter] = 3, [ShipType.HunterKiller] = 851, [ShipType.Jumpship] = 329, [ShipType.Jumptransport] = 1341,
        [ShipType.Penetrator] = 1886, [ShipType.Starship] = 2854, [ShipType.Transport] = 1988,
    }.ToFrozenDictionary();

    /// <summary>Fuel each ship type burns per year, in thousandths of a fuel unit (DATACNST.PAS:394-396).</summary>
    private static readonly FrozenDictionary<ShipType, int> _fuelConsumptionThousandthsByShip = new Dictionary<ShipType, int> {
        [ShipType.Fighter] = 10, [ShipType.HunterKiller] = 1086, [ShipType.Jumpship] = 659, [ShipType.Jumptransport] = 894,
        [ShipType.Penetrator] = 943, [ShipType.Starship] = 1427, [ShipType.Transport] = 994,
    }.ToFrozenDictionary();

    /// <summary>Fuel each cargo type burns per year, in thousandths of a fuel unit (DATACNST.PAS:394-396).</summary>
    private static readonly FrozenDictionary<CargoType, int> _fuelConsumptionThousandthsByCargo = new Dictionary<CargoType, int> {
        [CargoType.Legion] = 10, [CargoType.NinjaLegion] = 10, [CargoType.Ambrosia] = 15, [CargoType.Chemicals] = 10,
        [CargoType.Metals] = 10, [CargoType.Supplies] = 15, [CargoType.Trillum] = 20,
    }.ToFrozenDictionary();

    /// <summary>
    /// Tons of transport-equivalent cargo space one unit of this cargo type takes up (DATACNST.PAS:382-384)
    /// — Pascal's <c>CargoSpace</c> table. Internal, not private: Npe/NpeToolkit.cs's GetFleetComposition
    /// (Phase 6c) reads the same table rather than re-transcribing it.
    /// </summary>
    internal static readonly FrozenDictionary<CargoType, int> CargoSpacePerUnit = new Dictionary<CargoType, int> {
        [CargoType.Legion] = 5, [CargoType.NinjaLegion] = 5, [CargoType.Ambrosia] = 100, [CargoType.Chemicals] = 3,
        [CargoType.Metals] = 3, [CargoType.Supplies] = 2, [CargoType.Trillum] = 100,
    }.ToFrozenDictionary();

    /// <summary>
    /// Cargo space a jumptransport carries, relative to a transport's 1.0 (DATACNST.PAS:387-389's
    /// <c>TrnAdj</c> table — only nonzero entry besides Transport itself, which is always 1.0 and so
    /// never needs its own constant). Internal, not private: see <see cref="CargoSpacePerUnit"/>.
    /// </summary>
    internal const double JumptransportCargoAdjustment = 0.2;

    /// <summary>Removal priority when a fleet's cargo no longer fits (MISC.PAS:437-438) — chemicals first, ambrosia last.</summary>
    private static readonly CargoType[] _balancePriority = [
        CargoType.Chemicals, CargoType.Supplies, CargoType.Metals,
        CargoType.Legion, CargoType.NinjaLegion, CargoType.Trillum, CargoType.Ambrosia,
    ];

    /// <summary>
    /// FltMovementRate (DATACNST.PAS) — sectors per year a fleet type steps. Moved here from
    /// <see cref="Turns.FleetMovementHandler"/> (its original sole consumer, Phase 6a) once
    /// Phase 6c-2's <c>EstimatedDateOfArrival</c> (Entities/FleetLifecycle.cs) became a second real
    /// consumer — one transcribed table, not two copies of the same DATACNST.PAS data.
    /// </summary>
    private static readonly FrozenDictionary<FleetType, int> _movementRateByType = new Dictionary<FleetType, int>() {
        [FleetType.Standard] = 1,
        [FleetType.JumpFleet] = 10,
        [FleetType.HunterKillerFleet] = 10,
        [FleetType.Penetrator] = 2,
        [FleetType.AdvancedWarpFleet] = 2,
    }.ToFrozenDictionary();

    public static int MovementRate(FleetType fleetType) =>
        _movementRateByType.TryGetValue(fleetType, out var rate) ? rate : 1;

    /// <summary>FuelCapacity (MISC.PAS:168-183) — maximum fuel this ship distribution can hold.</summary>
    public static double FuelCapacity(ShipCounts ships)
    {
        var total = 1.0;
        foreach (var t in Enum.GetValues<ShipType>()) {
            total += _fuelCapacityHundredths[t] / 100.0 * ships[t];
        }

        return total;
    }

    /// <summary>FuelConsumption (MISC.PAS:185-200) — fuel this fleet burns per year, ships and cargo both.</summary>
    public static double FuelConsumption(ShipCounts ships, CargoHold cargo)
    {
        var total = 1.0;
        foreach (var t in Enum.GetValues<ShipType>()) {
            total += _fuelConsumptionThousandthsByShip[t] / 1000.0 * ships[t];
        }

        foreach (var t in Enum.GetValues<CargoType>()) {
            total += _fuelConsumptionThousandthsByCargo[t] / 1000.0 * cargo[t];
        }

        return total;
    }

    /// <summary>
    /// FleetCargoSpace (MISC.PAS:202-222) — free cargo space left, in units of transports. Can go
    /// negative (an unbalanced fleet, e.g. right after combat losses reduce ship counts) — the caller
    /// decides whether that matters, matching Pascal's own "need not be balanced" comment.
    /// </summary>
    public static int FleetCargoSpace(ShipCounts ships, CargoHold cargo)
    {
        var freeSpace = ships.Transports + ships.Jumptransports * JumptransportCargoAdjustment;
        foreach (var t in Enum.GetValues<CargoType>()) {
            freeSpace -= cargo[t] / (double)CargoSpacePerUnit[t];
        }

        return PascalRound(freeSpace);
    }

    /// <summary>
    /// BalanceFleet (MISC.PAS:431-465) — trims cargo in priority order (chemicals, supplies, metals,
    /// legions, ninja legions, trillum, ambrosia) until the fleet's cargo fits its ship capacity again,
    /// partially restoring the last type trimmed to exactly zero out the remaining deficit. Assumes (as
    /// Pascal does, unchecked) that ships alone never overflow their own capacity — <see cref="FleetCargoSpace"/>
    /// with all cargo zeroed is never negative, since ship counts are never negative.
    /// </summary>
    public static void BalanceFleet(ShipCounts ships, CargoHold cargo)
    {
        var priorityIndex = 0;
        var spaceLeft = FleetCargoSpace(ships, cargo);

        while (spaceLeft < 0) {
            var t = _balancePriority[priorityIndex];
            cargo[t] = 0;

            var newSpaceLeft = FleetCargoSpace(ships, cargo);
            if (newSpaceLeft < 0) {
                spaceLeft = newSpaceLeft;
                priorityIndex++;
            } else {
                cargo[t] = newSpaceLeft * CargoSpacePerUnit[t];
                spaceLeft = 0;
            }
        }
    }
}
