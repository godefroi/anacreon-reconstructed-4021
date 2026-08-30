using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// <see cref="Core.Turns.FleetMovementHandler"/>: GetNewPos/IsPassingThroughGate/
/// IsAtFortress against reference/verify/golden/fleetmove.golden — real FLEET.PAS/INTRFACE.PAS
/// arithmetic, no RNG. See FleetMoveCases' own doc comment for what each case exercises.
/// </summary>
public class FleetMoveTests
{
    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles))]
    [MethodDataSource(typeof(PascalGroundTruth.FleetMoveCases), nameof(PascalGroundTruth.FleetMoveCases.AsDataSource))]
    public async Task MatchesGoldenFile(PascalGroundTruth.FleetMoveCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("fleetmove.golden");
        var expected = golden[c.Name];

        var empire1 = new Empire { Name = "Empire1" };
        var empire2 = new Empire { Name = "Empire2" };
        var owners = new[] { empire1, empire2 };
        var game = new Game(new Galaxy(size: 20));
        game.Empires.Add(empire1);
        game.Empires.Add(empire2);

        var pos = new Coordinate(c.PosX, c.PosY);
        var dest = new Coordinate(c.DestX, c.DestY);

        var fleet = new Fleet { Owner = empire1, Location = pos };
        game.Galaxy.Fleets.Add(fleet);

        if (c.NebulaX != 0 || c.NebulaY != 0) {
            game.Galaxy.SetNebula(new Coordinate(c.NebulaX, c.NebulaY), NebulaType.DenseNebula);
        }

        Stargate? destGate = null;
        if (c.GateKind != 0) {
            game.Galaxy.Stargates.Add(new Stargate {
                Owner = owners[c.GateOwner - 1],
                Location = pos,
                Kind = c.GateKind == 1 ? StargateKind.Gate : StargateKind.WarpLink,
            });
        }

        if (c.DestGateKind != 0) {
            destGate = new Stargate {
                Owner = owners[c.DestGateOwner - 1],
                Location = dest,
                Kind = c.DestGateKind == 1 ? StargateKind.Gate : StargateKind.WarpLink,
            };
            game.Galaxy.Stargates.Add(destGate);
            if (c.DestGateKnown) {
                empire1.Stargates.MarkKnown(destGate);
            }
        }

        if (c.FortressAtPos) {
            game.Galaxy.Starbases.Add(new Starbase { Owner = empire1, Location = pos, Kind = StarbaseKind.Fortress });
        }

        var newPos = FleetMovementHandler.GetNewPos(pos, dest, game);
        var passGate = FleetMovementHandler.IsPassingThroughGate(fleet, pos, dest, game);
        var passFortress = FleetMovementHandler.IsAtFortress(pos, game);

        await Assert.That(newPos?.X ?? 0).IsEqualTo(int.Parse(expected["newpos_x"]));
        await Assert.That(newPos?.Y ?? 0).IsEqualTo(int.Parse(expected["newpos_y"]));
        await Assert.That(passGate ? 1 : 0).IsEqualTo(int.Parse(expected["passgate"]));
        await Assert.That(passFortress ? 1 : 0).IsEqualTo(int.Parse(expected["passfortress"]));
    }
}
