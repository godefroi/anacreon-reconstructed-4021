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
