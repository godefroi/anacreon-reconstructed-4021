using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Entities;

/// <summary>
/// <c>CommandRecord</c> (ORDERS.PAS:37-53, `docs/SAV_FILE_FORMAT.md`'s own CommandRecord section)
/// -- one compiled order in <see cref="Fleet.Orders"/>. Same union-splitting convention as
/// <see cref="NewsItem"/>'s <c>Subject</c>/<c>Position</c>: Pascal's <c>Loc.XY</c>/<c>Loc.ID</c>
/// union (the <c>DestCOM</c> variant) becomes two real nullable fields here, populated only for
/// <see cref="CommandType.Destination"/> -- <see cref="DestinationObject"/> when the target cell
/// was occupied at compile time, <see cref="DestinationPosition"/> otherwise. <c>Res</c>/<c>Trns</c>
/// (the <c>TransCOM</c> variant) becomes <see cref="TransferShip"/>/<see cref="TransferCargo"/>/
/// <see cref="TransferAmount"/>, populated only for <see cref="CommandType.Transfer"/> -- exactly
/// one of the two type fields is set, since <c>Res: ResourceTypes</c> ranges over <c>fgt..tri</c>
/// (<c>ORDERS.PAS</c>'s own <c>GetResourceType</c>: ship types 5-11, cargo types 12-18, sharing this
/// port's existing <see cref="ShipType"/>/<see cref="CargoType"/> ordinal offsets). Every other
/// <see cref="CommandType"/> (<c>Repeat</c>/<c>Abort</c>/<c>Sweep</c>/<c>Stop</c>/<c>Wait</c>/
/// <c>Refuel</c>) carries no payload at all -- real Pascal's own variant record is simply
/// unused/garbage for the real ones; <c>Refuel</c> (this port's own addition, see
/// <see cref="FleetOrderCompiler"/>'s own doc comment) simply needs none.
/// No real reference save exercises <c>TransCOM</c> (`docs/SAV_FILE_FORMAT.md`'s own verification
/// notes), so this decode is reasoned through from <c>ORDERS.PAS</c> directly, not empirically
/// confirmed the way <c>DestCOM</c> is (`FLEET_ORDERS.SAV`).
/// </summary>
public sealed record FleetOrder(
    CommandType Type,
    ISectorObject? DestinationObject = null,
    Coordinate? DestinationPosition = null,
    ShipType? TransferShip = null,
    CargoType? TransferCargo = null,
    int TransferAmount = 0);
