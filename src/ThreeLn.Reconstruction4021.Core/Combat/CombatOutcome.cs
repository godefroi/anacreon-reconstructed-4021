using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Turns;
using ThreeLn.Reconstruction4021.Core.Types;
using static ThreeLn.Reconstruction4021.Core.PascalMath;

namespace ThreeLn.Reconstruction4021.Core.Combat;

/// <summary>
/// Outcome application (ATTACK.PAS's ConquerWorld/ConquerEmpire/RestoreCombatant/ResolveAttack, plus
/// FLEET.PAS's DestroyFleet/AbortFleet and INTRFACE.PAS's DestroyEmpire — Phase 5 commit 5f). Called
/// by <see cref="CombatResolution.NPEAttack"/> once a whole engagement's Result/Casualties/Killed are
/// known; nothing in 5d/5e read those tallies for anything beyond returning them.
///
/// Empire elimination is plain <c>Game.Empires.Remove</c>, not a ported <c>InUse</c> flag — see
/// docs/PORT_DESIGN.md's "Empire elimination" section for why that self-heals <see cref="Game.NextEmpire"/>/
/// <see cref="Game.IsFirstEmpire"/> with no new guard needed anywhere. A human empire is never torn
/// down this way — <see cref="Empire.DefeatedBy"/> is set instead, matching Pascal's own
/// <c>EmpirePlayer</c> branch in ConquerEmpire.
/// </summary>
public static class CombatOutcome
{
    /// <summary>ConquerWorld (ATTACK.PAS:915-983).</summary>
    public static void ConquerWorld(IEconomicWorld world, Empire conqueror, Game game, Random random)
    {
        world.Reassign(conqueror);
        world.Efficiency = Math.Max(0, world.Efficiency - Rnd(random, 10, 20));
        ChangeRevolutionIndexOnConquest(world, random);

        // Scout(Emp,XY) (INTRFACE.PAS) -- VisibilityHandler.ScoutAdjacent is this port's own stand-in
        // for that exact primitive (see its own doc comment for the gaps it still has: no POk news on
        // first contact, no dark-nebula early exit).
        VisibilityHandler.ScoutAdjacent(world.Location, conqueror, game);
    }

    /// <summary>ConquerWorld's nested ChangeRevolutionIndex (ATTACK.PAS:946-974) — a tier cascade by the world's *current* RevolutionIndex, genuinely separate from Phase 4's rebellion-warning cascade (different tiers, different draws).</summary>
    private static void ChangeRevolutionIndexOnConquest(IEconomicWorld world, Random random)
    {
        var rev = world.RevolutionIndex;
        if (rev <= 10) {
            AnnualTickHandler.ChangeRevIndex(world, Rnd(random, 10, 20));
        } else if (rev <= 30) {
            var roll = Rnd(random, 1, 100);
            if (roll <= 20) {
                AnnualTickHandler.ChangeRevIndex(world, -Rnd(random, 5, 15));
            } else if (roll <= 50) {
                AnnualTickHandler.ChangeRevIndex(world, -Rnd(random, 3, 10));
            } else if (roll <= 90) {
                AnnualTickHandler.ChangeRevIndex(world, Rnd(random, 10, 15));
            } else {
                AnnualTickHandler.ChangeRevIndex(world, Rnd(random, 20, 30));
            }
        } else if (rev <= 55) {
            var roll = Rnd(random, 1, 100);
            if (roll <= 50) {
                AnnualTickHandler.ChangeRevIndex(world, -Rnd(random, 10, 20));
            } else if (roll <= 75) {
                AnnualTickHandler.ChangeRevIndex(world, -Rnd(random, 5, 10));
            } else if (roll <= 90) {
                AnnualTickHandler.ChangeRevIndex(world, Rnd(random, 1, 5));
            } else {
                AnnualTickHandler.ChangeRevIndex(world, Rnd(random, 5, 15));
            }
        } else if (rev <= 75) {
            AnnualTickHandler.ChangeRevIndex(world, -Rnd(random, 40, 55));
        } else if (rev <= 90) {
            AnnualTickHandler.ChangeRevIndex(world, -Rnd(random, 50, 65));
        } else {
            AnnualTickHandler.ChangeRevIndex(world, -Rnd(random, 30, 40));
        }
    }

