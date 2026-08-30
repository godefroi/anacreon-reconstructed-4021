using ThreeLn.Reconstruction4021.Core;
using ThreeLn.Reconstruction4021.Core.Combat;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.NewGame;
using ThreeLn.Reconstruction4021.Core.Npe;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests;

/// <summary>
/// <see cref="Core.Npe.NpeToolkit"/>'s read-and-compute methods (targeting, regional bookkeeping,
/// world designation). Hardcoded against real Game/Galaxy fixtures rather than golden-file,
/// deliberately: every method here either has no Rnd/Random call, or draws inside a
/// scan-every-planet loop whose count depends on live galaxy shape — building a golden case that
/// means anything close to real play needs a full hand-assembled universe, and this port's
/// SaveFormat layer could make that cheap.
/// </summary>
public class NpeToolkitTests
{
    private static Empire NewEmpire(string name) =>
        EmpireFactory.CreateEmpire(name, null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);

    [Test]
    public async Task MilitaryPower_SumsShipsAndDefensesByCombatPower()
    {
        var ships = new ShipCounts { Fighters = 10, Starships = 2 };
        var defenses = new DefenseCounts { Lams = 5 };

        var power = NpeToolkit.MilitaryPower(ships, defenses);

        // MPower (DATACNST.PAS:167-170) — the table MilitaryPower actually weights by, not
        // CombatConstants.CombatPower (ATTACK.PAS's own, differently-valued table for the surrender
        // algorithm — see CombatConstants.MPower's own doc comment).
        var expected = 10L * CombatConstants.MPower[AttackType.Fighter]
                      + 2L * CombatConstants.MPower[AttackType.Starship]
                      + 5L * CombatConstants.MPower[AttackType.Lam];
        await Assert.That(power).IsEqualTo(expected);
    }

    [Test]
    public async Task GetFleetComposition_ConquerMission_TakesJumpshipsFirst()
    {
        var owner = NewEmpire("Owner");
        var source = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Base };
        source.Ships.Jumpships = 100;
        source.Ships.Fighters = 100;

        // A tiny power budget: JSeq (jmp,hkr,pen,ssp,fgt) tries jmp first, and with 100 available it
        // easily covers this without spilling into fgt — a larger budget would legitimately spill
        // into fighters once jumpships hit their 100-ship supply cap, which isn't the case this test
        // is after.
        var (ships, cargo) = NpeToolkit.GetFleetComposition(source, power: 10, gat: 0, NpeMissionType.Conquer);

