using ThreeLn.Reconstruction4021.Core;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Turns;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests;

/// <summary>
/// UpdateProbes/ProbeScout (INTRFACE.PAS:1289-1359), resolved as the last step of
/// VisibilityHandler.RefreshVisibility. A probe has no in-flight position (Empire.ProbesInTransit is
/// just a list of destinations) — it resolves entirely in one RefreshVisibility call: scout the
/// destination's 3x3 ring in Pascal's fixed order, then clear the list. See VisibilityHandler's own
/// doc comments for why order matters (early exit on a successful destroy roll or a Dark Nebula cell).
/// </summary>
public class VisibilityHandlerProbeTests
{
    private static readonly Random _alwaysSucceeds = new FixedRandom(0);
    private static readonly Random _alwaysFails = new FixedRandom(99);

    private static Game BuildGame(params Empire[] empires)
    {
        var galaxy = new Core.Galaxy.Galaxy(size: 20);
        var game = new Game(galaxy);
        foreach (var empire in empires) {
            game.Empires.Add(empire);
            game.TurnHandlers[empire] = new FakeTurnHandler();
        }
        return game;
    }

    private sealed class FakeTurnHandler : ITurnHandler
    {
        public bool IsHuman => false;
        public void PlayTurn(Empire empire, Game game) { }
    }

    [Test]
    public async Task ResolvedProbe_IsRemovedFromTransitList()
    {
        var human = new Empire { Name = "Human" };
        var game = BuildGame(human);
        human.TryLaunchProbe(new Coordinate(10, 10));

        new VisibilityHandler(_alwaysFails).RefreshVisibility(human, game);

        await Assert.That(human.ProbesInTransit).IsEmpty();
    }

    [Test]
    public async Task DestinationCellItselfIsScouted()
    {
        var human = new Empire { Name = "Human" };
        var enemy = new Empire { Name = "Enemy" };
        var game = BuildGame(human, enemy);

        var target = new Planet { Owner = enemy, Location = new Coordinate(10, 10) };
        game.Galaxy.Planets.Add(target);
        human.TryLaunchProbe(new Coordinate(10, 10));

        new VisibilityHandler(_alwaysFails).RefreshVisibility(human, game);

        await Assert.That(human.Planets.Scouted).Contains(target);
    }

    [Test]
    public async Task AllNineRingCellsAreScoutedWhenNothingStopsTheScan()
    {
        var human = new Empire { Name = "Human" };
        var enemy = new Empire { Name = "Enemy" };
        var game = BuildGame(human, enemy);

        for (var dx = -1; dx <= 1; dx++) {
            for (var dy = -1; dy <= 1; dy++) {
                game.Galaxy.Planets.Add(new Planet { Owner = enemy, Location = new Coordinate(10 + dx, 10 + dy) });
            }
        }
        human.TryLaunchProbe(new Coordinate(10, 10));

        new VisibilityHandler(_alwaysFails).RefreshVisibility(human, game);

        await Assert.That(human.Planets.Scouted.Count).IsEqualTo(9);
    }

    [Test]
    public async Task VoidCellIsNeverScouted()
    {
        var human = new Empire { Name = "Human" };
        var game = BuildGame(human);
        human.TryLaunchProbe(new Coordinate(10, 10));

        new VisibilityHandler(_alwaysFails).RefreshVisibility(human, game);

        await Assert.That(human.Planets.Scouted).IsEmpty();
    }

    [Test]
    public async Task AlreadyScoutedCell_NeverRollsForDestruction()
    {
        // Legions=100 -> ISqrt=10 -> chanceToDestroy=10, would always destroy under _alwaysSucceeds
        // if it rolled at all. Already-Scouted skips the roll entirely (INTRFACE.PAS's own
        // `NOT Scouted(Emp,Obj)` gate), so the probe survives to scout the rest of the ring. Note
        // RefreshVisibility's own ClearScouted+ScoutObjects run before probe resolution in the same
        // call (matching Pascal's SetUpTurn order) -- "already scouted" has to come from something
        // that scouts it earlier in *this* call, not a Scouted flag set on a prior turn and never
        // refreshed, so armedWorld is scouted here via adjacency to an owned planet.
        var human = new Empire { Name = "Human" };
        var enemy = new Empire { Name = "Enemy" };
        var game = BuildGame(human, enemy);

        var armedWorld = new Planet { Owner = enemy, Location = new Coordinate(10, 10) };
        armedWorld.Cargo.Legions = 100;
        game.Galaxy.Planets.Add(armedWorld);
        game.Galaxy.Planets.Add(new Planet { Owner = human, Location = new Coordinate(11, 11) }); // adjacent

        var laterRingCell = new Planet { Owner = enemy, Location = new Coordinate(11, 10) };
        game.Galaxy.Planets.Add(laterRingCell);
        human.TryLaunchProbe(new Coordinate(10, 10));

        new VisibilityHandler(_alwaysSucceeds).RefreshVisibility(human, game);

        await Assert.That(human.Planets.Scouted).Contains(laterRingCell);
    }

