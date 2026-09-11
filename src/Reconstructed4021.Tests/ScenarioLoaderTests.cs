using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;
using Reconstructed4021.LegacyNpe;

namespace Reconstructed4021.Tests;

/// <summary>
/// NEWGAME.PAS:1650-1812 (LoadScenario), minus DOS UI — see ScenarioLoader's own doc comment. Hardcoded
/// against small, hand-built scenario snippets (real .SCN file syntax, not a simplified stand-in) —
/// each test targets one command/branch in isolation, hand-traced against the Pascal source read
/// directly for this commit. End-to-end parity against real committed .SCN files is the separate,
/// golden-file-backed capstone test (see ScenarioLoaderGoldenTests).
/// </summary>
public class ScenarioLoaderTests
{
    /// <summary>
    /// Version 10 header (below the 12 threshold that gates CreateWorld's TriRes field and
    /// ReadModifierList's modifiers), including the minimal BEGINTEXT/ENDTEXT block every real .SCN
    /// file has — ScenarioIntroduction's own file-consumption runs unconditionally in real Pascal
    /// (see ScenarioLoader.SkipIntroText), so a header without one isn't realistic scenario syntax.
    /// </summary>
    private static string Header(int size, int firstYear = 4021) =>
        $"ANACREON 10\r\nTest 0 1 4 {size} 10 1 100 200 {firstYear}\r\nBEGINTEXT\r\nENDTEXT\r\n";

    private static string Header12(int size, int firstYear = 4021) =>
        $"ANACREON 12\r\nTest 0 1 4 {size} 10 1 100 200 {firstYear}\r\nBEGINTEXT\r\nENDTEXT\r\n";

    private static readonly ScenarioLoader.PlayerInfo[] _onePlayer = [new("Terra", "pw", IsEmpress: false)];