    /// <summary>
    /// ConquerEmpire (ATTACK.PAS:985-1139). <c>Booty</c> (the set of world indices that joined the
    /// conqueror) is declared and threaded through real Pascal's own NPEAttack call chain but never
    /// read anywhere in it — dropped entirely, same "confirmed unused by reading" precedent as 5e's
    /// <c>RetrIndex</c>. The starbase-recapture loop (ATTACK.PAS:1099-1118) is real Pascal's own dead
    /// code, disabled with <c>(* ... *)</c> in source — not ported, matching that exact scoping.
    /// </summary>
    public static void ConquerEmpire(Empire conqueror, Empire enemyEmpire, Game game, Random random)
    {
        var enemyCapXY = enemyEmpire.Capital!.Location;
        var conquerorCapXY = conqueror.Capital!.Location;
        IEconomicWorld? newCapital = null;

        foreach (var planet in game.Galaxy.Planets.Where(p => p.Owner == enemyEmpire).ToList()) {
            var dist = planet.Location.DistanceTo(enemyCapXY);
            var distToConq = planet.Location.DistanceTo(conquerorCapXY);
            var pop = planet.Population;
            var revI = planet.RevolutionIndex;

            if (dist > 10 && distToConq < 10 && pop < Rnd(random, 900, 1100)) {
                ConquerWorld(planet, conqueror, game, random);
                enemyEmpire.AddNews(NewsType.WorldJoinedOtherEmpire, planet, otherEmpire: conqueror);
            } else if (pop > Rnd(random, 900, 1100) && revI > 50 && Rnd(random, 1, 100) < 75) {
                ((IEconomicWorld)planet).Reassign(Empire.Independent);
                planet.Type = WorldType.Independent;
                enemyEmpire.AddNews(NewsType.WorldDeclaredIndependence, planet);
            } else if (distToConq < 10 && Rnd(random, 1, 100) < 60 && revI > 35) {
                ConquerWorld(planet, conqueror, game, random);
                enemyEmpire.AddNews(NewsType.WorldJoinedOtherEmpire, planet, otherEmpire: conqueror);
            } else if (newCapital is null) {
                if (planet.TechLevel > TechLevel.Jump) {
                    newCapital = planet;
                }
            } else if (planet.TechLevel > newCapital.TechLevel) {
                newCapital = planet;
            } else if (planet.TechLevel == newCapital.TechLevel) {
                if (!IsBaseWorldType(newCapital.Type)) {
                    if (pop > newCapital.Population || IsBaseWorldType(planet.Type)) {
                        newCapital = planet;
                    }
                } else if (IsBaseWorldType(planet.Type) && pop > newCapital.Population) {
                    newCapital = planet;
                }
            }
        }

        if (enemyEmpire.LosesIfCapitalConquered || newCapital is null) {
            // ASSERT: enemy empire totally destroyed.
            if (game.TurnHandlers[enemyEmpire].IsHuman) {
                enemyEmpire.Capital = null;
                enemyEmpire.DefeatedBy = conqueror;
            } else {
                DestroyEmpire(enemyEmpire, game);
            }
        } else {
            NewCapital(enemyEmpire, newCapital, random);
            enemyEmpire.AddNews(NewsType.WorldIsNowCapital, newCapital);
        }
    }

    /// <summary>[BseTyp,BseSTyp,JmpTyp,JmpSTyp,StrTyp,StrSTyp] (ATTACK.PAS:1078,1083,1090) — every base/jumpship-base/starship-base designation, planet or starbase kind.</summary>
    private static bool IsBaseWorldType(WorldType type) => type is
        WorldType.Base or WorldType.BaseStarbase or
        WorldType.JumpshipBase or WorldType.JumpshipBaseStarbase or
        WorldType.StarshipBase or WorldType.StarshipBaseStarbase;

    /// <summary>ConquerEmpire's nested NewCapital (ATTACK.PAS:1003-1019).</summary>
    private static void NewCapital(Empire emp, IEconomicWorld newCap, Random random)
    {
        var oldTech = emp.TechnologyLevel;
        var newTech = newCap.TechLevel;
        if (oldTech > newTech) {
            SetEmpireTechnology(emp, newTech, newTech);
        } else if (oldTech < newTech) {
            SetEmpireTechnology(emp, newTech, newTech - 1);
        }

        emp.Capital = newCap;
        newCap.Type = WorldType.Capital;
        AnnualTickHandler.ChangeRevIndex(newCap, -Rnd(random, 30, 50));
        newCap.Efficiency = Rnd(random, 40, 60);
    }

