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
/// real Pascal order. Bookmarks (bare-coordinate names, <see cref="Empire.Bookmarks"/>) have no object
/// to sit alongside, so they're appended last.
/// </summary>
public static class NamesWindowReport
{
    public static List<NameEntry> BuildRows(Galaxy.Galaxy galaxy, Empire viewer)
    {
        var rows = new List<NameEntry>();
        rows.AddRange(galaxy.Fleets.Where(f => f.Names.ContainsKey(viewer)).Select(NameEntry.For));
        rows.AddRange(galaxy.Planets.Where(p => p.Names.ContainsKey(viewer)).Select(NameEntry.For));
        rows.AddRange(galaxy.Starbases.Where(s => s.Names.ContainsKey(viewer)).Select(NameEntry.For));
        rows.AddRange(galaxy.Stargates.Where(g => g.Names.ContainsKey(viewer)).Select(NameEntry.For));
        rows.AddRange(galaxy.ConstructionSites.Where(c => c.Names.ContainsKey(viewer)).Select(NameEntry.For));
        rows.AddRange(viewer.Bookmarks.Select(NameEntry.For));
        return rows;
    }
}

/// <summary>
/// A row in the Names window: either a named <see cref="ISectorObject"/> or a bare-coordinate
/// <see cref="LocationBookmark"/> -- never both, same closed-union shape as <c>NewsItem.Resource</c>.
/// </summary>
public sealed record NameEntry(ISectorObject? Object, LocationBookmark? Bookmark)
{
    public static NameEntry For(ISectorObject obj) => new(obj, null);
    public static NameEntry For(LocationBookmark bookmark) => new(null, bookmark);
}
