using ThreeLn.Reconstruction4021.Core.Combat;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Turns;
using ThreeLn.Reconstruction4021.Core.Types;
using static ThreeLn.Reconstruction4021.Core.PascalMath;

namespace ThreeLn.Reconstruction4021.Core.Npe;

/// <summary>
/// The shared toolkit multiple NPE personalities call in real Pascal (NPEINTR.PAS, 1,732 lines) —
/// fleet-composition planning, targeting, regional bookkeeping, and world (re)designation. Ported as
/// its own service <see cref="KingdomTurnHandler"/> depends on rather than folded into Kingdom's own
/// class, since Pirate/Berserker/Guardian call the same real Pascal procedures (see docs/PORT_DESIGN.md).
///
/// <b>Scope, split from NPEINTR.PAS's full surface on a real dependency line, not an arbitrary cut:</b>
/// everything here reads state and computes a value — nothing here creates, moves, or refuels a fleet.
/// The other half of NPEINTR.PAS (the five <c>Deploy*Fleet</c> procedures and all eight
/// <c>Implement*MSN</c> mission-execution procedures) needs real fleet-lifecycle primitives
/// (<c>DeployFleet</c>, <c>ChangeCompositionOfFleet</c>, <c>EstimatedDateOfArrival</c>/
/// <c>EstimatedRange</c>, <c>RefuelFleet</c>, <c>SetFleetDestination</c> — FLEET.PAS/PRIMINTR.PAS)
/// that don't exist anywhere in this port yet; every prior phase only ever moved or destroyed fleets
/// that scenario loading or human setup already created. That half lands as its own commit once those
/// primitives exist, with <see cref="KingdomTurnHandler"/> as its first real caller — bundling it in
/// here would make this whole file unverifiable until the very last line landed.
///
/// <b>Ground truth, deliberately hardcoded rather than golden-file this round:</b> every method below
/// either has no <c>Rnd</c>/<c>Random</c> call at all, or draws inside a scan-every-planet-in-the-galaxy
/// loop whose count depends on live galaxy shape — a golden case for the second group would need a
/// full hand-assembled universe to mean anything close to what real play produces. Phase 7's save/load
/// work is what makes building one of those cheap; see <c>NpeToolkitTests.cs</c> for the hand-derived
/// cases used instead, and reference/verify/README.md's own ground-truth split guidance.
/// </summary>
public static class NpeToolkit
{
    private static readonly ShipType[] _returnSequence = [ShipType.Fighter, ShipType.Jumpship, ShipType.Penetrator, ShipType.HunterKiller, ShipType.Starship];
    private static readonly ShipType[] _conquerSequence = [ShipType.Jumpship, ShipType.HunterKiller, ShipType.Penetrator, ShipType.Starship, ShipType.Fighter];
    private static readonly ShipType[] _stackSequence = [ShipType.Penetrator, ShipType.HunterKiller, ShipType.Starship, ShipType.Fighter, ShipType.Jumpship];
    private static readonly ShipType[] _jumpAttackSequence = [ShipType.Jumpship, ShipType.HunterKiller, ShipType.HunterKiller, ShipType.HunterKiller, ShipType.HunterKiller];
    private static readonly ShipType[] _raidTransportsSequence = [ShipType.HunterKiller, ShipType.HunterKiller, ShipType.HunterKiller, ShipType.HunterKiller, ShipType.HunterKiller];

    /// <summary>MilitaryPower (MISC.PAS:40-63) — a composite strength score, ships and defenses both weighted by <see cref="CombatConstants.CombatPower"/> (Pascal's MPower).</summary>
    public static long MilitaryPower(ShipCounts ships, DefenseCounts defenses)
    {
        long power = 0;
        foreach (var t in Enum.GetValues<DefenseType>()) {
            power += (long)defenses[t] * CombatConstants.CombatPower[t.ToAttackType()];
        }
        foreach (var t in Enum.GetValues<ShipType>()) {
            power += (long)ships[t] * CombatConstants.CombatPower[t.ToAttackType()];
        }
        return power;
    }

