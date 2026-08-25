using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Types;
using static ThreeLn.Reconstruction4021.Core.PascalMath;

namespace ThreeLn.Reconstruction4021.Core.Combat;

/// <summary>
/// The group/shell combat engine core (ATTACK.PAS, Phase 5 commit 5d): the per-round damage math one
/// call to <see cref="Battle"/> resolves for a single orbital shell, plus the setup procedures that
/// feed it (<see cref="CalculateCombatData"/>, <see cref="GetEnemy"/>, <see cref="DefaultDistribution"/>,
/// <see cref="ForcesUnknown"/>) and the post-round check (<see cref="EnemySurrenders"/>). Deliberately
/// does not include the multi-round/multi-shell driver (ATTNPE.PAS's GroupEngage and friends,
/// including AdvanceGroups' shell-to-shell movement and its transport-to-troop swap) or outcome
/// application (ResolveAttack/ConquerWorld/ConquerEmpire) — those are later commits (see
/// docs/ROADMAP.md's Phase 5 checklist). A group's <see cref="GroupRecord.Trg"/> staying null (no
/// target chosen) is real, expected Pascal behavior here, not a degenerate case — it's what every
/// group starts at before a later commit's targeting logic assigns one.
///
/// Every method takes <see cref="Random"/> explicitly (this repo's <see cref="PascalMath"/> convention)
/// rather than holding one as instance state — there is no per-combat instance to own it, and RNG call
/// *order*, not just values, is part of this engine's contract with the real Pascal (ShipsDestroyed
/// consumes exactly one Rnd(1,100) per call; every loop below walks groups by list order and tests
/// HashSet membership rather than enumerating a HashSet directly, so the draw sequence matches
/// ATTACK.PAS's own <c>FOR i:=1 TO NoOfGroups DO IF i IN AttackingGroups</c> shape exactly).
/// </summary>
public static class CombatEngine
{
    // AttackType's declared order is Pascal's LAM..nnj; slicing it this way instead of hand-listing
    // members keeps both slices in sync with the enum by construction.
    private static readonly AttackType[] _allAttackTypes = Enum.GetValues<AttackType>();
    private static readonly AttackType[] _shipAndDefenseAttackTypes = [.. _allAttackTypes.Where(t => t is not (AttackType.Legion or AttackType.NinjaLegion))]; // LAM..trn
    private static readonly ShellPosition[] _allShellPositions = Enum.GetValues<ShellPosition>();

    /// <summary>
    /// CalculateCombatData (ATTACK.PAS:344-383). Drops Pascal's own unused <c>FltID</c> parameter —
    /// confirmed by reading the body, it never appears in it. <paramref name="target"/> mirrors
    /// Pascal's IDNumber union: a <see cref="IEconomicWorld"/> (planet or starbase) or a
    /// <see cref="Fleet"/>.
    /// </summary>
    public static CombatDataRecord CalculateCombatData(Empire attacker, object target)
    {
        var capital = attacker.Capital
            ?? throw new InvalidOperationException("CalculateCombatData: attacker has no capital (GetCapital's Pascal precondition) — a live, still-attacking empire always has one.");
        var aTech = capital.TechLevel;

        var dTech = target switch {
            Fleet fleet => fleet.Owner.TechnologyLevel,
            IEconomicWorld world => world.TechLevel,
            _ => throw new ArgumentException($"CalculateCombatData: unsupported target type {target.GetType()}", nameof(target)),
        };

        var aShipAdj = CombatConstants.CombatTechAdj[(aTech, dTech)];
        var dShipAdj = CombatConstants.CombatTechAdj[(dTech, aTech)];

        var dGrndAdj = target switch {
            Starbase sb => PascalRound((dShipAdj / 100.0) * CombatConstants.CombatBaseAdj[sb.Kind]),
            Fleet => 0,
            IEconomicWorld world => PascalRound((dShipAdj / 100.0) * CombatConstants.CombatClassAdj[world.EffectiveClass]),
            _ => throw new ArgumentException($"CalculateCombatData: unsupported target type {target.GetType()}", nameof(target)),
        };

        var maxGdm = CombatConstants.GdmLaunch[dTech];
        var revIndex = target is IEconomicWorld { IsPlanet: true } planet ? planet.RevolutionIndex : 0;

        return new CombatDataRecord(target, dTech, aShipAdj, dShipAdj, dGrndAdj, maxGdm, revIndex);
    }

