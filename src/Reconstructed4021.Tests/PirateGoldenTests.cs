using System.Reflection;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Types;
using Reconstructed4021.LegacyNpe;

namespace Reconstructed4021.Tests;

/// <summary>
/// Cross-checks <see cref="PirateTurnHandler.PlayTurn"/> against NPE01.PAS's real
/// <c>ImplementPirateNPE</c> for one turn, via <c>reference/verify/golden/npepirate.golden</c> (see
/// <c>runworld.pas</c>'s <c>RunNpePirateCase</c> and <see cref="PascalGroundTruth.PirateCases"/> for what
/// each mode builds). Unlike <see cref="PirateTurnHandlerTests"/> (which hardcodes expected values from
/// reading the transcribed formulas), this drives the SAME seeded <see cref="GroundTruthRandom"/> both
/// sides now share and compares against a live Pascal run — the two sides can only agree by actually
/// matching, not by both being wrong the same way.
///
/// Every mode pre-seeds at most one tracked fleet (Mode 1 has none until DeployNewFleets creates it),
/// so there's never an ordering question between this port's Dictionary-keyed FleetStates and Pascal's
/// slot-indexed FleetData — deliberately, to sidestep needing any cross-side ordering convention at all.
/// </summary>
public class PirateGoldenTests
{
    private static (Game Game, Galaxy Galaxy, Empire Owner) NewGame()
    {
        var galaxy = new Galaxy(size: 25);
        var game = new Game(galaxy);
        var owner = EmpireFactory.CreateEmpire("Pirate", null, isEmpress: false, TechLevel.Warp, restlessness: 0, centralModifier: false, foundingYear: 0);
        game.Empires.Add(owner);
        return (game, galaxy, owner);
    }

