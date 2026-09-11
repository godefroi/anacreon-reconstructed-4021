using Reconstructed4021.Core;
using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;
using static Reconstructed4021.Core.PascalMath;

namespace Reconstructed4021.LegacyNpe;

/// <summary>
/// AI for the Berserker NPE (NPE04.PAS) — the one personality whose mobile roaming unit is a
/// <see cref="Starbase"/> (command base or fortress), not a fleet. Two loops run every turn: a
/// per-starbase mission state machine (<see cref="BaseMissionType"/>, driven by
/// <see cref="_baseStates"/>) that picks attack targets and wanders/refuels between them, and a
/// per-fleet loop (<see cref="_fleetStates"/>, the same shared <c>FleetDataArray</c> shape Kingdom
/// and Pirate also use) for the battle fleets a base dispatches. No diplomacy, no region/redesignation
/// bookkeeping — matches Pirate's own simplicity, not Kingdom's.
///
/// <c>GetNearestBSRKBase</c> (NPE04.PAS:40-72) is confirmed dead — grepped, no call site anywhere in
/// NPE04.PAS — not ported, same category as <c>Midway</c>/Pirate's own <c>Sheep</c>.
/// </summary>
public sealed class BerserkerTurnHandler : ITurnHandler
{
    private const int MinBasePower = 100000;
    private const int MaxDistanceToHome = 10;

    public bool IsHuman => false;

    private readonly Random _random;
    private readonly Dictionary<Fleet, BerserkerFleetState> _fleetStates = new();
    private readonly Dictionary<Starbase, BerserkerBaseState> _baseStates = new();

    /// <summary>
    /// InitializeBerserkerNPE (NPE04.PAS:568-597), minus the BaseData seeding: real Pascal's own
    /// seeding loop runs at scenario-load time (right after empire creation, before that scenario's
    /// own starbase-placement commands run — <c>ScenarioLoader.RunCreateNPEmpire</c>'s own doc
    /// comment confirms this port calls <see cref="INpeHandlerProvider.CreateNew"/> at the same
    /// point), so it always finds zero owned starbases and is a no-op in practice. The real seeding
    /// happens via the exact same "no mission yet" fallback <see cref="UpdateBases"/> already needs
    /// (NPE04.PAS:504-505's own <c>ELSE NewBSRKBaseTarget</c>), the first turn this empire actually
    /// owns a base — so there's nothing to seed here.
    /// </summary>
    public BerserkerTurnHandler(Empire empire, Random random)
    {
        _random = random;
        NpeToolkit.SetEmpireDefenses(empire, random);
    }

    /// <summary>`.SAV`/native-JSON import: reconstructs a handler from state a save file already recorded, matching <see cref="PirateTurnHandler"/>'s own load-path constructor.</summary>
    internal BerserkerTurnHandler(Dictionary<Fleet, BerserkerFleetState> fleetStates, Dictionary<Starbase, BerserkerBaseState> baseStates, Random random)
    {
        _random = random;
        _fleetStates = fleetStates;
        _baseStates = baseStates;
    }

    /// <summary>Read-back seam for `.SAV`/native-JSON export.</summary>
    internal IReadOnlyDictionary<Fleet, BerserkerFleetState> FleetStates => _fleetStates;

    /// <summary>See <see cref="FleetStates"/>.</summary>
    internal IReadOnlyDictionary<Starbase, BerserkerBaseState> BaseStates => _baseStates;

    /// <summary>ImplementBerserkerNPE (NPE04.PAS:599-615).</summary>
    public void PlayTurn(Empire empire, Game game)
    {
        EnforceFleetDataLinks(game);
        ReviewNews(empire, game);
        UpdateFleets(empire, game);
        UpdateBases(empire, game);
    }