    /// <summary>SetEmpireTechnology (PRIMINTR.PAS) — a full replace of the empire's unlocked-tech set to TechDev[setTo], not a union.</summary>
    private static void SetEmpireTechnology(Empire emp, TechLevel newLevel, TechLevel setTo)
    {
        emp.TechnologyLevel = newLevel;
        emp.Technology.ReplaceWith(TechCatalog.FullSetAt(setTo));
    }

    /// <summary>
    /// RestoreCombatant (ATTACK.PAS:1141-1181). The Fleet branch's own FleetCargoSpace/BalanceFleet/
    /// FuelCapacity clamp isn't ported — no fleet cargo-space/fuel-capacity system exists anywhere in
    /// this port yet (a movement-fidelity gap in the same category as docs/ROADMAP.md's tracked
    /// stargate/starbase-movement gaps), and it's structurally unreachable from here regardless: this
    /// method only ever subtracts casualties (fewer ships/cargo), which can only free up space, never
    /// exceed it.
    /// </summary>
    public static void RestoreCombatant(object combatant, AttackTally casualties)
    {
        var (ships, cargo) = GetShipsAndCargo(combatant);
        if (ships is null) {
            throw new ArgumentException("RestoreCombatant: combatant must be an IEconomicWorld or Fleet.", nameof(combatant));
        }

        foreach (var t in Enum.GetValues<ShipType>()) {
            ships[t] = Math.Max(0, ships[t] - casualties[t.ToAttackType()]);
        }
        cargo!.Legions = Math.Max(0, cargo.Legions - casualties[AttackType.Legion]);
        cargo.NinjaLegions = Math.Max(0, cargo.NinjaLegions - casualties[AttackType.NinjaLegion]);

        if (combatant is IEconomicWorld world) {
            foreach (var t in Enum.GetValues<DefenseType>()) {
                world.Defenses[t] = Math.Max(0, world.Defenses[t] - casualties[t.ToAttackType()]);
            }
        }
    }

    /// <summary>ResolveAttack (ATTACK.PAS:1183-1299), including its own nested ReportLosses.</summary>
    public static void ResolveAttack(
        AttackResultType result, Fleet attackerFleet, object target,
        bool hkAttack, bool capture, AttackTally casualties, AttackTally killed,
        Game game, Random random)
    {
        var subject = (ISectorObject)target;
        var attacker = attackerFleet.Owner;
        var defender = subject.Owner;
        var empireConquered = false;
        var revChange = 0;

        switch (result) {
            case AttackResultType.AttackerDestroyed:
                if (defender.IsIndependent) {
                    ChangeTotalRevIndex(attacker, Rnd(random, 1, 4));
                } else {
                    ChangeTotalRevIndex(attacker, Rnd(random, 2, 5));
                    ChangeTotalRevIndex(defender, -Rnd(random, 3, 6));
                    defender.AddNews(NewsType.EnemyEmpireDestroyed, subject, otherEmpire: attacker);
                    ReportLosses(defender, subject, killed);
                    game.AddGlobalNews([attacker, defender], subject, NewsType.EnemyAttackedEmpireGlobal, otherEmpire: attacker, defender: defender);
                }
                DestroyFleet(attackerFleet, game);
                break;

            case AttackResultType.AttackerRetreats:
                if (!defender.IsIndependent) {
                    if (hkAttack) {
                        defender.AddNews(NewsType.AttackedByUnknown, subject);
                    } else {
                        defender.AddNews(NewsType.EnemyEmpireRetreated, subject, otherEmpire: attacker);
                        game.AddGlobalNews([attacker, defender], subject, NewsType.EnemyAttackedEmpireGlobal, otherEmpire: attacker, defender: defender);
                    }
                    ReportLosses(defender, subject, killed);
                    ChangeTotalRevIndex(attacker, Rnd(random, 1, 3));
                    ChangeTotalRevIndex(defender, -Rnd(random, 1, 3));
                }
                break;

            case AttackResultType.DefenderConquered:
                if (target is IEconomicWorld world) {
                    ConquerWorld(world, attacker, game, random);
                    if (world.Type == WorldType.Capital) {
                        world.Type = WorldType.Independent;
                        empireConquered = true;
                        revChange = Rnd(random, 25, 50);
                        game.AddGlobalNews([attacker, defender], subject, NewsType.EnemyConqueredCapitalGlobal, otherEmpire: attacker, defender: defender);
                    } else {
                        revChange = Rnd(random, 5, 10);
                        game.AddGlobalNews([attacker, defender], subject, NewsType.EnemyConqueredWorldGlobal, otherEmpire: attacker, defender: defender);
                    }
                } else if (target is Fleet targetFleet) {
                    foreach (var t in Enum.GetValues<ShipType>()) {
                        killed[t.ToAttackType()] += targetFleet.Ships[t];
                    }

                    if (capture) {
                        AbortFleet(targetFleet, attackerFleet, report: false);
                    }
                    DestroyFleet(targetFleet, game);
                    revChange = Rnd(random, 1, 5);
                    game.AddGlobalNews([attacker, defender], subject, NewsType.EnemyConqueredWorldGlobal, otherEmpire: attacker, defender: defender);
                }

                if (defender.IsIndependent) {
                    ChangeTotalRevIndex(attacker, -Rnd(random, 2, 4));
                } else {
                    if (hkAttack && target is Fleet) {
                        defender.AddNews(NewsType.FleetDestroyedByUnknown, subject);
                    } else {
                        defender.AddNews(NewsType.WorldConqueredByEnemy, subject, otherEmpire: attacker);
                    }
                    ReportLosses(defender, subject, killed);
                    ChangeTotalRevIndex(attacker, -Rnd(random, 3, 6));
                    ChangeTotalRevIndex(defender, revChange);
                }

                if (empireConquered) {
                    ConquerEmpire(attacker, defender, game, random);
                }
                break;

            // DefenderCaptured: declared in Pascal's AttackResultTypes but never assigned anywhere in
            // ATTACK.PAS/ATTNPE.PAS, and ResolveAttack's own real CASE has no branch for it either —
            // confirmed by reading. A genuine no-op, not a gap.
        }
    }

