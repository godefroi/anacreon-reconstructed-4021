using System.Globalization;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;
using Reconstructed4021.Tests.PascalGroundTruth;

namespace Reconstructed4021.Tests;

/// <summary>
/// <see cref="FleetMovementHandler"/>'s per-turn fleet update against reference/verify/golden/fleetturn.golden,
/// from the real UpdateAllFleets/UpdateFleet/ExecuteFleetOrders (FLEET.PAS:492-905). See FleetTurnCases'
/// own doc comment for the setup and why the handler runs in legacy order-resolution mode.
/// </summary>
public class FleetTurnTests
{
    [Test]
    [DependsOn<GoldenFileTests>(nameof(GoldenFileTests.RegenerateAllGoldenFiles), ProceedOnFailure = true)]
    [MethodDataSource(typeof(FleetTurnCases), nameof(FleetTurnCases.AsDataSource))]
    public async Task MatchesGoldenFile(FleetTurnCase c)
    {
        var expected = GoldenFile.Load("fleetturn.golden")[c.Name];

        var empire1 = new Empire { Name = "Empire1" };
        var empire2 = new Empire { Name = "Empire2" };
        var game = new Game(new Galaxy(size: 20));
        game.Empires.Add(empire1);
        game.Empires.Add(empire2);

        var capital = new Planet { Owner = empire1, Location = new Coordinate(10, 10) };
        game.Galaxy.Planets.Add(capital);
        empire1.Capital = capital;

        var pos = new Coordinate(c.PosX, c.PosY);

        if (c.NebulaX != 0 || c.NebulaY != 0) {
            game.Galaxy.SetNebula(new Coordinate(c.NebulaX, c.NebulaY), NebulaType.DenseNebula);
        }

        var mine = new Coordinate(c.MineX, c.MineY);
        if (c.HasMine) {
            game.Galaxy.SetMine(mine, empire2);
        }

        if (c.DisrupterX != 0 || c.DisrupterY != 0) {
            game.Galaxy.Stargates.Add(new Stargate { Owner = empire2, Location = new Coordinate(c.DisrupterX, c.DisrupterY), Kind = StargateKind.Disrupter });
        }

        if (c.GateX != 0 || c.GateY != 0) {
            game.Galaxy.Stargates.Add(new Stargate { Owner = empire2, Location = new Coordinate(c.GateX, c.GateY), Kind = StargateKind.Gate });
        }

        if (c.FortressAtPos) {
            game.Galaxy.Starbases.Add(new Starbase { Owner = empire1, Location = pos, Kind = StarbaseKind.Fortress });
        }

        Planet? world = null;
        if (c.HasWorld) {
            world = new Planet { Owner = c.WorldOwner == 1 ? empire1 : empire2, Location = new Coordinate(c.WorldX, c.WorldY) };
            SetAmounts(world, FleetTurnCases.Amounts(c.World));
            game.Galaxy.Planets.Add(world);
        }

        var fleet = new Fleet { Owner = empire1, Location = pos, Fuel = c.Fuel, Status = FleetStatus.Ready };
        if (c.StartsInactive) {
            fleet.Destination = new Coordinate(c.InactiveDestX, c.InactiveDestY);
            fleet.Status = FleetStatus.Inactive;
        }
        SetAmounts(fleet, FleetTurnCases.Amounts(c.Fleet));
        game.Galaxy.Fleets.Add(fleet);

        var compiled = FleetOrderCompiler.Compile(game, empire1, c.Orders);
        await Assert.That(compiled.ErrorMessage).IsNull();
        FleetMovementHandler.CommitOrders(fleet, compiled.Orders, startAt: 1, game);

        var handler = new FleetMovementHandler(new FixedRandom(c.RngFixedValue), useLegacyOrderResolution: true);
        for (var turn = 0; turn < c.Turns; turn++) {
            handler.AdvanceFleets(game, empire2, empire1);
            handler.AdvanceFleets(game, empire1, empire2);
        }

        var actual = new List<string>();
        if (game.Galaxy.Fleets.Contains(fleet)) {
            var dest = fleet.Destination ?? fleet.Location;
            actual.Add("alive=1");
            actual.Add($"x={fleet.Location.X}");
            actual.Add($"y={fleet.Location.Y}");
            actual.Add($"destx={dest.X}");
            actual.Add($"desty={dest.Y}");
            actual.Add($"status={(int)fleet.Status}");
            actual.Add($"fuel={fleet.Fuel.ToString("F6", CultureInfo.InvariantCulture)}");
            AddAmounts(actual, "", fleet);
            actual.Add($"nextorder={fleet.NextOrder}");
        } else {
            actual.Add("alive=0");
        }

        if (c.HasMine) {
            actual.Add($"minescouted={(game.Galaxy.IsMineScoutedBy(empire1, mine) ? 1 : 0)}");
        }

        if (world is not null) {
            AddAmounts(actual, "w_", world);
        }

        AddAmounts(actual, "c_", capital);

        actual.Add($"news1={string.Join(",", empire1.News.Select(n => (int)n.Headline))}");
        actual.Add($"news2={string.Join(",", empire2.News.Select(n => (int)n.Headline))}");

        // Reports every differing field at once; one field's mismatch (fuel, say) usually drags others along.
        var actualFields = actual.Select(f => f.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);

        if (c.InTransitWherePascalSaysInactive) {
            // Pin both sides of the deliberate divergence, then leave status out of the comparison.
            await Assert.That(expected["status"]).IsEqualTo(((int)FleetStatus.Inactive).ToString(CultureInfo.InvariantCulture));
            await Assert.That(fleet.Status).IsEqualTo(FleetStatus.InTransit);
            actualFields["status"] = expected["status"];
        }
        var diffs = expected.Keys.Where(k => k != "case").Union(actualFields.Keys)
            .Where(k => expected.GetValueOrDefault(k) != actualFields.GetValueOrDefault(k))
            .Select(k => $"{k}: Pascal {expected.GetValueOrDefault(k) ?? "(absent)"}, C# {actualFields.GetValueOrDefault(k) ?? "(absent)"}")
            .ToList();
        if (diffs.Count > 0) {
            Assert.Fail(string.Join("\n", diffs));
        }
    }

    private static void SetAmounts(IShipCargoHolder holder, int[] amounts)
    {
        var shipTypes = Enum.GetValues<ShipType>();
        var cargoTypes = Enum.GetValues<CargoType>();
        for (var i = 0; i < shipTypes.Length; i++) {
            holder.Ships[shipTypes[i]] = amounts[i];
        }
        for (var i = 0; i < cargoTypes.Length; i++) {
            holder.Cargo[cargoTypes[i]] = amounts[shipTypes.Length + i];
        }
    }

    private static void AddAmounts(List<string> fields, string prefix, IShipCargoHolder holder)
    {
        var keys = FleetTurnCases.ResourceKeys;
        var shipTypes = Enum.GetValues<ShipType>();
        var cargoTypes = Enum.GetValues<CargoType>();
        for (var i = 0; i < shipTypes.Length; i++) {
            fields.Add($"{prefix}{keys[i]}={holder.Ships[shipTypes[i]]}");
        }
        for (var i = 0; i < cargoTypes.Length; i++) {
            fields.Add($"{prefix}{keys[shipTypes.Length + i]}={holder.Cargo[cargoTypes[i]]}");
        }
    }
}
