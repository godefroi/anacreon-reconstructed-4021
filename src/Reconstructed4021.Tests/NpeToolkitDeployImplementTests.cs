using Reconstructed4021.Core;
using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.LegacyNpe;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// <see cref="Core.Npe.NpeToolkit"/>'s Deploy*Fleet/Implement*MSN layer, on top of
/// <see cref="Core.Entities.FleetLifecycle"/>'s primitives. Hardcoded, same rationale as
/// <c>NpeToolkitTests</c> and <c>FleetLifecycleTests</c>.
/// </summary>
public class NpeToolkitDeployImplementTests
{
    private static Empire NewEmpire(string name) =>
        EmpireFactory.CreateEmpire(name, null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);

    private static (Game Game, Galaxy Galaxy) NewGame()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Game(galaxy);
        return (game, galaxy);
    }

    /// <summary>Returns each queued value in turn from <c>Next(int)</c> (clamped to the last one once exhausted), to tell apart code that draws an RNG bound once from code that re-draws it on every loop check.</summary>
    private sealed class SequenceRandom(params int[] values) : Random
    {
        private int _index;
        public override int Next(int maxValue) => values[Math.Min(_index++, values.Length - 1)];
    }

    [Test]
    public async Task DeployBattleFleet_LaunchesAndRecordsState()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var enemy = NewEmpire("Enemy");
        game.Empires.Add(owner);
        game.Empires.Add(enemy);

        var fromWorld = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Base };
        fromWorld.Ships.Starships = 100;
        var toTarget = new Planet { Location = new Coordinate(3, 0), Owner = enemy, Type = WorldType.Independent };
        galaxy.Planets.Add(fromWorld);
        galaxy.Planets.Add(toTarget);

        var fleetStates = new Dictionary<Fleet, KingdomFleetState>();
        NpeToolkit.DeployBattleFleet(owner, fleetStates, fromWorld, power: 100, gat: 0, NpeMissionType.Conquer, toTarget, game, new FixedRandom(0));

        await Assert.That(galaxy.Fleets).Count().IsEqualTo(1);
        var fleet = galaxy.Fleets[0];
        await Assert.That(fleetStates[fleet].Mission).IsEqualTo(NpeMissionType.Conquer);
        await Assert.That(fleetStates[fleet].Target).IsEqualTo(toTarget);
        await Assert.That(fleetStates[fleet].HomeBase).IsEqualTo(fromWorld);
    }

    [Test]
    public async Task DeployBattleFleet_ProbeCount_DrawsRngBoundOnce()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var enemy = NewEmpire("Enemy");
        game.Empires.Add(owner);
        game.Empires.Add(enemy);

        var fromWorld = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Base };
        fromWorld.Ships.Starships = 100;
        var toTarget = new Planet { Location = new Coordinate(3, 0), Owner = enemy, Type = WorldType.Independent };
        galaxy.Planets.Add(fromWorld);
        galaxy.Planets.Add(toTarget);

        var fleetStates = new Dictionary<Fleet, KingdomFleetState>();
        // First Rnd(1,4) draw returns 3 -> bound 4, so Pascal's FOR i:=1 TO Rnd(1,4) DO (bound drawn
        // once, at loop entry) launches 4 probes. A re-evaluate-every-iteration bug would re-draw the
        // bound on each loop check (every later draw returns 0 -> bound 1) and stop after just 1.
        NpeToolkit.DeployBattleFleet(owner, fleetStates, fromWorld, power: 100, gat: 0, NpeMissionType.Conquer, toTarget, game, new SequenceRandom(3, 0, 0, 0, 0));

        await Assert.That(owner.ProbesInTransit).Count().IsEqualTo(4);
    }

    [Test]
    public async Task DeployCargoFleet_LaunchesWithClampedCargoAndRecordsState()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var fromWorld = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Base };
        fromWorld.Ships.Transports = 50;
        fromWorld.Cargo.Metals = 10; // less than requested — CarryCargo clamps to what's actually there
        fromWorld.Cargo.Trillum = 1000; // enough range that DeployFleet's own EDA>Range abort check doesn't fire
        var toTarget = new Planet { Location = new Coordinate(2, 0), Owner = owner, Type = WorldType.Base };
        galaxy.Planets.Add(fromWorld);
        galaxy.Planets.Add(toTarget);

        var requestedCargo = new CargoHold { Metals = 100 };
        var fleetStates = new Dictionary<Fleet, KingdomFleetState>();
        NpeToolkit.DeployCargoFleet(owner, fleetStates, fromWorld, requestedCargo, carryCargo: true, NpeMissionType.Supply, toTarget, game);

        await Assert.That(galaxy.Fleets).Count().IsEqualTo(1);
        var fleet = galaxy.Fleets[0];
        await Assert.That(fleet.Cargo.Metals).IsEqualTo(10); // clamped to what fromWorld actually had
        await Assert.That(fleetStates[fleet].Mission).IsEqualTo(NpeMissionType.Supply);
    }

    [Test]
    public async Task DeployJumpAttack_PicksBestTargetAndDeploysFromRegionalCapital()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var enemy = NewEmpire("Enemy");
        game.Empires.Add(owner);
        game.Empires.Add(enemy);

        var capital = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Capital };
        // JumpAttackMSN's GetFleetComposition sequence only ever draws Jumpships/HunterKillers
        // (NPEINTR.PAS's JSeq) — Starships alone would leave nothing to compose a fleet from. 2000
        // each so MilitaryPower clears the fleetPower/2 qualification threshold (~15000).
        capital.Ships.Jumpships = 2000;
        capital.Ships.HunterKillers = 2000;
        capital.Cargo.Trillum = 1000; // enough range that DeployFleet's own EDA>Range abort check doesn't fire
        var target = new Planet { Location = new Coordinate(5, 0), Owner = enemy, Type = WorldType.Agricultural, Population = 1000 };
        galaxy.Planets.Add(capital);
        galaxy.Planets.Add(target);
        owner.Planets.MarkScouted(target);

        var persona = new NpeCharacter { WorldPower = 50 };
        var fleetStates = new Dictionary<Fleet, KingdomFleetState>();
        var regionCapitals = NpeToolkit.CreateRegionArray(owner, game);

        NpeToolkit.DeployJumpAttack(owner, enemy, regionCapitals, persona, fleetStates, game, new FixedRandom(0));

        await Assert.That(galaxy.Fleets).Count().IsEqualTo(1);
        await Assert.That(fleetStates[galaxy.Fleets[0]].Mission).IsEqualTo(NpeMissionType.JumpAttack);
        await Assert.That(fleetStates[galaxy.Fleets[0]].Target).IsEqualTo(target);
    }

    [Test]
    public async Task DeployJumpAttack_NoKnownEnemyTargets_DeploysNothing()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var enemy = NewEmpire("Enemy");
        game.Empires.Add(owner);
        game.Empires.Add(enemy);

        var capital = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Capital };
        galaxy.Planets.Add(capital);
        // enemy has no planets at all

        var persona = new NpeCharacter { WorldPower = 50 };
        var fleetStates = new Dictionary<Fleet, KingdomFleetState>();
        var regionCapitals = NpeToolkit.CreateRegionArray(owner, game);

        NpeToolkit.DeployJumpAttack(owner, enemy, regionCapitals, persona, fleetStates, game, new FixedRandom(0));

        await Assert.That(galaxy.Fleets).IsEmpty();
    }

    [Test]
    public async Task DeploySlowAttack_FallbackBranch_UsesJumpAttackMissionVerbatimQuirk()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var enemy = NewEmpire("Enemy");
        game.Empires.Add(owner);
        game.Empires.Add(enemy);

        // The nearest (only) regional capital is deliberately too weak to qualify (no ships), forcing
        // the GetBestBase fallback branch — the one that deploys with JumpAttackMSN instead of
        // SlowAttackMSN (NPEINTR.PAS:883's real copy/paste quirk, ported verbatim). strongerWorld is
        // NOT WorldType.Base/Capital, so CreateRegionArray never picks it up as a region capital in
        // the first place — GetBestBase finds it anyway, since it scans every owned planet.
        var weakCapital = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Capital };
        // Nonzero but negligible: AverageMilitaryPower(regionCapitals) (GetBestTarget's own basePower
        // ceiling) must stay above zero, or GetBestTarget's "defense < basePower" gate rejects even an
        // undefended target outright — a completely powerless region capital breaks target selection
        // itself, not just the qualification check this test is actually about.
        weakCapital.Ships.Fighters = 10;
        var strongerWorld = new Planet { Location = new Coordinate(1, 0), Owner = owner, Type = WorldType.Agricultural };
        // The fallback branch deploys with JumpAttackMSN (the quirk this test is about), whose
        // GetFleetComposition sequence only ever draws Jumpships/HunterKillers. Large enough that
        // MilitaryPower clears DeploySlowAttack's fleetPower/2 qualification threshold (~25000).
        strongerWorld.Ships.Jumpships = 4000;
        strongerWorld.Ships.HunterKillers = 4000;
        strongerWorld.Cargo.Legions = 1000;
        strongerWorld.Cargo.Trillum = 1000; // enough range that DeployFleet's own EDA>Range abort check doesn't fire
        var target = new Planet { Location = new Coordinate(5, 0), Owner = enemy, Type = WorldType.Agricultural, Population = 1000 };
        galaxy.Planets.Add(weakCapital);
        galaxy.Planets.Add(strongerWorld);
        galaxy.Planets.Add(target);
        owner.Planets.MarkScouted(target);

        var persona = new NpeCharacter { WorldPower = 50 };
        var fleetStates = new Dictionary<Fleet, KingdomFleetState>();
        var regionCapitals = NpeToolkit.CreateRegionArray(owner, game); // only weakCapital

        NpeToolkit.DeploySlowAttack(owner, enemy, regionCapitals, persona, fleetStates, game, new FixedRandom(0));

        await Assert.That(galaxy.Fleets).Count().IsEqualTo(1);
        await Assert.That(fleetStates[galaxy.Fleets[0]].Mission).IsEqualTo(NpeMissionType.JumpAttack); // not SlowAttack
        await Assert.That(fleetStates[galaxy.Fleets[0]].HomeBase).IsEqualTo(strongerWorld);
    }

    [Test]
    public async Task SetFleetReturn_SetsMissionTargetAndDestination()
    {
        var owner = NewEmpire("Owner");
        var fleet = new Fleet { Location = new Coordinate(5, 5), Owner = owner };
        var homeBase = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Base };
        var fleetStates = new Dictionary<Fleet, KingdomFleetState> { [fleet] = new KingdomFleetState { Mission = NpeMissionType.Conquer } };

        NpeToolkit.SetFleetReturn(fleet, homeBase, fleetStates);

        await Assert.That(fleetStates[fleet].Mission).IsEqualTo(NpeMissionType.Return);
        await Assert.That(fleetStates[fleet].Target).IsEqualTo(homeBase);
        await Assert.That(fleet.Destination).IsEqualTo(new Coordinate(0, 0));
    }

    [Test]
    public async Task DestroyAllFleetsInSector_EngagesWeakerScoutedEnemyFleets()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var enemy = NewEmpire("Enemy");
        game.Empires.Add(owner);
        game.Empires.Add(enemy);

        // CalculateCombatData's own precondition (GetCapital) — a real live empire always has one.
        var ownerCapital = new Planet { Location = new Coordinate(9, 9), Owner = owner, Type = WorldType.Capital, TechLevel = TechLevel.Jump };
        owner.Capital = ownerCapital;
        galaxy.Planets.Add(ownerCapital);

        var attacker = new Fleet { Location = new Coordinate(0, 0), Owner = owner };
        attacker.Ships.Starships = 100;
        var weakEnemy = new Fleet { Location = new Coordinate(0, 0), Owner = enemy };
        weakEnemy.Ships.Fighters = 1;
        galaxy.Fleets.Add(attacker);
        galaxy.Fleets.Add(weakEnemy);
        owner.Fleets.MarkScouted(weakEnemy);

        var attackerPower = NpeToolkit.MilitaryPower(attacker.Ships, new DefenseCounts());
        var allDestroyed = NpeToolkit.DestroyAllFleetsInSector(owner, attacker, attackerPower, game, new FixedRandom(0));

        await Assert.That(allDestroyed).IsTrue();
        await Assert.That(galaxy.Fleets).DoesNotContain(weakEnemy);
    }

    [Test]
    public async Task DestroyAllFleetsInSector_UnscoutedEnemyFleet_IsIgnored()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var enemy = NewEmpire("Enemy");
        game.Empires.Add(owner);
        game.Empires.Add(enemy);

        var attacker = new Fleet { Location = new Coordinate(0, 0), Owner = owner };
        attacker.Ships.Starships = 100;
        var unscoutedEnemy = new Fleet { Location = new Coordinate(0, 0), Owner = enemy };
        unscoutedEnemy.Ships.Fighters = 1;
        galaxy.Fleets.Add(attacker);
        galaxy.Fleets.Add(unscoutedEnemy);
        // deliberately not marking unscoutedEnemy as scouted

        var attackerPower = NpeToolkit.MilitaryPower(attacker.Ships, new DefenseCounts());
        var allDestroyed = NpeToolkit.DestroyAllFleetsInSector(owner, attacker, attackerPower, game, new FixedRandom(0));

        await Assert.That(allDestroyed).IsTrue(); // no unscouted/unengaged enemy counts against it
        await Assert.That(galaxy.Fleets).Contains(unscoutedEnemy); // never touched
    }

    [Test]
    public async Task ImplementReturnMSN_AbortsAndDestroysFleet()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = owner };
        fleet.Ships.Fighters = 3;
        var homeBase = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Base };
        galaxy.Fleets.Add(fleet);
        galaxy.Planets.Add(homeBase);

        NpeToolkit.ImplementReturnMSN(fleet, homeBase, game);

        await Assert.That(galaxy.Fleets).DoesNotContain(fleet);
        await Assert.That(homeBase.Ships.Fighters).IsEqualTo(3);
    }

    [Test]
    public async Task ImplementSupplyMSN_DumpsCargoAndReturns()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = owner };
        fleet.Cargo.Metals = 20;
        var target = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Base };
        var homeBase = new Planet { Location = new Coordinate(1, 1), Owner = owner, Type = WorldType.Base };
        galaxy.Fleets.Add(fleet);
        galaxy.Planets.Add(target);
        galaxy.Planets.Add(homeBase);

        var fleetStates = new Dictionary<Fleet, KingdomFleetState> { [fleet] = new KingdomFleetState { Mission = NpeMissionType.Supply } };
        NpeToolkit.ImplementSupplyMSN(fleet, target, homeBase, fleetStates, game);

        await Assert.That(target.Cargo.Metals).IsEqualTo(20);
        await Assert.That(fleet.Cargo.Metals).IsEqualTo(0);
        await Assert.That(fleetStates[fleet].Mission).IsEqualTo(NpeMissionType.Return);
    }

    [Test]
    public async Task ImplementRefuelMSN_DissolvesFleetAndRefuelsTarget()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = owner };
        var target = new Fleet { Location = new Coordinate(0, 0), Owner = owner, Fuel = 0.0 };
        target.Ships.Starships = 10;
        galaxy.Fleets.Add(fleet);
        galaxy.Fleets.Add(target);
        target.Cargo.Trillum = 1000;

        NpeToolkit.ImplementRefuelMSN(fleet, target, game);

        await Assert.That(galaxy.Fleets).DoesNotContain(fleet);
        await Assert.That(target.Fuel).IsGreaterThan(0);
    }

    [Test]
    public async Task ImplementConquerMSN_UndefendedIndependentTarget_ConquersAndReturns()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var fleet = new Fleet { Location = new Coordinate(5, 5), Owner = owner };
        fleet.Ships.Starships = 100;
        fleet.Ships.Transports = 20; // carries the troops to ground — cargo alone can't capture a world
        fleet.Cargo.Legions = 100; // ground troops — EnemySurrenders needs real capture strength (plGat) to actually conquer a world, not just ships
        var target = new Planet { Location = new Coordinate(5, 5), Owner = Empire.Independent, Type = WorldType.Independent };
        var homeBase = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Capital, TechLevel = TechLevel.Jump };
        owner.Capital = homeBase; // CalculateCombatData's own precondition (GetCapital)
        galaxy.Fleets.Add(fleet);
        galaxy.Planets.Add(target);
        galaxy.Planets.Add(homeBase);

        var fleetStates = new Dictionary<Fleet, KingdomFleetState> { [fleet] = new KingdomFleetState { Mission = NpeMissionType.Conquer, Target = target } };
        var result = NpeToolkit.ImplementConquerMSN(owner, fleet, target, homeBase, fleetStates, game, new FixedRandom(0));

        await Assert.That(result).IsEqualTo(AttackResultType.DefenderConquered);
        await Assert.That(fleetStates[fleet].Mission).IsEqualTo(NpeMissionType.Return);
    }

    [Test]
    public async Task ImplementRaidTrnMSN_StripsBootyExceptHunterKillers()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = owner };
        fleet.Ships.HunterKillers = 5;
        fleet.Ships.Transports = 3; // booty picked up during the raid
        fleet.Cargo.Metals = 10;
        var homeBase = new Planet { Location = new Coordinate(1, 1), Owner = owner, Type = WorldType.Base };
        galaxy.Fleets.Add(fleet);
        galaxy.Planets.Add(homeBase);

        var fleetStates = new Dictionary<Fleet, KingdomFleetState> { [fleet] = new KingdomFleetState { Mission = NpeMissionType.RaidTransports, Waiting = 0 } };
        NpeToolkit.ImplementRaidTrnMSN(owner, fleet, homeBase, fleetStates, game, new FixedRandom(0));

        await Assert.That(fleet.Ships.HunterKillers).IsEqualTo(5); // survives
        await Assert.That(fleet.Ships.Transports).IsEqualTo(0); // stripped
        await Assert.That(fleet.Cargo.Metals).IsEqualTo(0); // stripped
        await Assert.That(fleetStates[fleet].Waiting).IsEqualTo(1);
    }

    [Test]
    public async Task ImplementRaidTrnMSN_WaitingHitsFive_ReturnsHome()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = owner };
        fleet.Ships.HunterKillers = 5;
        var homeBase = new Planet { Location = new Coordinate(1, 1), Owner = owner, Type = WorldType.Base };
        galaxy.Fleets.Add(fleet);
        galaxy.Planets.Add(homeBase);

        var fleetStates = new Dictionary<Fleet, KingdomFleetState> { [fleet] = new KingdomFleetState { Mission = NpeMissionType.RaidTransports, Waiting = 5 } };
        NpeToolkit.ImplementRaidTrnMSN(owner, fleet, homeBase, fleetStates, game, new FixedRandom(0));

        await Assert.That(fleetStates[fleet].Mission).IsEqualTo(NpeMissionType.Return);
    }

    [Test]
    public async Task ImplementJumpAttackMSN_ClearSectorAndOutgunned_Conquers()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var enemy = NewEmpire("Enemy");
        game.Empires.Add(owner);
        game.Empires.Add(enemy);

        var fleet = new Fleet { Location = new Coordinate(5, 5), Owner = owner };
        fleet.Ships.Starships = 1000;
        fleet.Ships.Transports = 20; // carries the troops to ground — cargo alone can't capture a world
        fleet.Cargo.Legions = 100; // ground troops — EnemySurrenders needs real capture strength (plGat) to actually conquer a world, not just ships
        var target = new Planet { Location = new Coordinate(5, 5), Owner = enemy, Type = WorldType.Agricultural };
        var homeBase = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Capital, TechLevel = TechLevel.Jump };
        owner.Capital = homeBase; // CalculateCombatData's own precondition (GetCapital)
        galaxy.Fleets.Add(fleet);
        galaxy.Planets.Add(target);
        galaxy.Planets.Add(homeBase);

        var result = NpeToolkit.ImplementJumpAttackMSN(owner, fleet, target, homeBase, game, new FixedRandom(0));

        await Assert.That(result).IsEqualTo(AttackResultType.DefenderConquered);
    }

    [Test]
    public async Task ImplementJumpAttackMSN_OwnTarget_ReturnsNoAttack()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var fleet = new Fleet { Location = new Coordinate(5, 5), Owner = owner };
        var target = new Planet { Location = new Coordinate(5, 5), Owner = owner, Type = WorldType.Base };
        var homeBase = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Base };
        galaxy.Fleets.Add(fleet);
        galaxy.Planets.Add(target);
        galaxy.Planets.Add(homeBase);

        var result = NpeToolkit.ImplementJumpAttackMSN(owner, fleet, target, homeBase, game, new FixedRandom(0));

        await Assert.That(result).IsEqualTo(AttackResultType.None);
    }

    [Test]
    public async Task ImplementJumpAttackMSN_LamStrikeFires_ZeroesEnemyShipsInPowerGate()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var enemy = NewEmpire("Enemy");
        game.Empires.Add(owner);
        game.Empires.Add(enemy);

        var fleet = new Fleet { Location = new Coordinate(5, 5), Owner = owner };
        fleet.Ships.Starships = 50;
        var target = new Planet { Location = new Coordinate(5, 5), Owner = enemy, Type = WorldType.Agricultural };
        target.Ships.Starships = 1_000_000; // would clearly outgun the attacker if the power gate saw real ships
        target.Defenses[DefenseType.DefenseSatellite] = 10;
        target.Defenses[DefenseType.IonCannon] = 10;
        target.Defenses[DefenseType.Gdm] = 10;
        var homeBase = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Capital, TechLevel = TechLevel.Jump };
        homeBase.Defenses[DefenseType.Lam] = 1000; // >500 and within 5 sectors of target — LAM branch fires
        owner.Capital = homeBase;
        galaxy.Fleets.Add(fleet);
        galaxy.Planets.Add(target);
        galaxy.Planets.Add(homeBase);

        var result = NpeToolkit.ImplementJumpAttackMSN(owner, fleet, target, homeBase, game, new FixedRandom(0));

        // lamsToUse = min(1000, 2*10 + 10 + 10/2) = 35
        await Assert.That(homeBase.Defenses[DefenseType.Lam]).IsEqualTo(965);
        // NPEINTR.PAS:1318,1325's verbatim quirk: EnemySh is clobbered to zero by LAMAttack's VAR
        // ShipsDest out-param (never written on the world-target branch), so the power gate ignores
        // the target's real 1,000,000 ships and the attack proceeds instead of bailing out with
        // "world too powerful" (which real ship counts would otherwise force).
        await Assert.That(result).IsNotEqualTo(AttackResultType.None);
    }

    [Test]
    public async Task ImplementGuardMSN_MergesShipsIntoBase()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = owner };
        fleet.Ships.Fighters = 10;
        var baseWorld = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Base };
        baseWorld.Ships.Fighters = 5;
        galaxy.Fleets.Add(fleet);
        galaxy.Planets.Add(baseWorld);

        NpeToolkit.ImplementGuardMSN(fleet, baseWorld, game);

        await Assert.That(baseWorld.Ships.Fighters).IsEqualTo(15);
        await Assert.That(galaxy.Fleets).DoesNotContain(fleet); // donated everything -> self-destructs
    }

    [Test]
    public async Task ImplementStackMSN_DumpsOntoExistingGuardThenBecomesGuardIfRoomAllows()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = owner };
        fleet.Ships.Fighters = 5;
        var guard = new Fleet { Location = new Coordinate(0, 0), Owner = owner };
        guard.Ships.Fighters = 3;
        galaxy.Fleets.Add(fleet);
        galaxy.Fleets.Add(guard);

        var fleetStates = new Dictionary<Fleet, KingdomFleetState> {
            [fleet] = new KingdomFleetState { Mission = NpeMissionType.Stack },
            [guard] = new KingdomFleetState { Mission = NpeMissionType.Guard },
        };

        NpeToolkit.ImplementStackMSN(fleet, fleetStates, game);

        await Assert.That(guard.Ships.Fighters).IsEqualTo(8); // 3 + 5, all fit under the 9999 cap
        await Assert.That(galaxy.Fleets).DoesNotContain(fleet); // donated everything -> self-destructs, never reaches the become-guard branch
    }

    [Test]
    public async Task ImplementStackMSN_NoGuardsAndUnderCap_BecomesGuard()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = owner };
        fleet.Ships.Fighters = 5;
        galaxy.Fleets.Add(fleet);

        var fleetStates = new Dictionary<Fleet, KingdomFleetState> { [fleet] = new KingdomFleetState { Mission = NpeMissionType.Stack } };
        NpeToolkit.ImplementStackMSN(fleet, fleetStates, game);

        await Assert.That(fleetStates[fleet].Mission).IsEqualTo(NpeMissionType.Guard);
        await Assert.That(galaxy.Fleets).Contains(fleet);
    }
}