    /// <summary>ResolveAttack's nested ReportLosses (ATTACK.PAS:1196-1204). p2 is Pascal's raw AttackTypes ordinal, which includes the leading NoRes sentinel this port's AttackType drops — see AttackType's own doc comment.</summary>
    private static void ReportLosses(Empire emp, ISectorObject subject, AttackTally killed)
    {
        foreach (var t in Enum.GetValues<AttackType>()) {
            if (killed[t] > 0) {
                emp.AddNews(NewsType.DestructionDetail, subject, p1: killed[t], p2: (int)t + 1);
            }
        }
    }

    /// <summary>DestroyFleet (FLEET.PAS:211-244) — "is it live" is just "is it in Galaxy.Fleets" (see Galaxy's own doc comment), so this is a plain removal; no SetOfActiveFleets/sector-flag/order bookkeeping to keep in sync. Internal (not private): 5g's CombatStandalone.cs reuses this exact primitive rather than duplicating it (LAMAttack/SelfDestructObject both destroy fleets outside the ResolveAttack pipeline).</summary>
    internal static void DestroyFleet(Fleet fleet, Game game) => game.Galaxy.Fleets.Remove(fleet);

    /// <summary>
    /// AbortFleet (FLEET.PAS:150-209) — transfers <paramref name="source"/>'s ships/cargo onto
    /// <paramref name="ground"/> and reports the transfer, matching real Pascal's own GetShips/PutShips
    /// no-op for a construction-site/stargate ground (PRIMINTR.PAS:315-364,589-607, confirmed by
    /// reading) via <see cref="GetShipsAndCargo"/> returning null for anything that isn't an
    /// <see cref="IEconomicWorld"/> or <see cref="Fleet"/>. Fuel-to-trillum conversion and
    /// FleetNameDestruction's naming-system call aren't ported — no fuel-capacity or naming system
    /// exists anywhere in this port (see RestoreCombatant/ConquerWorld's own doc comments for the same
    /// gaps).
    /// </summary>
    private static void AbortFleet(Fleet source, object ground, bool report)
    {
        var (ships, cargo) = GetShipsAndCargo(ground);
        if (ships is null) {
            return;
        }

        var groundOwner = ((ISectorObject)ground).Owner;
        var attacker = source.Owner;

        if (report && groundOwner != attacker) {
            groundOwner.AddNews(NewsType.ShipsOrCargoTransferredToYou, (ISectorObject)ground, otherEmpire: attacker);
            foreach (var t in Enum.GetValues<ShipType>()) {
                if (source.Ships[t] != 0) {
                    // Pascal's raw ResourceTypes ordinal for fgt..trn is ShipType's own C# ordinal + 5
                    // (LAM=1,def=2,GDM=3,ion=4,fgt=5..trn=11 — verified against ATTACK.PAS's own
                    // CombatPower table comment).
                    groundOwner.AddNews(NewsType.TransferDetail, (ISectorObject)ground, p1: source.Ships[t], p2: (int)t + 5);
                }
            }
            foreach (var t in Enum.GetValues<CargoType>()) {
                if (source.Cargo[t] != 0) {
                    // Pascal's raw ResourceTypes ordinal for men..tri is CargoType's own C# ordinal + 12
                    // (continuing fgt..trn's run: pen=9,str=10,trn=11,men=12..tri=18).
                    groundOwner.AddNews(NewsType.TransferDetail, (ISectorObject)ground, p1: source.Cargo[t], p2: (int)t + 12);
                }
            }
        }

        foreach (var t in Enum.GetValues<ShipType>()) {
            ships[t] += source.Ships[t];
        }
        foreach (var t in Enum.GetValues<CargoType>()) {
            cargo![t] += source.Cargo[t];
        }
    }

