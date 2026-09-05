using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Entities;

/// <summary>
/// FLTWIND.PAS: FleetWindow (F5), <c>InitializeFleetDataArray</c>. The ordered row list the Fleet
/// Window scrolls through -- rows are <see cref="Fleet"/>s and, for the viewer's own Command Base/
/// Fortress <see cref="Starbase"/>s only, starbases too (real Pascal's own <c>GetBaseType(ID) IN
/// [cmm,frt]</c> filter -- <see cref="StarbaseKind.CommandBase"/>/<see cref="StarbaseKind.Fortress"/>).
///
/// Four blocks, not one combined sort: the viewer's own active fleets, then their own Command Base/
/// Fortress starbases, then known-and-scouted enemy fleets, then known-but-not-scouted enemy fleets.
/// The first three are each sorted descending by the same packed-word key <see cref="WorldStatusReport"/>
/// uses elsewhere (here, a fleet's own XY coordinate -- Y in the high byte, X the tie-breaker, per
/// <c>XYCoord</c>'s own <c>x,y: Coordinate</c> field order in GALAXY.PAS); the fourth block is left in
/// whatever order <see cref="Galaxy.Galaxy.Fleets"/> already has it, matching real Pascal's own source
/// exactly -- <c>InitializeFleetDataArray</c>'s last block has no <c>SortSection</c> call after it,
/// unlike the three before it.
/// </summary>
public static class FleetStatusReport
{
    public static List<ISectorObject> BuildRows(Galaxy.Galaxy galaxy, Empire viewer)
    {
        var rows = new List<ISectorObject>();

        rows.AddRange(galaxy.Fleets
            .Where(f => ReferenceEquals(f.Owner, viewer))
            .OrderByDescending(f => f.Location.Y)
            .ThenByDescending(f => f.Location.X));

        rows.AddRange(galaxy.Starbases
            .Where(s => ReferenceEquals(s.Owner, viewer) && s.Kind is StarbaseKind.CommandBase or StarbaseKind.Fortress)
            .OrderByDescending(s => s.Location.Y)
            .ThenByDescending(s => s.Location.X));

        rows.AddRange(galaxy.Fleets
            .Where(f => !ReferenceEquals(f.Owner, viewer) && Game.Scouted(viewer, f))
            .OrderByDescending(f => f.Location.Y)
            .ThenByDescending(f => f.Location.X));

        rows.AddRange(galaxy.Fleets
            .Where(f => !ReferenceEquals(f.Owner, viewer) && !Game.Scouted(viewer, f) && Game.Known(viewer, f)));

        return rows;
    }
}
