using Reconstructed4021.Core.Galaxy;
namespace Reconstructed4021.Core.Presentation;

/// <summary>
/// GetCoordName/RelativeX/RelativeY (PRIMINTR.PAS:1173-1193, 1413-1431): every coordinate shown to a
/// player is relative to that player's own capital ("0,0" there), never the raw galaxy-absolute
/// value -- and the Y axis is flipped (capital's Y minus the point's, not the other way around).
/// Shared by the galaxy map's own cursor readout and the fleet close-up's Position/Destination
/// fields (both in Reconstructed4021.Tui), which used to compute this same formula (one right, one
/// wrong -- absolute coordinates, a real reported bug) independently.
/// </summary>
public static class RelativeCoordinate
{
    public static string Format(Coordinate point, Coordinate origin) => $"{point.X - origin.X},{origin.Y - point.Y}";
}
