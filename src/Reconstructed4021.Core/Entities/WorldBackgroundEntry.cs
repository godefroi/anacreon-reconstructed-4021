namespace Reconstructed4021.Core.Entities;

/// <summary>
/// SCENA.PAS's SatisfiesConditions dispatches a condition row's clauses by their own leading
/// character (<c>CASE Parm[i][1] OF 'E': ... 'A': ...</c>), not by a boolean's polarity — this enum
/// matches that dispatch shape directly. Pascal's <c>CASE</c> has no <c>ELSE</c>, i.e. it isn't
/// declaring a closed two-state set on purpose; only 'E' and 'A' happen to be the only clause kinds
/// any real scenario (or the file format's own documentation) defines.
/// </summary>
public enum BackgroundConditionKind
{
    /// <summary>'E:' — passes only outside a conquest report (SCENA.PAS's Conquer=False), when the object's current owner is one of the listed empires.</summary>
    OwnedBy,

    /// <summary>'A:' — passes only inside a conquest report (Conquer=True), when the object's current (just-reassigned) owner is one of the listed empires.</summary>
    ConqueredBy,
}

/// <summary>
/// SCENA.PAS's SatisfiesConditions: one condition clause from a WorldBackgroundIndex row. A row's
/// conditions all have to pass (ANDed) for that row to match — see
/// <see cref="Game.FindWorldBackgroundText"/>.
/// </summary>
public sealed record BackgroundCondition(BackgroundConditionKind Kind, IReadOnlySet<Empire> Empires);

/// <summary>
/// One row of a .SCN file's own WorldBackgroundIndex (SCENA.PAS), already resolved from its literal
/// "type:index" token to the real object it names — see ScenarioLoader's own resolution pass, since
/// the index block is parsed before the objects and empires it references exist yet in every real
/// scenario file.
/// </summary>
public sealed record WorldBackgroundEntry(ISectorObject World, IReadOnlyList<BackgroundCondition> Conditions, int TextNumber);
