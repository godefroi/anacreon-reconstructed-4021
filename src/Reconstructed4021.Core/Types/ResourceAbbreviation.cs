namespace Reconstructed4021.Core.Types;

/// <summary>
/// `ORDERS.PAS`'s own `ResourceName` array (`fgt..tri`) -- the 3-letter codes a player types into a
/// fleet order (<c>TRANSFER 100 MET</c>) and this port's Production window legend already displays.
/// One shared table instead of two separate copies (<c>Tui.ProductionWindow</c>'s own
/// `ShipAbbrev`/`CargoAbbrev` used to duplicate this before <see cref="Entities.FleetOrderCompiler"/>
/// needed the exact same codes for parsing).
/// </summary>
public static class ResourceAbbreviation
{
    public static string Of(ShipType type) => type switch {
        ShipType.Fighter => "fgt", ShipType.HunterKiller => "hkr", ShipType.Jumpship => "jmp",
        ShipType.Jumptransport => "jtn", ShipType.Penetrator => "pen", ShipType.Starship => "str",
        ShipType.Transport => "trn", _ => type.ToString(),
    };

    public static string Of(CargoType type) => type switch {
        CargoType.Chemicals => "che", CargoType.Metals => "met", CargoType.Supplies => "sup",
        CargoType.Trillum => "tri", CargoType.Legion => "men", CargoType.NinjaLegion => "nnj",
        CargoType.Ambrosia => "amb", _ => type.ToString(),
    };
}