    /// <summary>EnforceNPEDataLinks (NPEINTR.PAS:1661-1689), fleet-half only — real Pascal never enforces/prunes BaseData (see <see cref="BerserkerBaseState"/>'s own doc comment). A private copy rather than the shared <see cref="NpeToolkit.EnforceNpeDataLinks"/>, since that one is hardcoded to <see cref="KingdomFleetState"/> — same precedent <see cref="PirateTurnHandler"/> already set.</summary>
    private void EnforceFleetDataLinks(Game game)
    {
        var alive = new HashSet<Fleet>(game.Galaxy.Fleets);
        foreach (var fleet in _fleetStates.Keys.Where(f => !alive.Contains(f)).ToList()) {
            _fleetStates.Remove(fleet);
        }
    }

    /// <summary>
    /// BSRKReviewNews/BaseIsBlocked (NPE04.PAS:410-479) — a blocked starbase either re-deploys its attack early (AttackBMS, once close enough), gives up toward wandering (RefuelBMS, or AttackBMS still too far), reasserts its current position (WaitForAttackBMS), or just re-rolls a new destination (WanderAroundBMS). DefendBMS is a no-op, matching <see cref="ImplementDefendBase"/>.
    ///
    /// Berserker is the first personality whose ReviewNews can deploy a fresh fleet mid-loop (the
    /// Attack arm's own <see cref="DeployBerserkerFleet"/> call) -- safe against mutating
    /// <c>empire.News</c> out from under this <c>foreach</c> only because neither of that path's own
    /// side effects (<see cref="FleetLifecycle.AbortFleet"/> on the out-of-range branch, always
    /// same-owner here since it always aborts back onto this empire's own launching starbase --
    /// <see cref="CombatOutcome.AbortFleet"/>'s own report guard skips <c>AddNews</c> entirely when
    /// attacker and ground owner match; <see cref="Empire.TryLaunchProbe"/> on the probe branch,
    /// which never calls <c>AddNews</c> at all) ever appends to this empire's own news list. Verified
    /// by reading both call chains directly, not assumed -- if either one changes to add news to the
    /// acting empire, this loop needs a <c>.ToList()</c> snapshot the way none of Kingdom/Pirate's own
    /// ReviewNews implementations have needed either, for the same reason.
    /// </summary>
    private void ReviewNews(Empire empire, Game game)
    {
        foreach (var item in empire.News) {
            if (item.Subject is not Starbase starbase || item.Headline != NewsType.StarbaseBlocked) {
                continue;
            }
            if (!_baseStates.TryGetValue(starbase, out var state)) {
                continue;
            }

            switch (state.Mission) {
                case BaseMissionType.Attack:
                    if (state.Target is { } target && starbase.Location.DistanceTo(target.Location) < 8) {
                        var (power, gat) = GetPowerToUse(target);
                        DeployBerserkerFleet(empire, starbase, target, power, gat, NpeMissionType.BerserkerAttack, game);
                        state.Mission = BaseMissionType.WaitForAttack;
                        state.Count = 0;
                    } else {
                        state.Mission = BaseMissionType.WanderAround;
                        state.Count = 0;
                        FleetLifecycle.SetFleetDestination(starbase, RandomPoint(game));
                    }
                    break;

                case BaseMissionType.Refuel:
                    state.Mission = BaseMissionType.WanderAround;
                    state.Count = 0;
                    FleetLifecycle.SetFleetDestination(starbase, RandomPoint(game));
                    break;

                case BaseMissionType.WaitForAttack:
                    FleetLifecycle.SetFleetDestination(starbase, starbase.Location);
                    break;

                case BaseMissionType.WanderAround:
                    FleetLifecycle.SetFleetDestination(starbase, RandomPoint(game));
                    break;
            }
        }
    }