    /// <summary>
    /// GetEnemy (ATTACK.PAS:1369-1424): the forces present at each shell around <paramref name="target"/>.
    /// For a planet/starbase, an owned world's own ships are never tech-gated here — the gate
    /// (<c>(Status&lt;&gt;Indep) OR (ShpI IN TechDev[Tech])</c>) only restricts an *independent* world,
    /// by its own real <see cref="IEconomicWorld.TechLevel"/> (not decremented, unlike
    /// AnnualTickHandler.Production.cs's EffectiveTechnologyLevel — a different gate for a different
    /// purpose, not reused here). TechDev[Tech] membership is equivalent to
    /// <c>TechCatalog.MinTechForShip[ship] &lt;= Tech</c>, per TechCatalog's own monotonic-superset doc
    /// comment.
    /// </summary>
    public static EnemyForces GetEnemy(object target)
    {
        var enemy = new EnemyForces();

        switch (target) {
            case Fleet fleet: {
                var split = fleet.Ships.Jumptransports + fleet.Ships.Transports == 0 ? 1.0 : 0.75;
                foreach (var ship in Enum.GetValues<ShipType>()) {
                    var at = ship.ToAttackType();
                    if (ship is ShipType.Jumptransport or ShipType.Transport) {
                        enemy[ShellPosition.Orbit, at] = fleet.Ships[ship];
                    } else {
                        enemy[ShellPosition.HighOrbit, at] = PascalRound(split * fleet.Ships[ship]);
                        enemy[ShellPosition.Orbit, at] = PascalRound((1 - split) * fleet.Ships[ship]);
                    }
                }
                break;
            }
            case IEconomicWorld world: {
                var owner = world.Owner;
                var tech = world.TechLevel;
                foreach (var pos in _allShellPositions) {
                    foreach (var ship in Enum.GetValues<ShipType>()) {
                        if (owner.IsIndependent && TechCatalog.MinTechForShip[ship] > tech) {
                            continue;
                        }
                        enemy[pos, ship.ToAttackType()] = PascalRound((owner.DefenseSettings.Fleets[pos][ship] / 100.0) * world.Ships[ship]);
                    }
                }

                enemy[ShellPosition.Orbit, AttackType.DefenseSatellite] = world.Defenses[DefenseType.DefenseSatellite];
                enemy[ShellPosition.SubOrbit, AttackType.Gdm] = world.Defenses[DefenseType.Gdm];
                enemy[ShellPosition.SubOrbit, AttackType.IonCannon] = world.Defenses[DefenseType.IonCannon];
                enemy[ShellPosition.SubOrbit, AttackType.Lam] = world.Defenses[DefenseType.Lam];
                enemy[ShellPosition.Ground, AttackType.Legion] = world.Cargo[CargoType.Legion];
                enemy[ShellPosition.Ground, AttackType.NinjaLegion] = world.Cargo[CargoType.NinjaLegion];
                break;
            }
            default:
                throw new ArgumentException($"GetEnemy: unsupported target type {target.GetType()}", nameof(target));
        }

        return enemy;
    }

    /// <summary>
    /// ForcesUnknown (ATTACK.PAS:1426-1437): whether an attacking hunter-killer fleet's true strength is
    /// hidden from the target's owner (small enough to stay cloaked, and not yet scouted).
    /// </summary>
    public static bool ForcesUnknown(Fleet attacker, Empire targetOwner) =>
        attacker.Type == FleetType.HunterKillerFleet && attacker.Ships.HunterKillers <= 500 && !targetOwner.Fleets.Scouted.Contains(attacker);

