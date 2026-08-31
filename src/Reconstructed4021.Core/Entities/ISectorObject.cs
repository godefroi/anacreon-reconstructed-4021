using Reconstructed4021.Core.Galaxy;

namespace Reconstructed4021.Core.Entities;

/// <summary>
/// Common shape of Pascal's <c>IDNumber</c> targets: a planet, starbase, stargate, construction site,
/// or fleet. <see cref="Planet"/>/<see cref="Starbase"/> already declare these members via
/// <see cref="IEconomicWorld"/>; <see cref="Stargate"/>/<see cref="ConstructionSite"/>/<see cref="Fleet"/>
/// already declare them independently. It names a shape <see cref="Turns.VisibilityHandler"/>'s own
/// probe-scouting helper already dispatches across by hand, so <c>NewsItem.Subject</c> and
/// <c>Game.AddGlobalNews</c>'s Scouted-check can hold/switch on one reference type instead of
/// <c>object</c>. Fleet joins the other four once ResolveAttack's fleet-target branch needs to fire
/// <c>GLBConq</c>/<c>BattleL</c> with a destroyed fleet as the news subject.
/// </summary>
public interface ISectorObject
{
    Coordinate Location { get; }
    Empire Owner { get; }

    /// <summary>
    /// PRIMINTR.PAS's naming system (AddName/DeleteName/Location2Index/GetDefinedName), reshaped to
    /// fit this port's object-reference model instead of Pascal's array-slot one: a bookmark tied to
    /// a specific object lives on the object itself, keyed by whichever empire named it (any empire
    /// can name anything it can see, not just its own — matches Pascal's real per-Player AddName).
    /// This is a deliberate inversion of Pascal's own per-empire linked list, not a structural port,
    /// because it makes cleanup free: once an object is removed from its owning <see cref="Galaxy.Galaxy"/>
    /// list, nothing can reach its <see cref="Names"/> dictionary again, so a destroyed object's names
    /// disappear along with it with no explicit cascading-delete call needed anywhere (see
    /// <c>docs/PORT_DESIGN.md</c>'s "Naming system" section for the full reasoning, including what's
    /// deliberately not ported). A bookmark for a bare coordinate with no object at all is the one
    /// case this can't hold — that's <see cref="Empire.Bookmarks"/>'s own job.
    /// </summary>
    Dictionary<Empire, string> Names { get; set; }
}
