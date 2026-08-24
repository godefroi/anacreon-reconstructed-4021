using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.NewGame;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests;

/// <summary>
/// Phase 2 commit 2e's capstone: loads real committed dos_131/*.SCN files through the C# ScenarioLoader
/// and compares an aggregate checksum against the same real file loaded by the patched Pascal
/// RunScenarioCase — see ScenarioCases' own doc comment for the full rationale (why an aggregate
/// checksum rather than a per-entity dump, why PRINCES.SCN is excluded, why this domain needed
/// PascalRandom instead of ForcedRandomValue).
///
/// Only asserts fields that never depend on Rnd()/RndVar() at all: pure counts and per-empire summary
/// fields that come straight from the scenario file's own explicit data, with no random draw anywhere
/// in their computation. Every field derived from a randomized formula — planet coordinates,
/// population, trillum, ships/cargo/defenses, class/tech, nebula cell count, AND starbase
/// population/efficiency (CreateBase's own RndVar(Pp,15) jitter, NEWGAME.PAS:1090) — is deliberately
/// NOT asserted here, even where the value happens to still be a plain count or an explicit-command
/// field, because RndVar's underlying Rnd(Min,Max) skips drawing entirely when Max&lt;=Min (INT.PAS)
/// — so any single Trunc/Round anywhere upstream landing on a different side of an exact-integer
/// boundary (confirmed via direct investigation: fpc's default x87 80-bit intermediate precision vs.
/// C#'s IEEE754 double can each round the same borderline Real expression differently, and even two
/// independently-written formulas under identical precision can differ by an ULP) changes how many
/// draws that call consumes, desyncing the shared RNG stream for every subsequent draw in the whole
/// file — confirmed concretely: AWAKEN.SCN's starbase population desyncs from just its 10 preceding
/// explicit CreateWorld commands, well before any CreateRandomWorlds runs, so "explicit command, not
/// randomized generation" does NOT make a field safe to assert here. This is not corruption and not
/// fixable by matching floating-point precision (confirmed: forcing fpc's harness to -CfSSE2/strict
/// double, see build.ps1 and PatchHarness.cs, still diverges — different boundary values flip instead
/// of the same ones). Formula-level correctness for these randomized values is already covered by the
/// dedicated 2d domain tests (randomplanet/nebula/trillumreserves), which use ForcedRandomValue and
/// don't chain into a real collision-retry loop.
/// </summary>
public class ScenarioLoaderGoldenTests
{
    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles))]
    [MethodDataSource(typeof(PascalGroundTruth.ScenarioCases), nameof(PascalGroundTruth.ScenarioCases.AsDataSource))]
    public async Task MatchesGoldenFile(PascalGroundTruth.ScenarioCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("scenario.golden")[c.Name];

        var path = Path.Combine(PascalGroundTruth.PascalHarness.RepoRoot, "reference", "scenarios", "dos_131", c.FileName);
        var text = File.ReadAllText(path);

        var random = new PascalRandom(c.Seed);
        var setup = new GalaxySetup(random);
        var loader = new ScenarioLoader(setup, random);
        var players = Enumerable.Range(1, c.NumPlayers)
            .Select(i => new ScenarioLoader.PlayerInfo($"Player{i}", $"pw{i}", IsEmpress: false))
            .ToArray();

        var game = loader.Load(text, players);

        var planets = game.Galaxy.Planets;
        var starbases = game.Galaxy.Starbases;

        await Assert.That($"{game.Year}").IsEqualTo(golden["year"]);
        await Assert.That($"{planets.Count}").IsEqualTo(golden["planetcount"]);
        await Assert.That($"{starbases.Count}").IsEqualTo(golden["starbasecount"]);
        await Assert.That($"{game.Galaxy.Stargates.Count}").IsEqualTo(golden["stargatecount"]);
        await Assert.That($"{game.Empires.Count}").IsEqualTo(golden["empirecount"]);
        await Assert.That($"{game.Empires.Sum(e => (int)e.TechnologyLevel)}").IsEqualTo(golden["sumempiretech"]);
        await Assert.That($"{game.Empires.Sum(e => e.RevolutionFactor)}").IsEqualTo(golden["sumrevfactor"]);
        await Assert.That($"{game.Empires.Count(e => e.LosesIfCapitalConquered)}").IsEqualTo(golden["sumcentralmodifier"]);
        await Assert.That($"{game.Empires.Count(e => e.IsEmpress)}").IsEqualTo(golden["sumempress"]);
        await Assert.That($"{CountMinedCells(game.Galaxy)}").IsEqualTo(golden["minedcellcount"]);

        // Fields dropped from exact-match above (see class doc comment) still get a cheap smoke test:
        // bounds derived from type/domain invariants, not from game-balance assumptions, so they can't
        // produce a false failure on legitimate scenario content and don't drift with the RNG stream.
        var maxCoord = game.Galaxy.Size - 1;
        await Assert.That(planets.Sum(p => p.Location.X)).IsBetween(0, planets.Count * maxCoord);
        await Assert.That(planets.Sum(p => p.Location.Y)).IsBetween(0, planets.Count * maxCoord);
        await Assert.That(planets.Sum(p => (int)p.Class)).IsBetween(0, planets.Count * (Enum.GetValues<WorldClass>().Length - 1));
        await Assert.That(planets.Sum(p => (int)p.TechLevel)).IsBetween(0, planets.Count * (Enum.GetValues<TechLevel>().Length - 1));
        await Assert.That(CountNebulaCells(game.Galaxy)).IsBetween(0, game.Galaxy.Size * game.Galaxy.Size);
        await Assert.That(planets.Sum(p => p.Population)).IsGreaterThanOrEqualTo(0);
        await Assert.That(planets.Sum(p => p.TrillumReserve)).IsGreaterThanOrEqualTo(0);
        await Assert.That(planets.Sum(SumShips) + starbases.Sum(s => s.Ships.Fighters)).IsGreaterThanOrEqualTo(0);
        await Assert.That(planets.Sum(SumCargo)).IsGreaterThanOrEqualTo(0);
        await Assert.That(planets.Sum(SumDefenses)).IsGreaterThanOrEqualTo(0);
        await Assert.That(starbases.Sum(s => s.Population)).IsGreaterThanOrEqualTo(0);
        await Assert.That(starbases.Sum(s => s.Efficiency)).IsGreaterThanOrEqualTo(0);
    }

    private static int SumShips(Planet p) =>
        p.Ships.Fighters + p.Ships.HunterKillers + p.Ships.Jumpships + p.Ships.Jumptransports +
        p.Ships.Penetrators + p.Ships.Starships + p.Ships.Transports;

    private static int SumCargo(Planet p) =>
        p.Cargo.Legions + p.Cargo.NinjaLegions + p.Cargo.Ambrosia + p.Cargo.Chemicals +
        p.Cargo.Metals + p.Cargo.Supplies + p.Cargo.Trillum;

    private static int SumDefenses(Planet p) =>
        p.Defenses.Lams + p.Defenses.DefenseSatellites + p.Defenses.Gdms + p.Defenses.IonCannons;

    private static int CountNebulaCells(Galaxy galaxy)
    {
        var count = 0;
        for (var x = 0; x < galaxy.Size; x++)
        for (var y = 0; y < galaxy.Size; y++) {
            if (galaxy.GetNebula(new Coordinate(x, y)) != NebulaType.None)
                count++;
        }
        return count;
    }

    /// <summary>Matches RunScenarioCase's own EnemyMine(XY)&lt;&gt;Indep check: an unmined or Independent-owned cell doesn't count.</summary>
    private static int CountMinedCells(Galaxy galaxy)
    {
        var count = 0;
        for (var x = 0; x < galaxy.Size; x++)
        for (var y = 0; y < galaxy.Size; y++) {
            var owner = galaxy.GetMineOwner(new Coordinate(x, y));
            if (owner is not null && !owner.IsIndependent)
                count++;
        }
        return count;
    }
}
