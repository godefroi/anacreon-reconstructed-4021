using ThreeLn.Reconstruction4021.Core;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.NewGame;
using ThreeLn.Reconstruction4021.Core.Turns;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests;

/// <summary>
/// Phase 6d's KingdomTurnHandler.PlayTurn — the first commit where a whole NPE turn runs end to end,
/// so unlike NpeToolkit's/FleetLifecycle's own per-procedure hardcoded tests, this exercises the real
/// dispatch wiring a single procedure's own test can't reach: StateDeptRecord entries created lazily
/// for Empire.Independent (the overwhelmingly common ConquerMSN target), UpdateFleets' liveness guard
/// across a mid-loop fleet destruction, and ExplorationAndProbing's empty-regionCapitals early return.
/// </summary>
public class KingdomTurnHandlerTests
{
    private static (Game Game, Galaxy Galaxy) NewGame()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Game(galaxy);
        return (game, galaxy);
    }

    [Test]
    public async Task PlayTurn_FirstTurn_NoOwnedNonBaseWorlds_DoesNotThrow()
    {
        // No owned worlds at all besides the capital, and no other empires — DefendEmpire/
        // ImperialExpansion/ExplorationAndProbing all effectively no-op, but this still exercises the
        // constructor's persona/state seeding and PlayTurn's own call sequence end to end.
        var (game, galaxy) = NewGame();
        var owner = EmpireFactory.CreateEmpire("Owner", null, isEmpress: false, TechLevel.Warp, restlessness: 0, centralModifier: false, foundingYear: 0);
        game.Empires.Add(owner);

        // Off the galaxy edge (not (0,0)): ExplorationAndProbing's REPEAT/UNTIL loop only terminates
        // once InGalaxy(x,y) succeeds at least MaxProbesInTransit times — FixedRandom always rolls the
        // *minimum* of its range (baseXY-maxProbeDist), so an edge-adjacent capital would roll a
        // permanently out-of-galaxy coordinate and hang forever, matching what real Pascal would also
        // do fed a non-progressing RNG (a property of the algorithm, not a porting bug).
        var capital = new Planet { Location = new Coordinate(50, 50), Owner = owner, Type = WorldType.Capital, Class = WorldClass.EarthLike, TechLevel = TechLevel.Warp };
        owner.Capital = capital;
        galaxy.Planets.Add(capital);

        var handler = new KingdomTurnHandler(owner, NpeEmpireType.Kingdom1, new FixedRandom(0));

        handler.PlayTurn(owner, game);
    }

    [Test]
    public async Task PlayTurn_ConquersIndependentWorld_HandlesBalanceForIndependentTarget()
    {
        // The scenario advisor flagged as the real risk here: UpdateFleets' ConquerMSN case does
        // Inc(State[EnemyEmp].Balance) where EnemyEmp=GetStatus(TargID) — for the overwhelmingly
        // common "conquered an independent world" case, that's Empire.Independent, which real
        // Pascal's own InitializeKingdom1/2NPE seeding loop (Empire1..Empire8) never touches either.
        // A StateDeptRecord dictionary keyed only on real empires throws here unless it creates that
        // entry lazily. Placed at the same coordinate as the capital so the deployed battle fleet's
        // SetFleetDestination sees Location==Destination and goes Ready the instant it launches — a
        // second PlayTurn call is still needed since ImperialExpansion (which deploys it) runs after
        // UpdateFleets (which would otherwise dispatch it) in PlayTurn's own real sequence.
        var (game, galaxy) = NewGame();
        var owner = EmpireFactory.CreateEmpire("Owner", null, isEmpress: false, TechLevel.Warp, restlessness: 0, centralModifier: false, foundingYear: 0);
        game.Empires.Add(owner);

        // Off the galaxy edge — see PlayTurn_FirstTurn_NoOwnedNonBaseWorlds_DoesNotThrow's own comment
        // on why (0,0) hangs ExplorationAndProbing under FixedRandom).
        var capital = new Planet { Location = new Coordinate(50, 50), Owner = owner, Type = WorldType.Capital, Class = WorldClass.EarthLike, TechLevel = TechLevel.Warp };
        capital.Ships.Starships = 1000; // bombardment power
        capital.Ships.Transports = 50; // ground-troop carriers — GetFleetComposition.EnemySurrenders needs real capture strength (plGat), not just ships (see NpeToolkitDeployImplementTests' own ImplementConquerMSN test)
        capital.Cargo.Legions = 1000; // menToTake's own -500 reserve buffer means fewer than 500 on hand never actually gets loaded
        owner.Capital = capital;
        galaxy.Planets.Add(capital);

        var target = new Planet { Location = new Coordinate(50, 50), Owner = Empire.Independent, Type = WorldType.Independent, Class = WorldClass.EarthLike, TechLevel = TechLevel.Warp, Population = 1000 };
        target.Defenses[DefenseType.Gdm] = 100; // nonzero, so GetBestTarget's fleetPower sizing doesn't compose an empty fleet
        galaxy.Planets.Add(target);
        owner.Planets.MarkScouted(target);

        var random = new FixedRandom(0);
        var handler = new KingdomTurnHandler(owner, NpeEmpireType.Kingdom2, random);

        handler.PlayTurn(owner, game); // ImperialExpansion deploys a Conquer fleet at target
        await Assert.That(galaxy.Fleets).Count().IsEqualTo(1);
        var fleet = galaxy.Fleets[0];
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.Ready); // same coordinate as capital -> instantly arrived

        handler.PlayTurn(owner, game); // UpdateFleets dispatches ImplementConquerMSN against it — DefenderConquered, which is what actually exercises Inc(State[EnemyEmp].Balance) against the lazily-created Independent entry

        await Assert.That(target.Owner).IsEqualTo(owner);
    }

    [Test]
    public async Task PlayTurn_NoRegionalCapitals_ExplorationDoesNotHang()
    {
        // ExplorationAndProbing's REPEAT/UNTIL hangs in real Pascal when RCap is empty (NoMoreProbes
        // is only ever set inside the FOR loop's own body) — this empire owns no Base/Capital world at
        // all, so CreateRegionArray returns an empty list. The test times out rather than failing
        // cleanly if the empty-regionCapitals guard is ever removed.
        var (game, galaxy) = NewGame();
        var owner = EmpireFactory.CreateEmpire("Owner", null, isEmpress: false, TechLevel.Warp, restlessness: 0, centralModifier: false, foundingYear: 0);
        game.Empires.Add(owner);

        var handler = new KingdomTurnHandler(owner, NpeEmpireType.Kingdom1, new FixedRandom(0));

        handler.PlayTurn(owner, game);
    }
}
