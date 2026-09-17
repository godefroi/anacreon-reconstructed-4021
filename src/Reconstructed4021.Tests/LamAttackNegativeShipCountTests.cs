using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// <see cref="CombatStandalone.LAMAttack"/> against a fleet whose <see cref="ShipCounts"/> already
/// has a negative count for some ship type -- not a real Pascal case (ShipArray is an unsigned Word
/// there, so this can't arise), but a real one in this port once a fleet is corrupted by an unrelated
/// bug (see GitHub #42/#44). No golden file: there's no Pascal ground truth for "what should happen
/// to data that's already invalid," just the invariants LAMAttack itself must hold regardless.
/// </summary>
public class LamAttackNegativeShipCountTests
{
    private static (Empire Player, Empire TargetOwner, Fleet Fleet, Reconstructed4021.Core.Game Game) BuildFleetScenario()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Reconstructed4021.Core.Game(galaxy);

        var player = new Empire { Name = "Player" };
        var targetOwner = EmpireFactory.CreateEmpire("Target", null, isEmpress: false, TechLevel.PreTech, restlessness: 0, centralModifier: false, foundingYear: 0);
        game.Empires.Add(player);
        game.Empires.Add(targetOwner);

        var fleet = new Fleet { Location = new Coordinate(5, 5), Owner = targetOwner };
        galaxy.Fleets.Add(fleet);

        return (player, targetOwner, fleet, game);
    }

    [Test]
    public async Task NegativeShipCountNeverProducesNegativeDestroyedCount()
    {
        var (player, _, fleet, game) = BuildFleetScenario();
        fleet.Ships.Jumptransports = -3448129; // corrupted, as seen in a real save (#44)
        fleet.Ships.Fighters = 100;

        var (shipsDestroyed, _) = CombatStandalone.LAMAttack(player, lamToUse: 500, fleet, game);

        await Assert.That(shipsDestroyed[ShipType.Jumptransport]).IsGreaterThanOrEqualTo(0);
        await Assert.That(shipsDestroyed[ShipType.Fighter]).IsGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task PartiallyCorruptedFleetIsDamagedNotDestroyed()
    {
        var (player, targetOwner, fleet, game) = BuildFleetScenario();
        fleet.Ships.Jumptransports = -3448129; // corrupted -- must never read as "already destroyed"
        fleet.Ships.Fighters = 100; // healthy stock that should take real damage

        CombatStandalone.LAMAttack(player, lamToUse: 500, fleet, game);

        await Assert.That(game.Galaxy.Fleets).Contains(fleet); // not destroyed outright
        await Assert.That(targetOwner.News.Any(n => n.Headline == NewsType.FleetDamagedByLams)).IsTrue();
        await Assert.That(targetOwner.News.Any(n => n.Headline == NewsType.FleetDestroyedByLams)).IsFalse();
        await Assert.That(fleet.Ships.Fighters).IsLessThan(100); // the healthy stock still took damage
    }

    [Test]
    public async Task FullyCorruptedFleetIsNotFalselyDestroyed()
    {
        var (player, targetOwner, fleet, game) = BuildFleetScenario();
        foreach (var t in Enum.GetValues<ShipType>()) {
            fleet.Ships[t] = -1;
        }

        var (shipsDestroyed, _) = CombatStandalone.LAMAttack(player, lamToUse: 500, fleet, game);

        foreach (var t in Enum.GetValues<ShipType>()) {
            await Assert.That(shipsDestroyed[t]).IsEqualTo(0);
        }
        await Assert.That(game.Galaxy.Fleets).Contains(fleet);
        await Assert.That(targetOwner.News.Any(n => n.Headline == NewsType.FleetDestroyedByLams)).IsFalse();
    }
}