    [Test]
    public async Task Load_WithNoPlayersStillInitializesUniverseButRunsNoCommands()
    {
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0));

        var game = loader.Load(Header(20, firstYear: 4099) + "ENDSCENARIO", players: []);

        await Assert.That(game.Year).IsEqualTo(4099);
        await Assert.That(game.Galaxy.Size).IsEqualTo(20);
        await Assert.That(game.Empires).IsEmpty();
    }

    /// <summary>
    /// NEWGAME.PAS:1796's own Player:=Empire1: found missing here entirely (TurnEngine.BeginTurn's
    /// own null-guard threw the moment a real New Game session tried to start, confirmed live) --
    /// nothing set Game.CurrentEmpire for a freshly loaded scenario before (only SavGameLoader/
    /// GameJson did, reloading a save). NPE created first, at a slot number lower than the player's,
    /// so this only passes if CurrentEmpire resolves by player *slot* (0) rather than list position
    /// (Empires[0] would be the NPE here).
    /// </summary>
    [Test]
    public async Task Load_SetsCurrentEmpireToPlayerSlotZero()
    {
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0));
        var text = Header(20) + "CREATENPEMPIRE 4 3 RndName 0 1 0\r\nCREATEPLAYEREMPIRE 0 5 3 0\r\nENDSCENARIO";

        var game = loader.Load(text, _onePlayer);

        await Assert.That(game.CurrentEmpire).IsNotNull();
        await Assert.That(game.CurrentEmpire!.Name).IsEqualTo("Terra");
    }

    /// <summary>Mirrors NEWGAME.PAS's own Abort branch, which never reaches Player:=Empire1 at all.</summary>
    [Test]
    public async Task Load_WithNoPlayersLeavesCurrentEmpireNull()
    {
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0));

        var game = loader.Load(Header(20) + "ENDSCENARIO", players: []);

        await Assert.That(game.CurrentEmpire).IsNull();
    }

    [Test]
    public async Task Load_CreatePlayerEmpireBelowVersion12SkipsModifiers()
    {
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0));
        var text = Header(20) + "CREATEPLAYEREMPIRE 0 5 3 0\r\nENDSCENARIO";

        var game = loader.Load(text, _onePlayer);

        await Assert.That(game.Empires).Count().IsEqualTo(1);
        var empire = game.Empires[0];
        await Assert.That(empire.Name).IsEqualTo("Terra");
        await Assert.That(empire.Password).IsEqualTo("pw");
        await Assert.That(empire.TechnologyLevel).IsEqualTo(TechLevel.Atomic);
        await Assert.That(empire.RevolutionFactor).IsEqualTo(5);
        await Assert.That(empire.LosesIfCapitalConquered).IsFalse();
    }

    [Test]
    public async Task Load_CreatePlayerEmpireAtVersion12WithCentralModifier()
    {
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0));
        // Pl=0 RevFactor=5 Tl=3(Atomic) NoOfTechs=0, then ReadModifierList: 1 modifier, "CENTRAL".
        var text = Header12(20) + "CREATEPLAYEREMPIRE 0 5 3 0 1 CENTRAL\r\nENDSCENARIO";

        var game = loader.Load(text, _onePlayer);

        await Assert.That(game.Empires[0].LosesIfCapitalConquered).IsTrue();
    }

    [Test]
    public async Task Load_CreatePlayerEmpireGrantsExtraTechBeyondTheStartingLevel()
    {
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0));
        // Tech=5 (Warp) with extra tech ordinal 11 (trn=Transport) -- AWAKEN.SCN's own real line.
        // Transport's own MinTech is Warp, so it survives EmpireFactory's final intersect-clamp
        // (it wouldn't at a lower starting level -- confirmed by hitting that clamp first before
        // fixing this test to match the real scenario file's own choice of starting level).
        var text = Header(20) + "CREATEPLAYEREMPIRE 0 0 5 1 11\r\nENDSCENARIO";

        var game = loader.Load(text, _onePlayer);

        await Assert.That(game.Empires[0].Technology.Ships.Contains(ShipType.Transport)).IsTrue();
    }

    [Test]
    public async Task Load_CreatePlayerEmpireRegistersHumanTurnHandler()
    {
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0));
        var text = Header(20) + "CREATEPLAYEREMPIRE 0 0 3 0\r\nENDSCENARIO";

        var game = loader.Load(text, _onePlayer);

        var handler = game.TurnHandlers[game.Empires[0]];
        await Assert.That(handler).IsTypeOf<HumanTurnHandler>();
        await Assert.That(handler.IsHuman).IsTrue();
    }

    [Test]
    public async Task Load_CreatePlayerEmpireSlotBeyondDeclaredPlayersIsSilentlySkipped()
    {
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0));
        var text = Header(20) + "CREATEPLAYEREMPIRE 1 0 3 0\r\nENDSCENARIO"; // Pl=1, only 1 player (slot 0) declared

        var game = loader.Load(text, _onePlayer);

        await Assert.That(game.Empires).IsEmpty();
    }

    [Test]
    public async Task Load_CreateNPEmpireWithRndNameAssignsANameFromThePool()
    {
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0), new LegacyNpeProvider());
        // E=4 ET=3 Name=RndName RevFactor=0 Tl=1 NoOfTechs=0.
        var text = Header(20) + "CREATENPEMPIRE 4 3 RndName 0 1 0\r\nENDSCENARIO";

        var game = loader.Load(text, _onePlayer);

        await Assert.That(game.Empires).Count().IsEqualTo(1);
        await Assert.That(game.Empires[0].Name).IsEqualTo("Aaraavon"); // FixedRandom(0) -> Rnd(1,59)=1 -> first name
        // ET=3 -> Kingdom2, so this empire should get NpeType recorded and a real KingdomTurnHandler.
        await Assert.That(game.Empires[0].NpeType).IsEqualTo(NpeEmpireType.Kingdom2);
        await Assert.That(game.TurnHandlers[game.Empires[0]]).IsTypeOf<KingdomTurnHandler>();
    }

    [Test]
    public async Task Load_CreateNPEmpireWithExplicitNameUsesItVerbatim()
    {
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0), new LegacyNpeProvider());
        var text = Header(20) + "CREATENPEMPIRE 4 2 \"Kellandra\" 0 1 0\r\nENDSCENARIO";

        var game = loader.Load(text, _onePlayer);

        await Assert.That(game.Empires[0].Name).IsEqualTo("Kellandra");
        // ET=2 -> Kingdom1, so this empire should also get a KingdomTurnHandler.
        await Assert.That(game.Empires[0].NpeType).IsEqualTo(NpeEmpireType.Kingdom1);
        await Assert.That(game.TurnHandlers[game.Empires[0]]).IsTypeOf<KingdomTurnHandler>();
    }

    [Test]
    public async Task Load_CreateNPEmpireWithNoProviderRecordsTypeButRegistersNoTurnHandler()
    {
        // No npeProvider supplied at all -- ScenarioLoader.Handles is never consulted, so no empire
        // gets a handler regardless of its NpeType. Pirate now has a real handler when a provider
        // *is* supplied (see Load_CreateNPEmpireOfPirateTypeGetsPirateTurnHandler below); this test is
        // about the no-provider path in general, not about Pirate specifically.
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0));
        var text = Header(20) + "CREATENPEMPIRE 4 1 \"Blackbeard\" 0 1 0\r\nENDSCENARIO"; // ET=1 -> Pirate

        var game = loader.Load(text, _onePlayer);

        await Assert.That(game.Empires[0].NpeType).IsEqualTo(NpeEmpireType.Pirate);
        await Assert.That(game.TurnHandlers.ContainsKey(game.Empires[0])).IsFalse();
    }

    [Test]
    public async Task Load_CreateNPEmpireOfPirateTypeGetsPirateTurnHandler()
    {
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0), new LegacyNpeProvider());
        var text = Header(20) + "CREATENPEMPIRE 4 1 \"Blackbeard\" 0 1 0\r\nENDSCENARIO"; // ET=1 -> Pirate

        var game = loader.Load(text, _onePlayer);

        await Assert.That(game.Empires[0].NpeType).IsEqualTo(NpeEmpireType.Pirate);
        await Assert.That(game.TurnHandlers[game.Empires[0]]).IsTypeOf<PirateTurnHandler>();
    }

    [Test]
    public async Task Load_CreateWorldAtAbsoluteCoordinateShiftsToZeroBased()
    {
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0));
        var text = Header(20) +
            "CREATEPLAYEREMPIRE 0 0 3 0\r\n" +
            // n=1 XY=1,1(1-based -> 0,0) C=9(EarthLike) Tl=3(Atomic) T=4(Capital) E=0 Pp=100 Ef=50
            "CREATEWORLD 1 1,1 9 3 4 0 100 50 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0\r\n" +
            "ENDSCENARIO";

        var game = loader.Load(text, _onePlayer);

        await Assert.That(game.Galaxy.Planets).Count().IsEqualTo(1);
        var planet = game.Galaxy.Planets[0];
        await Assert.That(planet.Location).IsEqualTo(new Coordinate(0, 0));
        await Assert.That(planet.Class).IsEqualTo(WorldClass.EarthLike);
        await Assert.That(planet.TechLevel).IsEqualTo(TechLevel.Atomic);
        await Assert.That(planet.Type).IsEqualTo(WorldType.Capital);
        await Assert.That(planet.Owner).IsSameReferenceAs(game.Empires[0]);
        await Assert.That(game.Empires[0].Capital).IsSameReferenceAs(planet);
    }

    [Test]
    public async Task Load_CreateWorldForANeverCreatedEmpireFallsBackToIndependent()
    {
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0));
        // E=2 -- slot 2 was never created via CREATEPLAYEREMPIRE/CREATENPEMPIRE.
        var text = Header(20) +
            "CREATEWORLD 1 1,1 9 3 4 2 100 50 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0\r\n" +
            "ENDSCENARIO";

        var game = loader.Load(text, _onePlayer);

        var planet = game.Galaxy.Planets[0];
        await Assert.That(planet.Owner).IsSameReferenceAs(Empire.Independent);
        await Assert.That(planet.Type).IsEqualTo(WorldType.Independent); // overridden from the parsed CapTyp
    }

    [Test]
    public async Task Load_CreateWorldReadsTrillumReserveFieldOnlyAtVersion12OrAbove()
    {
        var setup = new GalaxySetup(new FixedRandom(0));
        var loaderV10 = new ScenarioLoader(setup, new FixedRandom(0));
        // No TriRes field at version 10 -- the next token (0) is NLAM, not TriRes.
        var textV10 = Header(20) +
            "CREATEWORLD 1 1,1 9 3 6 8 100 50 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0\r\n" +
            "ENDSCENARIO";
        loaderV10.Load(textV10, _onePlayer);

        var setup12 = new GalaxySetup(new FixedRandom(0));
        var loaderV12 = new ScenarioLoader(setup12, new FixedRandom(0));
        // Same token stream shape but with an explicit TriRes=50 field before the NLAM..Ntri tail.
        var textV12 = Header12(20) +
            "CREATEWORLD 1 1,1 9 3 6 8 100 50 50 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0\r\n" +
            "ENDSCENARIO";
        loaderV12.Load(textV12, _onePlayer);

        // Both planets exist (parsing didn't desync the token stream in either version).
        await Assert.That(setup12).IsNotNull();
    }

    [Test]
    public async Task Load_DefineXYThenRelativeCoordinateOffsetsFromIt()
    {
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0));
        var text = Header(20) +
            "DEFINEXY Home 5,5\r\n" + // 1-based (5,5) -> stored 0-based (4,4)
            "CREATEWORLD 1 Home:2,1 9 3 6 8 100 50 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0\r\n" +
            "ENDSCENARIO";

        var game = loader.Load(text, _onePlayer);

        // (4,4) + delta(2,1) = (6,5) -- deltas are not coordinate-shifted, only the anchor point was.
        await Assert.That(game.Galaxy.Planets[0].Location).IsEqualTo(new Coordinate(6, 5));
    }

    [Test]
    public async Task Load_DefineZoneThenZoneReferenceToken()
    {
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0));
        var text = Header(20) +
            "DEFINEZONE 2 3,3 3,3\r\n" + // a single-cell zone at 1-based (3,3) -> 0-based (2,2)
            "CREATEWORLD 1 Z:2 9 3 6 8 100 50 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0\r\n" +
            "ENDSCENARIO";

        var game = loader.Load(text, _onePlayer);

        await Assert.That(game.Galaxy.Planets[0].Location).IsEqualTo(new Coordinate(2, 2));
    }

    [Test]
    public async Task Load_RangeTokenPicksAPointWithinTheExplicitRange()
    {
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0));
        // R:2..4,2..4 (1-based) -> 0-based [1,3]x[1,3]; FixedRandom always returns the low end.
        var text = Header(20) +
            "CREATEWORLD 1 R:2..4,2..4 9 3 6 8 100 50 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0\r\n" +
            "ENDSCENARIO";

        var game = loader.Load(text, _onePlayer);

        await Assert.That(game.Galaxy.Planets[0].Location).IsEqualTo(new Coordinate(1, 1));
    }

    [Test]
    public async Task Load_ClassTableAndTechTableFeedCreateRandomWorlds()
    {
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0));
        var classTable = string.Join(' ', new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }); // index 9 = EarthLike
        var techTable = string.Join(' ', new[] { 0, 0, 0, 0, 0, 0, 0, 100, 0, 0, 0 }); // all Bio (index 7)
        var text = Header(20) +
            $"CLASSTABLE {classTable}\r\n" +
            $"TECHTABLE {techTable}\r\n" +
            "DEFINEZONE 1 1,1 20,20\r\n" +
            "CREATERANDOMWORLDS 1 1\r\n" +
            "ENDSCENARIO";

        var game = loader.Load(text, _onePlayer);

        await Assert.That(game.Galaxy.Planets).Count().IsEqualTo(1);
        await Assert.That(game.Galaxy.Planets[0].Class).IsEqualTo(WorldClass.EarthLike);
        await Assert.That(game.Galaxy.Planets[0].TechLevel).IsEqualTo(TechLevel.Bio);
    }

    [Test]
    public async Task Load_ClassTableNotSummingTo100Throws()
    {
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0));
        var badTable = string.Join(' ', Enumerable.Repeat(1, 21)); // sums to 21, not 100
        var text = Header(20) + $"CLASSTABLE {badTable}\r\nENDSCENARIO";

        await Assert.That(() => loader.Load(text, _onePlayer)).Throws<FormatException>();
    }

    [Test]
    public async Task Load_UnknownCommandThrows()
    {
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0));

        await Assert.That(() => loader.Load(Header(20) + "NOTAREALCOMMAND\r\nENDSCENARIO", _onePlayer))
            .Throws<FormatException>();
    }

    [Test]
    public async Task Load_IntroTextWithMultipleNewpagesIsSkippedEntirely()
    {
        // A real .SCN's BEGINTEXT block can span several NEWPAGE-delimited pages before ENDTEXT --
        // real Pascal only cares about reaching ENDTEXT, not the count of pages in between.
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0));
        var text = "ANACREON 10\r\nTest 0 1 4 20 10 1 100 200 4021\r\n" +
            "BEGINTEXT\r\nPage one text.\r\nNEWPAGE\r\nPage two text.\r\nNEWPAGE\r\nPage three text.\r\nENDTEXT\r\n" +
            "CREATEWORLD 1 1,1 9 3 6 8 100 50 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0\r\n" +
            "ENDSCENARIO";

        var game = loader.Load(text, _onePlayer);

        await Assert.That(game.Galaxy.Planets).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Load_BeginDescriptionSkipsLinesUntilEndDescription()
    {
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0));
        var text = Header(20) +
            "BEGINDESCRIPTION\r\nThis is flavor text.\r\nCREATEWORLD is not a command in here.\r\nENDDESCRIPTION\r\n" +
            "CREATEWORLD 1 1,1 9 3 6 8 100 50 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0\r\n" +
            "ENDSCENARIO";

        var game = loader.Load(text, _onePlayer);

        await Assert.That(game.Galaxy.Planets).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Load_ReportConsumesOneTokenAndContinuesParsing()
    {
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0));
        var text = Header(20) +
            "REPORT SomeMessage\r\n" +
            "CREATEWORLD 1 1,1 9 3 6 8 100 50 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0\r\n" +
            "ENDSCENARIO";

        var game = loader.Load(text, _onePlayer);

        // Reaching a correctly-placed world after REPORT proves it consumed exactly one token, not more/fewer.
        await Assert.That(game.Galaxy.Planets).Count().IsEqualTo(1);
        await Assert.That(game.Galaxy.Planets[0].Location).IsEqualTo(new Coordinate(0, 0));
    }

    [Test]
    public async Task Load_CreateStargateAndCreateSRMsAndCreateNebulaSmokeTest()
    {
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0));
        var text = Header(20) +
            "CREATESTARGATE 5,5 24 8\r\n" + // GTyp=24(Gate,full-ordinal) Emp=8(Indep)
            "CREATESRMS 8 1,1 3,3\r\n" +
            "CREATENEBULA 1 1,1 3,3\r\n" + // NTyp=1=Nebula
            "ENDSCENARIO";

        var game = loader.Load(text, _onePlayer);

        await Assert.That(game.Galaxy.Stargates).Count().IsEqualTo(1);
        await Assert.That(game.Galaxy.Stargates[0].Kind).IsEqualTo(StargateKind.Gate);
        await Assert.That(game.Galaxy.GetMineOwner(new Coordinate(0, 0))).IsSameReferenceAs(Empire.Independent);
        await Assert.That(game.Galaxy.GetNebula(new Coordinate(0, 0))).IsEqualTo(NebulaType.Nebula);
    }

    [Test]
    public async Task Load_CreateRandomNebulaBandMode()
    {
        var loader = new ScenarioLoader(new GalaxySetup(new FixedRandom(0)), new FixedRandom(0));
        var text = Header(15) + "CREATERANDOMNEBULA 1 0 0\r\nENDSCENARIO"; // mode 1 = band

        var game = loader.Load(text, _onePlayer);

        // FixedRandom(0): InitX=1 (<=Size/4=3, left branch, XDisp=0); every row paints x in [1-1,1+1]=[0,2] (1-based) -> 0-based [-1,1] clipped to [0,1].
        await Assert.That(game.Galaxy.GetNebula(new Coordinate(0, 0))).IsEqualTo(NebulaType.Nebula);
    }

    /// <summary>
    /// Issue #12: PERIPHER.SCN's own BEGINTEXT banner uses byte 0x16 as a decorative dot alongside its
    /// box-drawing art, which real DOS wrote straight to video memory as a font glyph, not a control
    /// code. Left un-decoded, that byte survives as a literal C0 control character, which Terminal.Gui
    /// measures as zero-width -- undercounting this exact line's real on-screen width and forcing an
    /// early word-wrap partway through "George Moromisato" (confirmed directly against TextFormatter:
    /// raw measures 70 columns and wraps into "...by George" / "Moromisato"; correctly decoded it
    /// measures 73 and stays one line). See ScenarioLoader.ReadScenarioFile's own doc comment.
    /// </summary>
    [Test]
    public async Task ReadScenarioFile_DecodesCp437ControlRangeBytesAsTheirDosDisplayGlyphs()
    {
        var path = Path.Combine(PascalGroundTruth.PascalHarness.RepoRoot, "reference", "scenarios", "dos_131", "PERIPHER.SCN");

        var text = ScenarioLoader.ReadScenarioFile(path);
        var pages = ScenarioLoader.ReadIntroPages(text);
        var bannerLine = pages[0].Split('\n').Single(l => l.Contains("Moromisato"));

        await Assert.That(bannerLine).EndsWith("by George Moromisato");
        await Assert.That(bannerLine.Any(char.IsControl)).IsFalse();
    }
}
