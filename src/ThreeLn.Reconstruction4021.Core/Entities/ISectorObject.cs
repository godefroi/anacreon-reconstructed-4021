using ThreeLn.Reconstruction4021.Core.Galaxy;

namespace ThreeLn.Reconstruction4021.Core.Entities;

/// <summary>
/// Common shape of Pascal's <c>IDNumber</c> targets: a planet, starbase, stargate, construction site,
/// or fleet. <see cref="Planet"/>/<see cref="Starbase"/> already declare both members via
/// <see cref="IEconomicWorld"/>; <see cref="Stargate"/>/<see cref="ConstructionSite"/>/<see cref="Fleet"/>
/// already declare them independently. This interface adds no new members to any of the five — it
/// names a shape <see cref="Turns.VisibilityHandler"/>'s own probe-scouting helper already dispatches
/// across by hand, so <c>NewsItem.Subject</c> and <c>Game.AddGlobalNews</c>'s Scouted-check can
/// hold/switch on one reference type instead of <c>object</c>. Fleet joined the other four in Phase 5
/// commit 5f, once ResolveAttack's fleet-target branch needed to fire <c>GLBConq</c>/<c>BattleL</c>
/// with a destroyed fleet as the news subject.
/// </summary>
public interface ISectorObject
{
    Coordinate Location { get; }
    Empire Owner { get; }
}