    /// <summary>
    /// DefaultDistribution+DefaultGroup (ATTACK.PAS:1280-1367): splits a fleet's ships into one group
    /// per ship type present, each starting unaimed (<see cref="GroupRecord.Trg"/> null) at deep space.
    /// A transport/jumptransport group picks up ground assault troops from the fleet's own cargo —
    /// ninjas preferentially over plain legions, never both. Mutates <paramref name="fleet"/>'s
    /// Ships/Cargo exactly as Pascal's GetShips/GetCargo-then-mutate-locals does (real state, not a
    /// snapshot): a ship or trooper picked up into a group leaves the fleet's own counts.
    /// </summary>
    public static List<GroupRecord> DefaultDistribution(Fleet fleet)
    {
        var groups = new List<GroupRecord>();

        foreach (var ship in Enum.GetValues<ShipType>()) {
            if (fleet.Ships[ship] != 0 && ship is ShipType.Fighter or ShipType.HunterKiller or ShipType.Jumpship or ShipType.Penetrator or ShipType.Starship) {
                groups.Add(DefaultGroup(fleet, ship));
            }
        }

        foreach (var ship in Enum.GetValues<ShipType>()) {
            if (fleet.Ships[ship] != 0 && ship is ShipType.Jumptransport or ShipType.Transport) {
                groups.Add(DefaultGroup(fleet, ship));
            }
        }

        return groups;
    }

    private static GroupRecord DefaultGroup(Fleet fleet, ShipType shipType)
    {
        var num = fleet.Ships[shipType];
        fleet.Ships[shipType] = 0;

        var group = new GroupRecord { Typ = shipType.ToAttackType(), Num = num };

        if (shipType is ShipType.Jumptransport or ShipType.Transport) {
            var trnAdj = CombatConstants.TrnAdj[shipType];
            if (fleet.Cargo[CargoType.NinjaLegion] > 0) {
                var maxMen = Math.Min(ClampResource(PascalRound(trnAdj * num * CombatConstants.CargoSpace[CargoType.NinjaLegion])), fleet.Cargo[CargoType.NinjaLegion]);
                group.Gat = maxMen;
                group.GatTyp = AttackType.NinjaLegion;
                fleet.Cargo[CargoType.NinjaLegion] -= maxMen;
            } else {
                var maxMen = Math.Min(ClampResource(PascalRound(trnAdj * num * CombatConstants.CargoSpace[CargoType.Legion])), fleet.Cargo[CargoType.Legion]);
                group.Gat = maxMen;
                group.GatTyp = AttackType.Legion;
                fleet.Cargo[CargoType.Legion] -= maxMen;
            }
        }

        return group;
    }

    /// <summary>
    /// ShipsDestroyed (ATTACK.PAS:437-470): how many of <paramref name="defender"/> a group of
    /// <paramref name="numberAttacking"/> <paramref name="attacker"/>-type things destroys, given the
    /// defender's tech/terrain adjustment. One real distinction Pascal's own comment states outright:
    /// this can consume exactly one <see cref="Rnd"/> call even when it returns 0, so callers must not
    /// skip calling it just because the inputs look like they'll produce nothing (see EnemyAttack).
    /// </summary>
    public static int ShipsDestroyed(Random random, int numberAttacking, AttackType attacker, AttackType defender, int adj)
    {
        var temp = numberAttacking * (CombatConstants.CombatTable[(attacker, defender)] / 100.0);
        if (adj == 0) {
            adj = 1;
        }
        temp = 100 * (temp / adj);

        var temp3 = IntLmt(temp);
        var temp2 = (temp - temp3) * 100;
        if (temp2 > 100) {
            temp2 = 0;
        }
        if (Rnd(random, 1, 100) < temp2) {
            temp3 += 1;
        }

        return ClampResource(temp3);
    }

    /// <summary>
    /// Battle (ATTACK.PAS:878-913): resolves one round of combat at one orbital shell. Returns the
    /// groups destroyed this round. A no-op (nothing mutated, empty result) when no live group is
    /// currently at <paramref name="currentPosition"/>.
    /// </summary>
    public static ISet<GroupRecord> Battle(
        IReadOnlyList<GroupRecord> groups, EnemyForces enemy, ShellPosition currentPosition,
        CombatDataRecord combatData, CombatDetails details, AttackTally casualties, AttackTally killed, Random random)
    {
        var attackingGroups = GetConflict(groups, currentPosition);
        if (attackingGroups.Count == 0) {
            return new HashSet<GroupRecord>();
        }

        var eShDest = new AttackTally();
        var targ = GetTargetArray(currentPosition, groups, attackingGroups, enemy, killed, combatData, random);

        GroupAttack(groups, attackingGroups, eShDest, combatData, random);
        var gShDest = EnemyAttack(groups, attackingGroups, targ, combatData, details, random);

        var groupsDestroyed = UpdateGroupsDestroyed(groups, attackingGroups, gShDest, casualties);
        UpdateEnemyDestroyed(enemy, currentPosition, eShDest, killed);

        return groupsDestroyed;
    }

