using Reconstructed4021.Core;
using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.SaveFormat;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;
using static Reconstructed4021.Core.PascalMath;

namespace Reconstructed4021.LegacyNpe;

/// <summary>
/// The shared toolkit multiple NPE personalities call in real Pascal (NPEINTR.PAS, 1,732 lines):
/// fleet-composition planning, targeting, regional bookkeeping, world (re)designation, fleet
/// creation, and mission execution (the five <c>Deploy*Fleet</c> procedures and all eight
/// <c>Implement*MSN</c> procedures, built on the fleet-lifecycle primitives in
/// <see cref="Entities.FleetLifecycle"/>/<see cref="Entities.FleetLogistics"/>). Ported as its own service
/// <see cref="KingdomTurnHandler"/> depends on rather than folded into Kingdom's own class, since
/// Pirate/Berserker/Guardian call the same real Pascal procedures (see docs/PORT_DESIGN.md).
///
/// <b>Ground truth, deliberately hardcoded rather than golden-file:</b> every method below either
/// has no <c>Rnd</c>/<c>Random</c> call at all, or draws inside a scan-every-planet-in-the-galaxy
/// loop whose count depends on live galaxy shape — a golden case for the second group would need a
/// full hand-assembled universe to mean anything close to what real play produces. This port's
/// SaveFormat layer could make building one of those cheap; see <c>NpeToolkitTests</c> for the
/// hand-derived cases used instead, and reference/verify/README.md's own ground-truth split guidance.
/// </summary>
public static class NpeToolkit
{
    private static readonly ShipType[] _returnSequence = [ShipType.Fighter, ShipType.Jumpship, ShipType.Penetrator, ShipType.HunterKiller, ShipType.Starship];
    private static readonly ShipType[] _conquerSequence = [ShipType.Jumpship, ShipType.HunterKiller, ShipType.Penetrator, ShipType.Starship, ShipType.Fighter];
    private static readonly ShipType[] _stackSequence = [ShipType.Penetrator, ShipType.HunterKiller, ShipType.Starship, ShipType.Fighter, ShipType.Jumpship];
    private static readonly ShipType[] _jumpAttackSequence = [ShipType.Jumpship, ShipType.HunterKiller, ShipType.HunterKiller, ShipType.HunterKiller, ShipType.HunterKiller];
    private static readonly ShipType[] _raidTransportsSequence = [ShipType.HunterKiller, ShipType.HunterKiller, ShipType.HunterKiller, ShipType.HunterKiller, ShipType.HunterKiller];

    /// <summary>MilitaryPower (MISC.PAS:40-63) — a composite strength score, ships and defenses both weighted by <see cref="CombatConstants.MPower"/> (not <see cref="CombatConstants.CombatPower"/> — see that field's own doc comment on why these are two distinct tables).</summary>
    public static long MilitaryPower(ShipCounts ships, DefenseCounts defenses)
    {
        long power = 0;
        foreach (var t in Enum.GetValues<DefenseType>()) {
            power += (long)defenses[t] * CombatConstants.MPower[t.ToAttackType()];
        }
        foreach (var t in Enum.GetValues<ShipType>()) {
            power += (long)ships[t] * CombatConstants.MPower[t.ToAttackType()];
        }
        return power;
    }

    /// <summary>
    /// GetFleetComposition (NPEINTR.PAS:237-322) — the ship/cargo bundle a Deploy*Fleet call would
    /// carry for a mission of the given power/troop budget, read from what's on hand at
    /// <paramref name="source"/> without touching it (the actual subtraction happens in
    /// <see cref="Entities.FleetLifecycle.DeployFleet"/> itself). SSeq (NPEINTR.PAS:247-248) is
    /// declared but never referenced by the real sequence-selection below — SlowAttackMSN falls
    /// through to the same default as every other unlisted mission, not to SSeq as its name suggests
    /// (see docs/PASCAL_ARCHITECTURE_NOTES.md). BSRKAttackMSN's extra-transports branch is real and
    /// kept even though this port doesn't model Berserker — it's free once
    /// <paramref name="mission"/> can hold that value.
    /// </summary>
    public static (ShipCounts Ships, CargoHold Cargo) GetFleetComposition(IEconomicWorld source, long power, long gat, NpeMissionType mission)
    {
        var sequence = mission switch {
            NpeMissionType.Return => _returnSequence,
            NpeMissionType.Conquer => _conquerSequence,
            NpeMissionType.Stack => _stackSequence,
            NpeMissionType.JumpAttack => _jumpAttackSequence,
            NpeMissionType.RaidTransports => _raidTransportsSequence,
            _ => _conquerSequence, // JSeq is also the real Pascal default (the ELSE branch)
        };

        var ships = new ShipCounts();
        var cargo = new CargoHold();
        var atBase = source.Ships;
        var cargoAtBase = source.Cargo;

        var i = 0;
        while (power > 0 && i < 5) {
            var shipType = sequence[i];
            var mPower = CombatConstants.CombatPower[shipType.ToAttackType()];
            ships[shipType] = Math.Min(atBase[shipType], PascalRound((double)power / mPower) + 1);
            power -= (long)ships[shipType] * mPower;
            i++;
        }

        if (gat > 0) {
            var nnjToTake = Math.Min(gat / 3, cargoAtBase[CargoType.NinjaLegion]);
            gat -= 3 * nnjToTake;
            var menToTake = Math.Min(gat, Math.Max(0, cargoAtBase[CargoType.Legion] - 500L));
            gat -= menToTake;

            // Real Pascal uses CargoSpace[men] here for the combined men+nnj request, not a
            // per-troop-type figure — verbatim (they happen to be equal, 5 and 5, in the shipped
            // table, so this never actually diverges from using each type's own constant).
            var legionSpace = FleetLogistics.CargoSpacePerUnit[CargoType.Legion];
            // CargoSpaceNeeded is also a Word in real Pascal — a rounding round-trip (Ships[jtn]'s
            // Round(CargoSpaceNeeded/TrnAdj[jtn]) then Round(Ships[jtn]*TrnAdj[jtn]) back) can
            // overshoot by 1 and underflow it by a hair when jtn ship supply isn't the limiting
            // factor. Wrapped the same way as totalCargoSpace below, for the same reason: the
            // wrapped (huge) value, not a small negative one, is what real Pascal's own
            // LesserInt(ShpAtBase[trn], ...) then clamps against.
            var cargoSpaceNeeded = unchecked((ushort)PascalRound((menToTake + nnjToTake) / (double)legionSpace));

            ships.Jumptransports = Math.Min(atBase.Jumptransports, PascalRound(cargoSpaceNeeded / FleetLogistics.JumptransportCargoAdjustment));
            cargoSpaceNeeded = unchecked((ushort)(cargoSpaceNeeded - PascalRound(ships.Jumptransports * FleetLogistics.JumptransportCargoAdjustment)));
            ships.Transports = Math.Min(atBase.Transports, cargoSpaceNeeded);
            // cargoSpaceNeeded's final decrement (NPEINTR.PAS:301) is dead — nothing reads it again.

            // NPEINTR.PAS:305-307 multiplies by CargoSpacePerUnit both times (not divide-then-
            // multiply, as the transports<->raw-units unit conversion elsewhere in this method would
            // suggest) — ported verbatim, not "fixed." Real Pascal's TotalCargoSpace is a Word
            // (unsigned 16-bit), so the second line's subtraction wraps whenever Cargo[nnj]*
            // CargoSpacePerUnit exceeds it, rather than going negative — and that wrapped value is
            // always far larger than any realistic MenToTake (bounded by PascalMath.MaxResources), so
            // the following LesserInt/Math.Min always resolves to MenToTake in that case. Replicated
            // with an explicit ushort cast: a plain (signed, non-wrapping) int would instead go
            // negative and make Math.Min pick the wrong, negative operand — the opposite of what real
            // Pascal's wraparound produces.
            var totalCargoSpace = unchecked((ushort)PascalRound(ships.Jumptransports * FleetLogistics.JumptransportCargoAdjustment + ships.Transports * 1.0));

            cargo[CargoType.NinjaLegion] = (int)Math.Min(nnjToTake, PascalRound(totalCargoSpace * FleetLogistics.CargoSpacePerUnit[CargoType.NinjaLegion]));
            totalCargoSpace = unchecked((ushort)(totalCargoSpace - PascalRound(cargo[CargoType.NinjaLegion] * (double)FleetLogistics.CargoSpacePerUnit[CargoType.NinjaLegion])));
            cargo[CargoType.Legion] = (int)Math.Min(menToTake, PascalRound(totalCargoSpace * FleetLogistics.CargoSpacePerUnit[CargoType.Legion]));
        }

        if (mission == NpeMissionType.BerserkerAttack) {
            ships.Transports = atBase.Transports;
            ships.Jumptransports = atBase.Jumptransports;
        }

        if (ships.HunterKillers + ships.Jumpships + ships.Jumptransports + ships.Penetrators + ships.Starships + ships.Transports == 0) {
            // ASSERT: only fighters in the fleet (Ships[fgt] is deliberately excluded from that sum).
            cargo[CargoType.Trillum] = 10;
        }

        return (ships, cargo);
    }

    /// <summary>AlreadyTargetted (NPEINTR.PAS:324-344) — whether some fleet is already assigned this exact mission against this exact target. Reference equality: this port never duplicates entity objects, so it's exactly Pascal's SameID.</summary>
    public static bool AlreadyTargetted(IReadOnlyDictionary<Fleet, KingdomFleetState> fleetStates, object target, NpeMissionType mission) =>
        fleetStates.Values.Any(s => s.Mission == mission && ReferenceEquals(s.Target, target));

    /// <summary>GetPotentialRes (NPEINTR.PAS:346-376) — what's on hand at a world plus what's already inbound to it (any fleet currently returning there).</summary>
    public static (ShipCounts Ships, CargoHold Cargo) GetPotentialRes(IEconomicWorld world, IReadOnlyDictionary<Fleet, KingdomFleetState> fleetStates)
    {
        var ships = new ShipCounts();
        var cargo = new CargoHold();
        foreach (var t in Enum.GetValues<ShipType>()) {
            ships[t] = world.Ships[t];
        }
        foreach (var t in Enum.GetValues<CargoType>()) {
            cargo[t] = world.Cargo[t];
        }

        foreach (var (fleet, state) in fleetStates) {
            if (state.Mission != NpeMissionType.Return || !ReferenceEquals(state.Target, world)) {
                continue;
            }
            foreach (var t in Enum.GetValues<ShipType>()) {
                ships[t] += fleet.Ships[t];
            }
            foreach (var t in Enum.GetValues<CargoType>()) {
                cargo[t] += fleet.Cargo[t];
            }
        }

        return (ships, cargo);
    }

    /// <summary>
    /// MinimumDefense (NPEINTR.PAS:378-425) — how much MilitaryPower this empire thinks is enough to
    /// defend a world, no threat estimate. <c>persona.Defensive*50</c> is verbatim, not a typo: with
    /// Defensive up to 100 that's up to a x5000 multiplier on top of the population adjustment, before
    /// a further x3 (and possibly x6) per hostile world within 5 sectors — real Pascal, kept as-is
    /// rather than "fixed," using <see langword="long"/> so it doesn't overflow the way it would in
    /// Pascal's own LongInt for a populous world with several close neighbors.
    /// </summary>
    public static long MinimumDefense(IEconomicWorld world, NpeCharacter persona, Game game)
    {
        long baseValue = NpeConstants.TypeValue[world.Type];
        baseValue = PascalRound(baseValue * (0.5 + world.Population / 2500.0));
        baseValue *= persona.Defensive * 50L;

        var owner = world.Owner;
        foreach (var planet in game.Galaxy.Planets) {
            if (planet.Owner == owner || planet.Owner.IsIndependent) {
                continue;
            }
            if (planet.Location.DistanceTo(world.Location) > 5) {
                continue;
            }

            baseValue *= 3;
            if (planet.Type is WorldType.Base or WorldType.Capital) {
                baseValue *= 2;
            }
        }

        return baseValue;
    }

