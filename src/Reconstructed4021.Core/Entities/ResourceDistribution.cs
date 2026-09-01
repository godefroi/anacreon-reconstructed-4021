using Reconstructed4021.Core.Types;
using static Reconstructed4021.Core.PascalMath;

namespace Reconstructed4021.Core.Entities;

/// <summary>
/// One column of InputNewDistribution's own grid (FLTCOMM.PAS:134-489) -- a ship type or a cargo
/// type, in that procedure's own left-to-right display order (fgt..trn, then men..tri). Pascal's
/// single ResourceTypes enum (fgt..tri) covers both; this port keeps <see cref="ShipType"/>/
/// <see cref="CargoType"/> separate everywhere else, so this wraps whichever one applies instead of
/// introducing a third enum that would only ever exist for this one grid.
/// </summary>
public readonly struct ResourceColumn
{
    public ShipType? Ship { get; }
    public CargoType? Cargo { get; }

    /// <summary>ThingNames (DATACNST.PAS:106-119) -- the plural name GetChange/FillFleet's own error text uses ("Not enough X on ground.").</summary>
    public string Name { get; }

    private ResourceColumn(ShipType? ship, CargoType? cargo, string name)
    {
        Ship = ship;
        Cargo = cargo;
        Name = name;
    }

    public static readonly ResourceColumn[] All = [
        new(ShipType.Fighter, null, "fighter squadrons"),
        new(ShipType.HunterKiller, null, "hunter-killers"),
        new(ShipType.Jumpship, null, "jumpships"),
        new(ShipType.Jumptransport, null, "jumptransports"),
        new(ShipType.Penetrator, null, "penetrators"),
        new(ShipType.Starship, null, "starships"),
        new(ShipType.Transport, null, "transports"),
        new(null, CargoType.Legion, "legions"),
        new(null, CargoType.NinjaLegion, "ninja legions"),
        new(null, CargoType.Ambrosia, "kilotons of ambrosia"),
        new(null, CargoType.Chemicals, "megatons of chemicals"),
        new(null, CargoType.Metals, "megatons of metals"),
        new(null, CargoType.Supplies, "megatons of supplies"),
        new(null, CargoType.Trillum, "kilotons of trillum"),
    ];

    /// <summary>CargoSpace (DATACNST.PAS:382-384) -- tons of transport-equivalent space one unit of this cargo type takes, null for a ship column (ships take up their own hull, not cargo space).</summary>
    public int? CargoSpacePerUnit => Cargo is { } c ? FleetLogistics.CargoSpacePerUnit[c] : null;

    public int Get(ShipCounts ships, CargoHold cargo) => Ship is { } s ? ships[s] : cargo[Cargo!.Value];

    public void Set(ShipCounts ships, CargoHold cargo, int value)
    {
        if (Ship is { } s) {
            ships[s] = value;
        } else {
            cargo[Cargo!.Value] = value;
        }
    }
}

/// <summary>
/// InputNewDistribution's own transfer math (FLTCOMM.PAS:281-417), split out from that procedure's
/// I/O loop (its own <c>REPEAT ... GetCharacter</c> and screen-writing stay entirely in
/// <c>Tui.ResourceDistributionEditor</c>, per this port's Core/Tui split -- see that class's own doc
/// comment). Shared by every real caller of the original widget: LaunchFleetCommand (deploy),
/// TransferFleetCommand, AbortFleetCommand/JoinFleetCommand -- one column at a time, fleet on one
/// side and a world or another fleet ("ground") on the other.
/// </summary>
public static class ResourceDistribution
{
    /// <summary>MaxResources (TYPES.PAS:40) -- every ship/cargo count's own ceiling, and the transfer-amount bound GetChange itself enforces.</summary>
    public const int MaxResources = 9999;

