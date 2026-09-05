namespace Reconstructed4021.Core.Entities;

/// <summary>
/// NMSWIND.PAS's InitNamesDataArray: every object the viewer has given a name to, fleets first
/// (matching Pascal's own two-pass filter of its single per-empire linked list), then every other
/// named kind. Real Pascal's list is independent of the live galaxy -- a destroyed fleet's own
/// NameRecord survives with its <c>DestFlt</c> marker, rendered as "(destroyed)" -- but this port keeps
/// no such history: a destroyed <see cref="Fleet"/> is removed from <see cref="Galaxy.Galaxy.Fleets"/>
/// outright elsewhere in this port, so its name just stops appearing here instead. Non-fleet kinds have
/// no Pascal-given relative order beyond "whenever it was named" (also not tracked here), so this port
/// orders them by <see cref="Galaxy.Galaxy"/>'s own list order within each kind -- Planets, then
/// Starbases, then Stargates, then ConstructionSites -- a deterministic stand-in, not a transcription of
/// real Pascal order.
/// </summary>
public static class NamesWindowReport
{
    public static List<ISectorObject> BuildRows(Galaxy.Galaxy galaxy, Empire viewer)
    {
        var rows = new List<ISectorObject>();
        rows.AddRange(galaxy.Fleets.Where(f => f.Names.ContainsKey(viewer)));
        rows.AddRange(galaxy.Planets.Where(p => p.Names.ContainsKey(viewer)));
        rows.AddRange(galaxy.Starbases.Where(s => s.Names.ContainsKey(viewer)));
        rows.AddRange(galaxy.Stargates.Where(g => g.Names.ContainsKey(viewer)));
        rows.AddRange(galaxy.ConstructionSites.Where(c => c.Names.ContainsKey(viewer)));
        return rows;
    }
}