    /// <summary>
    /// Uses <see cref="PirateTurnHandler"/>'s internal load-path constructor via reflection, not the
    /// public one -- the public constructor calls <c>NpeToolkit.SetEmpireDefenses</c> (an extra
    /// <c>Rnd(1,100)</c> draw matching real Pascal's own <c>InitializePirateNPE</c>), which
    /// <c>RunNpePirateCase</c> deliberately skips (see that procedure's own doc comment) to keep this
    /// domain's seed reproducing exactly the sequence <c>ImplementPirateNPE</c> itself draws from, with
    /// nothing consumed ahead of it.
    /// </summary>
    private static PirateTurnHandler CreateHandler(Random random)
    {
        var huntingGround = new byte[20, 20];
        for (var x = 0; x < 20; x++) {
            for (var y = 0; y < 20; y++) {
                huntingGround[x, y] = 25;
            }
        }

        var ctor = typeof(PirateTurnHandler).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance, null,
            [typeof(Dictionary<Fleet, PirateFleetState>), typeof(byte[,]), typeof(byte[]), typeof(Random)], null)!;
        return (PirateTurnHandler)ctor.Invoke([new Dictionary<Fleet, PirateFleetState>(), huntingGround, new byte[9], random]);
    }

    private static Dictionary<Fleet, PirateFleetState> FleetStatesOf(PirateTurnHandler handler) =>
        (Dictionary<Fleet, PirateFleetState>)typeof(PirateTurnHandler)
            .GetField("_fleetStates", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(handler)!;

    private static byte[,] HuntingGroundOf(PirateTurnHandler handler) =>
        (byte[,])typeof(PirateTurnHandler)
            .GetField("_huntingGround", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(handler)!;

    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles))]
    [MethodDataSource(typeof(PascalGroundTruth.PirateCases), nameof(PascalGroundTruth.PirateCases.AsDataSource))]
    public async Task MatchesGoldenFile(PascalGroundTruth.PirateCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("npepirate.golden")[c.Name];
        var (game, galaxy, owner) = NewGame();
        var random = new GroundTruthRandom(c.Seed);
        var handler = CreateHandler(random);
        var enemy = EmpireFactory.CreateEmpire("Enemy", null, isEmpress: false, TechLevel.Warp, restlessness: 0, centralModifier: false, foundingYear: 0);
        game.Empires.Add(enemy);

        Fleet trackedFleet;
        Planet? targetWorld = null;

        switch (c.Mode) {
            case 1: {
                // No pre-existing fleet -- DeployNewFleets creates one from this planet's stock (the
                // real GetFleetComposition band that needs only hkr, NPE01.PAS:376-379).
                var home = new Planet { Location = new Coordinate(10, 10), Owner = owner, Type = WorldType.Capital, Class = WorldClass.EarthLike, TechLevel = TechLevel.Warp };
                home.Ships[ShipType.HunterKiller] = 3000;
                owner.Capital = home;
                galaxy.Planets.Add(home);

                // All-zero except one heavy cell: GetPatrolDestination's weighted scan is forced into
                // that exact block deterministically, matching RunNpePirateCase's own Mode 1 setup.
                var grid = HuntingGroundOf(handler);
                Array.Clear(grid);
                grid[c.HeavyBX - 1, c.HeavyBY - 1] = 200;

                handler.PlayTurn(owner, game);

                await Assert.That(galaxy.Fleets).Count().IsEqualTo(1);
                trackedFleet = galaxy.Fleets[0];
                break;
            }

            case 2: {
                trackedFleet = new Fleet { Location = new Coordinate(20, 20), Owner = owner };
                trackedFleet.Ships[ShipType.HunterKiller] = 50;
                trackedFleet.Ships[ShipType.Jumpship] = 50;
                galaxy.Fleets.Add(trackedFleet);
                FleetStatesOf(handler)[trackedFleet] = new PirateFleetState { Mission = NpeMissionType.WaitForTransports, Waiting = 3, BlockX = 2, BlockY = 2 };

                var enemyFleet = new Fleet { Location = new Coordinate(22, 20), Owner = enemy, Destination = new Coordinate(25, 20) };
                enemyFleet.Ships[ShipType.Jumptransport] = 50;
                galaxy.Fleets.Add(enemyFleet);

                handler.PlayTurn(owner, game);
                break;
            }

            case 3: {
                var home = new Planet { Location = new Coordinate(10, 10), Owner = owner, Type = WorldType.Capital, Class = WorldClass.EarthLike, TechLevel = TechLevel.Warp };
                owner.Capital = home;
                galaxy.Planets.Add(home);

                trackedFleet = new Fleet { Location = new Coordinate(20, 20), Owner = owner };
                galaxy.Fleets.Add(trackedFleet);
                FleetStatesOf(handler)[trackedFleet] = new PirateFleetState { Mission = NpeMissionType.WaitForTransports, Waiting = 0, BlockX = 2, BlockY = 2 };

                handler.PlayTurn(owner, game);
                break;
            }

            case 4: {
                var home = new Planet { Location = new Coordinate(10, 10), Owner = owner, Type = WorldType.Capital, Class = WorldClass.EarthLike, TechLevel = TechLevel.Warp };
                owner.Capital = home;
                galaxy.Planets.Add(home);

                trackedFleet = new Fleet { Location = new Coordinate(15, 15), Owner = owner };
                trackedFleet.Ships[ShipType.Fighter] = 50;
                trackedFleet.Ships[ShipType.Transport] = 20;
                trackedFleet.Ships[ShipType.HunterKiller] = 100;
                trackedFleet.Ships[ShipType.Jumpship] = 100;
                galaxy.Fleets.Add(trackedFleet);

                var enemyFleet = new Fleet { Location = new Coordinate(15, 15), Owner = enemy };
                enemyFleet.Ships[ShipType.Jumptransport] = 30;
                enemyFleet.Ships[ShipType.HunterKiller] = 10;
                galaxy.Fleets.Add(enemyFleet);

                FleetStatesOf(handler)[trackedFleet] = new PirateFleetState { Mission = NpeMissionType.AttackTransports, Waiting = 1, Target = enemyFleet };

                handler.PlayTurn(owner, game);
                break;
            }

            case 5: {
                var home = new Planet { Location = new Coordinate(10, 10), Owner = owner, Type = WorldType.Capital, Class = WorldClass.EarthLike, TechLevel = TechLevel.Warp };
                owner.Capital = home;
                galaxy.Planets.Add(home);

                targetWorld = new Planet { Location = new Coordinate(50, 50), Owner = enemy, Type = WorldType.Capital, Class = WorldClass.EarthLike, TechLevel = TechLevel.Warp, Population = 1000 };
                targetWorld.Cargo[CargoType.Chemicals] = 500;
                targetWorld.Cargo[CargoType.Metals] = 300;
                enemy.Capital = targetWorld;
                galaxy.Planets.Add(targetWorld);

                trackedFleet = new Fleet { Location = new Coordinate(15, 15), Owner = owner };
                trackedFleet.Ships[ShipType.Fighter] = 5000;
                trackedFleet.Ships[ShipType.HunterKiller] = 5000;
                // A world can only actually be conquered by landed troops -- see RunNpePirateCase's own
                // Mode 5 comment (ship-vs-ship combat alone always ends in an attacker retreat).
                trackedFleet.Ships[ShipType.Jumptransport] = 20;
                trackedFleet.Cargo[CargoType.NinjaLegion] = 50;
                galaxy.Fleets.Add(trackedFleet);

                FleetStatesOf(handler)[trackedFleet] = new PirateFleetState { Mission = NpeMissionType.AttackWorld, Waiting = 1, Target = targetWorld };

                handler.PlayTurn(owner, game);
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(c), c.Mode, "Unknown PirateCase mode.");
        }

        var state = FleetStatesOf(handler)[trackedFleet];
        await Assert.That((int)state.Mission).IsEqualTo(int.Parse(golden["mission"]));
        await Assert.That(state.Waiting).IsEqualTo(int.Parse(golden["waiting"]));
        await Assert.That(trackedFleet.Destination?.X ?? -1).IsEqualTo(int.Parse(golden["destx"]));
        await Assert.That(trackedFleet.Destination?.Y ?? -1).IsEqualTo(int.Parse(golden["desty"]));
        await Assert.That(trackedFleet.Ships[ShipType.Fighter]).IsEqualTo(int.Parse(golden["shfgt"]));
        await Assert.That(trackedFleet.Ships[ShipType.HunterKiller]).IsEqualTo(int.Parse(golden["shhkr"]));
        await Assert.That(trackedFleet.Ships[ShipType.Jumpship]).IsEqualTo(int.Parse(golden["shjmp"]));
        await Assert.That(trackedFleet.Ships[ShipType.Transport]).IsEqualTo(int.Parse(golden["shtrn"]));
        await Assert.That(trackedFleet.Cargo[CargoType.Chemicals]).IsEqualTo(int.Parse(golden["crche"]));
        await Assert.That(trackedFleet.Cargo[CargoType.Metals]).IsEqualTo(int.Parse(golden["crmet"]));
        await Assert.That(galaxy.Fleets.Count).IsEqualTo(int.Parse(golden["activefleetcount"]));
        await Assert.That(state.BlockX).IsEqualTo(int.Parse(golden["blockx"]));
        await Assert.That(state.BlockY).IsEqualTo(int.Parse(golden["blocky"]));

        // Modes 2/3 only: the +15/-5 HuntingGround arithmetic. Mode 1's own cell never moves
        // (DeployNewFleets doesn't touch HuntingGround) -- the blockx/blocky assertion above is what
        // actually verifies GetPatrolDestination's weighted-scan result for that mode.
        if (c.Mode is 2 or 3) {
            await Assert.That(HuntingGroundOf(handler)[state.BlockX - 1, state.BlockY - 1]).IsEqualTo(byte.Parse(golden["hgvalue"]));
        }

        // targetowner=8 is Pascal's Indep ordinal (Empire1..Empire8 are 0..7) -- Mode 5 only.
        if (targetWorld is not null) {
            await Assert.That(targetWorld.Owner.IsIndependent).IsEqualTo(int.Parse(golden["targetowner"]) == 8);
        }
    }
}
