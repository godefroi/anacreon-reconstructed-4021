using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.SaveFormat;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;
using Reconstructed4021.LegacyNpe;

namespace Reconstructed4021.Tests;

/// <summary>
/// Builds and pins the "Border Skirmish" fixture (`assets/saves/Border Skirmish.json`) the TUI's
/// <c>--load</c> flag reads to drop straight into a playable human-vs-Kingdom-NPE game without
/// routing through the whole New Game flow. Built directly against the same Core APIs
/// <see cref="ScenarioLoader"/>'s own <c>RunCreateWorld</c>/<c>RunCreatePlayerEmpire</c>/
/// <c>RunCreateNPEmpire</c> call internally, not through a hand-authored `.SCN` scenario text file
/// — the user's own explicit direction, since a `.SCN` scenario would mean routing through the
/// whole New Game flow just to reach a testable state. This test is both the fixture's generator
/// (the committed file is this test's own actual output, captured once) and its regression check:
/// if <see cref="GameJson"/>'s schema ever drifts, this catches it the same way every other
/// `GameJsonTests.AssertRoundTrips` call does.
/// </summary>
public class GameJsonFixtureTests
{
    private static readonly string FixturePath = Path.Combine(
        PascalGroundTruth.PascalHarness.RepoRoot, "assets", "saves", "Border Skirmish.json");

    /// <summary>
    /// Three planets, all pairwise within 5 sectors (Chebyshev distance) so an engagement is
    /// reachable in a handful of turns, per the user's explicit spec: the human capital heavily
    /// armed (10x the NPE's combined starting <see cref="ShipCounts"/>), the NPE's two worlds each
    /// lightly defended and specifically with no LAMs (the one weapon that would make an early
    /// attack costly).
    ///
    /// **Kingdom's starting population/efficiency deliberately low, confirmed by playtesting, not
    /// guessed**: a first pass used population/efficiency proportional to the human capital's own
    /// (1500/70 and 500/50) and found the NPE's own wartime economy erased the 10x ship-count
    /// advantage entirely by the time the human fleet crossed the galaxy — Kingdom's capital grew
    /// from 10 GDMs to 494 in the 4 turns transit took (`WarCabinet`/`DefendEmpire`'s real, aggressive
    /// defensive AI reacting to the incoming fleet, not a bug). Lowered to 150/20 (capital) and
    /// 100/15 (outpost) — confirmed via a hand-run diagnostic to keep 4-turn-transit growth modest
    /// (capital tops out around 161 fighters/54 jumpships/14 GDMs) so the human's numeric edge
    /// actually holds by the time contact happens, matching the "easy skirmish" this fixture is for.
    /// </summary>
    private static Game BuildGame()
    {
        var random = new Random(4021); // Matches Program.cs's own seed.
        var galaxySetup = new GalaxySetup(random);
        var galaxy = new Galaxy(5);

        var human = EmpireFactory.CreateEmpire("Human", null, isEmpress: false, TechLevel.Bio, restlessness: 0, centralModifier: false, foundingYear: 4021);
        var kingdom = EmpireFactory.CreateEmpire("Kingdom", null, isEmpress: false, TechLevel.Bio, restlessness: 0, centralModifier: false, foundingYear: 4021);
        kingdom.NpeType = NpeEmpireType.Kingdom2;

        // Zero base -- PascalMath.Jitter(0, N%) is always exactly 0, so CreateWorld's own ±20%
        // ship/cargo/defense jitter is a no-op here; the real numbers below are set directly
        // afterward so this fixture's ship counts are exact, not merely close.
        var zeroShips = new ShipCounts();
        var zeroCargo = new CargoHold();
        var zeroDefenses = new DefenseCounts();

        var humanCapital = galaxySetup.CreateWorld(galaxy, new Coordinate(0, 0), WorldClass.EarthLike, TechLevel.Bio, WorldType.Capital, human,
            population: 2000, efficiency: 80, trillumReserveBase: 100, zeroShips, zeroCargo, zeroDefenses);
        humanCapital.Ships.Fighters = 1500;
        humanCapital.Ships.HunterKillers = 500;
        humanCapital.Ships.Jumpships = 500;
        humanCapital.Ships.Transports = 500; // 3000 total -- exactly 10x the NPE's combined 300 (5x the original counts, plenty of depth for combat).
        humanCapital.Defenses.DefenseSatellites = 20;
        humanCapital.Defenses.Gdms = 20;
        humanCapital.Defenses.IonCannons = 10;
        humanCapital.Cargo.Chemicals = 500;
        humanCapital.Cargo.Metals = 500;
        humanCapital.Cargo.Supplies = 500;
        humanCapital.Cargo.Trillum = 200;

        var kingdomCapital = galaxySetup.CreateWorld(galaxy, new Coordinate(4, 4), WorldClass.EarthLike, TechLevel.Bio, WorldType.Capital, kingdom,
            population: 150, efficiency: 20, trillumReserveBase: 100, zeroShips, zeroCargo, zeroDefenses);
        kingdomCapital.Ships.Fighters = 150;
        kingdomCapital.Ships.HunterKillers = 50;
        kingdomCapital.Ships.Jumpships = 50; // 250 total.
        kingdomCapital.Defenses.DefenseSatellites = 10;
        kingdomCapital.Defenses.Gdms = 10;
        kingdomCapital.Defenses.IonCannons = 5; // No Lams -- deliberately undefended against the one weapon that would make an early attack costly.
        kingdomCapital.Cargo.Chemicals = 500;
        kingdomCapital.Cargo.Metals = 500;
        kingdomCapital.Cargo.Supplies = 500;
        kingdomCapital.Cargo.Trillum = 200;

        var kingdomOutpost = galaxySetup.CreateWorld(galaxy, new Coordinate(4, 1), WorldClass.EarthLike, TechLevel.Bio, WorldType.Outpost, kingdom,
            population: 100, efficiency: 15, trillumReserveBase: 100, zeroShips, zeroCargo, zeroDefenses);
        kingdomOutpost.Ships.Fighters = 50; // 50 total -- NPE grand total 300, so the human capital's 3000 is exactly 10x.
        kingdomOutpost.Defenses.DefenseSatellites = 5; // No Lams here either.

        // Fixed, not Game's own random default -- this fixture's committed JSON needs a stable Id to
        // stay byte-for-byte reproducible across regenerations.
        var game = new Game(galaxy) { Id = new Guid("00000000-0000-0000-0000-000000000002"), Year = 4021, CurrentEmpire = human };
        game.Empires.Add(human);
        game.Empires.Add(kingdom);
        game.TurnHandlers[human] = new HumanTurnHandler();
        game.TurnHandlers[kingdom] = new KingdomTurnHandler(kingdom, NpeEmpireType.Kingdom2, random); // Real Pascal's own InitializeNPE call.

        return game;
    }

