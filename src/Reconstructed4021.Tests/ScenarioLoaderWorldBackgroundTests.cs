using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.NewGame;

namespace Reconstructed4021.Tests;

/// <summary>
/// SCENA.PAS's WorldBackgroundIndex/TEXT parsing and DisplayBackground matching
/// (ScenarioLoader.ParseDescriptions/ResolveWorldBackground, Game.FindWorldBackgroundText).
/// Hand-built scenario snippets, same convention as ScenarioLoaderTests -- none of the real
/// committed dos_131/*.SCN files exercise the [C:id]/[N:id] placeholder substitution (confirmed by
/// grep across every scenario pack in this repo), so that path is only covered here.
/// </summary>
public class ScenarioLoaderWorldBackgroundTests
{
    private static string Header(int size) => $"ANACREON 10\r\nTest 0 1 4 {size} 10 1 100 200 4021\r\nBEGINTEXT\r\nENDTEXT\r\n";

    private static readonly ScenarioLoader.PlayerInfo[] _twoPlayers =
        [new("P1", null, IsEmpress: false), new("P2", null, IsEmpress: false)];

    /// <summary>
    /// Two players, two non-capital worlds (index 1 owned by player 1, index 2 by player 2, per
    /// ARRONAX.SCN's own real "type:index" ordering convention), and a WorldBackgroundIndex with two
    /// rows: one whose condition matches world 1's real owner, one whose condition never matches
    /// world 2's real owner (a deliberately wrong condition, mirroring the real inconsistency found
    /// in ARRONAX.SCN's own shipped file -- see ROADMAP.md's 8j entry).
    /// </summary>
    private static Game BuildGame(string description) =>
        new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0)).Load(
            Header(20) +
            description +
            "CREATEPLAYEREMPIRE 0 5 3 0\r\n" +
            "CREATEPLAYEREMPIRE 1 5 3 0\r\n" +
            "CREATEWORLD 1 1,1 9 3 6 0 100 50 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0\r\n" +
            "CREATEWORLD 2 2,2 9 3 6 1 100 50 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0\r\n" +
            "ENDSCENARIO",
            _twoPlayers);

    [Test]
    public async Task Load_ParsesIndexRowsAndResolvesToRealPlanetsAndEmpires()
    {
        var game = BuildGame(
            "BEGINDESCRIPTION\r\n" +
            "WorldBackgroundIndex\r\n" +
            "2:1 E:0          1 ; World 1, owned by player 1\r\n" +
            "2:2 E:0          2 ; World 2, condition never actually matches its real owner\r\n" +
            "EndIndex\r\n" +
            "TEXT 1\r\n" +
            "Hello [N:2:2] at [C:2:2].\r\n" +
            "ENDTEXT\r\n" +
            "TEXT 2\r\n" +
            "Never shown.\r\n" +
            "ENDTEXT\r\n" +
            "ENDDESCRIPTION\r\n");

        await Assert.That(game.WorldBackgroundIndex).Count().IsEqualTo(2);

        var entry1 = game.WorldBackgroundIndex[0];
        await Assert.That(entry1.World).IsSameReferenceAs(game.Galaxy.Planets[0]);
        await Assert.That(entry1.TextNumber).IsEqualTo(1);
        await Assert.That(entry1.Conditions).Count().IsEqualTo(1);
        await Assert.That(entry1.Conditions[0].Kind).IsEqualTo(BackgroundConditionKind.OwnedBy);
        await Assert.That(entry1.Conditions[0].Empires).Contains(game.Empires[0]);

        var entry2 = game.WorldBackgroundIndex[1];
        await Assert.That(entry2.World).IsSameReferenceAs(game.Galaxy.Planets[1]);
        await Assert.That(entry2.TextNumber).IsEqualTo(2);

        await Assert.That(game.BackgroundTexts[1]).Count().IsEqualTo(1);
        await Assert.That(game.BackgroundTexts[2]).Count().IsEqualTo(1);
    }

    /// <summary>
    /// SCENA.PAS's ParseLine substitutes at most one [C:id]/[N:id] marker per line (the first '['
    /// through the first ']') -- two separate lines here, not two markers on one line, to actually
    /// exercise both without relying on a Pascal quirk neither marker in this test needs.
    /// </summary>
    [Test]
    public async Task FindWorldBackgroundText_OwnedByMatchesRealOwner_SubstitutesCoordAndNamePlaceholders()
    {
        var game = BuildGame(
            "BEGINDESCRIPTION\r\n" +
            "WorldBackgroundIndex\r\n" +
            "2:1 E:0 1\r\n" +
            "EndIndex\r\n" +
            "TEXT 1\r\n" +
            "Location: [C:2:2]\r\n" +
            "Name: [N:2:2]\r\n" +
            "ENDTEXT\r\n" +
            "ENDDESCRIPTION\r\n");

        var world1 = game.Galaxy.Planets[0];
        var world2 = game.Galaxy.Planets[1];
        var viewer = game.Empires[0];

        var lines = Game.FindWorldBackgroundText(game, world1, viewer, conquer: false);

        // viewer has no capital (neither test world is Capital-typed) -- CoordinateName falls back
        // to the galaxy's own center, (size/2, size/2), computed here rather than hand-derived to
        // avoid re-deriving GetNextXY's own 1-based-to-0-based conversion by hand.
        var origin = game.Galaxy.Size / 2;
        var expectedCoord = $"{world2.Location.X - origin},{origin - world2.Location.Y}";
        await Assert.That(lines).IsNotNull();
        await Assert.That(lines![0]).IsEqualTo($"Location: {expectedCoord}");
        await Assert.That(lines![1]).IsEqualTo($"Name: {world2.Type}");
    }

    /// <summary>SCENA.PAS's ParseLine handles at most one marker per line -- a second bracket pair on the same line is left untouched, matching real Pascal's own OpenB/CloseB single Pos() lookup.</summary>
    [Test]
    public async Task FindWorldBackgroundText_SecondMarkerOnSameLine_IsLeftUnsubstituted()
    {
        var game = BuildGame(
            "BEGINDESCRIPTION\r\n" +
            "WorldBackgroundIndex\r\n" +
            "2:1 E:0 1\r\n" +
            "EndIndex\r\n" +
            "TEXT 1\r\n" +
            "Hello [N:2:2] at [C:2:2].\r\n" +
            "ENDTEXT\r\n" +
            "ENDDESCRIPTION\r\n");

        var world1 = game.Galaxy.Planets[0];
        var world2 = game.Galaxy.Planets[1];

        var lines = Game.FindWorldBackgroundText(game, world1, game.Empires[0], conquer: false);

        await Assert.That(lines![0]).IsEqualTo($"Hello {world2.Type} at [C:2:2].");
    }

    [Test]
    public async Task FindWorldBackgroundText_OwnerDoesNotMatchCondition_ReturnsNull()
    {
        var game = BuildGame(
            "BEGINDESCRIPTION\r\n" +
            "WorldBackgroundIndex\r\n" +
            "2:2 E:0 1\r\n" + // world 2 is really owned by empire slot 1, not 0
            "EndIndex\r\n" +
            "TEXT 1\r\nUnreachable.\r\nENDTEXT\r\n" +
            "ENDDESCRIPTION\r\n");

        var world2 = game.Galaxy.Planets[1];

        var lines = Game.FindWorldBackgroundText(game, world2, game.Empires[0], conquer: false);

        await Assert.That(lines).IsNull();
    }

    [Test]
    public async Task FindWorldBackgroundText_OwnedByRowNeverMatchesWhileConquering()
    {
        var game = BuildGame(
            "BEGINDESCRIPTION\r\n" +
            "WorldBackgroundIndex\r\n" +
            "2:1 E:0 1\r\n" +
            "EndIndex\r\n" +
            "TEXT 1\r\nUnreachable.\r\nENDTEXT\r\n" +
            "ENDDESCRIPTION\r\n");

        var world1 = game.Galaxy.Planets[0];

        var lines = Game.FindWorldBackgroundText(game, world1, game.Empires[0], conquer: true);

        await Assert.That(lines).IsNull();
    }

    [Test]
    public async Task FindWorldBackgroundText_ConqueredByRowOnlyMatchesWhileConquering()
    {
        var game = BuildGame(
            "BEGINDESCRIPTION\r\n" +
            "WorldBackgroundIndex\r\n" +
            "2:1 A:1 1\r\n" + // matches only during a conquest report, by empire slot 1
            "EndIndex\r\n" +
            "TEXT 1\r\nConquered!\r\nENDTEXT\r\n" +
            "ENDDESCRIPTION\r\n");

        var world1 = game.Galaxy.Planets[0];
        var conqueror = game.Empires[1];

        // SatisfiesConditions' 'A' clause checks the object's own *current* owner, not the viewer --
        // real Pascal's ConquerWorld already reassigns ownership before EnemyConquered's own
        // DisplayBackground(...,True,...) call runs, so simulate that same ordering here.
        ((IEconomicWorld)world1).Reassign(conqueror);

        await Assert.That(Game.FindWorldBackgroundText(game, world1, conqueror, conquer: false)).IsNull();

        var lines = Game.FindWorldBackgroundText(game, world1, conqueror, conquer: true);
        await Assert.That(lines).IsNotNull();
        await Assert.That(lines![0]).IsEqualTo("Conquered!");
    }

    /// <summary>Real Pascal's own REPEAT loop stops at the first row whose World matches and whose Conditions pass -- a later row for the same World never gets a chance, even if it would also match.</summary>
    [Test]
    public async Task FindWorldBackgroundText_MultipleMatchingRowsForSameWorld_FirstRowWins()
    {
        var game = BuildGame(
            "BEGINDESCRIPTION\r\n" +
            "WorldBackgroundIndex\r\n" +
            "2:1 E:0 1\r\n" +
            "2:1 E:0 2\r\n" +
            "EndIndex\r\n" +
            "TEXT 1\r\nFirst.\r\nENDTEXT\r\n" +
            "TEXT 2\r\nSecond.\r\nENDTEXT\r\n" +
            "ENDDESCRIPTION\r\n");

        var lines = Game.FindWorldBackgroundText(game, game.Galaxy.Planets[0], game.Empires[0], conquer: false);

        await Assert.That(lines![0]).IsEqualTo("First.");
    }

    [Test]
    public async Task Load_MalformedWorldBackgroundIndexRow_ThrowsFormatException()
    {
        await Assert.That(() => BuildGame(
            "BEGINDESCRIPTION\r\n" +
            "WorldBackgroundIndex\r\n" +
            "not-a-valid-row\r\n" +
            "EndIndex\r\n" +
            "ENDDESCRIPTION\r\n")).Throws<FormatException>();
    }

    [Test]
    public async Task Load_DescriptionWithNoWorldBackgroundIndex_ParsesCleanlyWithNoEntries()
    {
        var game = BuildGame("BEGINDESCRIPTION\r\nJust flavor text, no index at all.\r\nENDDESCRIPTION\r\n");

        await Assert.That(game.WorldBackgroundIndex).IsEmpty();
        await Assert.That(game.BackgroundTexts).IsEmpty();
    }
}
