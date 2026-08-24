using ThreeLn.Reconstruction4021.Core.Galaxy;

namespace ThreeLn.Reconstruction4021.Core.Entities;

/// <summary>
/// Common shape of Pascal's <c>IDNumber</c> targets that aren't fleets: a planet, starbase, stargate,
/// or construction site. <see cref="Planet"/>/<see cref="Starbase"/> already declare both members via
/// <see cref="IEconomicWorld"/>; <see cref="Stargate"/>/<see cref="ConstructionSite"/> already declare
/// them independently. This interface adds no new members to any of the four — it names a shape
/// <see cref="Turns.VisibilityHandler"/>'s own probe-scouting helper already dispatches across by hand,
/// so <c>NewsItem.Subject</c> and <c>Game.AddGlobalNews</c>'s Scouted-check can hold/switch on one
/// reference type instead of <c>object</c>.
/// </summary>
public interface ISectorObject
{
    Coordinate Location { get; }
    Empire Owner { get; }
}