    [Test]
    public async Task BorderSkirmish_MatchesTheCommittedFixture()
    {
        var expected = GameJson.Serialize(BuildGame(), new LegacyNpeProvider());

        await Assert.That(File.ReadAllText(FixturePath)).IsEqualTo(expected);
    }

    [Test]
    public async Task BorderSkirmish_RoundTrips()
    {
        var game = BuildGame();
        var npeProvider = new LegacyNpeProvider();
        var roundTripped = GameJson.Deserialize(GameJson.Serialize(game, npeProvider), new Random(0), npeProvider);

        var diffs = DeepGraphComparer.FindDifferences(game, roundTripped);

        await Assert.That(diffs).IsEmpty();
    }

    /// <summary>
    /// Overwrites the committed fixture with <see cref="BuildGame"/>'s current output -- run this
    /// after editing <see cref="BuildGame"/>, then re-run the full suite so
    /// <see cref="BorderSkirmish_MatchesTheCommittedFixture"/> and <see cref="BorderSkirmish_RoundTrips"/>
    /// confirm the new file. <c>[Explicit]</c> keeps this out of the default <c>dotnet test</c> run (it
    /// mutates a committed file); target it by name:
    /// <c>dotnet test -- --treenode-filter "/*/*/GameJsonFixtureTests/RegenerateFixture"</c>.
    /// </summary>
    [Test, Explicit]
    public async Task RegenerateFixture()
    {
        File.WriteAllText(FixturePath, GameJson.Serialize(BuildGame(), new LegacyNpeProvider()));
        await Task.CompletedTask;
    }
}
