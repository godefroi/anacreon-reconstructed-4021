using System.Collections.Frozen;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Presentation;

/// <summary>
/// CLSCOMM.PAS's DisplayFleetInfo and MISC.PAS's YesNo, shared by both <c>Reconstructed4021.Tui</c>'s
/// own Close Up/Fleet/Status windows and <c>Reconstructed4021.Tui2</c>'s own overlay equivalents --
/// previously two independent copies (Tui2's own restated here since it doesn't reference the Tui
/// project), moved here once that duplication risked the exact same "one right, one wrong" divergence
/// <see cref="RelativeCoordinate"/>'s own doc comment records having actually happened once already.
/// </summary>
public static class CloseUpWindowText
{
    /// <summary>MISC.PAS's YesNo (:78-96): a coarse magnitude bucket for a Scouted-but-not-owned count, not a real number. Width padding comes from each call site's own <c>{,5}</c> format, matching Pascal's own pre-padded 5-char literals.</summary>
    public static string YesNo(int level) => level switch
    {
        0 => "no",
        >= 1 and <= 500 => "yes-",
        >= 501 and <= 1500 => "yes1",
        >= 1501 and <= 2500 => "yes2",
        >= 2501 and <= 3500 => "yes3",
        >= 3501 and <= 4500 => "yes4",
        >= 4501 and <= 5500 => "yes5",
        >= 5501 and <= 6500 => "yes6",
        >= 6501 and <= 7500 => "yes7",
        >= 7501 and <= 8500 => "yes8",
        >= 8501 and <= 9500 => "yes9",
        >= 9501 and <= 9999 => "yes+",
        _ => "----",
    };

    public static readonly string[] FleetStatusNames = ["at destination", "In transit", "out of trillum", "lost"];

    /// <summary>DATACNST.PAS's TypeName (:71-92), keyed by <see cref="WorldType"/>. Used for both a
    /// <see cref="Planet"/> and a <see cref="Starbase"/>'s own "Type:" display -- GetType
    /// (PRIMINTR.PAS:471-480) reads the same <c>WorldTypes</c> field (<c>Planet[Index].Typ</c> /
    /// <c>Starbase[Index].Typ</c>) for either. A starbase's separate <c>StarbaseTypes</c>/GetBaseType
    /// (cmm/frt/cmp/otp, this port's own <see cref="StarbaseKind"/>) is a different axis entirely --
    /// combat modifiers, map glyph, "can build" gating -- never what this table's own text shows.</summary>
    public static readonly FrozenDictionary<WorldType, string> WorldTypeNames = new Dictionary<WorldType, string> {
        [WorldType.Agricultural] = "agricultural world",
        [WorldType.Ambrosia] = "ambrosia world",
        [WorldType.Base] = "base planet",
        [WorldType.BaseStarbase] = "base planet",
        [WorldType.Capital] = "capital",
        [WorldType.Chemical] = "chemical planet",
        [WorldType.Independent] = "independent world",
        [WorldType.JumpshipBase] = "jumpship base",
        [WorldType.JumpshipBaseStarbase] = "jumpship base",
        [WorldType.Mine] = "metal mine",
        [WorldType.NinjaWorld] = "ninja world",
        [WorldType.Outpost] = "outpost",
        [WorldType.RawMaterialMine] = "raw material mine",
        [WorldType.RawMaterialMineStarbase] = "raw material mine",
        [WorldType.StarshipBase] = "starship base",
        [WorldType.StarshipBaseStarbase] = "starship base",
        [WorldType.TransportBase] = "transport base",
        [WorldType.TransportBaseStarbase] = "transport base",
        [WorldType.University] = "university world",
        [WorldType.Terraform] = "terraform",
        [WorldType.TrillumMine] = "trillum mine",
    }.ToFrozenDictionary();

