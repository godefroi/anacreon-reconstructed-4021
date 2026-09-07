using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.SaveFormat;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// Builds and pins the "Garrisoned Outpost" fixture (`assets/saves/Garrisoned Outpost.json`) --
/// unlike <see cref="GameJsonFixtureTests"/>'s "Border Skirmish" (a multi-turn transit skirmish), this
/// one drops the human fleet directly into the same sector as the Kingdom target, already <see
/// cref="FleetStatus.Ready"/> with no <see cref="Fleet.Destination"/>, so the TUI's <c>--load</c> flag
/// reaches a live "Attack" test with zero deploy/travel/turn-advance steps first. Same
/// generator-and-regression-check shape as that fixture, same reason: built directly against Core
/// APIs, not hand-authored JSON.
///
/// Both an enemy fleet and an enemy world share the sector on purpose: <c>GameShell.Attack</c>'s own
/// target search picks the sole enemy fleet first (matching real Pascal's own <c>GetTarget</c>
/// priority, per that method's doc comment), so the first attack here exercises the
/// <c>AskToCapture</c>-confirm path (the Kingdom garrison is weak enough to plausibly surrender with
/// survivors, not just get wiped out). A second attack against the same sector, once no Kingdom fleet
/// remains, then finds only the outpost and exercises the plain conquest-report path instead -- one
/// fixture, both post-battle dialog branches.
/// </summary>
public class GarrisonedOutpostFixtureTests
{
    private static readonly string FixturePath = Path.Combine(
        PascalGroundTruth.PascalHarness.RepoRoot, "assets", "saves", "Garrisoned Outpost.json");

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
        // afterward, same convention "Border Skirmish" already established.
        var zeroShips = new ShipCounts();
        var zeroCargo = new CargoHold();
        var zeroDefenses = new DefenseCounts();

        var humanCapital = galaxySetup.CreateWorld(galaxy, new Coordinate(0, 0), WorldClass.EarthLike, TechLevel.Bio, WorldType.Capital, human,
            population: 2000, efficiency: 80, trillumReserveBase: 100, zeroShips, zeroCargo, zeroDefenses);
        humanCapital.Ships.Fighters = 1500;
        humanCapital.Ships.HunterKillers = 500;
        humanCapital.Ships.Jumpships = 500;
        humanCapital.Ships.Transports = 500;
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
        kingdomCapital.Ships.Jumpships = 50;
        kingdomCapital.Defenses.DefenseSatellites = 10;
        kingdomCapital.Defenses.Gdms = 10;
        kingdomCapital.Defenses.IonCannons = 5;
        kingdomCapital.Cargo.Chemicals = 500;
        kingdomCapital.Cargo.Metals = 500;
        kingdomCapital.Cargo.Supplies = 500;
        kingdomCapital.Cargo.Trillum = 200;

        // The target sector: a Kingdom outpost with a garrison fleet, both at (4,1).
        var kingdomOutpost = galaxySetup.CreateWorld(galaxy, new Coordinate(4, 1), WorldClass.EarthLike, TechLevel.Bio, WorldType.Outpost, kingdom,
            population: 100, efficiency: 15, trillumReserveBase: 100, zeroShips, zeroCargo, zeroDefenses);
        kingdomOutpost.Ships.Fighters = 20;
        kingdomOutpost.Defenses.DefenseSatellites = 5;

        var garrisonFleet = new Fleet { Location = new Coordinate(4, 1), Owner = kingdom, Status = FleetStatus.Ready, Fuel = 100 };
        garrisonFleet.Ships.Fighters = 60;
        garrisonFleet.Ships.HunterKillers = 15;
        galaxy.Fleets.Add(garrisonFleet);

        // The human attack fleet: already at the target sector, Ready, no Destination -- no deploy or
        // travel needed before Fleet menu > Attack. Heavily overmatches the garrison (2300 ships vs.
        // 75) so a battle resolves in a couple of rounds either way the player plays it.
        var attackFleet = new Fleet { Location = new Coordinate(4, 1), Owner = human, Status = FleetStatus.Ready, Fuel = 500 };
        attackFleet.Ships.Fighters = 2000;
        attackFleet.Ships.HunterKillers = 300;
        attackFleet.Names[human] = "Warfleet";
        galaxy.Fleets.Add(attackFleet);

        // Fixed, not Game's own random default -- this fixture's committed JSON needs a stable Id to
        // stay byte-for-byte reproducible across regenerations.
        var game = new Game(galaxy) { Id = new Guid("00000000-0000-0000-0000-000000000001"), Year = 4021, CurrentEmpire = human };
        game.Empires.Add(human);
        game.Empires.Add(kingdom);
        game.TurnHandlers[human] = new HumanTurnHandler();
        game.TurnHandlers[kingdom] = new KingdomTurnHandler(kingdom, NpeEmpireType.Kingdom2, random);

        return game;
    }

    [Test]
    public async Task GarrisonedOutpost_MatchesTheCommittedFixture()
    {
        var expected = GameJson.Serialize(BuildGame());

        await Assert.That(File.ReadAllText(FixturePath)).IsEqualTo(expected);
    }

    [Test]
    public async Task GarrisonedOutpost_RoundTrips()
    {
        var game = BuildGame();
        var roundTripped = GameJson.Deserialize(GameJson.Serialize(game), new Random(0));

        var diffs = DeepGraphComparer.FindDifferences(game, roundTripped);

        await Assert.That(diffs).IsEmpty();
    }

    /// <summary>
    /// Overwrites the committed fixture with <see cref="BuildGame"/>'s current output -- run this
    /// after editing <see cref="BuildGame"/> (a different ship count, a moved world, an added fleet),
    /// then re-run the full suite so <see cref="GarrisonedOutpost_MatchesTheCommittedFixture"/> and
    /// <see cref="GarrisonedOutpost_RoundTrips"/> confirm the new file. <c>[Explicit]</c> keeps this
    /// out of the default <c>dotnet test</c> run (it mutates a committed file); target it by name:
    /// <c>dotnet test -- --treenode-filter "/*/*/GarrisonedOutpostFixtureTests/RegenerateFixture"</c>.
    /// </summary>
    [Test, Explicit]
    public async Task RegenerateFixture()
    {
        File.WriteAllText(FixturePath, GameJson.Serialize(BuildGame()));
        await Task.CompletedTask;
    }
}