        await Assert.That(ships.Jumpships).IsGreaterThan(0);
        await Assert.That(ships.Fighters).IsEqualTo(0);
        await Assert.That(cargo[CargoType.Trillum]).IsEqualTo(0); // not fighters-only
    }

    [Test]
    public async Task GetFleetComposition_FightersOnly_GetsTrillumStash()
    {
        var owner = NewEmpire("Owner");
        var source = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Base };
        source.Ships.Fighters = 50;

        var (ships, cargo) = NpeToolkit.GetFleetComposition(source, power: 1000, gat: 0, NpeMissionType.Conquer);

        await Assert.That(ships.Fighters).IsGreaterThan(0);
        await Assert.That(cargo[CargoType.Trillum]).IsEqualTo(10);
    }

    [Test]
    public async Task AlreadyTargetted_MatchesOnMissionAndTarget()
    {
        var owner = NewEmpire("Owner");
        var target = new Planet { Location = new Coordinate(1, 1), Owner = Empire.Independent, Type = WorldType.Independent };
        var otherTarget = new Planet { Location = new Coordinate(2, 2), Owner = Empire.Independent, Type = WorldType.Independent };
        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = owner };

        var states = new Dictionary<Fleet, KingdomFleetState> {
            [fleet] = new KingdomFleetState { Mission = NpeMissionType.Conquer, Target = target },
        };

        await Assert.That(NpeToolkit.AlreadyTargetted(states, target, NpeMissionType.Conquer)).IsTrue();
        await Assert.That(NpeToolkit.AlreadyTargetted(states, otherTarget, NpeMissionType.Conquer)).IsFalse();
        await Assert.That(NpeToolkit.AlreadyTargetted(states, target, NpeMissionType.Return)).IsFalse();
    }

    [Test]
    public async Task GetPotentialRes_IncludesReturningFleetsBoundForThisWorld()
    {
        var owner = NewEmpire("Owner");
        var world = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Base };
        world.Ships.Fighters = 5;

        var returningFleet = new Fleet { Location = new Coordinate(1, 1), Owner = owner };
        returningFleet.Ships.Fighters = 3;
        var otherFleet = new Fleet { Location = new Coordinate(2, 2), Owner = owner };
        otherFleet.Ships.Fighters = 100; // not returning here — must not be counted

        var states = new Dictionary<Fleet, KingdomFleetState> {
            [returningFleet] = new KingdomFleetState { Mission = NpeMissionType.Return, Target = world },
            [otherFleet] = new KingdomFleetState { Mission = NpeMissionType.Guard, Target = world },
        };

        var (ships, _) = NpeToolkit.GetPotentialRes(world, states);

        await Assert.That(ships.Fighters).IsEqualTo(8);
    }

    [Test]
    public async Task MinimumDefense_HigherNearHostileBase()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Game(galaxy);
        var owner = NewEmpire("Owner");
        var enemy = NewEmpire("Enemy");
        game.Empires.Add(owner);
        game.Empires.Add(enemy);

        var persona = new NpeCharacter { Defensive = 10 };
        var world = new Planet { Location = new Coordinate(50, 50), Owner = owner, Type = WorldType.Capital, Population = 1000 };
        galaxy.Planets.Add(world);

        var baseline = NpeToolkit.MinimumDefense(world, persona, game);

        var hostileBase = new Planet { Location = new Coordinate(51, 50), Owner = enemy, Type = WorldType.Base };
        galaxy.Planets.Add(hostileBase);

        var withHostileNeighbor = NpeToolkit.MinimumDefense(world, persona, game);

        // x3 (nearby enemy) x2 (that enemy world is a Base) = x6 over baseline.
        await Assert.That(withHostileNeighbor).IsEqualTo(baseline * 6);
    }

    [Test]
    public async Task AverageMilitaryPower_EmptyRegionsReturnsZero()
    {
        await Assert.That(NpeToolkit.AverageMilitaryPower([])).IsEqualTo(0L);
    }

    [Test]
    public async Task AverageMilitaryPower_IgnoresDefensesOnlyCountsShips()
    {
        var owner = NewEmpire("Owner");
        var capital = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Capital };
        capital.Ships.Fighters = 10;
        capital.Defenses.Lams = 1000; // must not be counted — see this method's own doc comment

        var avg = NpeToolkit.AverageMilitaryPower([capital]);

        await Assert.That(avg).IsEqualTo(10L * CombatConstants.MPower[AttackType.Fighter]);
    }

    [Test]
    public async Task GetBestBase_PicksNearestQualifyingOwnedWorld()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Game(galaxy);
        var owner = NewEmpire("Owner");
        game.Empires.Add(owner);

        var target = new Planet { Location = new Coordinate(50, 50), Owner = Empire.Independent, Type = WorldType.Independent };

        var tooWeak = new Planet { Location = new Coordinate(49, 50), Owner = owner, Type = WorldType.Base };
        tooWeak.Ships.Fighters = 1; // negligible power, fails the power/2 check

        var farButStrong = new Planet { Location = new Coordinate(40, 50), Owner = owner, Type = WorldType.Base };
        farButStrong.Ships.Starships = 100;
        farButStrong.Cargo[CargoType.Legion] = 100;

        galaxy.Planets.Add(tooWeak);
        galaxy.Planets.Add(farButStrong);

        var best = NpeToolkit.GetBestBase(owner, target, fleetPower: 100, fleetGat: 10, game);

        await Assert.That(best).IsEqualTo(farButStrong);
    }

    [Test]
    public async Task GetBestPlanetToProtect_DefaultsToCapitalWhenNothingQualifies()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Game(galaxy);
        var owner = NewEmpire("Owner");
        var capital = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Capital };
        owner.Capital = capital;
        var baseWorld = new Planet { Location = new Coordinate(1, 1), Owner = owner, Type = WorldType.Base };
        galaxy.Planets.Add(capital);
        galaxy.Planets.Add(baseWorld);

        var best = NpeToolkit.GetBestPlanetToProtect(baseWorld, game);

        await Assert.That(best).IsEqualTo(capital);
    }

    [Test]
    public async Task GetBestPlanetToProtect_PicksWeakestNearbyNonBaseWorld()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Game(galaxy);
        var owner = NewEmpire("Owner");
        var capital = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Capital };
        owner.Capital = capital;
        var baseWorld = new Planet { Location = new Coordinate(10, 10), Owner = owner, Type = WorldType.Base };

        var weak = new Planet { Location = new Coordinate(11, 10), Owner = owner, Type = WorldType.Agricultural };
        weak.Defenses.Gdms = 1;
        var strong = new Planet { Location = new Coordinate(10, 11), Owner = owner, Type = WorldType.Agricultural };
        strong.Defenses.Gdms = 1000;

        galaxy.Planets.Add(capital);
        galaxy.Planets.Add(baseWorld);
        galaxy.Planets.Add(weak);
        galaxy.Planets.Add(strong);

        var best = NpeToolkit.GetBestPlanetToProtect(baseWorld, game);

        await Assert.That(best).IsEqualTo(weak);
    }

    [Test]
    public async Task GetBestRaiderTarget_LowRollFindsKnownEnemyGate()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Game(galaxy);
        var emp = NewEmpire("Scout");
        var enemy = NewEmpire("Enemy");

        var gate = new Stargate { Location = new Coordinate(5, 5), Owner = enemy, Kind = StargateKind.Gate };
        galaxy.Stargates.Add(gate);
        emp.Stargates.MarkKnown(gate);

        var best = NpeToolkit.GetBestRaiderTarget(emp, enemy, game, new FixedRandom(0)); // Rnd(1,100)=1, always <50/<25

        await Assert.That(best).IsEqualTo(gate);
    }

    [Test]
    public async Task GetBestRaiderTarget_HighRollFindsNothing()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Game(galaxy);
        var emp = NewEmpire("Scout");
        var enemy = NewEmpire("Enemy");

        var gate = new Stargate { Location = new Coordinate(5, 5), Owner = enemy, Kind = StargateKind.Gate };
        galaxy.Stargates.Add(gate);
        emp.Stargates.MarkKnown(gate);

        var best = NpeToolkit.GetBestRaiderTarget(emp, enemy, game, new FixedRandom(99)); // Rnd(1,100)=100, never <50/<25

        await Assert.That(best).IsNull();
    }

    [Test]
    public async Task GetBestTarget_PicksTheOnlyKnownNotAlreadyTargetedCandidate()
    {
        var emp = NewEmpire("Scout");
        var enemy = NewEmpire("Enemy");
        var persona = new NpeCharacter { WorldPower = 40 };

        var candidate = new Planet { Location = new Coordinate(0, 0), Owner = enemy, Type = WorldType.Agricultural, Population = 1000, TechLevel = TechLevel.Jump };
        emp.Planets.MarkKnown(candidate);

        var (target, defense, men) = NpeToolkit.GetBestTarget(
            emp, [candidate], basePower: 1_000_000, persona, new Dictionary<Fleet, KingdomFleetState>(), new FixedRandom(0, 0.5));

        await Assert.That(target).IsEqualTo(candidate);
        await Assert.That(defense).IsEqualTo(0L);
        await Assert.That(men).IsEqualTo(10L); // Cr[men]=0, 4*Cr[nnj]=0, +10 base
    }

    [Test]
    public async Task GetBestTarget_SkipsUnknownAndAlreadyTargeted()
    {
        var emp = NewEmpire("Scout");
        var enemy = NewEmpire("Enemy");
        var persona = new NpeCharacter { WorldPower = 40 };

        var unknown = new Planet { Location = new Coordinate(0, 0), Owner = enemy, Type = WorldType.Agricultural, Population = 1000 };
        var alreadyTargeted = new Planet { Location = new Coordinate(1, 1), Owner = enemy, Type = WorldType.Agricultural, Population = 1000 };
        emp.Planets.MarkKnown(alreadyTargeted);

        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = emp };
        var states = new Dictionary<Fleet, KingdomFleetState> {
            [fleet] = new KingdomFleetState { Mission = NpeMissionType.Conquer, Target = alreadyTargeted },
        };

        var (target, _, _) = NpeToolkit.GetBestTarget(emp, [unknown, alreadyTargeted], basePower: 1_000_000, persona, states, new FixedRandom(0, 0.5));

        await Assert.That(target).IsNull();
    }

    [Test]
    public async Task GetRegionalCapital_PicksNearestAndNullWhenEmpty()
    {
        var subject = new Planet { Location = new Coordinate(0, 0), Owner = Empire.Independent, Type = WorldType.Independent };
        var near = new Planet { Location = new Coordinate(1, 0), Owner = Empire.Independent, Type = WorldType.Base };
        var far = new Planet { Location = new Coordinate(10, 0), Owner = Empire.Independent, Type = WorldType.Base };

        var best = NpeToolkit.GetRegionalCapital(subject, [far, near]);
        await Assert.That(best).IsEqualTo(near);

        await Assert.That(NpeToolkit.GetRegionalCapital(subject, [])).IsNull();
    }

    [Test]
    public async Task CreateRegionArray_OnlyOwnedBaseOrCapitalPlanets()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Game(galaxy);
        var owner = NewEmpire("Owner");
        var other = NewEmpire("Other");

        var capital = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Capital };
        var baseWorld = new Planet { Location = new Coordinate(1, 1), Owner = owner, Type = WorldType.Base };
        var ordinaryWorld = new Planet { Location = new Coordinate(2, 2), Owner = owner, Type = WorldType.Agricultural };
        var foreignBase = new Planet { Location = new Coordinate(3, 3), Owner = other, Type = WorldType.Base };

        galaxy.Planets.Add(capital);
        galaxy.Planets.Add(baseWorld);
        galaxy.Planets.Add(ordinaryWorld);
        galaxy.Planets.Add(foreignBase);

        var regions = NpeToolkit.CreateRegionArray(owner, game);

        await Assert.That(regions).Contains(capital);
        await Assert.That(regions).Contains(baseWorld);
        await Assert.That(regions).DoesNotContain(ordinaryWorld);
        await Assert.That(regions).DoesNotContain(foreignBase);
    }

    [Test]
    public async Task GetNewDesignation_AmbrosiaClassWorldAlwaysBecomesAmbrosia()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Game(galaxy);
        var owner = NewEmpire("Owner");
        game.Empires.Add(owner);

        var world = new Planet {
            Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Agricultural,
            Class = WorldClass.Ambrosia, TechLevel = TechLevel.Bio,
        };
        galaxy.Planets.Add(world);

        var newType = NpeToolkit.GetNewDesignation(world, [], game, new FixedRandom(0, 0.99));

        await Assert.That(newType).IsEqualTo(WorldType.Ambrosia);
    }

    [Test]
    public async Task EnforceNpeDataLinks_DropsStateForFleetsNoLongerInGalaxy()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Game(galaxy);
        var owner = NewEmpire("Owner");

        var aliveFleet = new Fleet { Location = new Coordinate(0, 0), Owner = owner };
        var destroyedFleet = new Fleet { Location = new Coordinate(1, 1), Owner = owner };
        galaxy.Fleets.Add(aliveFleet); // destroyedFleet deliberately not added — simulates DestroyFleet having removed it

        var states = new Dictionary<Fleet, KingdomFleetState> {
            [aliveFleet] = new KingdomFleetState { Mission = NpeMissionType.Guard },
            [destroyedFleet] = new KingdomFleetState { Mission = NpeMissionType.Return },
        };

        NpeToolkit.EnforceNpeDataLinks(states, game);

        await Assert.That(states).ContainsKey(aliveFleet);
        await Assert.That(states).DoesNotContainKey(destroyedFleet);
    }

    [Test]
    public async Task SetEmpireDefenses_LowRoll_UsesInitDefenseRecordAndZerosStarbaseDist()
    {
        var owner = NewEmpire("Owner");
        owner.DefenseSettings.Starbases.Ground.Fighters = 42; // must be zeroed regardless of roll

        NpeToolkit.SetEmpireDefenses(owner, new FixedRandom(0)); // Rnd(1,100)=1 -> InitDefenseRecord

        await Assert.That(owner.DefenseSettings.Fleets.Ground.Jumptransports).IsEqualTo(100);
        await Assert.That(owner.DefenseSettings.Starbases.Ground.Fighters).IsEqualTo(0);
    }

    [Test]
    public async Task PlunderWorld_TransfersEverythingAndSetsTargetIndependent()
    {
        var owner = NewEmpire("Owner");
        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = owner };
        fleet.Ships.Fighters = 5;
        fleet.Ships.Transports = 10; // real cargo capacity — BalanceFleet would otherwise strip a fighter-only fleet's cargo to zero

        var target = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Base };
        target.Ships.Fighters = 3;
        target.Cargo[CargoType.Metals] = 7;

        NpeToolkit.PlunderWorld(fleet, target);

        await Assert.That(fleet.Ships.Fighters).IsEqualTo(8);
        await Assert.That(fleet.Cargo[CargoType.Metals]).IsEqualTo(7);
        await Assert.That(target.Ships.Fighters).IsEqualTo(0);
        await Assert.That(target.Cargo[CargoType.Metals]).IsEqualTo(0);
        await Assert.That(target.Type).IsEqualTo(WorldType.Independent);
        await Assert.That(target.Owner).IsEqualTo(Empire.Independent);
    }

    [Test]
    public async Task PlunderWorld_OwnerMismatch_NoOp()
    {
        var owner = NewEmpire("Owner");
        var otherOwner = NewEmpire("Other");
        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = owner };
        var target = new Planet { Location = new Coordinate(0, 0), Owner = otherOwner, Type = WorldType.Base };
        target.Ships.Fighters = 3;

        NpeToolkit.PlunderWorld(fleet, target);

        await Assert.That(target.Ships.Fighters).IsEqualTo(3);
        await Assert.That(target.Owner).IsEqualTo(otherOwner);
    }
}
