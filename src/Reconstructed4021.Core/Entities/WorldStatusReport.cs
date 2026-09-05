using Reconstructed4021.Core.Galaxy;

namespace Reconstructed4021.Core.Entities;

/// <summary>
/// STAWIND.PAS: StatusWindow (F3), <c>InitializeStatusDataArray</c>. The ordered world list the
/// Status Window scrolls through: <paramref name="viewer"/>'s own capital first, then their
/// remaining owned planets/starbases, then every scouted-but-not-owned planet/starbase -- two
/// separately-sorted blocks (own worlds, then known foreign worlds), not one combined sort.
///
/// Sort key within each block, descending, is real Pascal's own packed 16-bit sort key
/// (<c>QuickSortD</c>'s doc comment: "sorts... based on the first word of each element" -- the
/// element's first two fields, <c>Pop: Byte</c> then <c>Tech: TechLevel</c>, pack into one Pascal
/// Word with Tech in the high byte) confirmed to mean tech level dominates, population is only a
/// tie-breaker within one tech level -- not assumed from field declaration order alone. This uses
/// full <see cref="IEconomicWorld.Population"/> rather than Pascal's own <c>Hi(Population)</c>
/// truncation to the high byte: a real difference, but one that only ever changes tie order among
/// worlds at the same tech level on a pure intel display, never anything that affects play.
/// </summary>
public static class WorldStatusReport
{
    public static List<IEconomicWorld> BuildRows(Galaxy.Galaxy galaxy, Empire viewer)
    {
        var capital = viewer.Capital;
        var rows = new List<IEconomicWorld>();
        if (capital is not null) {
            rows.Add(capital);
        }

        IEnumerable<IEconomicWorld> AllWorlds() =>
            galaxy.Planets.Cast<IEconomicWorld>().Concat(galaxy.Starbases);

        rows.AddRange(AllWorlds()
            .Where(w => ReferenceEquals(w.Owner, viewer) && !ReferenceEquals(w, capital))
            .OrderByDescending(w => w.TechLevel)
            .ThenByDescending(w => w.Population));

        rows.AddRange(AllWorlds()
            .Where(w => !ReferenceEquals(w.Owner, viewer) && Game.Scouted(viewer, w))
            .OrderByDescending(w => w.TechLevel)
            .ThenByDescending(w => w.Population));

        return rows;
    }
}
