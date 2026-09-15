using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Presentation;
using Reconstructed4021.Core.Types;


namespace Reconstructed4021.Tui2.Shared;


// Reconstructed4021.Tui's CloseUpWindow.YesNo/DescribeFleetStatus/DescribeFleetDestination restated
// here since Tui2 doesn't reference the Tui project (same reasoning as GalaxyMapScreen's own restated
// EmpirePalette) -- these three have no Terminal.Gui dependency in the original either, just living on
// a TG-derived class. See CloseUpWindow's own doc comments for the CLSCOMM.PAS citations.
internal static class CloseUpWindowText
{
    internal static string YesNo(int level) => level switch
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

    internal static readonly string[] FleetStatusNames = ["at destination", "In transit", "out of trillum", "lost"];

    internal static string DescribeFleetStatus(Fleet fleet, Empire viewer, Game game)
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

    internal static string DescribeFleetDestination(Fleet fleet, Empire viewer, Coordinate origin)
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