    /// <summary>
    /// GetChange (FLTCOMM.PAS:281-344), minus its own line-editing I/O: <paramref name="amount"/> is
    /// already parsed. Positive moves ground-&gt;fleet (only when <paramref name="groundIsPlayerOwned"/>
    /// and the ground actually has that much); zero or negative moves fleet-&gt;ground (never gated on
    /// ownership -- taking cargo/ships off your own fleet is always allowed). Returns false with an
    /// error message and makes no change on any validation failure, exactly like Pascal's own
    /// <c>REPEAT...UNTIL Ok</c> retry loop.
    /// </summary>
    public static bool TryTransfer(ResourceColumn column, ShipCounts fleetShips, CargoHold fleetCargo,
        ShipCounts groundShips, CargoHold groundCargo, bool groundIsPlayerOwned, int amount, out string error)
    {
        var onGround = column.Get(groundShips, groundCargo);
        var inFleet = column.Get(fleetShips, fleetCargo);

        if (Math.Abs(amount) > MaxResources) {
            error = $"numbers from -{MaxResources} to {MaxResources}.";
            return false;
        }

        if (amount <= 0) {
            if (-amount > inFleet) {
                error = $"Not enough {column.Name} in fleet.";
                return false;
            }
        } else if (!groundIsPlayerOwned) {
            error = "This is not your territory.";
            return false;
        } else if (amount > onGround) {
            error = $"Not enough {column.Name} on ground.";
            return false;
        }

        column.Set(fleetShips, fleetCargo, inFleet + amount);
        column.Set(groundShips, groundCargo, onGround - amount);
        error = "";
        return true;
    }

    /// <summary>
    /// FillFleet (FLTCOMM.PAS:346-390): moves as much of one column from ground to fleet as the
    /// fleet's own transport capacity allows -- combat ships move in full, transports/jumptransports
    /// move in full only if there's already room, cargo moves only up to whatever space is left. A
    /// no-op (matching Pascal's own <c>WriteError</c> branch) when the ground isn't the player's own.
    /// </summary>
    public static void FillFleet(ResourceColumn column, ShipCounts fleetShips, CargoHold fleetCargo,
        ShipCounts groundShips, CargoHold groundCargo, bool groundIsPlayerOwned)
    {
        if (!groundIsPlayerOwned) {
            return;
        }

        var cargoSpaceAvail = FleetLogistics.FleetCargoSpace(fleetShips, fleetCargo);
        var onGround = column.Get(groundShips, groundCargo);
        var inFleet = column.Get(fleetShips, fleetCargo);
        int amountToMove;

        if (column.Ship is ShipType.Fighter or ShipType.HunterKiller or ShipType.Jumpship or ShipType.Penetrator or ShipType.Starship) {
            amountToMove = onGround;
        } else if (column.Ship is ShipType.Jumptransport or ShipType.Transport) {
            var adjustment = column.Ship == ShipType.Jumptransport ? FleetLogistics.JumptransportCargoAdjustment : 1.0;
            amountToMove = cargoSpaceAvail >= 0 ? onGround : Math.Min(onGround, PascalRound(-cargoSpaceAvail / adjustment));
        } else if (cargoSpaceAvail > 0) {
            amountToMove = Math.Min(cargoSpaceAvail * FleetLogistics.CargoSpacePerUnit[column.Cargo!.Value], onGround);
        } else {
            amountToMove = onGround;
        }

        if (inFleet + amountToMove > MaxResources) {
            amountToMove = MaxResources - inFleet;
        }

        column.Set(fleetShips, fleetCargo, inFleet + amountToMove);
        column.Set(groundShips, groundCargo, onGround - amountToMove);
    }

    /// <summary>
    /// EmptyFleet (FLTCOMM.PAS:392-417): moves one column entirely from fleet to ground, except cargo
    /// being emptied onto another fleet (<paramref name="groundIsAFleet"/>), which is capped at
    /// whatever space that fleet has left (0 if none) rather than always moving everything.
    /// </summary>
    public static void EmptyFleet(ResourceColumn column, ShipCounts fleetShips, CargoHold fleetCargo,
        ShipCounts groundShips, CargoHold groundCargo, bool groundIsAFleet)
    {
        var inFleet = column.Get(fleetShips, fleetCargo);
        var onGround = column.Get(groundShips, groundCargo);
        int amountToMove;

        if (column.Ship is not null || !groundIsAFleet) {
            amountToMove = inFleet;
        } else {
            var cargoSpaceAvail = FleetLogistics.FleetCargoSpace(groundShips, groundCargo);
            amountToMove = cargoSpaceAvail > 0 ? Math.Min(cargoSpaceAvail * FleetLogistics.CargoSpacePerUnit[column.Cargo!.Value], inFleet) : 0;
        }

        if (onGround + amountToMove > MaxResources) {
            amountToMove = MaxResources - onGround;
        }

        column.Set(fleetShips, fleetCargo, inFleet - amountToMove);
        column.Set(groundShips, groundCargo, onGround + amountToMove);
    }
}
