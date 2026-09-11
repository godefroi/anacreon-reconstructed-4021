using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Types;
using Reconstructed4021.LegacyNpe;

namespace Reconstructed4021.Tests;

/// <summary>
/// <see cref="BerserkerTurnHandler.PlayTurn"/> — a whole NPE turn end to end (PirateTurnHandlerTests'
/// own style). Unlike Kingdom/Pirate, a fresh mission a base picks (NewBerserkerBaseTarget) is never
/// acted on the same turn it's picked (UpdateFleets runs before UpdateBases each turn, and a base
/// with no existing state just seeds one and moves on — see PlayTurn's own doc comment), so most
/// scenarios here need more than one PlayTurn call, same as Pirate's own give-up/return sequences.
///
/// <see cref="FixedRandom"/> with a fixed low roll always resolves <c>Rnd(lo,hi)</c> to <c>lo</c> —
/// every ship-count/coordinate roll below is computed from that, not guessed.
/// </summary>
public class BerserkerTurnHandlerTests
{
    private static (Game Game, Galaxy Galaxy) NewGame()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Game(galaxy);
        return (game, galaxy);
    }

    private static Empire NewEmpire(string name) =>
        EmpireFactory.CreateEmpire(name, null, isEmpress: false, TechLevel.Warp, restlessness: 0, centralModifier: false, foundingYear: 0);

    [Test]
    public async Task PlayTurn_FreshEmpire_NoOwnedStarbases_DoesNotThrow()
    {
        var (game, _) = NewGame();
        var owner = NewEmpire("Owner");
        game.Empires.Add(owner);

        var handler = new BerserkerTurnHandler(owner, new FixedRandom(0));

        handler.PlayTurn(owner, game);
    }

    [Test]
    public async Task NewBerserkerBaseTarget_WeakBase_SeeksHomeInsteadOfAttacking()
    {
        // MilitaryPower(ships,defenses=0) below MinBasePower(100000) -- FindHomeBMS, not a target
        // scan. A regional capital (the owner's own capital planet) sits within MaxDistanceToHome(10)
        // -- the second PlayTurn's own ImplementFindHomeBase transitions to RefuelBMS and points the
        // base at it.
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        game.Empires.Add(owner);

        var capital = new Planet { Location = new Coordinate(10, 10), Owner = owner, Type = WorldType.Capital, Class = WorldClass.EarthLike, TechLevel = TechLevel.Warp };
        owner.Capital = capital;
        galaxy.Planets.Add(capital);

        var starbase = new Starbase { Location = new Coordinate(15, 10), Owner = owner, Kind = StarbaseKind.CommandBase };
        starbase.Ships[ShipType.HunterKiller] = 10; // 10*20=200 power -- well below MinBasePower
        galaxy.Starbases.Add(starbase);

        var handler = new BerserkerTurnHandler(owner, new FixedRandom(0));
        handler.PlayTurn(owner, game); // seeds FindHomeBMS
        handler.PlayTurn(owner, game); // ImplementFindHomeBase -> RefuelBMS, destination = capital

        await Assert.That(starbase.Destination).IsEqualTo(capital.Location);
    }

    [Test]
    public async Task NewBerserkerBaseTarget_NoQualifyingWorld_Wanders()
    {
        // No planet anywhere meets the loot gate (met+che>2000 OR tri>1000) -- falls back to a
        // random point. Under FixedRandom(0), Rnd(1,SizeOfGalaxy) resolves to the low bound (1,1).
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        game.Empires.Add(owner);

        var starbase = new Starbase { Location = new Coordinate(50, 50), Owner = owner, Kind = StarbaseKind.CommandBase };
        starbase.Ships[ShipType.HunterKiller] = 6000; // 120000 power -- clears MinBasePower
        galaxy.Starbases.Add(starbase);

        var handler = new BerserkerTurnHandler(owner, new FixedRandom(0));
        handler.PlayTurn(owner, game);

        await Assert.That(starbase.Destination).IsEqualTo(new Coordinate(1, 1));
    }

    [Test]
    public async Task NewBerserkerBaseTarget_PicksQualifyingWeakerWorld_SetsAttackDestination()
    {
        // Protect=0 (undefended target) < BasePower always, regardless of the jittering NextDouble()
        // roll -- the one and only qualifying candidate gets picked.
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        game.Empires.Add(owner);

        var starbase = new Starbase { Location = new Coordinate(10, 10), Owner = owner, Kind = StarbaseKind.CommandBase };
        starbase.Ships[ShipType.HunterKiller] = 6000;
        galaxy.Starbases.Add(starbase);

        var target = new Planet { Location = new Coordinate(12, 10), Owner = Empire.Independent, Type = WorldType.Independent, Class = WorldClass.EarthLike, TechLevel = TechLevel.PreTech };
        target.Cargo[CargoType.Metals] = 2500;
        galaxy.Planets.Add(target);

        var handler = new BerserkerTurnHandler(owner, new FixedRandom(0));
        handler.PlayTurn(owner, game);

        await Assert.That(starbase.Destination).IsEqualTo(target.Location);
    }

    [Test]
    public async Task AttackBase_CloseEnough_DeploysAttackFleet()
    {
        // Fortress within 5 sectors of its target qualifies (ImplementAttackBMS's own OR condition) --
        // placed 3 away so it's real range-checking, not the Dist=1/co-located shortcut the conquest
        // test below uses.
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        game.Empires.Add(owner);

        var starbase = new Starbase { Location = new Coordinate(10, 10), Owner = owner, Kind = StarbaseKind.Fortress };
        starbase.Ships[ShipType.HunterKiller] = 6000;
        starbase.Ships[ShipType.Jumpship] = 6000;
        starbase.Cargo[CargoType.Trillum] = 10000;
        galaxy.Starbases.Add(starbase);

        var target = new Planet { Location = new Coordinate(13, 10), Owner = Empire.Independent, Type = WorldType.Independent, Class = WorldClass.EarthLike, TechLevel = TechLevel.PreTech };
        target.Cargo[CargoType.Metals] = 2500;
        galaxy.Planets.Add(target);

        var handler = new BerserkerTurnHandler(owner, new FixedRandom(0));
        handler.PlayTurn(owner, game); // seeds AttackBMS, destination = target
        handler.PlayTurn(owner, game); // ImplementAttackBase: Dist=3<=5 -> deploys

        var fleet = galaxy.Fleets.Single();
        await Assert.That(fleet.Owner).IsEqualTo(owner);
        await Assert.That(fleet.Destination).IsEqualTo(target.Location);
    }

    [Test]
    public async Task AttackMission_ConquersUndefendedWorld_PlundersAndHolocausts()
    {
        // Starbase and target share a coordinate (Dist=0, satisfies Fortress's own <=5 arm) so the
        // freshly deployed attack fleet is already Ready the instant it launches (same trick as
        // PirateTurnHandlerTests' own RaiderReachesUndefendedWorld test) -- UpdateFleets (which runs
        // before UpdateBases each turn, so this always takes one more PlayTurn than the base's own
        // deployment) dispatches it on the very next call. FixedRandom(0) makes Rnd(1,3) always
        // resolve to 1 -- DestroyWorld's own 1-in-3 roll always fires here.
        //
        // Ship-vs-ship combat alone can never conquer a world (only landed troops satisfy
        // FleetRetreats' own NoMenLeft check, the same constraint NpeAttackCases.AttackWrldConquers
        // documents for Pirate) -- the starbase needs real Jumptransport/Legion stock so
        // GetFleetComposition's own GAT-troop section actually drafts some, not just combat ships.
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        game.Empires.Add(owner);

        var starbase = new Starbase { Location = new Coordinate(10, 10), Owner = owner, Kind = StarbaseKind.Fortress };
        starbase.Ships[ShipType.HunterKiller] = 6000;
        starbase.Ships[ShipType.Jumpship] = 6000;
        starbase.Ships[ShipType.Starship] = 6000;
        starbase.Ships[ShipType.Fighter] = 6000;
        starbase.Ships[ShipType.Penetrator] = 6000;
        starbase.Ships[ShipType.Jumptransport] = 6000;
        starbase.Cargo[CargoType.Legion] = 6000;
        starbase.Cargo[CargoType.Trillum] = 10000;
        galaxy.Starbases.Add(starbase);
        owner.Capital = starbase; // CalculateCombatData needs a live attacker to have a capital -- a starbase qualifies (Empire.Capital's own doc comment).

        var target = new Planet { Location = new Coordinate(10, 10), Owner = Empire.Independent, Type = WorldType.Independent, Class = WorldClass.EarthLike, TechLevel = TechLevel.PreTech };
        target.Cargo[CargoType.Metals] = 2500;
        target.Population = 1000;
        galaxy.Planets.Add(target);

        var handler = new BerserkerTurnHandler(owner, new FixedRandom(0));
        handler.PlayTurn(owner, game); // seeds AttackBMS
        handler.PlayTurn(owner, game); // ImplementAttackBase deploys the fleet (already Ready)
        handler.PlayTurn(owner, game); // UpdateFleets dispatches ImplementAttackMission -> conquers

        await Assert.That(target.Owner.IsIndependent).IsTrue();
        await Assert.That(target.Cargo[CargoType.Metals]).IsEqualTo(0);
        await Assert.That(target.Population).IsLessThan(1000);
        var fleet = galaxy.Fleets.Single(f => f.Owner == owner);
        await Assert.That(fleet.Cargo[CargoType.Metals]).IsGreaterThan(0);
    }

    [Test]
    public async Task ReviewNews_StarbaseBlockedWhileWandering_PicksNewRandomDestination()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        game.Empires.Add(owner);

        var starbase = new Starbase { Location = new Coordinate(50, 50), Owner = owner, Kind = StarbaseKind.CommandBase };
        starbase.Ships[ShipType.HunterKiller] = 6000; // clears MinBasePower so it wanders (no qualifying target), not FindHome.
        galaxy.Starbases.Add(starbase);

        var handler = new BerserkerTurnHandler(owner, new FixedRandom(0));
        handler.PlayTurn(owner, game); // seeds WanderAroundBMS, destination (1,1)

        starbase.Destination = new Coordinate(99, 99); // simulate a stale in-flight order the news event below reacts to
        owner.AddNews(NewsType.StarbaseBlocked, starbase);

        handler.PlayTurn(owner, game); // BaseIsBlocked's own WanderAroundBMS arm -- re-rolls a destination

        await Assert.That(starbase.Destination).IsEqualTo(new Coordinate(1, 1));
    }

    [Test]
    public async Task RefuelBase_MergesEscortFleetIntoStarbase_ThenPicksFreshMission()
    {
        // Starbase and home share a coordinate (Dist=0): satisfies ImplementFindHomeBase's own
        // MaxDistanceToHome(10) check on turn 2, ImplementRefuelBase's own <5 check on turn 3, and
        // makes the escort fleet ImplementRefuelBase deploys already Ready the instant it launches
        // (same co-location trick as AttackMission_ConquersUndefendedWorld above) -- UpdateFleets
        // (which runs before UpdateBases, so a base-deployed fleet always needs one more PlayTurn)
        // dispatches ImplementReturnMission on turn 4, merging the escort's ships into the starbase
        // via AbortFleet and destroying it.
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        game.Empires.Add(owner);

        var starbase = new Starbase { Location = new Coordinate(10, 10), Owner = owner, Kind = StarbaseKind.CommandBase };
        starbase.Ships[ShipType.HunterKiller] = 10; // 10*20=200 power -- below MinBasePower, seeks home instead of attacking.
        galaxy.Starbases.Add(starbase);

        var home = new Planet { Location = new Coordinate(10, 10), Owner = owner, Type = WorldType.Capital, Class = WorldClass.EarthLike, TechLevel = TechLevel.Warp };
        home.Ships[ShipType.HunterKiller] = 6000;
        home.Ships[ShipType.Jumpship] = 6000;
        home.Cargo[CargoType.Trillum] = 10000;
        owner.Capital = home;
        galaxy.Planets.Add(home);

        var handler = new BerserkerTurnHandler(owner, new FixedRandom(0));
        handler.PlayTurn(owner, game); // seeds FindHomeBMS
        handler.PlayTurn(owner, game); // ImplementFindHomeBase -> RefuelBMS, target = home
        handler.PlayTurn(owner, game); // ImplementRefuelBase deploys the escort fleet (already Ready)

        var escort = galaxy.Fleets.Single();
        await Assert.That(escort.Owner).IsEqualTo(owner);
        await Assert.That(escort.Ships[ShipType.HunterKiller]).IsGreaterThan(0);

        handler.PlayTurn(owner, game); // UpdateFleets dispatches ImplementReturnMission -> merges into the starbase

        await Assert.That(galaxy.Fleets.Contains(escort)).IsFalse();
        await Assert.That(starbase.Ships[ShipType.HunterKiller]).IsGreaterThan(10);
    }
}
