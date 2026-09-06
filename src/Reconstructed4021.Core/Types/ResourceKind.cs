namespace Reconstructed4021.Core.Types;

/// <summary>
/// Which of the three "resource" enums a <see cref="Entities.NewsItem"/> is about -- DATACNST.PAS's
/// combined <c>ResourceTypes</c> ordinal (<see cref="DefenseType"/> 1-4, <see cref="ShipType"/> 5-11,
/// <see cref="CargoType"/> 12-18) as a real 3-case type instead of that raw combined int, or three
/// separate nullable fields on <see cref="Entities.NewsItem"/>. Unlike
/// <see cref="Entities.TechCatalog.TechGrantIdentity"/>, every case already holds its real enum --
/// nothing downstream casts an ordinal itself. The combined-ordinal math still exists (real Pascal
/// <c>.SAV</c> bytes and <see cref="Tui.NewsWindow"/>'s own <c>ResourceNames</c> table are both indexed
/// by it) but is confined to <see cref="Ordinal"/>/<see cref="FromOrdinal"/>.
/// </summary>
public abstract record ResourceKind
{
    private ResourceKind() { }

    public sealed record Defense(DefenseType Type) : ResourceKind;
    public sealed record Ship(ShipType Type) : ResourceKind;
    public sealed record Cargo(CargoType Type) : ResourceKind;

    /// <summary>DATACNST.PAS's ThingNames (:100-119) -- a plain per-case switch, not a lookup table indexed by any ordinal.</summary>
    public string DisplayName => this switch {
        Defense d => d.Type switch {
            DefenseType.Lam => "LAMs",
            DefenseType.DefenseSatellite => "defense satellites",
            DefenseType.Gdm => "GDMs",
            DefenseType.IonCannon => "ion cannons",
            _ => throw new ArgumentOutOfRangeException(nameof(ResourceKind)),
        },
        Ship s => s.Type switch {
            ShipType.Fighter => "fighter squadrons",
            ShipType.HunterKiller => "hunter-killers",
            ShipType.Jumpship => "jumpships",
            ShipType.Jumptransport => "jumptransports",
            ShipType.Penetrator => "penetrators",
            ShipType.Starship => "starships",
            ShipType.Transport => "transports",
            _ => throw new ArgumentOutOfRangeException(nameof(ResourceKind)),
        },
        Cargo c => c.Type switch {
            CargoType.Legion => "legions",
            CargoType.NinjaLegion => "ninja legions",
            CargoType.Ambrosia => "kilotons of ambrosia",
            CargoType.Chemicals => "megatons of chemicals",
            CargoType.Metals => "megatons of metals",
            CargoType.Supplies => "megatons of supplies",
            CargoType.Trillum => "kilotons of trillum",
            _ => throw new ArgumentOutOfRangeException(nameof(ResourceKind)),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(ResourceKind)),
    };

    /// <summary>
    /// Pascal's combined <c>ResourceTypes</c> ordinal (1-18) -- inverse of <see cref="FromOrdinal"/>.
    /// Exists only for the real <c>.SAV</c> file-format boundary (<c>SavGameLoader.LoadNewsData</c>'s
    /// own on-disk <c>Parm1-3</c> encoding); nothing else in this port indexes by it -- see
    /// <see cref="DisplayName"/>.
    /// </summary>
    public int Ordinal => this switch {
        Defense d => (int)d.Type + 1,
        Ship s => (int)s.Type + 5,
        Cargo c => (int)c.Type + 12,
        _ => throw new ArgumentOutOfRangeException(nameof(ResourceKind)),
    };

    public static ResourceKind FromOrdinal(int ordinal) => ordinal switch {
        >= 1 and <= 4 => new Defense((DefenseType)(ordinal - 1)),
        >= 5 and <= 11 => new Ship((ShipType)(ordinal - 5)),
        >= 12 and <= 18 => new Cargo((CargoType)(ordinal - 12)),
        _ => throw new ArgumentOutOfRangeException(nameof(ordinal), ordinal, "Not a valid combined ResourceTypes ordinal (1-18)."),
    };

    /// <summary>
    /// Which <c>Parm</c> slot (1-3) a pre-<see cref="Entities.NewsItem.Resource"/> encoding packs the
    /// combined ordinal into, keyed by headline -- shared by <see cref="SaveFormat.SavGameLoader"/>
    /// (the real, permanent <c>.SAV</c> shape) and <see cref="SaveFormat.GameJson"/> (decoding a save
    /// written before this field existed) so the two decoders can't drift apart. <c>Parm1</c> for the
    /// three "lacks/needs a resource" shortfall headlines (no separate amount to also carry);
    /// <c>Parm2</c> for <c>DestructionDetail</c>/<c>TransferDetail</c>, whose own <c>Parm1</c> is the
    /// amount.
    /// </summary>
    public static readonly IReadOnlyDictionary<NewsType, int> LegacyParmSlot = new Dictionary<NewsType, int> {
        [NewsType.LacksRawMaterial] = 1,
        [NewsType.ConstructionLacksRawMaterial] = 1,
        [NewsType.DefensesLackResources] = 1,
        [NewsType.DestructionDetail] = 2,
        [NewsType.TransferDetail] = 2,
    };

    /// <summary>
    /// The three <see cref="LegacyParmSlot"/> headlines that are always <see cref="Cargo"/> (never
    /// <see cref="Defense"/>/<see cref="Ship"/>) -- <c>ReportResourceShortfall</c>/
    /// <c>UseUpRawMaterial</c> only ever report a raw-material shortfall. Real Pascal <c>.SAV</c> bytes
    /// for these always hold the correctly-offset combined ordinal (12-18); a JSON save written by this
    /// port's own code before it fixed a real bug (a missing <c>+12</c>, live until this port's own
    /// commit that added it) instead holds the bare 0-based <see cref="CargoType"/> ordinal (0-6) --
    /// see <see cref="FromLegacyCargoOnlyOrdinal"/>.
    /// </summary>
    public static readonly IReadOnlySet<NewsType> CargoOnlyHeadlines = new HashSet<NewsType> {
        NewsType.LacksRawMaterial, NewsType.ConstructionLacksRawMaterial, NewsType.DefensesLackResources,
    };

    /// <summary>
    /// Cargo-only legacy decode for <see cref="CargoOnlyHeadlines"/>. The two possible stored shapes --
    /// correctly-offset (12-18) and this port's own old missing-<c>+12</c> bug (bare 0-6) -- never
    /// overlap (<see cref="CargoType"/> only has 7 members), so this is unambiguous regardless of which
    /// era wrote the JSON save; <see cref="FromOrdinal"/> alone can't tell them apart because it assumes
    /// the offset was always applied.
    /// </summary>
    public static Cargo FromLegacyCargoOnlyOrdinal(int value) => value switch {
        >= 12 and <= 18 => new Cargo((CargoType)(value - 12)),
        >= 0 and <= 6 => new Cargo((CargoType)value),
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Not a valid legacy Cargo-only ordinal (bug-era 0-6, or offset 12-18)."),
    };
}