    /// <summary>CLSCOMM.PAS's ClassName (:38-60), keyed by <see cref="WorldClass"/>.</summary>
    public static readonly FrozenDictionary<WorldClass, string> WorldClassNames = new Dictionary<WorldClass, string> {
        [WorldClass.Ambrosia] = "Ambrosia",
        [WorldClass.Arid] = "Arid",
        [WorldClass.Artificial] = "Artificial",
        [WorldClass.Barren] = "Barren",
        [WorldClass.ClassJ] = "Class j",
        [WorldClass.ClassK] = "Class k",
        [WorldClass.ClassL] = "Class l",
        [WorldClass.ClassM] = "Class m",
        [WorldClass.Desert] = "Desert",
        [WorldClass.EarthLike] = "Earth-like",
        [WorldClass.Forest] = "Forest world",
        [WorldClass.GasGiant] = "Gas Giant",
        [WorldClass.Hostile] = "Hostile life",
        [WorldClass.Ice] = "Ice world",
        [WorldClass.Jungle] = "Jungle world",
        [WorldClass.Ocean] = "Ocean world",
        [WorldClass.Paradise] = "Paradise",
        [WorldClass.Poisonous] = "Poisonous",
        [WorldClass.Ruins] = "Ancient ruins",
        [WorldClass.Underground] = "Underground",
        [WorldClass.Volcanic] = "Volcanic",
    }.ToFrozenDictionary();

    /// <summary>DATACNST.PAS's TechN (:152-163), keyed by <see cref="TechLevel"/>.</summary>
    public static readonly FrozenDictionary<TechLevel, string> TechLevelNames = new Dictionary<TechLevel, string> {
        [TechLevel.PreTech] = "pre-tech",
        [TechLevel.Primitive] = "primitive",
        [TechLevel.PreAtomic] = "pre-atomic",
        [TechLevel.Atomic] = "atomic",
        [TechLevel.PreWarp] = "pre-warp",
        [TechLevel.Warp] = "warp",
        [TechLevel.Jump] = "jump",
        [TechLevel.Bio] = "bio-tech",
        [TechLevel.Starship] = "starship",
        [TechLevel.PreGate] = "pre-gate",
        [TechLevel.Gate] = "gate",
    }.ToFrozenDictionary();

    /// <summary>
    /// "Kind" label shown alongside a name (CLSCOMM.PAS's DisplayBasicInfo, the "Type:" field) -- not a
    /// name-fallback itself, see <see cref="DescribeLocation"/> for that job. Previously two independent
    /// copies (Reconstructed4021.Tui's own CloseUpWindow, Reconstructed4021.Tui2's own CloseUpOverlay),
    /// each calling <c>.ToString()</c> on <see cref="WorldType"/>/<see cref="StarbaseKind"/> directly --
    /// the raw C# identifier ("TransportBase"), not the real display string ("transport base"). Moved
    /// here and fixed the same way <see cref="FleetStatusNames"/>'s own duplication was.
    /// </summary>
    public static string DescribeKind(ISectorObject obj) => obj switch
    {
        IEconomicWorld world => WorldTypeNames[world.Type],
        Fleet => "Fleet",
        Stargate g => g.Kind.ToString(),
        ConstructionSite c => $"{c.Building} site",
        _ => "Unknown",
    };

    /// <summary>
    /// DisplayFleetInfo's own Emp=Player branch (CLSCOMM.PAS:700-716): an in-transit fleet you own
    /// shows its real ETA (<see cref="FleetLifecycle.EstimatedDateOfArrival"/>); a Scouted-but-not-owned
    /// one shows the literal "(?)" placeholder baked into Pascal's own <c>FltStatusName[FInTrans]</c>
    /// ('In transit (?)'), since nothing here computes another empire's ETA; anything less than
    /// Scouted is "(unknown)".
    /// </summary>
    public static string DescribeFleetStatus(Fleet fleet, Empire viewer, Game game)
    {
        var owned = ReferenceEquals(fleet.Owner, viewer);
        if (!owned && !Game.Scouted(viewer, fleet))
        {
            return "(unknown)";
        }

        if (fleet.Status != FleetStatus.InTransit)
        {
            return FleetStatusNames[(int)fleet.Status];
        }

        return owned
            ? $"In transit ({FleetLifecycle.EstimatedDateOfArrival(fleet, game)})"
            : "In transit (?)";
    }

    /// <summary>
    /// DisplayFleetInfo's own destination gate (CLSCOMM.PAS:720-732): even Scouted, a non-owned
    /// fleet's destination only shows while that fleet is <see cref="FleetStatus.Ready"/>; while it's
    /// still in transit, where it's headed stays hidden regardless.
    /// </summary>
    public static string DescribeFleetDestination(Fleet fleet, Empire viewer, Coordinate origin)
    {
        var visible = ReferenceEquals(fleet.Owner, viewer) ||
            (fleet.Status == FleetStatus.Ready && Game.Scouted(viewer, fleet));

        if (!visible)
        {
            return "(unknown)";
        }

        return fleet.Destination is { } dest ? RelativeCoordinate.Format(dest, origin) : "(none)";
    }
}