    /// <summary>UpdateFleets (NPE04.PAS:511-536) — course-corrects every tracked fleet toward its current target before checking readiness (real Pascal does this unconditionally, not just for Ready fleets), then dispatches Ready ones. Unmatched missions are destroyed, matching NPE04's own <c>ELSE DestroyFleet</c> (unlike Pirate's silent no-op).</summary>
    private void UpdateFleets(Empire empire, Game game)
    {
        foreach (var (fleet, state) in _fleetStates.ToList()) {
            if (!game.Galaxy.Fleets.Contains(fleet)) {
                continue;
            }

            if (state.Mission == NpeMissionType.BerserkerReturn && state.Target is { } returnTarget) {
                FleetLifecycle.SetFleetDestination(fleet, returnTarget.Location);
            }

            if (fleet.Status != FleetStatus.Ready) {
                continue;
            }

            switch (state.Mission) {
                case NpeMissionType.BerserkerReturn:
                    ImplementReturnMission(empire, fleet, state, game);
                    break;

                case NpeMissionType.BerserkerAttack:
                    ImplementAttackMission(empire, fleet, state, game);
                    break;

                default:
                    CombatOutcome.DestroyFleet(fleet, game);
                    break;
            }
        }
    }

    /// <summary>ImplementBSRKReturnMSN (NPE04.PAS:241-252) — merges into its target starbase if still owned, else just disbands. Target is always the launching/wandering starbase for a BerserkerReturn fleet (see <see cref="DeployBerserkerFleet"/>'s own two call sites), never a planet.</summary>
    private void ImplementReturnMission(Empire empire, Fleet fleet, BerserkerFleetState state, Game game)
    {
        if (state.Target is Starbase target && target.Owner == empire) {
            FleetLifecycle.AbortFleet(fleet, target, game);
            NewBerserkerBaseTarget(empire, target, game);
        } else {
            CombatOutcome.DestroyFleet(fleet, game);
        }
    }

    /// <summary>ImplementBSRKAttackMSN (NPE04.PAS:254-284) — attacks via the shared ImplementJumpAttackMSN; a conquest plunders (and 1-in-3 holocausts) the target, a destroyed attacker sends its launching base looking for a new target immediately, anything else retasks the same fleet home in place (mutating <paramref name="state"/>, not redeploying — matches Pascal writing straight back into the same FleetData slot).</summary>
    private void ImplementAttackMission(Empire empire, Fleet fleet, BerserkerFleetState state, Game game)
    {
        if (state.Target is not { } target || state.HomeBase is not { } homeBase) {
            return;
        }

        var result = NpeToolkit.ImplementJumpAttackMSN(empire, fleet, target, homeBase, game, _random);

        if (result == AttackResultType.DefenderConquered) {
            NpeToolkit.PlunderWorld(fleet, target);
            if (Rnd(_random, 1, 3) == 1) {
                DestroyWorld(target);
            }
        }

        if (result == AttackResultType.AttackerDestroyed) {
            if (homeBase is Starbase homeStarbase) {
                NewBerserkerBaseTarget(empire, homeStarbase, game);
            }
        } else {
            state.Mission = NpeMissionType.BerserkerReturn;
            state.Target = homeBase;
            FleetLifecycle.SetFleetDestination(fleet, homeBase.Location);
        }
    }

