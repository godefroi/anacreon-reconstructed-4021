using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;
using static Reconstructed4021.Core.PascalMath;

namespace Reconstructed4021.Core.Combat;

/// <summary>
/// The pure bookkeeping behind ATTCOMM.PAS's <c>GetGroups</c> (Fleet Group Configuration): moving
/// ships/troops between a fleet's own available pool and up to <see cref="MaxGroups"/> combat groups.
/// Deliberately separate from <see cref="CombatEngine.DefaultDistribution"/> — that one builds groups
/// that are already full (one per ship type present, used only by the automatic engine); this one
/// starts from empty groups and a full pool, matching <c>GetGroups</c>' own starting state
/// (<c>FillChar(Gp,SizeOf(Gp),0)</c>, then <c>GetShips</c>/<c>GetCargo</c> into the pool) — the
/// interactive path's own player-driven starting point. Same Core/Tui split as
/// <see cref="ResourceDistribution"/> (pure logic) / <c>ResourceDistributionEditor</c> (Tui grid).
/// </summary>
public static class FleetGroupConfiguration
{
    /// <summary>MaxNoOfGroups (ATTACK.PAS:24).</summary>
    public const int MaxGroups = 9;

    /// <summary>
    /// ChangeGroupType (ATTCOMM.PAS:846-868), plus LoadShips' own <c>Typ&lt;&gt;CurTyp</c> branch
    /// (ATTCOMM.PAS:879-892): returns whatever the group currently holds (ships and any carried
    /// troops) to the pool under its *old* type before switching. A no-op if <paramref name="newType"/>
    /// already matches.
    /// </summary>
    public static void ChangeGroupType(ShipCounts shipPool, CargoHold cargoPool, GroupRecord group, AttackType newType)
    {
        if (group.Typ == newType) {
            return;
        }
        ReturnToPool(shipPool, cargoPool, group);
        group.Typ = newType;
    }

    private static void ReturnToPool(ShipCounts shipPool, CargoHold cargoPool, GroupRecord group)
    {
        if (group.Num > 0) {
            var ship = group.Typ.AsShipType() ?? throw new InvalidOperationException($"ReturnToPool: group Typ {group.Typ} is not a ship type.");
            shipPool[ship] += group.Num;
            group.Num = 0;
        }
        if (group.Gat > 0) {
            cargoPool[ToCargoType(group.GatTyp!.Value)] += group.Gat;
            group.Gat = 0;
            group.GatTyp = null;
        }
    }

    /// <summary>
    /// LoadShips (ATTCOMM.PAS:870-919, the transfer itself — the caller is responsible for calling
    /// <see cref="ChangeGroupType"/> first if the group's own <c>Typ</c> needs to change). Positive
    /// <paramref name="amount"/> pulls from the pool (clamped to what's available); negative returns
    /// ships to the pool (clamped to the group's own <see cref="GroupRecord.Num"/>) — Pascal's own
    /// <c>LesserInt</c>/<c>-LesserInt</c> pair.
    /// </summary>
    public static void LoadShips(ShipCounts shipPool, GroupRecord group, int amount)
    {
        var ship = group.Typ.AsShipType() ?? throw new InvalidOperationException($"LoadShips: group Typ {group.Typ} is not a ship type.");
        var transfer = amount > 0 ? Math.Min(amount, shipPool[ship]) : -Math.Min(-amount, group.Num);
        shipPool[ship] -= transfer;
        group.Num += transfer;
    }

    /// <summary>
    /// LoadTransports (ATTCOMM.PAS:951-975) — the 'M'/'N' command: only valid for a Transport/
    /// Jumptransport group. Returns any troops the group already carries to the pool first, then loads
    /// as many of <paramref name="troopType"/> as the pool has, capped by cargo space
    /// (<c>TrnAdj[Typ]*Num*CargoSpace[troopType]</c>, the same formula <see cref="CombatEngine.DefaultGroup"/>
    /// already uses for the automatic path).
    /// </summary>
    public static void LoadTroops(CargoHold cargoPool, GroupRecord group, CargoType troopType)
    {
        if (group.Typ is not (AttackType.Transport or AttackType.Jumptransport)) {
            throw new InvalidOperationException($"LoadTroops: group Typ {group.Typ} cannot carry troops.");
        }
        if (group.Gat > 0) {
            cargoPool[ToCargoType(group.GatTyp!.Value)] += group.Gat;
        }

        var shipType = group.Typ.AsShipType()!.Value;
        var capacity = ClampResource(PascalRound(CombatConstants.TrnAdj[shipType] * group.Num * CombatConstants.CargoSpace[troopType]));
        var gat = Math.Min(cargoPool[troopType], capacity);

        group.Gat = gat;
        group.GatTyp = ToAttackType(troopType);
        cargoPool[troopType] -= gat;
    }

    /// <summary>
    /// GetGroups' own tail (ATTCOMM.PAS:1037-1069): drops every still-empty group, then compacts the
    /// rest into two passes — every non-transport/jumptransport group first, then every transport/
    /// jumptransport group (Pascal's own two separate <c>FOR i:=1 TO MaxNoOfGroups</c> loops, not one)
    /// — and finally, for any transport/jumptransport group still carrying no troops, auto-loads
    /// whatever's left in the pool. This last step checks <c>Cr[men]</c> *before* <c>Cr[nnj]</c>
    /// (ATTCOMM.PAS:1055-1068) — the *opposite* priority from <see cref="CombatEngine.DefaultGroup"/>'s
    /// own ninja-preferred order for the automatic path's tail-load. Confirmed against source, not
    /// assumed to match: a real, asymmetric Pascal quirk between the two screens' otherwise-similar
    /// auto-load steps, preserved as-is (see docs/QUESTIONS_FOR_GEORGE.md for the class of "deliberate,
    /// or incidental?" question this could also be, though this one wasn't judged surprising enough on
    /// its own to add there — GetGroups is player-driven and DefaultDistribution isn't, so a scenario
    /// author biasing the two differently isn't inherently odd the way GroupTarget's total lack of
    /// auto-aim is).
    /// </summary>
    public static List<GroupRecord> Finalize(IReadOnlyList<GroupRecord> groups, CargoHold cargoPool)
    {
        var result = new List<GroupRecord>();

        foreach (var g in groups) {
            if (g.Num > 0 && g.Typ is not (AttackType.Transport or AttackType.Jumptransport)) {
                result.Add(g);
            }
        }
        foreach (var g in groups) {
            if (g.Num > 0 && g.Typ is AttackType.Transport or AttackType.Jumptransport) {
                result.Add(g);
            }
        }

        foreach (var g in result) {
            if (g.Gat != 0 || g.Typ is not (AttackType.Transport or AttackType.Jumptransport)) {
                continue;
            }

            if (cargoPool[CargoType.Legion] > 0) {
                LoadTroops(cargoPool, g, CargoType.Legion);
            } else if (cargoPool[CargoType.NinjaLegion] > 0) {
                LoadTroops(cargoPool, g, CargoType.NinjaLegion);
            }
        }

        return result;
    }

    private static CargoType ToCargoType(AttackType type) => type switch {
        AttackType.Legion => CargoType.Legion,
        AttackType.NinjaLegion => CargoType.NinjaLegion,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "ToCargoType: only Legion/NinjaLegion are ground-assault-troop AttackTypes."),
    };

    private static AttackType ToAttackType(CargoType type) => type switch {
        CargoType.Legion => AttackType.Legion,
        CargoType.NinjaLegion => AttackType.NinjaLegion,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "ToAttackType: only Legion/NinjaLegion can be loaded as ground-assault troops."),
    };
}