    /// <summary>GetConflict (ATTACK.PAS:399-414): every still-live group currently at <paramref name="currentPosition"/>.</summary>
    private static HashSet<GroupRecord> GetConflict(IReadOnlyList<GroupRecord> groups, ShellPosition currentPosition)
    {
        var result = new HashSet<GroupRecord>();
        foreach (var g in groups) {
            if (g.Pos == currentPosition && g.Sta != GroupStatus.Destroyed) {
                result.Add(g);
            }
        }
        return result;
    }

    /// <summary>TotalProtection (ATTACK.PAS:416-435): combined escort strength of every attacking group already aimed at something.</summary>
    private static double TotalProtection(IReadOnlyList<GroupRecord> groups, ISet<GroupRecord> attackingGroups)
    {
        double total = 0;
        foreach (var g in groups) {
            if (!attackingGroups.Contains(g) || g.Trg is null || g.Typ is AttackType.Legion or AttackType.NinjaLegion) {
                continue;
            }
            var ship = g.Typ.AsShipType() ?? throw new InvalidOperationException($"TotalProtection: group Typ {g.Typ} is neither a ship nor a troop type.");
            total += CombatConstants.ProtecOffered[ship] * g.Num;
        }
        return total;
    }

    private static Dictionary<GroupRecord, AttackTally> GetTargetArray(
        ShellPosition currentPosition, IReadOnlyList<GroupRecord> groups, ISet<GroupRecord> attackingGroups,
        EnemyForces enemy, AttackTally killed, CombatDataRecord combatData, Random random)
    {
        var priority1 = BuildPriority1(groups, attackingGroups, combatData);
        var priority2 = BuildPriority2(groups, attackingGroups, priority1);
        return BuildTargetArray(currentPosition, groups, enemy, combatData, killed, priority2, random);
    }

    /// <summary>BuildPriority1 (ATTACK.PAS:494-556): how important each attacking group is to destroy, 0-1000 scaled.</summary>
    private static Dictionary<GroupRecord, double> BuildPriority1(IReadOnlyList<GroupRecord> groups, ISet<GroupRecord> attackingGroups, CombatDataRecord combatData)
    {
        var cover = TotalProtection(groups, attackingGroups);
        var priority1 = new Dictionary<GroupRecord, double>();
        double total = 0;

        foreach (var g in groups) {
            if (!attackingGroups.Contains(g)) {
                continue;
            }

            var p = PascalRound((CombatConstants.ShipValue[g.Typ] / 1000.0) * g.Num) + 1;

            if (g.Typ is AttackType.Transport or AttackType.Jumptransport && combatData.Target is not Fleet) {
                p = IntLmt(p * (1.0 + (int)g.Pos));
            } else if (g.Typ == AttackType.Starship) {
                p = IntLmt(p * (16.0 - 2 * (int)g.Pos));
            }

            if (g.Trg is null && g.Typ is not (AttackType.Legion or AttackType.NinjaLegion)) {
                var ship = g.Typ.AsShipType() ?? throw new InvalidOperationException($"BuildPriority1: group Typ {g.Typ} is neither a ship nor a troop type.");
                var perCentCover = IntLmt((cover / g.Num / CombatConstants.ProtecNeeded[ship]) * 100);
                if (perCentCover > 100) {
                    perCentCover = 100;
                }
                p = PascalRound(p * (1 - perCentCover / 100.0));
            }

            if (g.Trg is null && g.Typ == AttackType.HunterKiller && !g.Flg) {
                p = 0;
            }

            priority1[g] = p;
            total += p;
        }

        if (total == 0) {
            total = 1;
        }
        foreach (var g in priority1.Keys.ToList()) {
            priority1[g] = PascalRound((priority1[g] / total) * 1000);
        }

        return priority1;
    }

