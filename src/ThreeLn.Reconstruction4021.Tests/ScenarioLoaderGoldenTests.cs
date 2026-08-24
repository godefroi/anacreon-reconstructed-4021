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

        var sumShips = planets.Sum(SumShips) + starbases.Sum(s => s.Ships.Fighters);

        await Assert.That($"{game.Year}").IsEqualTo(golden["year"]);
        await Assert.That($"{planets.Count}").IsEqualTo(golden["planetcount"]);
        await Assert.That($"{planets.Sum(p => p.Location.X)}").IsEqualTo(golden["sumplanetx"]);
        await Assert.That($"{planets.Sum(p => p.Location.Y)}").IsEqualTo(golden["sumplanety"]);
        await Assert.That($"{planets.Sum(p => p.Population)}").IsEqualTo(golden["sumpop"]);
        await Assert.That($"{planets.Sum(p => p.Efficiency)}").IsEqualTo(golden["sumeff"]);
        await Assert.That($"{planets.Sum(p => p.TrillumReserve)}").IsEqualTo(golden["sumtri"]);
        await Assert.That($"{planets.Sum(p => (int)p.Class)}").IsEqualTo(golden["sumclass"]);
        await Assert.That($"{planets.Sum(p => (int)p.TechLevel)}").IsEqualTo(golden["sumtech"]);
        await Assert.That($"{sumShips}").IsEqualTo(golden["sumships"]);
        await Assert.That($"{planets.Sum(SumCargo)}").IsEqualTo(golden["sumcargo"]);
        await Assert.That($"{planets.Sum(SumDefenses)}").IsEqualTo(golden["sumdefns"]);
        await Assert.That($"{starbases.Count}").IsEqualTo(golden["starbasecount"]);
        await Assert.That($"{starbases.Sum(s => s.Population)}").IsEqualTo(golden["sumstarbasepop"]);
        await Assert.That($"{starbases.Sum(s => s.Efficiency)}").IsEqualTo(golden["sumstarbaseeff"]);
        await Assert.That($"{game.Galaxy.Stargates.Count}").IsEqualTo(golden["stargatecount"]);
        await Assert.That($"{game.Empires.Count}").IsEqualTo(golden["empirecount"]);
        await Assert.That($"{game.Empires.Sum(e => (int)e.TechnologyLevel)}").IsEqualTo(golden["sumempiretech"]);
        await Assert.That($"{game.Empires.Sum(e => e.RevolutionFactor)}").IsEqualTo(golden["sumrevfactor"]);
        await Assert.That($"{game.Empires.Count(e => e.LosesIfCapitalConquered)}").IsEqualTo(golden["sumcentralmodifier"]);
        await Assert.That($"{game.Empires.Count(e => e.IsEmpress)}").IsEqualTo(golden["sumempress"]);
        await Assert.That($"{CountNebulaCells(game.Galaxy)}").IsEqualTo(golden["nebulacellcount"]);
        await Assert.That($"{CountMinedCells(game.Galaxy)}").IsEqualTo(golden["minedcellcount"]);
    }

    private static int SumShips(Planet p) =>
        p.Ships.Fighters + p.Ships.HunterKillers + p.Ships.Jumpships + p.Ships.Jumptransports +
        p.Ships.Penetrators + p.Ships.Starships + p.Ships.Transports;

    private static int SumCargo(Planet p) =>
        p.Cargo.Legions + p.Cargo.NinjaLegions + p.Cargo.Ambrosia + p.Cargo.Chemicals +
        p.Cargo.Metals + p.Cargo.Supplies + p.Cargo.Trillum;

    private static int SumDefenses(Planet p) =>
        p.Defenses.Lams + p.Defenses.DefenseSatellites + p.Defenses.Gdms + p.Defenses.IonCannons;

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
}