    /// <summary>
    /// GetFleetComposition (NPEINTR.PAS:237-322) — the ship/cargo bundle a Deploy*Fleet call would
    /// carry for a mission of the given power/troop budget, read from what's on hand at
    /// <paramref name="source"/> without touching it (the actual subtraction happens in DeployFleet
    /// itself, not ported here — see this class's own doc comment). SSeq (NPEINTR.PAS:247-248) is
    /// declared but never referenced by the real sequence-selection below — SlowAttackMSN falls
    /// through to the same default as every other unlisted mission, not to SSeq as its name suggests
    /// (see docs/PASCAL_ARCHITECTURE_NOTES.md). BSRKAttackMSN's extra-transports branch is real and
    /// kept even though Berserker isn't implemented yet — it's free once <paramref name="mission"/>
    /// can hold that value.
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
    /// weighted roll over every <see cref="WorldType"/>. <paramref name="persona"/> is real Pascal's
    /// own parameter but confirmed unused: the whole procedure body never reads a single
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
                RedesignateWorldType(planet, newType, random);
            }
        }
    }

    /// <summary>
    /// DesignateWorld (INTRFACE.PAS:187-221), scoped to what ReDesignateEmpire can actually reach:
    /// NewType is never Capital here (Chance[CapTyp] never leaves 0 in GetNewDesignation, and
    /// ReDesignateEmpire's own caller already excludes the capital world from consideration), so the
    /// capital-swap/tech-reset branch (INTRFACE.PAS:195-214) is real Pascal but confirmed unreachable
    /// from this call path — not ported under this name. A general-purpose DesignateWorld (a future
    /// human "change world designation" command, Phase 8) would need that branch back. The efficiency
    /// reduction below is NOT conditional on the capital branch, though — it fires for every call.
    /// </summary>
    private static void RedesignateWorldType(IEconomicWorld world, WorldType newType, Random random)
    {
        world.Efficiency -= PascalRound(world.Efficiency / (1.5 + random.NextDouble()));
        world.Type = newType;
    }

    /// <summary>
    /// EnforceNPEDataLinks (NPEINTR.PAS:1661-1689) — drops mission state for any fleet that's no
    /// longer alive. Real Pascal reconciles a slot-indexed array against SetOfActiveFleets; this
    /// port's Dictionary&lt;Fleet,...&gt; needs the same pruning, since a fleet destroyed elsewhere
    /// (Combat/CombatOutcome.cs's DestroyFleet) would otherwise leave a dangling dictionary key.
    /// </summary>
    public static void EnforceNpeDataLinks(Dictionary<Fleet, KingdomFleetState> fleetStates, Game game)
    {
        var alive = new HashSet<Fleet>(game.Galaxy.Fleets);
        foreach (var fleet in fleetStates.Keys.Where(f => !alive.Contains(f)).ToList()) {
            fleetStates.Remove(fleet);
        }
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
    /// or heads home, biased by <see cref="NpeCharacter.Offensive"/>. No caller exists yet in this port
    /// (NPE00/NPE02's per-turn driver, Phase 6d) — ported now since it's part of NPEINTR.PAS's own
    /// declared surface, same "primitive ready for whoever needs it" precedent as Phase 5g's
    /// SelfDestructObject.
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
    public static void ImplementRefuelMSN(Fleet fleet, object target, Game game)
    {
        CombatOutcome.AbortFleet(fleet, target, report: true);
        CombatOutcome.DestroyFleet(fleet, game);

        var (targetShips, targetCargo) = target switch {
            Fleet f => (f.Ships, f.Cargo),
            IEconomicWorld w => (w.Ships, w.Cargo),
            _ => throw new ArgumentException($"ImplementRefuelMSN: expected a Fleet or IEconomicWorld target, got {target.GetType()}.", nameof(target)),
        };
        var targetFuel = target is Fleet targetFleet ? targetFleet.Fuel : 0;
        var maxFuel = FleetLogistics.FuelCapacity(targetShips);
        var tonsNeeded = (int)((maxFuel - targetFuel) / FleetLogistics.FuelPerTon) + 1;
        var maxTri = Math.Min(tonsNeeded, targetCargo.Trillum);

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
}