    /// <summary>BSRKDestroyWorld (NPE04.PAS:150-194) — kills population, wrecks a random share of every industry, and reverts tech. Ported verbatim including the real quirk that this always runs after <see cref="NpeToolkit.PlunderWorld"/> already reassigned the target to <see cref="Empire.Independent"/>, so the news this fires lands on Independent's own (never-read) news list, not on any real player's.</summary>
    private void DestroyWorld(IEconomicWorld target)
    {
        var owner = target.Owner;
        owner.AddNews(NewsType.WorldHolocausted, target);

        var pop = target.Population;
        var deaths = Math.Min(pop - 10, Math.Max(pop / 2, 100 + Rnd(_random, 1, 100)));
        target.Population = pop - deaths;
        owner.AddNews(NewsType.HolocaustDeaths, target, p1: deaths);

        foreach (var industry in Enum.GetValues<IndustryType>()) {
            // Real Pascal: IndLost:=GreaterInt(Indus[Ind], Indus[Ind]-Rnd(10,50)) in this branch.
            // Rnd(10,50) is always positive, so Indus[Ind]-Rnd(10,50) is always <= Indus[Ind], and
            // GreaterInt always picks Indus[Ind] itself -- a full wipeout of that industry, not a
            // 10-50-point loss as the shape of the formula suggests. Almost certainly a real Pascal
            // bug (GreaterInt(0, ...) would be the usual floor-at-zero idiom), ported verbatim rather
            // than "fixed" -- the Rnd(10,50) draw itself is still real and must still happen even
            // though its result is discarded, to keep this fleet's RNG draw count matching source.
            var indLost = Rnd(_random, 1, 2) == 1
                ? Math.Max(target.Industry[industry], target.Industry[industry] - Rnd(_random, 10, 50))
                : target.Industry[industry] / 2;

            if (indLost > 0) {
                target.Industry[industry] -= indLost;
                owner.AddNews(NewsType.IndustryDestroyed, target, p1: indLost, p2: (int)industry);
            }
        }

        target.TechLevel = (TechLevel)Rnd(_random, 0, 2);
        owner.AddNews(NewsType.TechLevelRegressed, target, p1: (int)target.TechLevel);
    }

    /// <summary>UpdateBases (NPE04.PAS:481-509) — every owned command base/fortress dispatches on its current mission, or picks a fresh one via NewBerserkerBaseTarget if it has none tracked yet (this is also where real Pascal's own scenario-load-time seeding effectively happens — see the public constructor's own doc comment).</summary>
    private void UpdateBases(Empire empire, Game game)
    {
        foreach (var starbase in game.Galaxy.Starbases.Where(b => b.Owner == empire && b.Kind is StarbaseKind.CommandBase or StarbaseKind.Fortress).ToList()) {
            if (!_baseStates.TryGetValue(starbase, out var state)) {
                NewBerserkerBaseTarget(empire, starbase, game);
                continue;
            }

            switch (state.Mission) {
                case BaseMissionType.Attack:
                    ImplementAttackBase(empire, starbase, state, game);
                    break;
                case BaseMissionType.Defend:
                    ImplementDefendBase();
                    break;
                case BaseMissionType.FindHome:
                    ImplementFindHomeBase(empire, starbase, state, game);
                    break;
                case BaseMissionType.Refuel:
                    ImplementRefuelBase(empire, starbase, state, game);
                    break;
                case BaseMissionType.WaitForAttack:
                    ImplementWaitForAttackBase(empire, starbase, state, game);
                    break;
                case BaseMissionType.WanderAround:
                    ImplementWanderAroundBase(empire, starbase, state, game);
                    break;
                default:
                    NewBerserkerBaseTarget(empire, starbase, game);
                    break;
            }
        }
    }

    /// <summary>
    /// NewBSRKBaseTarget (NPE04.PAS:74-148) — the retargeting brain. A weak base (own ship-only
    /// power below <see cref="MinBasePower"/>) goes looking for home instead of picking a fight;
    /// otherwise scans every non-owned planet in the galaxy with enough loot on hand, comparing this
    /// base's own power (jittered by one real-valued 0..1 roll, not <see cref="RndVar"/>'s ±% jitter)
    /// against the target's, ignoring the target's LAM defenses entirely once this base carries 4000+
    /// of its own, and picks the nearest one that qualifies.
    /// </summary>
    private void NewBerserkerBaseTarget(Empire empire, Starbase starbase, Game game)
    {
        var state = GetOrAddBaseState(starbase);

        var baseLams = starbase.Defenses[DefenseType.Lam];
        var basePower = NpeToolkit.MilitaryPower(starbase.Ships, new DefenseCounts());

        if (basePower < MinBasePower) {
            state.Mission = BaseMissionType.FindHome;
            return;
        }

        IEconomicWorld? bestTarget = null;
        var bestDistance = int.MaxValue;

        foreach (var candidate in game.Galaxy.Planets) {
            if (candidate.Owner == empire) {
                continue;
            }

            var cargo = candidate.Cargo;
            if (!((long)cargo[CargoType.Metals] + cargo[CargoType.Chemicals] > 2000 || cargo[CargoType.Trillum] > 1000)) {
                continue;
            }

            var testDefenses = baseLams < 4000 ? candidate.Defenses : new DefenseCounts();
            if (basePower + PascalRound(basePower * _random.NextDouble()) <= NpeToolkit.MilitaryPower(candidate.Ships, testDefenses)) {
                continue;
            }

            var distance = candidate.Location.DistanceTo(starbase.Location);
            if (distance < bestDistance) {
                bestDistance = distance;
                bestTarget = candidate;
            }
        }

        if (bestTarget is not null) {
            state.Mission = BaseMissionType.Attack;
            state.Target = bestTarget;
            FleetLifecycle.SetFleetDestination(starbase, bestTarget.Location);
        } else {
            state.Mission = BaseMissionType.WanderAround;
            state.Count = 0;
            FleetLifecycle.SetFleetDestination(starbase, RandomPoint(game));
        }
    }