    /// <summary>
    /// AverageMilitaryPower (NPEINTR.PAS:427-451) — mean ship-only power across an empire's regional
    /// capitals. Verbatim quirk: real Pascal zeroes its Defns array once and never repopulates it
    /// before calling MilitaryPower, so a base's actual defenses are never counted here, only its
    /// ships — kept as-is (Chesterton's fence), not "fixed" to read real defenses.
    /// </summary>
    public static long AverageMilitaryPower(IReadOnlyList<IEconomicWorld> regionCapitals)
    {
        if (regionCapitals.Count == 0) {
            return 0;
        }

        var empty = new DefenseCounts();
        var total = regionCapitals.Sum(b => (double)MilitaryPower(b.Ships, empty));
        return PascalRound(total / regionCapitals.Count);
    }

    /// <summary>
    /// GetBestBase (NPEINTR.PAS:453-503) — the nearest owned world with enough ships/troops to deploy
    /// a fleet of the given size, or null if none qualifies. Same verbatim zero-defenses quirk as
    /// <see cref="AverageMilitaryPower"/> — real Pascal's nested Qualifies also never populates Defns.
    /// </summary>
    public static IEconomicWorld? GetBestBase(Empire emp, ISectorObject target, long fleetPower, long fleetGat, Game game)
    {
        IEconomicWorld? best = null;
        var bestDistance = 15;
        var empty = new DefenseCounts();

        bool Qualifies(IEconomicWorld candidate) =>
            MilitaryPower(candidate.Ships, empty) > fleetPower / 2
            && candidate.Cargo[CargoType.Legion] + 5L * candidate.Cargo[CargoType.NinjaLegion] > fleetGat;

        foreach (var planet in game.Galaxy.Planets) {
            if (planet.Owner != emp) {
                continue;
            }
            var distance = planet.Location.DistanceTo(target.Location);
            if (distance < bestDistance && Qualifies(planet)) {
                best = planet;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// GetBestPlanetToProtect (NPEINTR.PAS:630-666) — the weakest-defended non-base world within 5
    /// sectors of <paramref name="baseObject"/>, or that empire's capital if none qualifies.
    /// <paramref name="baseObject"/> is <see cref="ISectorObject"/>, not <see cref="IEconomicWorld"/>:
    /// real Pascal's <c>BaseID: IDNumber</c> is generic, and its one real 6c-2 call site
    /// (<c>ImplementStackMSN</c>) passes a <see cref="Fleet"/>, not a world — only
    /// <see cref="ISectorObject.Owner"/>/<see cref="ISectorObject.Location"/> are ever read.
    /// </summary>
    public static IEconomicWorld? GetBestPlanetToProtect(ISectorObject baseObject, Game game)
    {
        var emp = baseObject.Owner;
        IEconomicWorld? best = emp.Capital;
        var lowestDefenses = long.MaxValue;

        foreach (var planet in game.Galaxy.Planets) {
            if (planet.Owner != emp || planet.Type is WorldType.Base or WorldType.Capital) {
                continue;
            }
            if (planet.Location.DistanceTo(baseObject.Location) > 5) {
                continue;
            }

            var power = MilitaryPower(planet.Ships, planet.Defenses);
            if (power < lowestDefenses) {
                best = planet;
                lowestDefenses = power;
            }
        }

        return best;
    }

    /// <summary>
    /// GetBestRaiderTarget (NPEINTR.PAS:668-713) — a gate, else a construction site, else a planet, of
    /// the enemy's, chosen by an independent coin-flip per candidate rather than a scored search. The
    /// gate pass rolls for <i>every active gate in the galaxy</i>, not just the enemy's — real Pascal's
    /// `Rnd(1,100)&lt;50 AND Known(...) AND GetStatus(...)=EnemyEmp` short-circuits left-to-right
    /// (Turbo Pascal's default), so the roll (the leftmost operand) always happens before the ownership
    /// check; the construction-site and planet passes already loop only the enemy's own, so no
    /// equivalent draw-count subtlety there. Ported as three matching short-circuited loops so the RNG
    /// draw count/order stays identical to source.
    /// </summary>
    public static object? GetBestRaiderTarget(Empire emp, Empire enemyEmp, Game game, Random random)
    {
        object? best = null;

        foreach (var gate in game.Galaxy.Stargates) {
            if (Rnd(random, 1, 100) < 50 && Game.Known(emp, gate) && gate.Owner == enemyEmp) {
                best = gate;
            }
        }

        if (best is null) {
            foreach (var site in game.Galaxy.ConstructionSites) {
                if (site.Owner != enemyEmp) {
                    continue;
                }
                if (Rnd(random, 1, 100) < 50 && Game.Known(emp, site)) {
                    best = site;
                }
            }
        }

        if (best is null) {
            foreach (var planet in game.Galaxy.Planets) {
                if (planet.Owner != enemyEmp) {
                    continue;
                }
                if (Rnd(random, 1, 100) < 25 && Game.Known(emp, planet)) {
                    best = planet;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// GetBestTarget (NPEINTR.PAS:715-775) — the highest-value known, not-already-targeted enemy world
    /// weaker than <paramref name="basePower"/>, scored by tech/class/population and discounted by
    /// defenses. <paramref name="candidates"/> is real Pascal's <c>SetOfPossibilities</c> parameter —
    /// callers pass the enemy's owned planets, not a port of Pascal's bitset type.
    /// </summary>
    public static (IEconomicWorld? Target, long Defense, long Men) GetBestTarget(
        Empire emp, IEnumerable<IEconomicWorld> candidates, long basePower, NpeCharacter persona,
        IReadOnlyDictionary<Fleet, KingdomFleetState> fleetStates, Random random)
    {
        IEconomicWorld? target = null;
        long targetDefense = 0;
        long targetMen = 0;
        var targetValue = 0.0;

        var factor = (1.5 + random.NextDouble()) * (persona.WorldPower / 20.0);

        foreach (var candidate in candidates) {
            if (!Game.Known(emp, candidate) || AlreadyTargetted(fleetStates, candidate, NpeMissionType.Conquer)) {
                continue;
            }

            var calcValue = factor * ((int)candidate.TechLevel + 1) * NpeConstants.ClassValue[candidate.EffectiveClass];
            calcValue *= candidate.Population / 1000.0;

            var defense = MilitaryPower(candidate.Ships, candidate.Defenses);
            var troops = candidate.Cargo[CargoType.Legion] + 4L * candidate.Cargo[CargoType.NinjaLegion] + 10;
            var defValue = 100 + PascalRound((defense + troops) / 1000.0);
            calcValue /= defValue;

            if (calcValue > targetValue && defense < basePower) {
                targetValue = calcValue;
                targetDefense = defense;
                targetMen = troops;
                target = candidate;
            }
        }

        return (target, targetDefense, targetMen);
    }

    /// <summary>
    /// GetRegionalCapital (NPEINTR.PAS:888-915) — the nearest of an empire's own regional capitals to
    /// a subject. Real Pascal leaves its result variable uninitialized (undefined behavior) if
    /// <paramref name="regionCapitals"/> is empty; every real call site sources that list from
    /// <see cref="CreateRegionArray"/>, which is never guaranteed nonempty (a brand-new empire with no
    /// base yet), so this returns null rather than inventing a fallback. Callers must handle it.
    /// </summary>
    public static IEconomicWorld? GetRegionalCapital(ISectorObject subject, IReadOnlyList<IEconomicWorld> regionCapitals)
    {
        IEconomicWorld? closest = null;
        var bestDistance = 100;

        foreach (var candidate in regionCapitals) {
            var distance = subject.Location.DistanceTo(candidate.Location);
            if (distance < bestDistance) {
                bestDistance = distance;
                closest = candidate;
            }
        }

        return closest;
    }

    /// <summary>MaxNoOfRegions (NPEINTR.PAS:28) — the fixed cap on how many regional capitals CreateRegionArray tracks, in scan order; an empire with more bases than this only has its first 20 (by galaxy scan order) considered a "region."</summary>
    public const int MaxNoOfRegions = 20;

    /// <summary>CreateRegionArray (NPEINTR.PAS:1634-1659) — every base/capital planet this empire owns, capped at <see cref="MaxNoOfRegions"/>. Planets only — starbases are never regional capitals in real Pascal (ID.ObjTyp is fixed to Pln before the scan).</summary>
    public static List<IEconomicWorld> CreateRegionArray(Empire emp, Game game) =>
        [.. game.Galaxy.Planets
            .Where(p => p.Owner == emp && p.Type is WorldType.Base or WorldType.Capital)
            .Take(MaxNoOfRegions)
            .Cast<IEconomicWorld>()];

    /// <summary>
    /// GetNewDesignation (NPEINTR.PAS:917-1036) — what an owned world should be redesignated as, by a
    /// weighted roll over every <see cref="WorldType"/>. <c>persona</c> is real Pascal's own
    /// parameter but confirmed unused: the whole procedure body never reads a single
    /// <c>Persona.</c> field, so it's dropped here rather than threaded through unread.
    /// </summary>
    public static WorldType GetNewDesignation(IEconomicWorld world, IReadOnlyList<IEconomicWorld> regionCapitals, Game game, Random random)
    {
        var tech = world.TechLevel;
        var pop = world.Population;

        var classAdj = new Dictionary<IndustryType, double>();
        foreach (var ind in Enum.GetValues<IndustryType>()) {
            var adj = AnnualTickHandler.ClassIndustryAdjustment[(world.EffectiveClass, ind)] / 100.0;
            classAdj[ind] = adj * adj;
        }

        var chance = new Dictionary<WorldType, double>();
        foreach (var type in Enum.GetValues<WorldType>()) {
            chance[type] = tech < NpeConstants.MinTechForType[type] ? 0 : NpeConstants.TypeDefault[type];
        }

        chance[world.Type] *= 20;

        chance[WorldType.RawMaterialMine] *= classAdj[IndustryType.Mining] * classAdj[IndustryType.Chemical] * classAdj[IndustryType.TrillumMining];
        chance[WorldType.Mine] *= classAdj[IndustryType.Mining];
        chance[WorldType.Chemical] *= classAdj[IndustryType.Chemical];
        chance[WorldType.TrillumMine] *= classAdj[IndustryType.TrillumMining];
        chance[WorldType.Agricultural] *= classAdj[IndustryType.Supply];
        chance[WorldType.JumpshipBase] *= classAdj[IndustryType.Chemical] * classAdj[IndustryType.Mining] * classAdj[IndustryType.Supply];
        chance[WorldType.StarshipBase] *= classAdj[IndustryType.Mining] * classAdj[IndustryType.Supply];
        chance[WorldType.Base] *= classAdj[IndustryType.Mining] * classAdj[IndustryType.Supply];

        if (pop > 2000) {
            chance[WorldType.Base] *= 25;
            chance[WorldType.StarshipBase] *= 28;
            chance[WorldType.JumpshipBase] *= 30;
        } else if (pop > 1000) {
            chance[WorldType.Base] *= 12;
            chance[WorldType.StarshipBase] *= 6;
            chance[WorldType.JumpshipBase] *= 12;
        }

        var regionalCapital = GetRegionalCapital(world, regionCapitals);
        if (regionalCapital is not null) {
            var distance = world.Location.DistanceTo(regionalCapital.Location);
            if (distance is < 5 and > 0) {
                chance[WorldType.Base] *= 0.01;
            }
        }

        var owner = world.Owner;
        var closeToACapital = false;
        foreach (var empire in game.Empires) {
            if (closeToACapital) {
                break;
            }
            var capital = empire.Capital;
            if (capital is not null && Game.Known(owner, capital) && world.Location.DistanceTo(capital.Location) <= 5) {
                chance[WorldType.Base] *= 30;
                closeToACapital = true;
            }
        }

        if (world.EffectiveClass is WorldClass.Ambrosia or WorldClass.Paradise && tech >= TechLevel.Bio) {
            return WorldType.Ambrosia;
        }

        var total = 0;
        foreach (var type in Enum.GetValues<WorldType>()) {
            chance[type] = PascalRound(chance[type]);
            total += (int)chance[type];
        }

        if (total < 100) {
            return WorldType.Independent;
        }

        var roll = (int)(random.NextDouble() * total) + 1;
        var newType = WorldType.Agricultural;
        while ((roll > chance[newType] || chance[newType] == 0) && newType != WorldType.TrillumMine) {
            roll -= (int)chance[newType];
            newType++;
        }

        if (newType == WorldType.TrillumMine && chance[WorldType.TrillumMine] == 0) {
            newType = WorldType.Independent;
        }

        return newType;
    }

    /// <summary>
    /// ReDesignateEmpire (NPEINTR.PAS:1038-1058) — considers every owned planet except the capital for
    /// redesignation, skipping any that's already a Base/StarshipBase/JumpshipBase 90% of the time
    /// (the 10% re-roll chance is drawn only when the type check doesn't already skip it — Pascal's
    /// `NOT(A OR B)` short-circuits B, so a planet that's already one of those three types never
    /// consumes an Rnd draw here at all).
    /// </summary>
    public static void ReDesignateEmpire(Empire emp, IReadOnlyList<IEconomicWorld> regionCapitals, Game game, Random random)
    {
        var capital = emp.Capital;

        foreach (var planet in game.Galaxy.Planets.Where(p => p.Owner == emp).ToList()) {
            if (planet.Type is WorldType.JumpshipBase or WorldType.StarshipBase or WorldType.Base) {
                continue;
            }
            if (Rnd(random, 1, 100) < 10) {
                continue;
            }
            if (ReferenceEquals(planet, capital)) {
                continue;
            }

            var newType = GetNewDesignation(planet, regionCapitals, game, random);
            if (newType != planet.Type) {
                // WorldDesignation.Redesignate (INTRFACE.PAS:187-221): NewType is never Capital here
                // (Chance[CapTyp] never leaves 0 in GetNewDesignation, and this method's own caller
                // already excludes the capital world from consideration), so its capital-swap branch
                // never fires from this call path -- confirmed, not assumed.
                WorldDesignation.Redesignate(planet, newType, random);
            }
        }
    }

    /// <summary>
    /// EnforceNPEDataLinks (NPEINTR.PAS:1661-1689) — drops mission state for any fleet that's no
    /// longer alive. Real Pascal reconciles a slot-indexed array against SetOfActiveFleets; this
    /// port's Dictionary&lt;Fleet,...&gt; needs the same pruning, since a fleet destroyed elsewhere
    /// (<see cref="CombatOutcome.DestroyFleet"/>) would otherwise leave a dangling dictionary key.
    /// </summary>
    public static void EnforceNpeDataLinks(Dictionary<Fleet, KingdomFleetState> fleetStates, Game game)
    {
        var alive = new HashSet<Fleet>(game.Galaxy.Fleets);
        foreach (var fleet in fleetStates.Keys.Where(f => !alive.Contains(f)).ToList()) {
            fleetStates.Remove(fleet);
        }
    }

    /// <summary>
    /// MidCourseCorrection (NPEINTR.PAS:1542-1561) — a returning fleet whose home base has fallen to
    /// someone else mid-flight gets redirected to the nearest remaining base instead. Called by
    /// <see cref="KingdomTurnHandler"/>'s own UpdateFleets.
    /// </summary>
    public static void MidCourseCorrection(Empire emp, Fleet fleet, KingdomFleetState state, IReadOnlyList<IEconomicWorld> regionCapitals)
    {
        if (state.Mission != NpeMissionType.Return || state.Target is not IEconomicWorld target || target.Owner == emp) {
            return;
        }

        var newBase = GetRegionalCapital(fleet, regionCapitals);
        if (newBase is null) {
            return;
        }

        FleetLifecycle.SetFleetDestination(fleet, newBase.Location);
        state.Target = newBase;
    }

    /// <summary>
    /// SetEmpireDefenses (NPEINTR.PAS:1691-1699) — rolls one of four preset fleet-defense
    /// distributions. A real, easy-to-miss quirk, ported verbatim rather than "fixed": DATASTRC.PAS's
    /// DefenseRecord has two fields, ShellDefDist (fleets) and StarbaseDefDist (starbases), but every
    /// one of the four preset constants here (InitDefenseRecord, Defense1, Defense2, Defense3 —
    /// DATACNST.PAS:373-379, NPEINTR.PAS:41-63) only initializes ShellDefDist. Confirmed two ways,
    /// not inferred: a probe under this project's own fpc -Mtp shows the omitted field reads as
    /// zero, and a raw byte scan of the real TP-compiled 1.31 ANACREON.EXE finds all four constants'
    /// 35-byte StarbaseDefDist span all-zero too (see docs/PASCAL_ARCHITECTURE_NOTES.md for the
    /// offsets) — genuine Turbo Pascal 7 behavior in the shipped game, not an FPC artifact. So every
    /// call resets the empire's *starbase* shell distribution to all zeros too, 100% of the time —
    /// not just the fleet distribution the roll is nominally about.
    /// </summary>
    public static void SetEmpireDefenses(Empire emp, Random random)
    {
        var chosen = Rnd(random, 1, 100) switch {
            <= 25 => NpeConstants.InitDefenseFleets,
            <= 75 => NpeConstants.Defense1Fleets,
            <= 85 => NpeConstants.Defense3Fleets,
            _ => NpeConstants.Defense2Fleets,
        };

        CopyPlan(chosen, emp.DefenseSettings.Fleets);
        ZeroPlan(emp.DefenseSettings.Starbases);
    }

    private static void CopyPlan(ShellDefensePlan source, ShellDefensePlan dest)
    {
        foreach (var pos in Enum.GetValues<ShellPosition>()) {
            foreach (var ship in Enum.GetValues<ShipType>()) {
                dest[pos][ship] = source[pos][ship];
            }
        }
    }

    private static void ZeroPlan(ShellDefensePlan dest)
    {
        foreach (var pos in Enum.GetValues<ShellPosition>()) {
            foreach (var ship in Enum.GetValues<ShipType>()) {
                dest[pos][ship] = 0;
            }
        }
    }

    /// <summary>
    /// PlunderWorld (NPEINTR.PAS:1701-1729) — strips a conquered world's ships/cargo into the fleet
    /// that took it, then sets it independent. Guarded on the fleet's owner still matching the
    /// target's stored owner, matching real Pascal's own guard — a no-op otherwise.
    /// </summary>
    public static void PlunderWorld(Fleet fleet, IEconomicWorld target)
    {
        if (fleet.Owner != target.Owner) {
            return;
        }

        foreach (var t in Enum.GetValues<ShipType>()) {
            fleet.Ships[t] += target.Ships[t];
        }
        foreach (var t in Enum.GetValues<CargoType>()) {
            fleet.Cargo[t] += target.Cargo[t];
        }
        FleetLogistics.BalanceFleet(fleet.Ships, fleet.Cargo);

        foreach (var t in Enum.GetValues<ShipType>()) {
            target.Ships[t] = 0;
        }
        foreach (var t in Enum.GetValues<CargoType>()) {
            target.Cargo[t] = 0;
        }

        target.Type = WorldType.Independent;
        target.Reassign(Empire.Independent);
    }

    /// <summary>MaxNoOfGuards (NPEINTR.PAS:37) — how many guard fleets a stacking fleet can dump itself onto before it's redirected to protect a planet instead.</summary>
    public const int MaxNoOfGuards = 3;

    /// <summary>
    /// DeployBattleFleet (NPEINTR.PAS:505-559) — composes and launches a fleet from
    /// <paramref name="fromWorld"/> toward <paramref name="toTarget"/> for the given mission, aborting
    /// it right back if it can't make the trip on the fuel it launched with. Real Pascal's own
    /// "Slot=0" (fleet-data array full) arm can't happen with this port's Dictionary — dropped; the
    /// EDA&gt;Range arm is the only one that ever fires here, and dropping the other half of an
    /// already-false-short-circuited OR changes nothing observable (neither arm has a side effect).
    /// </summary>
    public static void DeployBattleFleet(
        Empire emp, Dictionary<Fleet, KingdomFleetState> fleetStates,
        IEconomicWorld fromWorld, long power, long gat, NpeMissionType newMission, ISectorObject toTarget,
        Game game, Random random)
    {
        var (ships, cargo) = GetFleetComposition(fromWorld, power, gat, newMission);
        var fleet = FleetLifecycle.DeployFleet(emp, fromWorld, ships, cargo, toTarget.Location, game);

        if (FleetLifecycle.EstimatedDateOfArrival(fleet, game) > FleetLifecycle.EstimatedRange(fleet)) {
            CombatOutcome.AbortFleet(fleet, fromWorld, report: true);
            CombatOutcome.DestroyFleet(fleet, game);
            return;
        }

        fleetStates[fleet] = new KingdomFleetState { Mission = newMission, HomeBase = fromWorld, Target = toTarget, Waiting = 0 };

        if (toTarget.Owner != emp && !toTarget.Owner.IsIndependent) {
            // Pascal's FOR i:=1 TO Rnd(1,4) DO draws the bound once, at loop entry — re-evaluating
            // Rnd() on every iteration check (as a naive C# translation would) draws the RNG a
            // different number of times and picks a different probe count.
            var probesToLaunch = Rnd(random, 1, 4);
            for (var i = 0; i < probesToLaunch; i++) {
                emp.TryLaunchProbe(toTarget.Location);
            }
        }
    }

    /// <summary>
    /// DeployCargoFleet (NPEINTR.PAS:561-628) — composes a jumptransport/transport-only fleet sized to
    /// carry <paramref name="cargo"/> (clamped to what's actually on <paramref name="fromWorld"/> when
    /// <paramref name="carryCargo"/> is true, zeroed otherwise — Pascal's own VAR in/out semantics on
    /// <paramref name="cargo"/>, mutated in place here too), then launches it. Same EDA&gt;Range abort
    /// check and dropped "Slot=0" arm as <see cref="DeployBattleFleet"/>; no probe launch here (real
    /// Pascal doesn't send any for a cargo run).
    /// </summary>
    public static void DeployCargoFleet(
        Empire emp, Dictionary<Fleet, KingdomFleetState> fleetStates,
        IEconomicWorld fromWorld, CargoHold cargo, bool carryCargo, NpeMissionType newMission, ISectorObject toTarget,
        Game game)
    {
        var atBase = fromWorld.Ships;
        var cargoSpaceNeeded = -FleetLogistics.FleetCargoSpace(new ShipCounts(), cargo);

        var ships = new ShipCounts {
            Jumptransports = Math.Min(atBase.Jumptransports, PascalRound(cargoSpaceNeeded / FleetLogistics.JumptransportCargoAdjustment) + 1),
            Transports = Math.Min(atBase.Transports, cargoSpaceNeeded + 1),
        };

        var jtnCargo = PascalRound(ships.Jumptransports * FleetLogistics.JumptransportCargoAdjustment);
        if (jtnCargo < cargoSpaceNeeded && jtnCargo < ships.Transports) {
            ships.Jumptransports = 0;
        } else {
            ships.Transports = 0;
        }

        if (carryCargo) {
            var groundCargo = fromWorld.Cargo;
            foreach (var t in Enum.GetValues<CargoType>()) {
                cargo[t] = Math.Min(cargo[t], groundCargo[t]);
            }
        } else {
            foreach (var t in Enum.GetValues<CargoType>()) {
                cargo[t] = 0;
            }
        }

        FleetLogistics.BalanceFleet(ships, cargo);

        var fleet = FleetLifecycle.DeployFleet(emp, fromWorld, ships, cargo, toTarget.Location, game);

        if (FleetLifecycle.EstimatedDateOfArrival(fleet, game) > FleetLifecycle.EstimatedRange(fleet)) {
            CombatOutcome.AbortFleet(fleet, fromWorld, report: true);
            CombatOutcome.DestroyFleet(fleet, game);
            return;
        }

        fleetStates[fleet] = new KingdomFleetState { Mission = newMission, HomeBase = fromWorld, Target = toTarget, Waiting = 0 };
    }

    /// <summary>
    /// DeployJumpAttack (NPEINTR.PAS:784-828) — finds the best known enemy target and launches a
    /// battle fleet at it from the nearest regional capital, or another qualifying world if the
    /// capital itself lacks the ships.
    /// </summary>
    public static void DeployJumpAttack(
        Empire emp, Empire enemyEmp, IReadOnlyList<IEconomicWorld> regionCapitals, NpeCharacter persona,
        Dictionary<Fleet, KingdomFleetState> fleetStates, Game game, Random random)
    {
        var basePower = AverageMilitaryPower(regionCapitals);
        var candidates = game.Galaxy.Planets.Where(p => p.Owner == enemyEmp);
        var (target, targetDefense, targetMen) = GetBestTarget(emp, candidates, basePower, persona, fleetStates, random);
        if (target is null) {
            return;
        }

        var fleetPower = 30000L + PascalRound((random.NextDouble() + Rnd(random, 2, 5)) * targetDefense);
        var fleetGat = PascalRound(2.0 * targetMen);

        var homeBase = GetRegionalCapital(target, regionCapitals);
        if (homeBase is null) {
            return;
        }

        if (MilitaryPower(homeBase.Ships, new DefenseCounts()) > fleetPower / 2) {
            DeployBattleFleet(emp, fleetStates, homeBase, fleetPower, fleetGat, NpeMissionType.JumpAttack, target, game, random);
        } else {
            var betterBase = GetBestBase(emp, target, fleetPower, fleetGat, game);
            if (betterBase is not null) {
                DeployBattleFleet(emp, fleetStates, betterBase, fleetPower, fleetGat, NpeMissionType.JumpAttack, target, game, random);
            }
        }
    }

    /// <summary>DeployHKRaiders (NPEINTR.PAS:830-846) — sends a HunterKiller raiding fleet at the best available raider target (a gate, construction site, or planet — see <see cref="GetBestRaiderTarget"/>).</summary>
    public static void DeployHKRaiders(
        Empire emp, Empire enemyEmp, IReadOnlyList<IEconomicWorld> regionCapitals,
        Dictionary<Fleet, KingdomFleetState> fleetStates, Game game, Random random)
    {
        if (GetBestRaiderTarget(emp, enemyEmp, game, random) is not ISectorObject target) {
            return;
        }

        var homeBase = GetRegionalCapital(target, regionCapitals);
        if (homeBase is null) {
            return;
        }

        var fleetPower = (long)CombatConstants.CombatPower[AttackType.HunterKiller] * Rnd(random, 100, 5000);
        DeployBattleFleet(emp, fleetStates, homeBase, fleetPower, 0, NpeMissionType.RaidTransports, target, game, random);
    }

    /// <summary>
    /// DeploySlowAttack (NPEINTR.PAS:848-886) — the same shape as <see cref="DeployJumpAttack"/> with a
    /// bigger power budget. Real Pascal quirk, ported verbatim: the primary branch deploys with
    /// <see cref="NpeMissionType.SlowAttack"/>, but the fallback branch (nearest capital lacks ships,
    /// <see cref="GetBestBase"/> finds another world) deploys with <see cref="NpeMissionType.JumpAttack"/>
    /// instead (NPEINTR.PAS:883) — an adjacent-branch copy/paste slip in the original source, not a
    /// transcription error here. See docs/PASCAL_ARCHITECTURE_NOTES.md.
    /// </summary>
    public static void DeploySlowAttack(
        Empire emp, Empire enemyEmp, IReadOnlyList<IEconomicWorld> regionCapitals, NpeCharacter persona,
        Dictionary<Fleet, KingdomFleetState> fleetStates, Game game, Random random)
    {
        var basePower = AverageMilitaryPower(regionCapitals);
        var candidates = game.Galaxy.Planets.Where(p => p.Owner == enemyEmp);
        var (target, targetDefense, targetMen) = GetBestTarget(emp, candidates, basePower, persona, fleetStates, random);
        if (target is null) {
            return;
        }

        var fleetPower = 50000L + PascalRound((random.NextDouble() + Rnd(random, 2, 5)) * targetDefense);
        var fleetGat = PascalRound(2.0 * targetMen);

        var homeBase = GetRegionalCapital(target, regionCapitals);
        if (homeBase is null) {
            return;
        }

        if (MilitaryPower(homeBase.Ships, new DefenseCounts()) > fleetPower / 2) {
            DeployBattleFleet(emp, fleetStates, homeBase, fleetPower, fleetGat, NpeMissionType.SlowAttack, target, game, random);
        } else {
            var betterBase = GetBestBase(emp, target, fleetPower, fleetGat, game);
            if (betterBase is not null) {
                DeployBattleFleet(emp, fleetStates, betterBase, fleetPower, fleetGat, NpeMissionType.JumpAttack, target, game, random); // verbatim quirk, see doc comment above
            }
        }
    }

    /// <summary>SetFleetReturn (NPEINTR.PAS:1062-1079) — points a fleet back toward its home base. Real Pascal's own <c>Emp</c> parameter is confirmed unused (only FltID/BaseID/FleetData are ever read) — dropped here.</summary>
    public static void SetFleetReturn(Fleet fleet, IEconomicWorld homeBase, Dictionary<Fleet, KingdomFleetState> fleetStates)
    {
        var state = fleetStates[fleet];
        state.Mission = NpeMissionType.Return;
        state.Target = homeBase;
        FleetLifecycle.SetFleetDestination(fleet, homeBase.Location);
    }

    /// <summary>
    /// SetRaidingFleetNewTarget (NPEINTR.PAS:1081-1124) — a raiding fleet presses on to a fresh target
    /// or heads home, biased by <see cref="NpeCharacter.Offensive"/>. Called by
    /// <see cref="KingdomTurnHandler"/>'s own per-turn fleet dispatch.
    /// </summary>
    public static void SetRaidingFleetNewTarget(
        Empire emp, Fleet fleet, IEconomicWorld target, IEconomicWorld homeBase,
        Dictionary<Fleet, KingdomFleetState> fleetStates, NpeCharacter persona, Game game, Random random)
    {
        var enemyEmp = target.Owner;

        if (Rnd(random, 1, 100) <= persona.Offensive && enemyEmp != emp) {
            var fleetPower = MilitaryPower(fleet.Ships, new DefenseCounts());
            var candidates = game.Galaxy.Planets.Where(p => p.Owner == enemyEmp);
            var (newTarget, _, _) = GetBestTarget(emp, candidates, fleetPower, persona, fleetStates, random);
            if (newTarget is not null) {
                FleetLifecycle.SetFleetDestination(fleet, newTarget.Location);
                var state = fleetStates[fleet];
                state.Mission = NpeMissionType.JumpAttack;
                state.Target = newTarget;
            } else {
                SetFleetReturn(fleet, homeBase, fleetStates);
            }
        } else {
            SetFleetReturn(fleet, homeBase, fleetStates);
        }
    }

    /// <summary>
    /// DestroyAllFleetsInSector (NPEINTR.PAS:1126-1162) — engages every other-empire fleet
    /// <paramref name="fleet"/> currently shares a sector with, weaker-than-half-power ones only.
    /// Iterates the galaxy's fleets in list order rather than Pascal's fixed slot-index order — this
    /// port's own accepted iteration-order gap for scan-dependent RNG (see this class's own doc
    /// comment on the hardcoded-test ground-truth split).
    /// </summary>
    public static bool DestroyAllFleetsInSector(Empire emp, Fleet fleet, long fleetPower, Game game, Random random)
    {
        var allDestroyed = true;
        var emptyDefenses = new DefenseCounts();

        foreach (var enemy in game.Galaxy.Fleets.Where(f => f.Location == fleet.Location).ToList()) {
            if (enemy.Owner == emp || !Game.Scouted(emp, enemy)) {
                continue;
            }

            if (fleetPower > MilitaryPower(enemy.Ships, emptyDefenses) / 2) {
                var result = CombatResolution.NPEAttack(emp, fleet, enemy, AttackIntentionType.CaptureTransports, game, random);
                if (result.Result != AttackResultType.DefenderConquered) {
                    allDestroyed = false;
                }
            } else {
                allDestroyed = false;
            }
        }

        return allDestroyed;
    }

    /// <summary>ImplementReturnMSN (NPEINTR.PAS:1164-1168) — the fleet has arrived home; merge it into its target and dissolve it.</summary>
    public static void ImplementReturnMSN(Fleet fleet, IEconomicWorld target, Game game)
    {
        CombatOutcome.AbortFleet(fleet, target, report: true);
        CombatOutcome.DestroyFleet(fleet, game);
    }

    /// <summary>ImplementSupplyMSN (NPEINTR.PAS:1170-1189) — dumps the fleet's entire cargo onto the target (all types, unconditionally — MoveThings called with NoOfThg equal to the source's own current amount always takes its "move everything" branch), then heads home.</summary>
    public static void ImplementSupplyMSN(Fleet fleet, IEconomicWorld target, IEconomicWorld homeBase, Dictionary<Fleet, KingdomFleetState> fleetStates, Game game)
    {
        foreach (var t in Enum.GetValues<CargoType>()) {
            target.Cargo[t] = ClampResource(target.Cargo[t] + fleet.Cargo[t]);
            fleet.Cargo[t] = 0;
        }

        FleetLifecycle.ChangeCompositionOfFleet(fleet, target, fleet.Ships, fleet.Cargo, target.Ships, target.Cargo, game);

        SetFleetReturn(fleet, homeBase, fleetStates);
    }

    /// <summary>
    /// ImplementRefuelMSN (NPEINTR.PAS:1191-1209) — the fleet dissolves into <paramref name="target"/>
    /// (same as <see cref="ImplementReturnMSN"/>), which then converts as much of its own trillum into
    /// fuel as it needs (capped at what's on hand). <paramref name="target"/>'s dynamic type is
    /// ambiguous at this method's own source (depends on 6d-level mission-assignment logic not built
    /// yet) — see <see cref="FleetLifecycle.RefuelFleet"/>'s own doc comment for what happens when it's
    /// not a <see cref="Fleet"/>.
    /// </summary>
    public static void ImplementRefuelMSN(Fleet fleet, IShipCargoHolder target, Game game)
    {
        CombatOutcome.AbortFleet(fleet, target, report: true);
        CombatOutcome.DestroyFleet(fleet, game);

        var targetFuel = target is Fleet targetFleet ? targetFleet.Fuel : 0;
        var maxFuel = FleetLogistics.FuelCapacity(target.Ships);
        var tonsNeeded = (int)((maxFuel - targetFuel) / FleetLogistics.FuelPerTon) + 1;
        var maxTri = Math.Min(tonsNeeded, target.Cargo.Trillum);

        FleetLifecycle.RefuelFleet(target, target, maxTri);
    }

    /// <summary>
    /// ImplementConquerMSN (NPEINTR.PAS:1211-1240) — attacks an undefended independent target
    /// outright; otherwise a 25% chance per turn to give up and head home while waiting the enemy out.
    /// </summary>
    public static AttackResultType ImplementConquerMSN(
        Empire emp, Fleet fleet, IEconomicWorld target, IEconomicWorld homeBase,
        Dictionary<Fleet, KingdomFleetState> fleetStates, Game game, Random random)
    {
        var otherFleetsAtTarget = game.Galaxy.Fleets.Any(f => f.Location == target.Location && f.Owner != fleet.Owner);

        AttackResultType result;
        var shouldReturn = false;

        if (!otherFleetsAtTarget && target.Owner.IsIndependent) {
            result = CombatResolution.NPEAttack(emp, fleet, target, AttackIntentionType.Conquer, game, random).Result;
            shouldReturn = true;
        } else {
            if (Rnd(random, 1, 4) == 1) {
                shouldReturn = true;
            }
            result = AttackResultType.AttackerRetreats;
        }

        if (shouldReturn) {
            SetFleetReturn(fleet, homeBase, fleetStates);
        }

        return result;
    }

    /// <summary>
    /// ImplementRaidTrnMSN (NPEINTR.PAS:1242-1282) — clears the sector of weaker enemy fleets, destroys
    /// whatever construction site/stargate sits there if the sector's fully cleared, then either heads
    /// home after 5 turns of loitering or keeps waiting — stripping all booty every turn either way.
    /// Real Pascal's own <c>TargetID</c> parameter is confirmed unused: <c>GetObject(TargetXY,TargetID)</c>
    /// overwrites it with whatever's at the fleet's own location before it's ever read — dropped here.
    /// </summary>
    public static void ImplementRaidTrnMSN(Empire emp, Fleet fleet, IEconomicWorld homeBase, Dictionary<Fleet, KingdomFleetState> fleetStates, Game game, Random random)
    {
        var fleetPower = MilitaryPower(fleet.Ships, new DefenseCounts());
        var allFleetsDestroyed = DestroyAllFleetsInSector(emp, fleet, fleetPower, game, random);

        var objectHere = game.Galaxy.GetObjectAt(fleet.Location);
        if (allFleetsDestroyed && objectHere is ConstructionSite or Stargate) {
            CombatResolution.NPEAttack(emp, fleet, objectHere, AttackIntentionType.DestroyTransports, game, random);
        }

        var state = fleetStates[fleet];
        if (state.Waiting == 5) {
            SetFleetReturn(fleet, homeBase, fleetStates);
        } else {
            state.Waiting++;
        }

        // destroy booty (NPEINTR.PAS:1276-1281): every ship type except HunterKiller, and all cargo,
        // gets stripped every turn a raiding fleet lingers — it can't hold onto captured loot.
        foreach (var t in Enum.GetValues<ShipType>()) {
            if (t != ShipType.HunterKiller) {
                fleet.Ships[t] = 0;
            }
        }
        foreach (var t in Enum.GetValues<CargoType>()) {
            fleet.Cargo[t] = 0;
        }
    }

    /// <summary>
    /// ImplementJumpAttackMSN (NPEINTR.PAS:1284-1337) — clears the sector, optionally softens the
    /// target with a LAM strike launched from <paramref name="homeBase"/> (within 5 sectors, at least
    /// 500 LAMs on hand), then attacks if the fleet still outguns what's left.
    /// </summary>
    public static AttackResultType ImplementJumpAttackMSN(Empire emp, Fleet fleet, IEconomicWorld target, IEconomicWorld homeBase, Game game, Random random)
    {
        var fleetPower = MilitaryPower(fleet.Ships, new DefenseCounts());
        var allFleetsDestroyed = DestroyAllFleetsInSector(emp, fleet, fleetPower, game, random);

        if (!allFleetsDestroyed || target.Owner == emp) {
            return AttackResultType.None;
        }

        // Pascal snapshots GetShips(TargetID,EnemySh) here (NPEINTR.PAS:1309), before any LAM strike.
        var enemyShips = target.Ships;

        if (homeBase.Defenses[DefenseType.Lam] > 500 && homeBase.Location.DistanceTo(target.Location) <= 5) {
            var lamsToUse = Math.Min(
                homeBase.Defenses[DefenseType.Lam],
                2 * target.Defenses[DefenseType.DefenseSatellite] + target.Defenses[DefenseType.IonCannon] + target.Defenses[DefenseType.Gdm] / 2);
            var (shipsDestroyed, _) = CombatStandalone.LAMAttack(emp, lamsToUse, target, game);
            homeBase.Defenses[DefenseType.Lam] -= lamsToUse;

            // Verbatim Pascal quirk (NPEINTR.PAS:1318, 1325): EnemySh is passed as LAMAttack's VAR
            // ShipsDest out-param. TargetID here is always a world, and LAMAttack's world branch
            // never writes ShipsDest (only fleet targets do) — so EnemySh comes back as LAMAttack's
            // own zeroed local, clobbering the real enemy ship count read a moment earlier. The
            // final power comparison below sees zero enemy ships whenever the LAM branch fires,
            // regardless of what the world's real ship count is.
            enemyShips = shipsDestroyed;
        }

        // MilitaryPower(Sh,Df) with Df zeroed — the fleet's own defenses is a local scratch var real
        // Pascal never populates before this comparison, same verbatim quirk as AverageMilitaryPower/
        // GetBestBase's own doc comments.
        if (MilitaryPower(fleet.Ships, new DefenseCounts()) > MilitaryPower(enemyShips, target.Defenses) / 2) {
            return CombatResolution.NPEAttack(emp, fleet, target, AttackIntentionType.Conquer, game, random).Result;
        }

        return AttackResultType.None;
    }

    /// <summary>
    /// ImplementStackMSN (NPEINTR.PAS:1339-1422) — dumps as much of the fleet as fits onto every
    /// same-empire guard fleet already in its sector; if it still has ships left and there's room
    /// under <see cref="MaxNoOfGuards"/>, it becomes a guard itself, else it's redirected to protect
    /// whatever planet needs it most.
    /// </summary>
    public static void ImplementStackMSN(Fleet fleet, Dictionary<Fleet, KingdomFleetState> fleetStates, Game game)
    {
        var emp = fleet.Owner;
        var guardsHere = fleetStates
            .Where(kv => !ReferenceEquals(kv.Key, fleet) && kv.Key.Owner == emp && kv.Key.Location == fleet.Location && kv.Value.Mission == NpeMissionType.Guard)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var guard in guardsHere) {
            DumpStuff(fleet, guard, game);
        }

        if (game.Galaxy.Fleets.Contains(fleet) && guardsHere.Count < MaxNoOfGuards) {
            fleetStates[fleet].Mission = NpeMissionType.Guard;
        } else {
            var newWorld = GetBestPlanetToProtect(fleet, game);
            if (newWorld is not null) {
                SetFleetReturn(fleet, newWorld, fleetStates);
            }
        }
    }

    /// <summary>
    /// DumpStuff (NPEINTR.PAS's own nested procedure inside ImplementStackMSN) — transfers up to 9999
    /// of each ship type (every type but HunterKiller) from <paramref name="fleet"/> onto
    /// <paramref name="guard"/>, dragging along a proportional share of troops for jumptransports and
    /// transports. Two asymmetric unit-conversion paths, ported verbatim: the jumptransport branch
    /// treats the transferred ship count as directly comparable to a troop count with no CargoSpace
    /// conversion, while the transport branch correctly divides by CargoSpace[Legion] — a real
    /// inconsistency between the two branches in source, not a transcription slip here.
    /// </summary>
    private static void DumpStuff(Fleet fleet, Fleet guard, Game game)
    {
        var fleetShips = new ShipCounts();
        var guardShips = new ShipCounts();
        var fleetCargo = new CargoHold();
        var guardCargo = new CargoHold();
        foreach (var t in Enum.GetValues<ShipType>()) {
            fleetShips[t] = fleet.Ships[t];
            guardShips[t] = guard.Ships[t];
        }
        foreach (var t in Enum.GetValues<CargoType>()) {
            fleetCargo[t] = fleet.Cargo[t];
            guardCargo[t] = guard.Cargo[t];
        }

        var legionSpace = FleetLogistics.CargoSpacePerUnit[CargoType.Legion];

        foreach (var t in Enum.GetValues<ShipType>()) {
            if (t == ShipType.HunterKiller) {
                continue;
            }

            var trans = fleetShips[t] + guardShips[t] < 9999 ? fleetShips[t] : 9999 - guardShips[t];
            fleetShips[t] -= trans;
            guardShips[t] += trans;

            if (t == ShipType.Jumptransport) {
                var gatTrn = Math.Min(trans, fleetCargo[CargoType.Legion]);
                fleetCargo[CargoType.Legion] -= gatTrn;
                guardCargo[CargoType.Legion] = Math.Min(9999, guardCargo[CargoType.Legion] + gatTrn);
                trans -= gatTrn;

                gatTrn = Math.Min(trans, fleetCargo[CargoType.NinjaLegion]);
                fleetCargo[CargoType.NinjaLegion] -= gatTrn;
                guardCargo[CargoType.NinjaLegion] = Math.Min(9999, guardCargo[CargoType.NinjaLegion] + gatTrn);
            } else if (t == ShipType.Transport) {
                var gatTrn = Math.Min(trans, legionSpace * fleetCargo[CargoType.Legion]);
                fleetCargo[CargoType.Legion] -= gatTrn;
                guardCargo[CargoType.Legion] = Math.Min(9999, guardCargo[CargoType.Legion] + gatTrn);
                trans = Math.Max(0, trans - gatTrn / legionSpace);

                gatTrn = Math.Min(trans, fleetCargo[CargoType.NinjaLegion]);
                fleetCargo[CargoType.NinjaLegion] -= gatTrn;
                guardCargo[CargoType.NinjaLegion] = Math.Min(9999, guardCargo[CargoType.NinjaLegion] + gatTrn);
            }
        }

        FleetLifecycle.ChangeCompositionOfFleet(fleet, guard, fleetShips, fleetCargo, guardShips, guardCargo, game);
    }

    /// <summary>
    /// ImplementGuardMSN (NPEINTR.PAS:1424-1456) — merges as much of the fleet as fits (capped at 9000
    /// on the base's side, 9999 on the fleet's own remaining side) into its guarded base; cargo is
    /// untouched, only ships transfer.
    /// </summary>
    public static void ImplementGuardMSN(Fleet fleet, IEconomicWorld baseWorld, Game game)
    {
        var newFleetShips = new ShipCounts();
        var newBaseShips = new ShipCounts();
        foreach (var t in Enum.GetValues<ShipType>()) {
            newFleetShips[t] = fleet.Ships[t];
            newBaseShips[t] = baseWorld.Ships[t];
        }

        foreach (var t in Enum.GetValues<ShipType>()) {
            if (t == ShipType.HunterKiller) {
                continue;
            }

            var trans = newBaseShips[t] + newFleetShips[t] <= 9000 ? newFleetShips[t] : 9000 - newBaseShips[t];
            if (newFleetShips[t] - trans > 9999) {
                trans = newFleetShips[t] - 9999;
            }

            newBaseShips[t] = ClampResource(newBaseShips[t] + trans);
            newFleetShips[t] = ClampResource(newFleetShips[t] - trans);
        }

        FleetLifecycle.ChangeCompositionOfFleet(fleet, baseWorld, newFleetShips, fleet.Cargo, newBaseShips, baseWorld.Cargo, game);
    }

    // --- NPE00.PAS: defense, expansion, logistics, exploration, diplomacy ------------------------
    // Confirmed shared with Pirate (NPE01.PAS) and Berserker (NPE04.PAS) call sites, not
    // Kingdom-exclusive — same "shared toolkit" reasoning as NPEINTR.PAS above, so these live here
    // rather than on KingdomTurnHandler.

    /// <summary>
    /// AttackEnemyFleets (NPE00.PAS's own nested procedure inside DefendEmpire) — LAM-strikes then
    /// directly attacks any weaker enemy fleet sitting over <paramref name="world"/>, using a
    /// one-shot battle fleet dissolved back into <paramref name="world"/>'s stock afterward (not a
    /// tracked mission — this is same-turn combat, not a deployed fleet). Verbatim quirks: the LAM
    /// strike zeroes the base's entire LAM stock (not just what was used, unlike
    /// ImplementJumpAttackMSN's subtract-what-was-used), and the post-strike military-power
    /// comparisons deliberately reuse <c>mPower</c> computed *before* the strike, not a fresh read.
    /// </summary>
    private static void AttackEnemyFleets(Empire emp, IEconomicWorld world, IEconomicWorld baseWorld, Game game, Random random)
    {
        var xy = world.Location;

        foreach (var enemyFleet in game.Galaxy.Fleets.Where(f => f.Owner != emp && Game.Scouted(emp, f) && f.Location == xy).ToList()) {
            var mPower = MilitaryPower(enemyFleet.Ships, new DefenseCounts());

            if (mPower > 30000 && baseWorld.Location.DistanceTo(xy) <= 5) {
                var lam = baseWorld.Defenses[DefenseType.Lam];
                if (lam > 500 && Rnd(random, 1, 100) < 50) {
                    CombatStandalone.LAMAttack(emp, lam, enemyFleet, game);
                    baseWorld.Defenses[DefenseType.Lam] = 0;
                }
            }

            if (!game.Galaxy.Fleets.Contains(enemyFleet)) {
                continue; // ASSERT (NPE00.PAS): enemy fleet not destroyed by LAMs.
            }

            var (ships, cargo) = GetFleetComposition(world, mPower * 2, 0, NpeMissionType.JumpAttack);
            if (!FleetLifecycle.NoShips(ships) && (MilitaryPower(ships, world.Defenses) < mPower || MilitaryPower(ships, new DefenseCounts()) > mPower)) {
                var battleFleet = FleetLifecycle.DeployFleet(emp, world, ships, cargo, xy, game);
                var result = CombatResolution.NPEAttack(emp, battleFleet, enemyFleet, AttackIntentionType.CaptureTransports, game, random).Result;
                if (result != AttackResultType.AttackerDestroyed) {
                    CombatOutcome.AbortFleet(battleFleet, world, report: true);
                    CombatOutcome.DestroyFleet(battleFleet, game);
                }
            }
        }
    }

    /// <summary>NoOfGuardsAtBase (NPE00.PAS's own nested procedure inside DefendEmpire) — how many of this empire's own tracked fleets are guarding the given base right now.</summary>
    private static int NoOfGuardsAtBase(Empire emp, IEconomicWorld baseWorld, Dictionary<Fleet, KingdomFleetState> fleetStates) =>
        fleetStates.Count(kv => kv.Key.Owner == emp && kv.Value.Mission == NpeMissionType.Guard && kv.Key.Location == baseWorld.Location);

    /// <summary>GetBestBaseToProtect (NPE00.PAS's own nested procedure inside DefendEmpire) — the regional capital (other than <paramref name="fromWorld"/>) with the fewest guard fleets on station.</summary>
    private static IEconomicWorld? GetBestBaseToProtect(Empire emp, IReadOnlyList<IEconomicWorld> regionCapitals, Dictionary<Fleet, KingdomFleetState> fleetStates, IEconomicWorld fromWorld)
    {
        IEconomicWorld? best = null;
        var lowest = MaxNoOfGuards;

        foreach (var candidate in regionCapitals) {
            if (ReferenceEquals(candidate, fromWorld)) {
                continue;
            }
            var guards = NoOfGuardsAtBase(emp, candidate, fleetStates);
            if (guards < lowest) {
                lowest = guards;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// DefendEmpire (NPE00.PAS:198-404) — attacks any enemy fleet sitting over an owned world, then
    /// reinforces worlds that are under the persona's own minimum defense, sends a non-base world's
    /// surplus ships home to its regional capital, and redirects an overfull base's own surplus to
    /// whichever planet/base needs it most.
    /// </summary>
    public static void DefendEmpire(Empire emp, IReadOnlyList<IEconomicWorld> regionCapitals, Dictionary<Fleet, KingdomFleetState> fleetStates, NpeCharacter persona, Game game, Random random)
    {
        foreach (var world in game.Galaxy.Planets.Where(p => p.Owner == emp).ToList()) {
            if (world.Type is WorldType.Base or WorldType.Capital) {
                AttackEnemyFleets(emp, world, world, game, random);

                if (NoOfGuardsAtBase(emp, world, fleetStates) == MaxNoOfGuards) {
                    var protectWorld = GetBestPlanetToProtect(world, game);
                    if (protectWorld is not null) {
                        DeployBattleFleet(emp, fleetStates, world, 9000L * CombatConstants.CombatPower[AttackType.Fighter], 0, NpeMissionType.Return, protectWorld, game, random);
                    }

                    var protectBase = GetBestBaseToProtect(emp, regionCapitals, fleetStates, world);
                    if (protectBase is not null) {
                        DeployBattleFleet(emp, fleetStates, world, 150000, 0, NpeMissionType.Stack, protectBase, game, random);
                    }
                }

                continue;
            }

            var (ships, cargo) = GetPotentialRes(world, fleetStates);
            var powerAvail = MilitaryPower(ships, world.Defenses);
            var shipPower = MilitaryPower(ships, new DefenseCounts());
            var optimumPower = MinimumDefense(world, persona, game);
            var optimumGat = optimumPower / 20;
            var gatPower = cargo[CargoType.Legion] + 3L * cargo[CargoType.NinjaLegion];
            var homeBase = GetRegionalCapital(world, regionCapitals);
            if (homeBase is null) {
                continue; // real Pascal's GetRegionalCapital leaves BaseID undefined here (no regional capitals at all yet) — nothing sane to do.
            }

            AttackEnemyFleets(emp, world, homeBase, game, random);

            var powerDiff = optimumPower - powerAvail;
            var gatDiff = optimumGat - gatPower;
            if (Math.Min(shipPower, Math.Abs(powerDiff)) > 10000) {
                if (powerAvail < optimumPower) {
                    var maxBaseGat = Math.Max(0, homeBase.Cargo[CargoType.Legion] + 3L * homeBase.Cargo[CargoType.NinjaLegion] - 3000);
                    gatDiff = Math.Min(maxBaseGat, Math.Max(0, gatDiff));
                    DeployBattleFleet(emp, fleetStates, homeBase, powerDiff, gatDiff, NpeMissionType.Return, world, game, random);
                } else if (NoOfGuardsAtBase(emp, homeBase, fleetStates) < MaxNoOfGuards) {
                    gatDiff = Math.Max(0, -gatDiff);
                    DeployBattleFleet(emp, fleetStates, world, -powerDiff, gatDiff, NpeMissionType.Stack, homeBase, game, random);
                }
            }
        }
    }

    /// <summary>ModifyPersona (NPE00.PAS's own nested procedure inside ImperialExpansion) — evolves Imperialist toward ImpGene by a RandomGene-gated random walk.</summary>
    private static void ModifyPersona(NpeCharacter persona, Random random)
    {
        if (Rnd(random, 1, 100) > persona.RandomGene) {
            return;
        }

        var delta = Jitter(random, persona.ImperialistGene, persona.FactorGene) - Jitter(random, persona.Imperialist, persona.FactorGene);
        var temp = persona.Imperialist + PascalRound(delta / 12.0 * persona.FactorGene);

        persona.Imperialist = temp switch {
            > 100 => 100,
            < 0 => 0,
            _ => temp,
        };
    }

    /// <summary>
    /// ImperialExpansion (NPE00.PAS:406-509) — rolls a persona-driven chance to conquer an
    /// independent world within reach of a regional capital; if it fires, picks the best known
    /// target and launches one battle fleet at it. Always evolves Imperialist afterward, whether or
    /// not the roll fired.
    /// </summary>
    public static void ImperialExpansion(Empire emp, IReadOnlyList<IEconomicWorld> regionCapitals, Dictionary<Fleet, KingdomFleetState> fleetStates, NpeCharacter persona, Game game, Random random)
    {
        if (Rnd(random, 1, 100) <= persona.Imperialist) {
            var basePower = AverageMilitaryPower(regionCapitals);
            var threshold = 2 + 10 - persona.SphereX / 10;

            var candidates = new List<IEconomicWorld>();
            foreach (var planet in game.Galaxy.Planets) {
                if (!planet.Owner.IsIndependent) {
                    continue;
                }
                var nearestBase = GetRegionalCapital(planet, regionCapitals);
                if (nearestBase is not null && planet.Location.DistanceTo(nearestBase.Location) <= threshold) {
                    candidates.Add(planet);
                }
            }

            var (target, targetDefense, targetMen) = GetBestTarget(emp, candidates, basePower, persona, fleetStates, random);
            if (target is not null) {
                var fleetPower = PascalRound((1.5 + random.NextDouble()) * targetDefense);
                var fleetGat = PascalRound(1.5 * targetMen);
                var homeBase = GetRegionalCapital(target, regionCapitals);
                if (homeBase is not null && MilitaryPower(homeBase.Ships, new DefenseCounts()) > targetDefense) {
                    DeployBattleFleet(emp, fleetStates, homeBase, fleetPower, fleetGat, NpeMissionType.Conquer, target, game, random);
                }
            }
        }

        ModifyPersona(persona, random);
    }

    /// <summary>NPEConquest (NPE00.PAS:674-693) — redesignates a world this empire just conquered, per its own persona. Real Pascal's trailing GetCoord/GetRegionalCapital calls compute values never read again — dead, dropped here.</summary>
    public static void NPEConquest(Empire emp, IEconomicWorld world, AttackResultType result, IReadOnlyList<IEconomicWorld> regionCapitals, NpeCharacter persona, Game game, Random random)
    {
        if (result != AttackResultType.DefenderConquered || world.Owner != emp) {
            return;
        }

        var newType = GetNewDesignation(world, regionCapitals, game, random);
        if (newType != world.Type) {
            // WorldDesignation.Redesignate: GetNewDesignation's own Chance[CapTyp] starts at
            // NpeConstants.TypeDefault[Capital]=0 and nothing later multiplies it back up, so NewType
            // is never Capital here either -- confirmed, not assumed.
            WorldDesignation.Redesignate(world, newType, random);
        }
    }

    /// <summary>
    /// GetClosestCargoWorld (NPE00.PAS:603-642) — the nearest owned world holding at least as much
    /// of every requested cargo type as <paramref name="required"/> asks for. A closer world that
    /// falls short doesn't block a farther one that qualifies — real Pascal only ever advances
    /// ClosestDist on an accepted candidate.
    /// </summary>
    private static IEconomicWorld? GetClosestCargoWorld(Empire emp, ISectorObject subject, CargoHold required, Game game)
    {
        IEconomicWorld? closest = null;
        var closestDist = 99;

        foreach (var planet in game.Galaxy.Planets) {
            if (planet.Owner != emp) {
                continue;
            }
            var dist = planet.Location.DistanceTo(subject.Location);
            if (dist < closestDist) {
                var cargo = planet.Cargo;
                if (Enum.GetValues<CargoType>().All(t => cargo[t] >= required[t])) {
                    closest = planet;
                    closestDist = dist;
                }
            }
        }

        return closest;
    }

    /// <summary>
    /// CargoSupplyFleet (NPE00.PAS:644-672) — finds the nearest owned world holding enough of
    /// <paramref name="cargo"/> and either ships it straight to <paramref name="destination"/>, or,
    /// if that world doesn't have the transports to spare, first routes empty transports there from
    /// its own regional capital.
    /// </summary>
    public static void CargoSupplyFleet(Empire emp, IEconomicWorld destination, CargoHold cargo, IReadOnlyList<IEconomicWorld> regionCapitals, Dictionary<Fleet, KingdomFleetState> fleetStates, Game game)
    {
        var cargoWorld = GetClosestCargoWorld(emp, destination, cargo, game);
        if (cargoWorld is null) {
            return;
        }

        if (FleetLogistics.FleetCargoSpace(cargoWorld.Ships, cargo) > 0) {
            if (!AlreadyTargetted(fleetStates, destination, NpeMissionType.Supply)) {
                DeployCargoFleet(emp, fleetStates, cargoWorld, cargo, carryCargo: true, NpeMissionType.Supply, destination, game);
            }
        } else if (!AlreadyTargetted(fleetStates, cargoWorld, NpeMissionType.SupplyTransports)) {
            var homeBase = GetRegionalCapital(cargoWorld, regionCapitals);
            if (homeBase is not null) {
                DeployCargoFleet(emp, fleetStates, homeBase, cargo, carryCargo: false, NpeMissionType.SupplyTransports, cargoWorld, game);
            }
        }
    }

    /// <summary>RNIndustryLack (NPE00.PAS:59-69) — ReviewNews's IndLack handler: request 1000 metals for whichever world reported the shortfall.</summary>
    private static void RNIndustryLack(Empire emp, IEconomicWorld world, IReadOnlyList<IEconomicWorld> regionCapitals, Dictionary<Fleet, KingdomFleetState> fleetStates, Game game) =>
        CargoSupplyFleet(emp, world, new CargoHold { Metals = 1000 }, regionCapitals, fleetStates, game);

    /// <summary>SendRescueFleet (NPE00.PAS's own nested procedure inside ReviewNews) — ReviewNews's NoFuel handler: if the fleet's own regional capital can spare the trillum, sends it enough fuel to get moving again.</summary>
    private static void SendRescueFleet(Empire emp, Fleet fleet, IReadOnlyList<IEconomicWorld> regionCapitals, Dictionary<Fleet, KingdomFleetState> fleetStates, Game game)
    {
        var homeBase = GetRegionalCapital(fleet, regionCapitals);
        if (homeBase is null) {
            return;
        }

        var fuelNeeded = 1 + PascalRound(FleetLogistics.FuelCapacity(fleet.Ships) / FleetLogistics.FuelPerTon);
        var jtnNeeded = 1 + PascalRound(fuelNeeded / 10.0);

        if (homeBase.Ships.Jumptransports > jtnNeeded && homeBase.Cargo.Trillum > fuelNeeded) {
            DeployCargoFleet(emp, fleetStates, homeBase, new CargoHold { Trillum = fuelNeeded }, carryCargo: true, NpeMissionType.Refuel, fleet, game);
        }
    }

    /// <summary>
    /// AttackSeverity (ReviewNews's own nested function, NPE00.PAS:118-135) — how bad a single attack
    /// report was, 10-100, scored off the total power of every ship/defense/troop type destroyed in
    /// the DestructionDetail entries that immediately follow the headline in the news list (real
    /// Pascal's linked-list <c>News^.Next</c> walk collapses to a forward index scan here, since
    /// <see cref="Empire.News"/> is a plain list in insertion order — the same order ReportLosses
    /// appends its own detail entries in). <see cref="CombatConstants.MPower"/>, not
    /// <see cref="CombatConstants.CombatPower"/> — see that field's own doc comment.
    /// </summary>
    private static int AttackSeverity(IReadOnlyList<NewsItem> news, int headlineIndex, long basePower)
    {
        long total = 1;
        for (var i = headlineIndex + 1; i < news.Count && news[i].Headline == NewsType.DestructionDetail; i++) {
            var attackType = news[i].Resource!.ToAttackType();
            total += news[i].Parm1 * CombatConstants.MPower[attackType];
        }

        if (basePower == 0) {
            basePower = 1;
        }

        return (int)Math.Min(100, 10 + PascalRound(50.0 * total / basePower));
    }

    /// <summary>
    /// RespondToEnemyAttack (ReviewNews's own nested procedure, NPE00.PAS:81-116) — escalates this
    /// enemy's Policy tier and raises Aggressiveness in response to one attack report. Each Policy
    /// arm's condition chain is an ELSE-IF ladder, ported as one <c>&amp;&amp;</c>/ternary chain per
    /// arm rather than a nested if, matching the established short-circuit-evaluation-order convention
    /// (docs/PORT_DESIGN.md) so the Rnd draw count/order stays identical to source.
    /// </summary>
    private static void RespondToEnemyAttack(StateDeptRecord state, NpeCharacter persona, int severity, Random random)
    {
        state.Policy = state.Policy switch {
            PolicyType.Neutral => severity > 50 && Rnd(random, 1, 100) < persona.Provoke ? PolicyType.Preempt : PolicyType.Harass,
            PolicyType.Harass when severity > 50 && Rnd(random, 1, 100) < persona.Provoke => PolicyType.Conflict,
            PolicyType.Harass when Rnd(random, 1, 100) < persona.Provoke => PolicyType.Preempt,
            PolicyType.Preempt when severity > 35 && Rnd(random, 1, 100) <= persona.Provoke => PolicyType.Conflict,
            PolicyType.Conflict when severity > 75 && Rnd(random, 1, 100) <= persona.Provoke / 2 => PolicyType.War,
            _ => state.Policy,
        };

        var aggInc = 10 + PascalRound(severity / 5.0);
        if (state.Aggressiveness + aggInc > 100) {
            state.Aggressiveness = 100;
        } else if (aggInc > 0 && state.Aggressiveness + aggInc < 35) {
            state.Aggressiveness = 35;
        } else {
            state.Aggressiveness += aggInc;
        }
    }

    /// <summary>
    /// ReviewNews (NPE00.PAS:71-196). Its Balance decrement only special-cases BattleL: real Pascal
    /// checks <c>Loc1.ID.ObjTyp IN [Pln,Base,Gate]</c> against the conquered object's own type; this
    /// port's WorldConqueredByEnemy headline is only ever raised with an <see cref="IEconomicWorld"/>
    /// subject (never a Fleet, Gate, or ConstructionSite — confirmed by reading every AddNews call
    /// site for it in <see cref="CombatOutcome"/>), so <c>news.Subject is IEconomicWorld</c> is the
    /// exact same condition, not an approximation.
    /// </summary>
    public static void ReviewNews(
        Empire emp, IReadOnlyList<IEconomicWorld> regionCapitals, Dictionary<Fleet, KingdomFleetState> fleetStates,
        NpeCharacter persona, Dictionary<Empire, StateDeptRecord> state, PolicyType defaultPolicy, Game game, Random random)
    {
        var basePower = AverageMilitaryPower(regionCapitals);

        for (var i = 0; i < emp.News.Count; i++) {
            var news = emp.News[i];
            switch (news.Headline) {
                case NewsType.WorldConqueredByEnemy:
                case NewsType.EnemyEmpireDestroyed:
                case NewsType.EnemyEmpireRetreated:
                case NewsType.ConstructionSiteDestroyed:
                case NewsType.StargateDestroyed:
                case NewsType.FleetDamagedByLams:
                case NewsType.FleetDestroyedByLams:
                case NewsType.EmpireAttackedWithLams:
                    if (news.OtherEmpire is { } attEmp) {
                        var attEmpState = GetOrCreateState(state, attEmp, defaultPolicy);
                        var alwaysDecrement = news.Headline == NewsType.WorldConqueredByEnemy && news.Subject is IEconomicWorld;
                        if (alwaysDecrement || Rnd(random, 1, 100) < 25) {
                            attEmpState.Balance--;
                        }

                        var severity = AttackSeverity(emp.News, i, basePower);
                        RespondToEnemyAttack(attEmpState, persona, severity, random);
                    }
                    break;

                case NewsType.FleetOutOfFuel:
                    if (news.Subject is Fleet fleet) {
                        SendRescueFleet(emp, fleet, regionCapitals, fleetStates, game);
                    }
                    break;
                case NewsType.IndustryLacksMetals:
                    if (news.Subject is IEconomicWorld world) {
                        RNIndustryLack(emp, world, regionCapitals, fleetStates, game);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// State[Emp] (NPETYPES.PAS's StateDeptArray) — created on first reference rather than pre-seeded,
    /// same rationale as <see cref="Turns.KingdomTurnHandler"/>'s own doc comment on why
    /// <see cref="Entities.Empire.Independent"/> needs a slot too. Shared here (not a
    /// KingdomTurnHandler-private method) since StateDepartment/StateDeptReport/WarCabinet/ReviewNews
    /// all need the identical creation semantics <see cref="KingdomTurnHandler"/>'s own UpdateFleets
    /// already established for its own Conquer/JumpAttack Balance increments.
    /// </summary>
    internal static StateDeptRecord GetOrCreateState(Dictionary<Empire, StateDeptRecord> state, Empire emp, PolicyType defaultPolicy)
    {
        if (!state.TryGetValue(emp, out var record)) {
            record = new StateDeptRecord { Policy = defaultPolicy, AttackChance = 50 };
            state[emp] = record;
        }
        return record;
    }

    /// <summary>EmpireMilitary/TotalMilitary (StateDeptReport, NPEINTR.PAS:1588-1590,1605-1607) — 1 plus each ship type's thousands-of-hulls count times its MPower weight.</summary>
    private static long MilitaryTotal(ShipCounts totalShips)
    {
        long military = 1;
        foreach (var t in Enum.GetValues<ShipType>()) {
            military += PascalRound(totalShips[t] / 1000.0) * CombatConstants.MPower[t.ToAttackType()];
        }
        return military;
    }

    /// <summary>
    /// StateDeptReport (NPEINTR.PAS:1563-1632) — refreshes TotalMilitary/Worlds for this empire's own
    /// State slot, then TotalMilitary/Worlds/ThreatAssess for every other active empire's slot.
    /// Real Pascal bug, ported verbatim (already documented at docs/PASCAL_ARCHITECTURE_NOTES.md's
    /// "StateDeptReport calls GetCapital(Emp,...) instead of GetCapital(EnemyEmp,...)"): the per-enemy
    /// loop's <c>Tech</c> local is read off <paramref name="emp"/>'s own capital every iteration, so
    /// it's always identical to <c>empireTech</c> — the tech-based threat multiplier below can
    /// therefore never actually fire (<c>tech &gt; empireTech</c>/<c>tech &lt; empireTech</c> are both
    /// always false), left in rather than "fixed" since this is a real observed source defect, not a
    /// transcription slip.
    /// </summary>
    public static void StateDeptReport(Empire emp, Dictionary<Empire, StateDeptRecord> state, PolicyType defaultPolicy, Game game)
    {
        var (empireWorlds, _, empireSInd, empireShips) = EmpireWindowReport.GetEmpireStatus(emp, game);
        var empireMilitary = MilitaryTotal(empireShips);
        var empireTech = emp.Capital?.TechLevel ?? TechLevel.PreTech;

        var selfState = GetOrCreateState(state, emp, defaultPolicy);
        selfState.TotalMilitary = empireMilitary;
        selfState.Worlds = empireWorlds;

        foreach (var enemyEmp in game.Empires) {
            if (enemyEmp == emp || enemyEmp.Status == EmpireStatus.Eliminated) {
                continue;
            }

            var (worlds, _, enemySInd, enemyShips) = EmpireWindowReport.GetEmpireStatus(enemyEmp, game);
            var enemyMilitary = MilitaryTotal(enemyShips);

            var enemyState = GetOrCreateState(state, enemyEmp, defaultPolicy);
            enemyState.TotalMilitary = enemyMilitary;
            enemyState.Worlds = worlds;

            var tech = empireTech; // see this method's own doc comment — the confirmed GetCapital(Emp,...) bug.

            var threat = 50.0;
            if (tech > empireTech) {
                threat *= 1.5;
            } else if (tech < empireTech) {
                threat *= 0.75;
            }

            threat *= 1 + ((enemySInd - empireSInd) / 5.0);
            threat *= (double)enemyMilitary / empireMilitary;

            enemyState.ThreatAssess = threat > 100 ? 100 : PascalRound(threat);
        }
    }

    /// <summary>
    /// StateDepartment (NPEINTR.PAS:1460-1540) — starts and ends wars: recomputes each active enemy's
    /// Policy tier off the Balance/UsefulPower/ThreatAssess numbers StateDeptReport/UpdateFleets keep
    /// current, then cools Aggressiveness by 1-2 every turn regardless of which branch fired. Each
    /// Policy arm is an ELSE-IF ladder ending in "policy unchanged" when nothing qualifies — ported as
    /// one if/else-if chain per arm so the Rnd draw order matches the &amp;&amp;-short-circuit
    /// convention exactly (docs/PORT_DESIGN.md).
    /// </summary>
    public static void StateDepartment(Empire emp, NpeCharacter persona, Dictionary<Empire, StateDeptRecord> state, PolicyType defaultPolicy, Game game, Random random)
    {
        var selfState = GetOrCreateState(state, emp, defaultPolicy);
        var usefulPower = selfState.Worlds == 0 ? (double)selfState.TotalMilitary : (double)selfState.TotalMilitary / selfState.Worlds;

        foreach (var enemyEmp in game.Empires) {
            if (enemyEmp == emp || enemyEmp.Status == EmpireStatus.Eliminated) {
                continue;
            }

            var enemyState = GetOrCreateState(state, enemyEmp, defaultPolicy);
            var enemyPower = enemyState.Worlds == 0 ? (double)enemyState.TotalMilitary : (double)enemyState.TotalMilitary / enemyState.Worlds;
            var newPolicy = enemyState.Policy;

            if (enemyState.Balance < -1) {
                newPolicy = PolicyType.Conflict;
            } else if (enemyState.Balance < 0) {
                newPolicy = PolicyType.Preempt;
            } else {
                switch (enemyState.Policy) {
                    case PolicyType.Neutral:
                        if (usefulPower > enemyPower && usefulPower > 10 && enemyState.ThreatAssess > 50 && Rnd(random, 1, 100) < persona.Offensive) {
                            newPolicy = PolicyType.Harass;
                        } else if (usefulPower > 3 * enemyPower && usefulPower > 30 && enemyState.ThreatAssess > 75 && Rnd(random, 1, 100) < persona.Offensive / 2) {
                            newPolicy = PolicyType.Preempt;
                        }
                        break;

                    case PolicyType.Harass:
                        if (usefulPower > enemyPower && usefulPower > 30 && enemyState.ThreatAssess > 50 && Rnd(random, 1, 100) < persona.Offensive) {
                            newPolicy = PolicyType.Preempt;
                        } else if (usefulPower > 3 * enemyPower && usefulPower > 30 && enemyState.ThreatAssess > 75 && Rnd(random, 1, 100) < persona.Offensive) {
                            newPolicy = PolicyType.Preempt;
                        } else if (enemyState.Aggressiveness < 10 && Rnd(random, 1, 100) > persona.Offensive) {
                            newPolicy = PolicyType.Neutral;
                        } else if (usefulPower < 5) {
                            newPolicy = PolicyType.Neutral;
                        }
                        break;

                    case PolicyType.Preempt:
                        if (usefulPower > enemyPower && enemyState.ThreatAssess > 50 && Rnd(random, 1, 100) < persona.Offensive) {
                            newPolicy = PolicyType.Conflict;
                        } else if (usefulPower > 4 * enemyPower && enemyState.ThreatAssess > 50 && Rnd(random, 1, 100) < persona.Offensive) {
                            newPolicy = PolicyType.Conflict;
                        } else if (enemyState.Aggressiveness < 15 && enemyState.ThreatAssess < 50 && Rnd(random, 1, 100) > persona.Offensive) {
                            newPolicy = PolicyType.Neutral;
                        } else if (usefulPower < 10) {
                            newPolicy = PolicyType.Harass;
                        }
                        break;

                    case PolicyType.Conflict:
                        if (enemyState.Aggressiveness < 20 && enemyState.ThreatAssess < 50 && Rnd(random, 1, 100) > persona.Offensive) {
                            newPolicy = PolicyType.Neutral;
                        } else if (enemyState.Aggressiveness > 80 && enemyPower > 2 * usefulPower) {
                            newPolicy = PolicyType.Neutral;
                        }
                        break;
                }
            }

            enemyState.Policy = newPolicy;
            enemyState.Aggressiveness -= Rnd(random, 1, 2);
        }
    }

    /// <summary>
    /// WarCabinet (NPE00.PAS:511-601) — deploys raiding/battle fleets against every active empire per
    /// its current Policy tier, then probes each active empire's capital. Real Pascal quirk, ported
    /// verbatim, not fixed: unlike <see cref="StateDepartment"/>'s own loop (which explicitly skips
    /// <c>EnemyEmp=Emp</c>), this one has no such guard — <c>EmpireActive(Emp)</c> is trivially true,
    /// so an empire whose default Policy seeds at or above HarassPLT (Kingdom2's does, per
    /// InitializeKingdom2NPE) has a real per-turn chance of deploying HK raiders/battle fleets against
    /// its own gates/construction sites/planets: <see cref="GetBestRaiderTarget"/>/<see cref="GetBestTarget"/>
    /// filter candidates only by "owned by EnemyEmp," with no separate "not the attacker" exclusion.
    /// Gated on <see cref="Game.Known"/> for the attacker's own worlds, same as any other target — in
    /// full turn sequencing that's normally already true by the time NPE turns run (VisibilityHandler
    /// marks an empire's own worlds known), so this is a real, latent defect rather than one that
    /// necessarily fires every turn; <c>KingdomTurnHandlerTests</c>' own fixtures don't trigger it only
    /// because they never call <c>MarkScouted</c> on the capital. Same category of verbatim-preserved
    /// defect as <see cref="DeploySlowAttack"/>'s mission-type copy/paste slip.
    /// </summary>
    public static void WarCabinet(
        Empire emp, IReadOnlyList<IEconomicWorld> regionCapitals, Dictionary<Fleet, KingdomFleetState> fleetStates,
        NpeCharacter persona, Dictionary<Empire, StateDeptRecord> state, PolicyType defaultPolicy, Game game, Random random)
    {
        foreach (var enemyEmp in game.Empires) {
            var enemyState = GetOrCreateState(state, enemyEmp, defaultPolicy);
            var noOfRaidersOut = fleetStates.Values.Count(s => s.Mission == NpeMissionType.RaidTransports);

            if (noOfRaidersOut < NpeConstants.MaxNoOfRaiders
                && (enemyState.Balance < 0 || (enemyState.Policy >= PolicyType.Harass && Rnd(random, 1, 100) <= enemyState.AttackChance))) {
                DeployHKRaiders(emp, enemyEmp, regionCapitals, fleetStates, game, random);
            }

            if ((enemyState.Balance < 0 && Rnd(random, 1, 2) == 1) || Rnd(random, 1, 100) <= enemyState.AttackChance) {
                switch (enemyState.Policy) {
                    case PolicyType.Harass:
                        if (noOfRaidersOut < NpeConstants.MaxNoOfRaiders) {
                            DeployHKRaiders(emp, enemyEmp, regionCapitals, fleetStates, game, random);
                        }
                        break;

                    case PolicyType.Preempt:
                        if (Rnd(random, 1, 100) <= 50) {
                            if (noOfRaidersOut < NpeConstants.MaxNoOfRaiders) {
                                DeployHKRaiders(emp, enemyEmp, regionCapitals, fleetStates, game, random);
                            }
                        } else {
                            DeployJumpAttack(emp, enemyEmp, regionCapitals, persona, fleetStates, game, random);
                        }
                        break;

                    case PolicyType.Conflict:
                        if (Rnd(random, 1, 100) <= 75) {
                            DeployJumpAttack(emp, enemyEmp, regionCapitals, persona, fleetStates, game, random);
                        } else {
                            DeploySlowAttack(emp, enemyEmp, regionCapitals, persona, fleetStates, game, random);
                        }
                        break;

                    case PolicyType.War:
                        if (Rnd(random, 1, 100) <= 50) {
                            DeployJumpAttack(emp, enemyEmp, regionCapitals, persona, fleetStates, game, random);
                        } else {
                            DeploySlowAttack(emp, enemyEmp, regionCapitals, persona, fleetStates, game, random);
                        }
                        break;
                }
            }

            if (Rnd(random, 1, 100) <= enemyState.Aggressiveness && enemyEmp.Capital is { } enemyCapital) {
                // FOR i:=1 TO Rnd(1,4) DO — bound drawn once at loop entry, same gotcha as
                // DeployBattleFleet's own probe-launch loop (see that method's own doc comment).
                var probesToLaunch = Rnd(random, 1, 4);
                for (var i = 0; i < probesToLaunch; i++) {
                    var x = Rnd(random, enemyCapital.Location.X - 4, enemyCapital.Location.X + 4);
                    var y = Rnd(random, enemyCapital.Location.Y - 4, enemyCapital.Location.Y + 4);
                    if (x >= 0 && x < game.Galaxy.Size && y >= 0 && y < game.Galaxy.Size) {
                        emp.TryLaunchProbe(new Coordinate(x, y));
                    }
                }
            }
        }
    }

    /// <summary>
    /// ExplorationAndProbing (NPE00.PAS:695-737) — launches every available probe from each regional
    /// capital at a random nearby sector, sized by persona SphereX. Real Pascal's REPEAT/UNTIL hangs
    /// forever when RCap is empty (NoMoreProbes is only ever set inside the FOR loop's own body) —
    /// not reproduced; an empty <paramref name="regionCapitals"/> returns immediately instead.
    /// </summary>
    public static void ExplorationAndProbing(Empire emp, IReadOnlyList<IEconomicWorld> regionCapitals, NpeCharacter persona, Game game, Random random)
    {
        if (regionCapitals.Count == 0) {
            return;
        }

        var maxProbeDist = 24 - 2 * ISqrt(persona.SphereX);

        bool noMoreProbes;
        do {
            noMoreProbes = false;
            foreach (var capital in regionCapitals) {
                var baseXy = capital.Location;
                var x = Rnd(random, baseXy.X - maxProbeDist, baseXy.X + maxProbeDist);
                var y = Rnd(random, baseXy.Y - maxProbeDist, baseXy.Y + maxProbeDist);
                if (x >= 0 && x < game.Galaxy.Size && y >= 0 && y < game.Galaxy.Size) {
                    if (!emp.TryLaunchProbe(new Coordinate(x, y))) {
                        noMoreProbes = true;
                    }
                }
            }
        } while (!noMoreProbes);
    }
}