    /// <summary>
    /// DestroyEmpire (INTRFACE.PAS:1612-1658). CleanUpNPE isn't ported — it's NPE-AI-decision state
    /// (Phase 6) that doesn't exist anywhere in this port yet, and real Pascal only ever calls it for a
    /// non-human empire, which is already this method's only real precondition (ConquerEmpire's own
    /// ELSE branch never reaches here for a human — see the human branch just above its one call site).
    /// DeleteAllNames isn't ported either — no naming system exists in this port (matching AbortFleet's
    /// own established gap). EraseNews is <c>Empire.News.Clear()</c>.
    /// </summary>
    private static void DestroyEmpire(Empire empire, Game game)
    {
        foreach (Planet planet in game.Galaxy.Planets.Where(p => p.Owner == empire).ToList()) {
            IEconomicWorld world = planet;
            world.Reassign(Empire.Independent);
            world.Type = WorldType.Independent;
            world.InitializeSelfSufficiency();
        }

        foreach (var starbase in game.Galaxy.Starbases.Where(s => s.Owner == empire).ToList()) {
            ((IEconomicWorld)starbase).Reassign(Empire.Independent);
        }

        foreach (var fleet in game.Galaxy.Fleets.Where(f => f.Owner == empire).ToList()) {
            var ground = FindGroundObject(fleet.Location, game);
            if (ground is not null) {
                AbortFleet(fleet, ground, report: true);
            }
            DestroyFleet(fleet, game);
        }

        empire.News.Clear();
        game.Empires.Remove(empire);
    }

    /// <summary>
    /// GetObject(XY,Ground) (PRIMINTR.PAS), scoped to what AbortFleet can actually do anything with —
    /// a Planet or Starbase. Real Pascal's GetObject can also return a stargate or construction site,
    /// but AbortFleet's own GetShips/PutShips already no-op for those (see AbortFleet's own doc
    /// comment); treating "nothing but a gate/construction site here" the same as "nothing here at
    /// all" only skips the phantom ShipsOrCargoTransferredToYou news real Pascal would otherwise fire
    /// for a transfer that silently evaporates either way.
    /// </summary>
    private static object? FindGroundObject(Coordinate location, Game game) =>
        game.Galaxy.Planets.FirstOrDefault(p => p.Location == location) as object
        ?? game.Galaxy.Starbases.FirstOrDefault(s => s.Location == location);

    private static (ShipCounts? Ships, CargoHold? Cargo) GetShipsAndCargo(object obj) => obj switch {
        IEconomicWorld world => (world.Ships, world.Cargo),
        Fleet fleet => (fleet.Ships, fleet.Cargo),
        _ => (null, null),
    };

    /// <summary>ChangeTotalRevIndex (PRIMINTR.PAS:1085-1096) — a real-time mutator distinct from AnnualTickHandler's own NewTotalRevIndex scratch-accumulate-then-commit dictionary (Phase 1); the two never run simultaneously, only at different times within the same year. Internal (not private): 5g's CombatStandalone.cs reuses this exact primitive rather than duplicating it.</summary>
    internal static void ChangeTotalRevIndex(Empire emp, int change)
    {
        if (!emp.IsIndependent) {
            emp.TotalRevolutionIndex += change;
        }
    }
}