    private BerserkerBaseState GetOrAddBaseState(Starbase starbase)
    {
        if (!_baseStates.TryGetValue(starbase, out var state)) {
            state = new BerserkerBaseState();
            _baseStates[starbase] = state;
        }
        return state;
    }

    /// <summary>ImplementAttackBMS (NPE04.PAS:286-307) — once close enough (a fortress within 5, or exactly adjacent for a command base), launches the attack fleet and starts waiting for it.</summary>
    private void ImplementAttackBase(Empire empire, Starbase starbase, BerserkerBaseState state, Game game)
    {
        if (state.Target is not { } target) {
            return;
        }

        var distance = starbase.Location.DistanceTo(target.Location);
        if ((starbase.Kind == StarbaseKind.Fortress && distance <= 5) || distance == 1) {
            var (power, gat) = GetPowerToUse(target);
            DeployBerserkerFleet(empire, starbase, target, power, gat, NpeMissionType.BerserkerAttack, game);
            state.Mission = BaseMissionType.WaitForAttack;
            state.Count = 0;
        }
    }

    /// <summary>ImplementDefendBMS (NPE04.PAS:309-311) — a confirmed empty stub in real Pascal (matches docs/ROADMAP.md's own note); a genuine no-op, not a TODO.</summary>
    private static void ImplementDefendBase()
    {
    }

    /// <summary>ImplementFindHomeBMS (NPE04.PAS:313-351) — the nearest regional capital within MaxDistanceToHome, or wander if none qualifies.</summary>
    private void ImplementFindHomeBase(Empire empire, Starbase starbase, BerserkerBaseState state, Game game)
    {
        var regionCapitals = NpeToolkit.CreateRegionArray(empire, game);
        var home = NpeToolkit.GetRegionalCapital(starbase, regionCapitals);

        if (home is not null && starbase.Location.DistanceTo(home.Location) <= MaxDistanceToHome) {
            state.Mission = BaseMissionType.Refuel;
            state.Target = home;
            FleetLifecycle.SetFleetDestination(starbase, home.Location);
        } else {
            state.Mission = BaseMissionType.WanderAround;
            state.Count = 0;
            FleetLifecycle.SetFleetDestination(starbase, RandomPoint(game));
        }
    }

    /// <summary>ImplementRefuelBMS (NPE04.PAS:353-372) — once close to and still owning its chosen home world, sends an oversized "supply fleet" from that home to merge into this base.</summary>
    private void ImplementRefuelBase(Empire empire, Starbase starbase, BerserkerBaseState state, Game game)
    {
        if (state.Target is not { } home) {
            return;
        }

        if (starbase.Location.DistanceTo(home.Location) < 5 && home.Owner == empire) {
            DeployBerserkerFleet(empire, home, starbase, 9000000, 9000000, NpeMissionType.BerserkerReturn, game);
            state.Mission = BaseMissionType.WaitForAttack;
            state.Count = 0;
        }
    }

