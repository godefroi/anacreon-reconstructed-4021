using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Entities;

/// <summary>
/// Construction-site creation (CONSTR.PAS: ConstructCommand, INTRFACE.PAS: Construction) -- kept
/// separate from <see cref="Turns.AnnualTickHandler"/>'s own per-tick <c>UpdateConstruction</c> (the
/// countdown/completion stepper) the same way <see cref="FleetLifecycle"/> is split from
/// <see cref="Turns.FleetMovementHandler"/>: this is a one-shot mutator a player command calls, not
/// part of the annual tick.
/// </summary>
public static class ConstructionLifecycle
{
    /// <summary>
    /// Construction (INTRFACE.PAS:481-516), minus the fixed-slot allocation (<c>GetConstructionSlot</c>
    /// -- this port's <see cref="Galaxy.Galaxy.ConstructionSites"/> has no slot cap, matching every
    /// other entity list here) and the sector-occupancy grid write (<c>Sector[Loc.x]^[Loc.y].Obj</c> --
    /// this port defers that to the movement phase, see <c>AnnualTickHandler.Construction.cs</c>'s own
    /// doc comment). No upfront resource cost -- real Pascal's own <c>Construction</c> never touches
    /// <c>Cargo</c> either; <c>UseUpRawMaterial</c> draws the per-year cost down starting next tick.
    ///
    /// Doesn't mark <paramref name="owner"/>'s own <see cref="Empire.ConstructionSites"/> visibility
    /// (Pascal's own <c>ScoutedBy:=[Empr]; KnownBy:=[Empr]</c>) -- unnecessary here: <see cref="Game.Visible"/>/
    /// <see cref="Game.ScoutedOrOwned"/> already fall back to plain ownership, the same reason freshly
    /// created Starbases/Stargates (<c>AnnualTickHandler.Construction.cs</c>'s own
    /// <c>CreateStarbase</c>/<c>CreateStargate</c>) don't mark it either -- confirmed no call site does.
    /// </summary>
    public static ConstructionSite StartConstruction(Game game, Empire owner, ConstructionType type, Coordinate location)
    {
        var site = new ConstructionSite {
            Location = location,
            Owner = owner,
            Building = type,
            YearsToCompletion = ConstructionCatalog.YearsToBuild[type],
        };

        game.Galaxy.ConstructionSites.Add(site);
        return site;
    }
}
