using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Entities;

/// <summary>
/// Empire menu's Tech Tree screen -- a UI real Pascal never had. Tech advancement there is a per-turn
/// random roll weighted by capital efficiency and university/ruins worlds (UPDATE.PAS's NewTechLevel),
/// not a player-directed research queue, so there was nothing to browse toward; the closest real
/// Pascal equivalent is the turn-start Empire Status Report's "technologies mastered beyond your
/// level" list (<see cref="TechCatalog.UnlockedBeyond"/>), which only ever shows the extras, not the
/// whole picture. This groups every <see cref="TechCatalog"/> entry by its own <see cref="TechCatalog.AllEntries"/>-supplied
/// <c>MinTech</c>, ascending, so the viewer can see what's owned at every level up through their own
/// and what's still locked above it.
/// </summary>
public static class TechTreeReport
{
    public readonly record struct Row(TechLevel Level, TechCatalog.TechGrantIdentity Identity, bool Owned);

    public static List<Row> BuildRows(Empire viewer) =>
        [.. TechCatalog.AllEntries(viewer.Technology)
            .Select(e => new Row(e.MinTech, e.Identity, e.Owned))
            .OrderBy(r => r.Level)];
}