    /// <summary>BuildPriority2 (ATTACK.PAS:558-609): per-(group, incoming AttackType) share of priority1, weighted by how effective that AttackType is against the group.</summary>
    private static Dictionary<GroupRecord, Dictionary<AttackType, double>> BuildPriority2(
        IReadOnlyList<GroupRecord> groups, ISet<GroupRecord> attackingGroups, Dictionary<GroupRecord, double> priority1)
    {
        var priority2 = new Dictionary<GroupRecord, Dictionary<AttackType, double>>();
        foreach (var g in groups) {
            priority2[g] = [];
        }

        foreach (var g in groups) {
            if (!attackingGroups.Contains(g)) {
                continue;
            }
            foreach (var shpI in _allAttackTypes) {
                priority2[g][shpI] = PascalRound(priority1[g] * (CombatConstants.CombatTable[(shpI, g.Typ)] / (double)CombatConstants.WeapEff[shpI]));
            }
        }

        foreach (var shpI in _allAttackTypes) {
            double total = 0;
            foreach (var g in groups) {
                total += priority2[g].GetValueOrDefault(shpI);
            }
            if (total == 0) {
                total = 1;
            }

            foreach (var g in groups) {
                priority2[g][shpI] = PascalRound((priority2[g].GetValueOrDefault(shpI) / total) * 1000);
            }
        }

        return priority2;
    }

