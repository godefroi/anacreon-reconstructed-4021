using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Types;
using static ThreeLn.Reconstruction4021.Core.PascalMath;

namespace ThreeLn.Reconstruction4021.Core.Combat;

/// <summary>
/// Standalone attack mechanics (Phase 5 commit 5g) that share CombatOutcome/CombatEngine's own
/// primitives but sit outside the ResolveAttack pipeline — each is its own separate Pascal entry
/// point, not a branch NPEAttack's group/shell engine ever reaches on its own.
///
/// HolocaustWorld/HolocaustEffectiveness (ATTACK.PAS) are deliberately NOT here: their only caller,
/// MSCCOMM.PAS's HolocaustCommand, is wrapped in a Pascal comment block (<c>(* ... *)</c>) in both the
/// 1.31 and 2.0 source trees, and PLAYTURN.PAS's own dispatch entry for it sits inside a separate
/// commented-out <c>(*ARTIFACTS ... *)</c> block — even HolocaustCommand's own forward interface
/// declaration is commented out, so the real game could not have called it. Confirmed genuinely dead
/// code, not "no caller wired up yet" (that phrase means live Pascal with no port-side consumer built
/// yet, e.g. <see cref="SelfDestructObject"/> below) — same treatment already given to
/// BATTLE.PAS/BOMBER.PAS and ConquerEmpire's commented-out starbase-recapture loop.
/// </summary>
public static class CombatStandalone
{
    /// <summary>
    /// LAMAttack (ATTACK.PAS:1623-1704). Live callers: DESIGN.PAS's LaunchLAM (human command, Phase 8)
    /// and NPE03/NPE00/NPEINTR's guardian/NPE strikes (Phase 6) — unlike Holocaust, genuinely reachable
    /// in the shipped game, just with no consumer built yet in this port. Has no Rnd calls at all (pure
    /// proportional-distribution arithmetic), unlike every other 5x commit's math.
    ///
    /// Returns the per-type destroyed counts Pascal threads back through <c>ShipsDest</c>/<c>DefnsDest</c>
    /// VAR params — real callers only use them for UI display text, which doesn't exist in this port yet,
    /// but they're real computed output worth keeping (and are exactly what a future golden-file domain
    /// would assert against). <see cref="Target"/> mirrors Pascal's IDNumber union: a <see cref="Fleet"/>
    /// or an <see cref="IEconomicWorld"/> (planet or starbase) — LAMs are never launched at a
    /// construction site or stargate.
    ///
    /// BalanceFleet's post-damage cargo rebalance isn't ported — no fleet cargo-capacity system exists
    /// anywhere in this port yet (same gap as RestoreCombatant/AbortFleet's own doc comments). Cargo is
    /// untouched either way here since LAMs only ever destroy ships/defenses, never cargo.
    /// FleetNameDestruction's naming-system call is dropped too (AbortFleet's own established gap) —
    /// its only load-bearing effect was setting Pascal's Loc to the fleet itself, which this port's
    /// <paramref name="target"/> reference already is.
    /// </summary>
    public static (ShipCounts ShipsDestroyed, DefenseCounts DefensesDestroyed) LAMAttack(
        Empire player, int lamToUse, object target, Game game)
    {
        var shipsDestroyed = new ShipCounts();
        var defensesDestroyed = new DefenseCounts();
        var subject = (ISectorObject)target;
        var targetOwner = subject.Owner;

        if (target is Fleet fleet) {
            var ships = fleet.Ships;
            var totalShipSpace = Enum.GetValues<ShipType>().Sum(t => ships[t] * (CombatConstants.ProtecNeeded[t] / 100.0));
            if (totalShipSpace == 0) {
                totalShipSpace = 1;
            }

            foreach (var t in Enum.GetValues<ShipType>()) {
                var lamsPerType = PascalRound(lamToUse * ((ships[t] * (CombatConstants.ProtecNeeded[t] / 100.0)) / totalShipSpace));
                shipsDestroyed[t] = Math.Min((int)(lamsPerType / 100.0 * CombatConstants.CombatTable[(AttackType.Lam, t.ToAttackType())]), ships[t]);
            }

            if (Enum.GetValues<ShipType>().All(t => ships[t] - shipsDestroyed[t] == 0)) {
                targetOwner.AddNews(NewsType.FleetDestroyedByLams, fleet, otherEmpire: player);
                CombatOutcome.DestroyFleet(fleet, game);
            } else {
                targetOwner.AddNews(NewsType.FleetDamagedByLams, fleet, otherEmpire: player);
                foreach (var t in Enum.GetValues<ShipType>()) {
                    ships[t] -= shipsDestroyed[t];
                }
            }

            foreach (var t in Enum.GetValues<ShipType>()) {
                if (shipsDestroyed[t] > 0) {
                    // Pascal's raw ResourceTypes ordinal for fgt..trn is ShipType's own C# ordinal + 5
                    // (LAM=1,def=2,GDM=3,ion=4,fgt=5..trn=11 — see CombatOutcome.AbortFleet's own comment).
                    targetOwner.AddNews(NewsType.DestructionDetail, fleet, p1: shipsDestroyed[t], p2: (int)t + 5);
                }
            }
        } else if (target is IEconomicWorld world) {
            targetOwner.AddNews(NewsType.EmpireAttackedWithLams, subject, otherEmpire: player);

            var defenses = world.Defenses;
            var totalDefns = Enum.GetValues<DefenseType>().Sum(t => defenses[t]);
            if (totalDefns == 0) {
                totalDefns = 1;
            }

            foreach (var t in Enum.GetValues<DefenseType>()) {
                var lamsPerType = PascalRound((lamToUse / (double)totalDefns) * defenses[t]);
                defensesDestroyed[t] = Math.Min((int)(lamsPerType / 100.0 * CombatConstants.CombatTable[(AttackType.Lam, t.ToAttackType())]), defenses[t]);
                defenses[t] -= defensesDestroyed[t];
                if (defensesDestroyed[t] > 0) {
                    // Pascal's raw ResourceTypes ordinal for LAM..ion is DefenseType's own C# ordinal + 1.
                    targetOwner.AddNews(NewsType.DestructionDetail, subject, p1: defensesDestroyed[t], p2: (int)t + 1);
                }
            }
        } else {
            throw new ArgumentException($"LAMAttack: unsupported target type {target.GetType()}", nameof(target));
        }

        game.AddGlobalNews([targetOwner, player], subject, NewsType.EmpireLamStrikeGlobal, otherEmpire: player, defender: targetOwner);

        return (shipsDestroyed, defensesDestroyed);
    }

