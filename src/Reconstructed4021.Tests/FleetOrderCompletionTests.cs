using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

public class FleetOrderCompletionTests
{
    // Capital at (10,10), so relative x,y = (X-10, 10-Y). Near: named own world at (11,10) = "1,0".
    // Far: unnamed scouted independent world at (12,10) = "2,0". Seen: known but not scouted, (10,13) = "0,-3".
    // Hidden: neither, (15,10).
    private sealed record Fixture(Game Game, Empire Viewer, Planet Near, Planet Far, Planet Seen, Planet Hidden);

    private static Fixture NewFixture()
    {
        var viewer = new Empire { Name = "Viewer" };
        var game = new Game(new Galaxy(size: 20));
        game.Empires.Add(viewer);

        Planet Add(int x, int y, Empire owner, int population)
        {
            var planet = new Planet { Location = new Coordinate(x, y), Owner = owner, Class = WorldClass.EarthLike, Type = WorldType.Agricultural, Population = population };
            game.Galaxy.Planets.Add(planet);
            return planet;
        }

        var capital = Add(10, 10, viewer, 1000);
        viewer.Capital = capital;
        var near = Add(11, 10, viewer, 900);
        near.Names[viewer] = "Bee";
        var far = Add(12, 10, Empire.Independent, 500);
        var seen = Add(10, 13, Empire.Independent, 300);
        var hidden = Add(15, 10, Empire.Independent, 200);

        viewer.Planets.MarkKnown(far);
        viewer.Planets.MarkScouted(far);
        viewer.Planets.MarkKnown(seen);
        return new Fixture(game, viewer, near, far, seen, hidden);
    }

    [Test]
    public async Task Candidates_AreNearestFirst_AndOmitWhatTheViewerCannotSee()
    {
        var f = NewFixture();

        var candidates = FleetOrderCompletion.Candidates(f.Game, f.Viewer, new Coordinate(11, 10), "");

        // Distances from (11,10): capital 1, Near 0, Far 1, Seen 3. Hidden (4) is not listed at all.
        await Assert.That(candidates).Count().IsEqualTo(4);
        await Assert.That(candidates[0].Object).IsEqualTo(f.Near);
        await Assert.That(candidates[3].Object).IsEqualTo(f.Seen);
        await Assert.That(candidates.Any(c => c.Object == f.Hidden)).IsFalse();
    }

    [Test]
    public async Task Candidate_InsertsTheNameWhenNamed_AndTheRelativeCoordinateOtherwise()
    {
        var f = NewFixture();

        var candidates = FleetOrderCompletion.Candidates(f.Game, f.Viewer, new Coordinate(10, 10), "");

        await Assert.That(candidates.Single(c => c.Object == f.Near).InsertText).IsEqualTo("Bee");
        await Assert.That(candidates.Single(c => c.Object == f.Far).InsertText).IsEqualTo("2,0");
        await Assert.That(candidates.Single(c => c.Object == f.Seen).Coordinates).IsEqualTo("0,-3");
    }

    [Test]
    public async Task Distance_IsTheGamesTravelDistanceFromTheReferencePoint()
    {
        var f = NewFixture();

        var candidates = FleetOrderCompletion.Candidates(f.Game, f.Viewer, new Coordinate(10, 10), "");

        await Assert.That(candidates.Single(c => c.Object == f.Near).Distance).IsEqualTo(1);
        await Assert.That(candidates.Single(c => c.Object == f.Far).Distance).IsEqualTo(2);
        await Assert.That(candidates.Single(c => c.Object == f.Seen).Distance).IsEqualTo(3);
    }

    [Test]
    [Arguments("2,", "2,0")]
    [Arguments("0,-", "0,-3")]
    [Arguments("be", "Bee")]
    [Arguments("  BEE ", "Bee")]
    public async Task Filter_MatchesCoordinatePrefixOrName(string argument, string expectedInsert)
    {
        var f = NewFixture();

        var candidates = FleetOrderCompletion.Candidates(f.Game, f.Viewer, new Coordinate(10, 10), argument);

        await Assert.That(candidates).Count().IsEqualTo(1);
        await Assert.That(candidates[0].InsertText).IsEqualTo(expectedInsert);
    }

    [Test]
    public async Task Decode_ReportsWhatTheDestinationPointsAt()
    {
        var f = NewFixture();

        var world = FleetOrderCompletion.Decode(f.Game, f.Viewer, "dest 2,0");
        var emptySpace = FleetOrderCompletion.Decode(f.Game, f.Viewer, "DEST 5,5");
        var unseenWorld = FleetOrderCompletion.Decode(f.Game, f.Viewer, "DEST 5,0");
        var outOfBounds = FleetOrderCompletion.Decode(f.Game, f.Viewer, "DEST 99,99");
        var byName = FleetOrderCompletion.Decode(f.Game, f.Viewer, "DEST Bee");

        await Assert.That(world!.Target).IsEqualTo(f.Far);
        await Assert.That(emptySpace).IsEqualTo(new FleetOrderCompletion.Decoded(true, null, "5,5"));
        // A world the viewer hasn't seen decodes as empty space, not as itself.
        await Assert.That(unseenWorld).IsEqualTo(new FleetOrderCompletion.Decoded(true, null, "5,0"));
        await Assert.That(outOfBounds!.Resolved).IsFalse();
        await Assert.That(byName!.Target).IsEqualTo(f.Near);
    }

    [Test]
    [Arguments("WAIT")]
    [Arguments("DEST")]
    [Arguments("")]
    public async Task Decode_IsNullWithoutADestArgument(string line)
    {
        var f = NewFixture();

        await Assert.That(FleetOrderCompletion.Decode(f.Game, f.Viewer, line)).IsNull();
    }

    [Test]
    public async Task ReferencePoint_IsTheNearestResolvableDestAbove_ElseTheFleet()
    {
        var f = NewFixture();
        var fleet = new Coordinate(3, 3);
        string[] lines = ["DEST 2,0", "WAIT", "DEST nowhere", "DEST"];

        await Assert.That(FleetOrderCompletion.ReferencePoint(f.Game, f.Viewer, fleet, lines, 0)).IsEqualTo(fleet);
        await Assert.That(FleetOrderCompletion.ReferencePoint(f.Game, f.Viewer, fleet, lines, 3)).IsEqualTo(new Coordinate(12, 10));
    }

    [Test]
    public async Task Apply_ReplacesTheArgumentAndKeepsTheCommandWordAsTyped()
    {
        var f = NewFixture();
        var candidate = FleetOrderCompletion.Candidates(f.Game, f.Viewer, new Coordinate(10, 10), "2,")[0];

        await Assert.That(FleetOrderCompletion.Apply("deSt 2,", candidate)).IsEqualTo("deSt 2,0");
        await Assert.That(FleetOrderCompletion.Apply("DESTination", candidate)).IsEqualTo("DESTination 2,0");
    }
}