    /// <summary>
    /// BuildTargetArray (ATTACK.PAS:611-693): the final per-group incoming-attack count, split from
    /// priority2 across whatever the defending side actually has at this shell. LAM and (at the Orbit
    /// shell) GDM interceptions are resolved here too — each consumes real RNG per attacking group with
    /// nonzero priority for that weapon (in group list order, gated exactly like Pascal's own
    /// <c>Priority2[i,LAM]&lt;&gt;0</c>/<c>Priority2[i,GDM]&lt;&gt;0</c> checks, load-bearing: LAM/GDM
    /// vs. a troop group's CombatTable entry is 0, and this gate is what keeps that 0 out of a divisor).
    /// </summary>
    private static Dictionary<GroupRecord, AttackTally> BuildTargetArray(
        ShellPosition currentPosition, IReadOnlyList<GroupRecord> groups, EnemyForces enemy,
        CombatDataRecord combatData, AttackTally killed, Dictionary<GroupRecord, Dictionary<AttackType, double>> priority2, Random random)
    {
        var targ = new Dictionary<GroupRecord, AttackTally>();
        foreach (var g in groups) {
            targ[g] = new AttackTally();
        }

        foreach (var shpI in Enum.GetValues<ShipType>().Select(s => s.ToAttackType())) {
            foreach (var g in groups) {
                targ[g][shpI] = PascalRound((priority2[g].GetValueOrDefault(shpI) / 1000.0) * enemy[currentPosition, shpI]);
            }
        }

        if (currentPosition == ShellPosition.Ground) {
            foreach (var g in groups) {
                targ[g][AttackType.Legion] = PascalRound((priority2[g].GetValueOrDefault(AttackType.Legion) / 1000.0) * enemy[ShellPosition.Ground, AttackType.Legion]);
                targ[g][AttackType.NinjaLegion] = PascalRound((priority2[g].GetValueOrDefault(AttackType.NinjaLegion) / 1000.0) * enemy[ShellPosition.Ground, AttackType.NinjaLegion]);
            }
        }

        if (currentPosition is ShellPosition.HighOrbit or ShellPosition.Orbit) {
            foreach (var g in groups) {
                targ[g][AttackType.DefenseSatellite] = PascalRound((priority2[g].GetValueOrDefault(AttackType.DefenseSatellite) / 1000.0) * enemy[ShellPosition.Orbit, AttackType.DefenseSatellite]);
            }
        }

        if (currentPosition == ShellPosition.SubOrbit) {
            foreach (var g in groups) {
                targ[g][AttackType.IonCannon] = PascalRound((priority2[g].GetValueOrDefault(AttackType.IonCannon) / 1000.0) * enemy[ShellPosition.SubOrbit, AttackType.IonCannon]);
            }
        }

        var noOfLam = 0;
        foreach (var g in groups) {
            if (priority2[g].GetValueOrDefault(AttackType.Lam) == 0) {
                continue;
            }
            noOfLam = ClampResource(noOfLam + g.Num * ((100 + Rnd(random, 0, 100)) / (double)CombatConstants.CombatTable[(AttackType.Lam, g.Typ)]));
        }
        noOfLam = Math.Min(noOfLam, enemy[ShellPosition.SubOrbit, AttackType.Lam]);
        enemy[ShellPosition.SubOrbit, AttackType.Lam] -= noOfLam;
        killed[AttackType.Lam] += noOfLam;
        foreach (var g in groups) {
            targ[g][AttackType.Lam] = PascalRound((priority2[g].GetValueOrDefault(AttackType.Lam) / 1000.0) * noOfLam);
        }

        if (currentPosition == ShellPosition.Orbit) {
            var noOfGdm = 0;
            foreach (var g in groups) {
                if (priority2[g].GetValueOrDefault(AttackType.Gdm) == 0) {
                    continue;
                }
                noOfGdm = ClampResource(noOfGdm + g.Num * ((100 + Rnd(random, 0, 100)) / (double)CombatConstants.CombatTable[(AttackType.Gdm, g.Typ)]));
            }
            noOfGdm = Math.Min(noOfGdm, combatData.MaxGdm + Rnd(random, 0, 10));
            noOfGdm = Math.Min(noOfGdm, enemy[ShellPosition.SubOrbit, AttackType.Gdm]);
            enemy[ShellPosition.SubOrbit, AttackType.Gdm] -= noOfGdm;
            killed[AttackType.Gdm] += noOfGdm;

            foreach (var g in groups) {
                var gdmAtTarget = PascalRound((priority2[g].GetValueOrDefault(AttackType.Gdm) / 1000.0) * noOfGdm);
                var ship = g.Typ.AsShipType()
                    ?? throw new InvalidOperationException("BuildTargetArray: GDM interception against a troop-typed group needs AdvanceGroups' Typ<-GATTyp swap (Phase 5 commit 5e) — not reachable from this build.");
                gdmAtTarget -= PascalRound(CombatConstants.GdmKill[ship] / 10.0 * g.Num);
                if (gdmAtTarget < 0) {
                    gdmAtTarget = 0;
                }
                targ[g][AttackType.Gdm] = gdmAtTarget;
            }
        }

        return targ;
    }

    /// <summary>InRangeOfDefense (ATTACK.PAS:719-747): whether a group attacking from <paramref name="attackingFrom"/> is close enough to reach <paramref name="target"/>.</summary>
    private static bool InRangeOfDefense(AttackType attacker, AttackType target, ShellPosition attackingFrom) => target switch {
        AttackType.Gdm or AttackType.Lam => attackingFrom == ShellPosition.SubOrbit,
        AttackType.IonCannon => (attackingFrom == ShellPosition.SubOrbit && attacker != AttackType.Fighter) || attackingFrom == ShellPosition.Ground,
        AttackType.DefenseSatellite => attackingFrom == ShellPosition.Orbit,
        AttackType.Legion or AttackType.NinjaLegion => attackingFrom == ShellPosition.Ground || (attackingFrom == ShellPosition.SubOrbit && attacker is AttackType.Penetrator or AttackType.Starship),
        _ => true,
    };

