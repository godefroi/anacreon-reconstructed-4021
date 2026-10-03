using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// Loads real committed dos_131/*.SCN files through the C# ScenarioLoader and compares an aggregate
/// checksum against the same file loaded by the patched Pascal RunScenarioCase, both sides drawing
/// from the same seeded <see cref="GroundTruthRandom"/> stream. See ScenarioCases' doc comment for why
/// an aggregate checksum and why PRINCES.SCN is excluded.
///
/// Every printed field is asserted exactly, including the RNG-derived ones. A single extra or missing
/// draw anywhere in a load shifts every later draw, so these sums catch draw-count drift as well as
/// formula bugs. The loader gets a <see cref="LegacyNpe.LegacyNpeProvider"/> because Pascal always runs
/// InitializeNPE (NEWGAME.PAS:1257), whose persona and SetEmpireDefenses draws are part of that
/// stream.
///
/// The one exception is AWAKEN.SCN's starbase fields. AWAKEN creates 212 planets against TYPES.PAS's
/// MaxNoOfPlanets = 200, and with range checking off, the last CreateRandomWorlds writes past the
/// Planet array into the Starbase array that follows it, overwriting both starbases' Pop, Eff and
/// fighters. Real Turbo Pascal 1.31 would corrupt them the same way. sumstarbasepop, sumstarbaseeff
/// and sumships (which includes starbase fighters) are skipped for that case only, since a corrupted
/// value isn't meaningful to match.
/// </summary>
public class ScenarioLoaderGoldenTests
{
    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles), ProceedOnFailure = true)]
    [MethodDataSource(typeof(PascalGroundTruth.ScenarioCases), nameof(PascalGroundTruth.ScenarioCases.AsDataSource))]
    public async Task MatchesGoldenFile(PascalGroundTruth.ScenarioCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("scenario.golden")[c.Name];

        var path = Path.Combine(PascalGroundTruth.PascalHarness.RepoRoot, "reference", "scenarios", "dos_131", c.FileName);
        var text = File.ReadAllText(path);

        var random = new GroundTruthRandom(c.Seed);
        var setup = new GalaxySetup(random);
        var loader = new ScenarioLoader(setup, random, new LegacyNpe.LegacyNpeProvider());
        // Matches NEWGAME.PAS's own InputEmpireName patch exactly (reference/verify/README.md) --
        // test_player_N/test_pass_N, gender alternating starting male (0-based index even = male).
        var players = Enumerable.Range(1, c.NumPlayers)
            .Select(i => new ScenarioLoader.PlayerInfo($"test_player_{i}", $"test_pass_{i}", IsEmpress: (i - 1) % 2 != 0))
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

        await Assert.That($"{planets.Sum(p => p.Location.X)}").IsEqualTo(golden["sumplanetx"]);
        await Assert.That($"{planets.Sum(p => p.Location.Y)}").IsEqualTo(golden["sumplanety"]);
        await Assert.That($"{planets.Sum(p => p.Population)}").IsEqualTo(golden["sumpop"]);
        await Assert.That($"{planets.Sum(p => p.Efficiency)}").IsEqualTo(golden["sumeff"]);
        await Assert.That($"{planets.Sum(p => p.TrillumReserve)}").IsEqualTo(golden["sumtri"]);
        await Assert.That($"{planets.Sum(p => (int)p.Class)}").IsEqualTo(golden["sumclass"]);
        await Assert.That($"{planets.Sum(p => (int)p.TechLevel)}").IsEqualTo(golden["sumtech"]);
        await Assert.That($"{planets.Sum(SumCargo)}").IsEqualTo(golden["sumcargo"]);
        await Assert.That($"{planets.Sum(SumDefenses)}").IsEqualTo(golden["sumdefns"]);
        await Assert.That($"{CountNebulaCells(game.Galaxy)}").IsEqualTo(golden["nebulacellcount"]);
        await Assert.That($"{CountMinedCells(game.Galaxy)}").IsEqualTo(golden["minedcellcount"]);

        if (c.FileName != "AWAKEN.SCN") {
            await Assert.That($"{planets.Sum(SumShips) + starbases.Sum(s => s.Ships.Fighters)}").IsEqualTo(golden["sumships"]);
            await Assert.That($"{starbases.Sum(s => s.Population)}").IsEqualTo(golden["sumstarbasepop"]);
            await Assert.That($"{starbases.Sum(s => s.Efficiency)}").IsEqualTo(golden["sumstarbaseeff"]);
        }

        await Assert.That($"{planets.Sum(p => (int)p.Type)}").IsEqualTo(golden["sumtype"]);
        await Assert.That($"{planets.Sum(p => Enum.GetValues<IndustryType>().Sum(i => p.Industry[i]))}").IsEqualTo(golden["sumindus"]);
        await Assert.That($"{planets.Sum(p => (int)p.SelfSufficiency.Chemical + (int)p.SelfSufficiency.Metal + (int)p.SelfSufficiency.Supply + (int)p.SelfSufficiency.Trillum)}")
            .IsEqualTo(golden["sumissp"]);
        await Assert.That($"{planets.Count(p => p.Owner.IsIndependent)}").IsEqualTo(golden["indepplanets"]);

        // The harness lists per-empire values sorted, since its empire slots follow the .SCN's empire
        // numbers and Game.Empires doesn't.
        await Assert.That(SortedList(game.Empires.Select(TechMask))).IsEqualTo(golden["techmasks"]);
        await Assert.That(SortedList(game.Empires.Select(e => planets.Count(p => p.Owner == e)))).IsEqualTo(golden["planetcounts"]);
        await Assert.That(SortedList(game.Empires.Select(e => e.Capital switch {
            Planet p => planets.IndexOf(p) + 1,
            Starbase s => 1000 + starbases.IndexOf(s) + 1,
            _ => 0,
        }))).IsEqualTo(golden["capitals"]);

        // Kingdom NPE personas, packed base 128 the way the harness packs them.
        var personas = game.TurnHandlers.Values.OfType<LegacyNpe.KingdomTurnHandler>()
            .Select(h => (LegacyNpe.NpeCharacter)typeof(LegacyNpe.KingdomTurnHandler)
                .GetField("_persona", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(h)!)
            .ToList();
        await Assert.That(SortedList(personas.Select(p => ((p.ImperialistGene * 128 + p.DefensiveGene) * 128 + p.OffensiveGene) * 128 + p.Provoke)))
            .IsEqualTo(golden["kingdomgenes"]);
        await Assert.That(SortedList(personas.Select(p => (p.WorldPower * 128 + p.SphereX) * 128 + p.Offset)))
            .IsEqualTo(golden["kingdomtraits"]);
    }

    private static string SortedList(IEnumerable<int> values) => string.Join(",", values.Order());

    /// <summary>
    /// The empire's technology as Pascal's TechnologySet bitmask (bit n = TechnologyTypes ordinal n,
    /// TYPES.PAS:63-66). The ordinals are written out here rather than taken from ScenarioLoader's
    /// grant table, so a wrong entry in that table can't cancel itself out.
    /// </summary>
    private static int TechMask(Empire e)
    {
        var mask = 0;
        void Set(int ordinal) => mask |= 1 << ordinal;
        var t = e.Technology;
        if (t.Defenses.Contains(DefenseType.Lam)) Set(1);
        if (t.Defenses.Contains(DefenseType.DefenseSatellite)) Set(2);
        if (t.Defenses.Contains(DefenseType.Gdm)) Set(3);
        if (t.Defenses.Contains(DefenseType.IonCannon)) Set(4);
        if (t.Ships.Contains(ShipType.Fighter)) Set(5);
        if (t.Ships.Contains(ShipType.HunterKiller)) Set(6);
        if (t.Ships.Contains(ShipType.Jumpship)) Set(7);
        if (t.Ships.Contains(ShipType.Jumptransport)) Set(8);
        if (t.Ships.Contains(ShipType.Penetrator)) Set(9);
        if (t.Ships.Contains(ShipType.Starship)) Set(10);
        if (t.Ships.Contains(ShipType.Transport)) Set(11);
        if (t.Resources.Contains(CargoType.Legion)) Set(12);
        if (t.Resources.Contains(CargoType.NinjaLegion)) Set(13);
        if (t.Resources.Contains(CargoType.Ambrosia)) Set(14);
        if (t.Resources.Contains(CargoType.Chemicals)) Set(15);
        if (t.Resources.Contains(CargoType.Metals)) Set(16);
        if (t.Resources.Contains(CargoType.Supplies)) Set(17);
        if (t.Resources.Contains(CargoType.Trillum)) Set(18);
        if (t.Constructions.Contains(ConstructionType.Minefield)) Set(19);
        if (t.Constructions.Contains(ConstructionType.CommandBase)) Set(20);
        if (t.Constructions.Contains(ConstructionType.Fortress)) Set(21);
        if (t.Constructions.Contains(ConstructionType.IndustrialComplex)) Set(22);
        if (t.Constructions.Contains(ConstructionType.Outpost)) Set(23);
        if (t.Constructions.Contains(ConstructionType.Gate)) Set(24);
        if (t.Constructions.Contains(ConstructionType.WarpLink)) Set(25);
        if (t.Constructions.Contains(ConstructionType.Disrupter)) Set(26);
        return mask;
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