    /// <summary>ImplementWaitForAttackBMS (NPE04.PAS:374-395) — waits 3 turns then recalls its own position (a no-op movement order, matching source), gives up and retargets after 10.</summary>
    private void ImplementWaitForAttackBase(Empire empire, Starbase starbase, BerserkerBaseState state, Game game)
    {
        if (state.Count > 10) {
            NewBerserkerBaseTarget(empire, starbase, game);
        } else if (state.Count == 3) {
            FleetLifecycle.SetFleetDestination(starbase, starbase.Location);
            state.Count++;
        } else {
            state.Count++;
        }
    }

    /// <summary>ImplementWanderAroundBMS (NPE04.PAS:397-408) — gives up wandering and retargets after 10 turns.</summary>
    private void ImplementWanderAroundBase(Empire empire, Starbase starbase, BerserkerBaseState state, Game game)
    {
        if (state.Count > 10) {
            NewBerserkerBaseTarget(empire, starbase, game);
        } else {
            state.Count++;
        }
    }

    /// <summary>
    /// DeployBSRKAttackMSN (NPE04.PAS:232-239) and ImplementRefuelBMS's own inline
    /// 9,000,000/9,000,000 call (NPE04.PAS:367) both launch through the exact same shared NPEINTR.PAS
    /// <c>DeployBattleFleet</c> real Pascal gives Kingdom too — confirmed by reading NPE04.PAS itself,
    /// not assumed (it defines no composition/deployment logic of its own). Can't call
    /// <see cref="NpeToolkit.DeployBattleFleet"/> directly (hardcoded to <see cref="KingdomFleetState"/>);
    /// this reproduces its body against <see cref="_fleetStates"/> instead, including its probe-launch
    /// tail, which fires exactly as often for a Berserker attack fleet as it would for Kingdom's.
    /// </summary>
    private void DeployBerserkerFleet(Empire empire, IEconomicWorld fromWorld, IEconomicWorld toTarget, long power, long gat, NpeMissionType mission, Game game)
    {
        var (ships, cargo) = NpeToolkit.GetFleetComposition(fromWorld, power, gat, mission);
        var fleet = FleetLifecycle.DeployFleet(empire, fromWorld, ships, cargo, toTarget.Location, game);

        if (FleetLifecycle.EstimatedDateOfArrival(fleet, game) > FleetLifecycle.EstimatedRange(fleet)) {
            FleetLifecycle.AbortFleet(fleet, fromWorld, game);
            return;
        }

        _fleetStates[fleet] = new BerserkerFleetState { Mission = mission, Target = toTarget, HomeBase = fromWorld };

        if (toTarget.Owner != empire && !toTarget.Owner.IsIndependent) {
            var probesToLaunch = Rnd(_random, 1, 4);
            for (var i = 0; i < probesToLaunch; i++) {
                empire.TryLaunchProbe(toTarget.Location);
            }
        }
    }

    /// <summary>GetPowerToUse (NPE04.PAS:211-230) — the force this base commits to an attack, sized off the target's own real defenses (not the LAM-conditional zeroing NewBerserkerBaseTarget's own scan used).</summary>
    private (long Power, long Gat) GetPowerToUse(IEconomicWorld target)
    {
        var targetDefense = NpeToolkit.MilitaryPower(target.Ships, target.Defenses);
        var targetMen = (long)target.Cargo[CargoType.Legion] + 4 * target.Cargo[CargoType.NinjaLegion] + 10;

        var power = 50000L + Rnd(_random, 2, 5) * targetDefense + targetDefense / Rnd(_random, 2, 25);
        var gat = 2 * targetMen;
        return (power, gat);
    }

    private Coordinate RandomPoint(Game game) => new(Rnd(_random, 1, game.Galaxy.Size), Rnd(_random, 1, game.Galaxy.Size));
}