    /// <summary>
    /// GroupAttack (ATTACK.PAS:705-774): each attacking group's own damage against whatever it's aimed
    /// at. A group with no target yet (<see cref="GroupRecord.Trg"/> null) contributes nothing — real
    /// Pascal behavior (CombatTable's NoRes column is all zero), not a guard this port added; see
    /// AttackType's own doc comment.
    /// </summary>
    private static void GroupAttack(IReadOnlyList<GroupRecord> groups, ISet<GroupRecord> attackingGroups, AttackTally eShDest, CombatDataRecord combatData, Random random)
    {
        foreach (var g in groups) {
            if (!attackingGroups.Contains(g) || g.Trg is not { } trg) {
                continue;
            }

            var adj = trg is AttackType.Legion or AttackType.NinjaLegion ? combatData.DGrndAdj : combatData.DShipAdj;
            var temp = ShipsDestroyed(random, g.Num, g.Typ, trg, adj);

            if (g.Sta == GroupStatus.Advancing && trg is AttackType.Starship or AttackType.Transport or AttackType.DefenseSatellite) {
                temp += temp / 2;
            }

            if (!InRangeOfDefense(g.Typ, trg, g.Pos)) {
                temp = 0;
            }

            eShDest[trg] = ClampResource(eShDest[trg] + temp);

            if (g.Typ == AttackType.HunterKiller) {
                g.Flg = true;
            }
        }
    }

    /// <summary>
    /// EnemyAttack (ATTACK.PAS:776-808): the defender's return fire against every attacking group, from
    /// every AttackType in turn — called for all 13 AttackTypes per attacking group regardless of
    /// whether that type actually has anything targeting the group (<see cref="ShipsDestroyed"/> still
    /// consumes RNG for a zero count), matching Pascal's own unconditional loop exactly.
    /// </summary>
    private static Dictionary<GroupRecord, int> EnemyAttack(
        IReadOnlyList<GroupRecord> groups, ISet<GroupRecord> attackingGroups, Dictionary<GroupRecord, AttackTally> targ,
        CombatDataRecord combatData, CombatDetails details, Random random)
    {
        var gShDest = new Dictionary<GroupRecord, int>();

        foreach (var g in groups) {
            if (!attackingGroups.Contains(g)) {
                continue;
            }

            var groupDamage = 0;
            foreach (var shpI in _allAttackTypes) {
                var temp = ShipsDestroyed(random, targ[g][shpI], shpI, g.Typ, combatData.AShipAdj);

                if (g.Sta == GroupStatus.Advancing && (shpI is AttackType.Starship or AttackType.Transport || (shpI == AttackType.DefenseSatellite && g.Pos == ShellPosition.Orbit))) {
                    temp += temp / 2;
                }

                groupDamage = ClampResource(groupDamage + temp);
                details.Record(shpI, g, temp, g.Num);
            }
            gShDest[g] = groupDamage;
        }

        return gShDest;
    }

    /// <summary>
    /// UpdateGroupsDestroyed (ATTACK.PAS:827-876): applies this round's incoming damage to each
    /// attacking group, destroying it outright if it can't survive, otherwise reducing its count (and,
    /// for a transport/jumptransport, bleeding some of its carried troops proportionally — always
    /// against <see cref="CargoType.Legion"/>'s cargo-space constant regardless of whether the troops
    /// carried are actually ninjas, a real Pascal quirk (ATTACK.PAS:865) preserved verbatim, not fixed).
    /// </summary>
    private static ISet<GroupRecord> UpdateGroupsDestroyed(
        IReadOnlyList<GroupRecord> groups, ISet<GroupRecord> attackingGroups, Dictionary<GroupRecord, int> gShDest, AttackTally casualties)
    {
        var destroyed = new HashSet<GroupRecord>();

        foreach (var g in groups) {
            if (!attackingGroups.Contains(g)) {
                continue;
            }
            var dest = gShDest[g];

            if (g.Num - dest <= 0) {
                casualties[g.Typ] += g.Num;
                g.Num = 0;
                g.Sta = GroupStatus.Destroyed;
                if (g.Typ is AttackType.Jumptransport or AttackType.Transport && g.Gat > 0) {
                    casualties[g.GatTyp!.Value] += g.Gat;
                }
                destroyed.Add(g);
            } else {
                if (g.Typ is AttackType.Jumptransport or AttackType.Transport && dest > 0 && g.Gat > 0) {
                    var trnWithMen = g.Gat / (CombatConstants.CargoSpace[CargoType.Legion] * CombatConstants.TrnAdj[g.Typ.AsShipType()!.Value]);
                    var menLost = Math.Min(g.Gat, ClampResource(dest * trnWithMen / g.Num + 2));
                    g.Gat -= menLost;
                    casualties[g.GatTyp!.Value] += menLost;
                }
                g.Num -= dest;
                casualties[g.Typ] += dest;
            }
        }

        return destroyed;
    }