    /// <summary>
    /// DestroyConstructionOrGate (ATTACK.PAS:1706-1733) — live: called by ATTNPE.PAS's NPEAttack for a
    /// construction-site/stargate target (wired into <see cref="CombatResolution.NPEAttack"/>'s own
    /// Con/Gate branch) and by ATTCOMM.PAS's human command (Phase 8, no consumer yet). Reads
    /// <c>position:</c>, not <c>subject:</c>, because the object itself is already gone by the time
    /// AddNews fires — same "removed first, then position-only AddNews" shape as
    /// AnnualTickHandler.Construction's own ConstructionCompleted call.
    /// </summary>
    public static void DestroyConstructionOrGate(Empire attacker, bool hkSurprise, object target, Game game, Random random)
    {
        switch (target) {
            case ConstructionSite site: {
                var owner = site.Owner;
                var location = site.Location;
                game.Galaxy.ConstructionSites.Remove(site);
                CombatOutcome.ChangeTotalRevIndex(owner, Rnd(random, 3, 7));
                if (hkSurprise) {
                    owner.AddNews(NewsType.ConstructionDestroyedByUnknown, position: location);
                } else {
                    owner.AddNews(NewsType.ConstructionSiteDestroyed, position: location, otherEmpire: attacker);
                }
                break;
            }
            case Stargate gate: {
                var owner = gate.Owner;
                var location = gate.Location;
                game.Galaxy.Stargates.Remove(gate);
                CombatOutcome.ChangeTotalRevIndex(owner, Rnd(random, 7, 15));
                if (hkSurprise) {
                    owner.AddNews(NewsType.StargateDestroyedByUnknown, position: location);
                } else {
                    owner.AddNews(NewsType.StargateDestroyed, position: location, otherEmpire: attacker);
                }
                break;
            }
            default:
                throw new ArgumentException($"DestroyConstructionOrGate: unsupported target type {target.GetType()}", nameof(target));
        }
    }

    /// <summary>
    /// SelfDestructObject (SBASE.PAS:29-86) — live: MSCCOMM.PAS's SelfDestructCommand (Phase 8, no
    /// consumer wired up yet, same precedent as Empire.News.Clear()). <paramref name="target"/> is
    /// always a <see cref="Starbase"/> or <see cref="Stargate"/> — Pascal's own SelfDestructCommand
    /// only ever offers "bases, and stargates" as targets.
    ///
    /// The per-empire "who's scouted this and should be told" broadcast is exactly
    /// <see cref="Game.AddGlobalNews"/>'s own job (Pascal hand-rolls the identical
    /// EmpireActive/Scouted loop here) — reused rather than duplicated. The fleet-destruction loop is
    /// separate: every fleet at the same location is destroyed regardless of owner, but only a
    /// *foreign* fleet's owner gets told (matching real Pascal's <c>OtherEmp&lt;&gt;Emp</c> guard), so
    /// it's a plain per-fleet AddNews, not a broadcast. FleetNameDestruction's naming-system call is
    /// dropped (AbortFleet's own established gap) — its only load-bearing effect was setting Pascal's
    /// Loc to the fleet itself, which this port already has as a direct reference.
    /// </summary>
    public static void SelfDestructObject(object target, Game game)
    {
        var subject = (ISectorObject)target;
        var owner = subject.Owner;
        var location = subject.Location;

        game.AddGlobalNews([owner], subject, NewsType.StarbaseSelfDestructed, otherEmpire: owner);

        foreach (var fleet in game.Galaxy.Fleets.Where(f => f.Location == location).ToList()) {
            var fleetOwner = fleet.Owner;
            if (fleetOwner != owner) {
                fleetOwner.AddNews(NewsType.FleetsDestroyedInExplosion, fleet);
                foreach (var t in Enum.GetValues<ShipType>()) {
                    if (fleet.Ships[t] > 0) {
                        fleetOwner.AddNews(NewsType.DestructionDetail, fleet, p1: fleet.Ships[t], p2: (int)t + 5);
                    }
                }
            }
            CombatOutcome.DestroyFleet(fleet, game);
        }

        switch (target) {
            case Starbase starbase:
                game.Galaxy.Starbases.Remove(starbase);
                break;
            case Stargate gate:
                game.Galaxy.Stargates.Remove(gate);
                break;
            default:
                throw new ArgumentException($"SelfDestructObject: unsupported target type {target.GetType()}", nameof(target));
        }
    }
}
