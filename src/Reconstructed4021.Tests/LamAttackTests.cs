using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// <see cref="Core.Combat.CombatStandalone"/>: LAMAttack against
/// reference/verify/golden/lamattack.golden — real ATTACK.PAS LAM-strike arithmetic (Round/Trunc
/// against ProtecNeeded/CombatTable) with no RNG involved, so every case is a pure deterministic
/// check of the proportional-distribution formula. See LamAttackCases' own doc comment for what each
/// case is chosen to exercise.
/// </summary>
public class LamAttackTests
{
    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles))]
    [MethodDataSource(typeof(PascalGroundTruth.LamAttackCases), nameof(PascalGroundTruth.LamAttackCases.AsDataSource))]
    public async Task MatchesGoldenFile(PascalGroundTruth.LamAttackCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("lamattack.golden");
        var expected = golden[c.Name];

        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);

        var player = new Empire { Name = "Player" };
        var targetOwner = EmpireFactory.CreateEmpire("Target", null, isEmpress: false, TechLevel.PreTech, restlessness: 0, centralModifier: false, foundingYear: 0);
        game.Empires.Add(player);
        game.Empires.Add(targetOwner);

        IShipCargoHolder target;
        if (c.TargetIsFleet) {
            var fleet = new Fleet { Location = new Coordinate(5, 5), Owner = targetOwner };
            fleet.Ships.Fighters = c.Fgt;
            fleet.Ships.HunterKillers = c.Hkr;
            fleet.Ships.Penetrators = c.Pen;
            fleet.Ships.Transports = c.Trn;
            galaxy.Fleets.Add(fleet);
            target = fleet;
        } else {
            var planet = new Planet { Location = new Coordinate(5, 5), Owner = targetOwner, Class = WorldClass.EarthLike, Type = WorldType.Capital, TechLevel = TechLevel.Warp };
            planet.Defenses.Lams = c.Lam;
            planet.Defenses.DefenseSatellites = c.Def;
            planet.Defenses.Gdms = c.Gdm;
            planet.Defenses.IonCannons = c.Ion;
            galaxy.Planets.Add(planet);
            target = planet;
        }

        var (shipsDestroyed, defensesDestroyed) = CombatStandalone.LAMAttack(player, c.LamToUse, target, game);

        await Assert.That(shipsDestroyed[ShipType.Fighter]).IsEqualTo(int.Parse(expected["shipsdest_fgt"]));
        await Assert.That(shipsDestroyed[ShipType.HunterKiller]).IsEqualTo(int.Parse(expected["shipsdest_hkr"]));
        await Assert.That(shipsDestroyed[ShipType.Penetrator]).IsEqualTo(int.Parse(expected["shipsdest_pen"]));
        await Assert.That(shipsDestroyed[ShipType.Transport]).IsEqualTo(int.Parse(expected["shipsdest_trn"]));
        await Assert.That(defensesDestroyed[DefenseType.Lam]).IsEqualTo(int.Parse(expected["defnsdest_lam"]));
        await Assert.That(defensesDestroyed[DefenseType.DefenseSatellite]).IsEqualTo(int.Parse(expected["defnsdest_def"]));
        await Assert.That(defensesDestroyed[DefenseType.Gdm]).IsEqualTo(int.Parse(expected["defnsdest_gdm"]));
        await Assert.That(defensesDestroyed[DefenseType.IonCannon]).IsEqualTo(int.Parse(expected["defnsdest_ion"]));
    }
}