    /// <summary>UpdateEnemyDestroyed (ATTACK.PAS:810-825): removes this round's confirmed kills from the defender's own forces at this shell.</summary>
    private static void UpdateEnemyDestroyed(EnemyForces enemy, ShellPosition currentPosition, AttackTally eShDest, AttackTally killed)
    {
        foreach (var shpI in _allAttackTypes) {
            var destroyed = Math.Min(eShDest[shpI], enemy[currentPosition, shpI]);
            enemy[currentPosition, shpI] -= destroyed;
            killed[shpI] += destroyed;
        }
    }

    /// <summary>
    /// EnemySurrenders (ATTACK.PAS:232-342): whether the defender gives up after this round, comparing
    /// relative forces and casualty ratios (fleet defenders) or ground troop strength plus tech-level
    /// thresholds (world defenders).
    /// </summary>
    public static bool EnemySurrenders(IReadOnlyList<GroupRecord> groups, EnemyForces enemy, AttackTally casualties, AttackTally killed, CombatDataRecord combatData)
    {
        double plGat = 0, plShPow = 0;
        foreach (var g in groups) {
            if (g.Sta == GroupStatus.Destroyed) {
                continue;
            }
            if (g.Typ == AttackType.NinjaLegion) {
                plGat += 5.0 * g.Num;
            } else if (g.Typ == AttackType.Legion) {
                plGat += g.Num;
            } else if (g.Typ == AttackType.Transport) {
                plGat += g.Num / 5.0;
            } else if (g.Typ == AttackType.Jumptransport) {
                plGat += g.Num / 2.0;
            } else {
                plShPow += CombatConstants.CombatPower[g.Typ] * (g.Num / 100.0);
            }
        }
        plGat = plGat * combatData.AShipAdj / 100.0;
        plShPow = plShPow * combatData.AShipAdj / 100.0;

        double enShPow = 0, atShPow = 0;
        foreach (var pos in _allShellPositions) {
            foreach (var shpI in _shipAndDefenseAttackTypes) {
                var temp = CombatConstants.CombatPower[shpI] * (enemy[pos, shpI] / 100.0);
                enShPow += temp;
                if (shpI is not (AttackType.Jumptransport or AttackType.Transport)) {
                    atShPow += temp;
                }
            }
        }

        var enMen = enemy[ShellPosition.Ground, AttackType.Legion] + 5.0 * enemy[ShellPosition.Ground, AttackType.NinjaLegion];
        enMen = enMen * combatData.DGrndAdj / 100.0;
        enShPow = enShPow * combatData.DShipAdj / 100.0;

        double attKilled = 0, defKilled = 0;
        foreach (var shpI in _shipAndDefenseAttackTypes) {
            attKilled += (CombatConstants.CombatPower[shpI] / 100.0) * casualties[shpI];
            defKilled += (CombatConstants.CombatPower[shpI] / 100.0) * killed[shpI];
        }

        var attA = plShPow > 0 ? attKilled / plShPow : 0;
        var defA = enShPow > 0 ? defKilled / enShPow : 0;

        if (combatData.Target is Fleet) {
            if (defA > attA * 2 && enShPow < plShPow / 3) {
                return true;
            }
            if (defA > 0 && attA == 0 && plShPow > atShPow) {
                return true;
            }
            return enShPow == 0;
        }

        if (combatData.RevIndex > 70 && plGat > enMen && plShPow > atShPow) {
            return true;
        }
        if (enMen == 0 && plGat > 0) {
            return true;
        }
        if (atShPow < plShPow / 2 && enMen < plGat / 2 && defA > attA && combatData.DTech is >= TechLevel.Atomic and <= TechLevel.Warp) {
            return true;
        }
        if (enMen < plGat / 4 && combatData.DTech is >= TechLevel.Atomic and <= TechLevel.Warp) {
            return true;
        }
        return defA > attA * 2 && enMen < plGat / 2 && combatData.DTech is >= TechLevel.Warp and <= TechLevel.Jump;
    }
}
