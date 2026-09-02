using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;
using static Reconstructed4021.Core.PascalMath;

namespace Reconstructed4021.Core.Combat;

/// <summary>
/// Outcome application (ATTACK.PAS's ConquerWorld/ConquerEmpire/RestoreCombatant/ResolveAttack, plus
/// FLEET.PAS's DestroyFleet/AbortFleet and INTRFACE.PAS's DestroyEmpire). Called
/// by <see cref="CombatResolution.NPEAttack"/> once a whole engagement's Result/Casualties/Killed are
/// known; neither <see cref="CombatEngine"/> nor <see cref="CombatResolution"/> reads those tallies
/// for anything beyond returning them.
///
/// Empire elimination is one unified lifecycle (see <see cref="Types.EmpireStatus"/> and
/// docs/PORT_DESIGN.md's "Empire elimination" section), matching real Pascal's own permanent
/// per-empire array: an NPE's
/// loss transitions straight to <see cref="Types.EmpireStatus.Eliminated"/> synchronously, inside
/// <see cref="DestroyEmpire"/>, right here; a human's loss instead parks at
/// <see cref="Types.EmpireStatus.PendingElimination"/> until their own next turn-prologue
/// (<see cref="Turns.TurnEngine.BeginTurn"/>) calls the same <see cref="DestroyEmpire"/> to
/// finish the transition — matching Pascal's <c>ConquerEmpire</c> (immediate for
/// <c>NOT EmpirePlayer</c>) versus <c>PROLOG.PAS</c>'s <c>EmpireNews</c> (deferred for a player).
/// <see cref="Game.Empires"/> is never touched by either path — it's a permanent roster now.
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
        // for that exact primitive.
        VisibilityHandler.ScoutAdjacent(world.Location, conqueror, game);
    }

    /// <summary>ConquerWorld's nested ChangeRevolutionIndex (ATTACK.PAS:946-974): a tier cascade by the world's *current* RevolutionIndex, genuinely separate from <see cref="AnnualTickHandler"/>'s own rebellion-warning cascade (different tiers, different draws).</summary>
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
            if (enemyEmpire.NpeType is null) {
                enemyEmpire.Capital = null;
                enemyEmpire.Status = EmpireStatus.PendingElimination;
                enemyEmpire.DefeatedBy = conqueror;
            } else {
                DestroyEmpire(enemyEmpire, conqueror, game);
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
    /// FuelCapacity clamp isn't called here, though all three exist in <see cref="FleetLogistics"/> —
    /// structurally unreachable regardless: this method only ever subtracts casualties (fewer
    /// ships/cargo), which can only free up space, never exceed it.
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

    /// <summary>DestroyFleet (FLEET.PAS:211-244) — "is it live" is just "is it in Galaxy.Fleets" (see Galaxy's own doc comment), so this is a plain removal; no SetOfActiveFleets/sector-flag/order bookkeeping to keep in sync. Internal (not private): <see cref="CombatStandalone"/> reuses this exact primitive rather than duplicating it (LAMAttack/SelfDestructObject both destroy fleets outside the ResolveAttack pipeline).</summary>
    internal static void DestroyFleet(Fleet fleet, Game game) => game.Galaxy.Fleets.Remove(fleet);

    /// <summary>
    /// AbortFleet (FLEET.PAS:150-209) — transfers <paramref name="source"/>'s ships/cargo onto
    /// <paramref name="ground"/> and reports the transfer, matching real Pascal's own GetShips/PutShips
    /// no-op for a construction-site/stargate ground (PRIMINTR.PAS:315-364,589-607, confirmed by
    /// reading) via <see cref="GetShipsAndCargo"/> returning null for anything that isn't an
    /// <see cref="IEconomicWorld"/> or <see cref="Fleet"/>. Leftover fuel converts to trillum
    /// (FLEET.PAS:169,176-183) before the report loop below reads <c>source.Cargo</c>, matching
    /// Pascal's own ordering (<c>Cr2[Tri]</c> is folded in at line 177, before the Trns2 report loop at
    /// 197-199 reads it) — <see cref="FleetLogistics"/>'s <see cref="FleetLogistics.FuelPerTon"/> is the
    /// only piece of that model this method needs; no capacity clamp applies on either branch (Pascal
    /// has none here). Every call site destroys <paramref name="source"/> immediately after calling
    /// this (grep-confirmed), so mutating <paramref name="source"/>'s own fields in place rather than
    /// working from a copy, unlike Pascal's Sh2/Cr2, is safe. FleetNameDestruction/DeleteName's own
    /// naming-system call (FLEET.PAS:208) needs no equivalent here: <paramref name="source"/>'s own
    /// <see cref="ISectorObject.Names"/> simply goes with it once every caller removes it from
    /// <see cref="Galaxy.Galaxy.Fleets"/> — see that property's own remarks. Internal (not
    /// private): <see cref="Npe.NpeToolkit.ImplementReturnMSN"/>/<see cref="Npe.NpeToolkit.ImplementRefuelMSN"/>
    /// call this exact primitive rather than duplicating it, same precedent as <see cref="DestroyFleet"/>.
    /// </summary>
    internal static void AbortFleet(Fleet source, object ground, bool report)
    {
        var (ships, cargo) = GetShipsAndCargo(ground);
        if (ships is null) {
            return;
        }

        if (ground is Fleet groundFleet) {
            groundFleet.Fuel += source.Fuel;
        } else {
            source.Cargo.Trillum = ClampResource(source.Cargo.Trillum + ClampResource(source.Fuel / FleetLogistics.FuelPerTon));
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
    /// DestroyEmpire (INTRFACE.PAS:1612-1658). <c>IF (NOT EmpirePlayer(Emp)) THEN CleanUpNPE(Emp)</c>
    /// disposes that empire's own NPE data record; the port-side equivalent is the unconditional
    /// <see cref="Game.TurnHandlers"/> removal below, which now fires for a human too (their own AI
    /// decision data, such as it is, is equally moot once <see cref="Types.EmpireStatus.Eliminated"/>).
    /// <see cref="Game.Empires"/> is never touched — it's a permanent roster (see
    /// <see cref="Types.EmpireStatus"/>); <paramref name="conqueror"/> lets both call sites (the NPE
    /// branch here, and <see cref="Turns.TurnEngine.AdvanceOneTurn"/>'s deferred human path, which
    /// already recorded <paramref name="conqueror"/> at <see cref="Types.EmpireStatus.PendingElimination"/>
    /// time and just needs it reasserted here) share one tail. This is distinct from, and doesn't
    /// touch, a *different* Kingdom's own diplomacy dictionary still referencing this empire after
    /// it's gone — real Pascal's fixed per-empire arrays never clear those either, which is why the
    /// SaveFormat layer's narrower "orphan empire" handling stays necessary regardless (see
    /// GameJson's EntityIndex remarks). DeleteAllNames (INTRFACE.PAS:1653) is deliberately not
    /// ported: real Pascal needs it to free <paramref name="empire"/>'s own bookmarks from its one
    /// reused global <c>Universe</c> before the next game loads into the same memory; this port
    /// rebuilds <see cref="Game"/> fresh every load, so there's nothing to leak, and leaving a dead
    /// empire's own <see cref="Empire.Bookmarks"/>/<see cref="ISectorObject.Names"/> entries in place
    /// has no gameplay-visible effect — see <c>docs/PORT_DESIGN.md</c>'s "Naming system" section.
    /// EraseNews is <c>Empire.News.Clear()</c>.
    /// </summary>
    internal static void DestroyEmpire(Empire empire, Empire conqueror, Game game)
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
        empire.Status = EmpireStatus.Eliminated;
        empire.DefeatedBy = conqueror;
        game.TurnHandlers.Remove(empire);
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

    /// <summary>ChangeTotalRevIndex (PRIMINTR.PAS:1085-1096) — a real-time mutator distinct from AnnualTickHandler's own NewTotalRevIndex scratch-accumulate-then-commit dictionary; the two never run simultaneously, only at different times within the same year. Internal (not private): <see cref="CombatStandalone"/> reuses this exact primitive rather than duplicating it.</summary>
    internal static void ChangeTotalRevIndex(Empire emp, int change)
    {
        if (!emp.IsIndependent) {
            emp.TotalRevolutionIndex += change;
        }
    }
}