    [Test]
    public async Task SuccessfulDestroyRoll_StopsScoutingTheRestOfTheRing()
    {
        var human = new Empire { Name = "Human" };
        var enemy = new Empire { Name = "Enemy" };
        var game = BuildGame(human, enemy);

        // First offset in Pascal's order is the destination cell itself (0,0), then (0,-1).
        var armedWorld = new Planet { Owner = enemy, Location = new Coordinate(10, 10) };
        armedWorld.Cargo.Legions = 100; // ISqrt(100) = 10
        game.Galaxy.Planets.Add(armedWorld);

        var nextRingCell = new Planet { Owner = enemy, Location = new Coordinate(10, 9) };
        game.Galaxy.Planets.Add(nextRingCell);
        human.TryLaunchProbe(new Coordinate(10, 10));

        new VisibilityHandler(_alwaysSucceeds).RefreshVisibility(human, game);

        await Assert.That(human.Planets.Scouted).DoesNotContain(nextRingCell);
    }

    [Test]
    public async Task IndependentWorldWithLegions_NeverDestroysTheProbe()
    {
        var human = new Empire { Name = "Human" };
        var game = BuildGame(human);

        var independentWorld = new Planet { Owner = Empire.Independent, Location = new Coordinate(10, 10) };
        independentWorld.Cargo.Legions = 100;
        game.Galaxy.Planets.Add(independentWorld);

        var nextRingCell = new Planet { Owner = Empire.Independent, Location = new Coordinate(10, 9) };
        game.Galaxy.Planets.Add(nextRingCell);
        human.TryLaunchProbe(new Coordinate(10, 10));

        new VisibilityHandler(_alwaysSucceeds).RefreshVisibility(human, game);

        await Assert.That(human.Planets.Scouted).Contains(nextRingCell);
    }

    [Test]
    public async Task DarkNebulaCell_StopsScoutingTheRestOfTheRing()
    {
        var human = new Empire { Name = "Human" };
        var enemy = new Empire { Name = "Enemy" };
        var game = BuildGame(human, enemy);

        // Destination cell itself (first in Pascal's order) is a Dark Nebula.
        game.Galaxy.SetNebula(new Coordinate(10, 10), NebulaType.DarkNebula);

        var nextRingCell = new Planet { Owner = enemy, Location = new Coordinate(10, 9) };
        game.Galaxy.Planets.Add(nextRingCell);
        human.TryLaunchProbe(new Coordinate(10, 10));

        new VisibilityHandler(_alwaysFails).RefreshVisibility(human, game);

        await Assert.That(human.Planets.Scouted).DoesNotContain(nextRingCell);
    }

    [Test]
    public async Task EarlierRingCellDestruction_PreventsLaterCellFromBeingScouted()
    {
        // Ordering case: (0,-1) precedes (1,-1) in Pascal's fixed order. A destroy roll at (0,-1)
        // must prevent (1,-1) from ever being scouted -- an unordered implementation could get this
        // wrong by processing (1,-1) first. Note the destroying cell itself is never marked Scouted
        // either: Pascal's Exit on a successful destroy roll happens before ScoutObject is called.
        var human = new Empire { Name = "Human" };
        var enemy = new Empire { Name = "Enemy" };
        var game = BuildGame(human, enemy);

        var earlierCell = new Planet { Owner = enemy, Location = new Coordinate(10, 9) }; // (0,-1)
        earlierCell.Cargo.Legions = 100;
        game.Galaxy.Planets.Add(earlierCell);

        var laterCell = new Planet { Owner = enemy, Location = new Coordinate(11, 9) }; // (1,-1)
        game.Galaxy.Planets.Add(laterCell);

        human.TryLaunchProbe(new Coordinate(10, 10));

        new VisibilityHandler(_alwaysSucceeds).RefreshVisibility(human, game);

        await Assert.That(human.Planets.Scouted).DoesNotContain(earlierCell);
        await Assert.That(human.Planets.Scouted).DoesNotContain(laterCell);
    }

    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles))]
    [MethodDataSource(typeof(PascalGroundTruth.ProbeScoutCases), nameof(PascalGroundTruth.ProbeScoutCases.AsDataSource))]
    public async Task MatchesGoldenFile(PascalGroundTruth.ProbeScoutCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("probescout.golden");

        var human = new Empire { Name = "Human" };
        var enemy = new Empire { Name = "Enemy" };
        var game = BuildGame(human, enemy);

        var destOwner = c.DestOwnedByIndependent ? Empire.Independent : enemy;
        var destination = new Coordinate(10, 10);
        var destPlanet = new Planet { Owner = destOwner, Location = destination };
        destPlanet.Cargo.Legions = c.DestLegions;
        game.Galaxy.Planets.Add(destPlanet);
        if (c.DestAlreadyScouted) {
            // RefreshVisibility's own ClearScouted+ScoutObjects run before probe resolution in the
            // same call, so "already scouted" has to come from something that scouts it earlier in
            // *this* call (an owned planet adjacent to it), not a Scouted flag set once and never
            // refreshed -- see VisibilityHandlerProbeTests.AlreadyScoutedCell_NeverRollsForDestruction.
            game.Galaxy.Planets.Add(new Planet { Owner = human, Location = new Coordinate(11, 11) });
        }

        var ringCell = new Planet { Owner = enemy, Location = new Coordinate(10, 9) }; // (0,-1)
        game.Galaxy.Planets.Add(ringCell);

        human.TryLaunchProbe(destination);

        new VisibilityHandler(new FixedRandom(c.RngFixedValue)).RefreshVisibility(human, game);

        var expected = golden[c.Name];
        await Assert.That(human.Planets.Scouted.Contains(destPlanet) ? 1 : 0).IsEqualTo(int.Parse(expected["destscouted"]));
        await Assert.That(human.Planets.Scouted.Contains(ringCell) ? 1 : 0).IsEqualTo(int.Parse(expected["ringscouted"]));
    }
}
