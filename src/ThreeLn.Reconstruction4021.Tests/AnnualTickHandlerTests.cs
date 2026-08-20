using ThreeLn.Reconstruction4021.Core;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Turns;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests;

/// <summary>
/// Verifies Commit 1 of the economy phase: population growth, efficiency, food consumption, and
/// revolution/rebellion for planets (UPDATE.PAS:UpdateWorld's planet branch, minus the production
/// pipeline and tech advancement, which are later commits). All tests use FixedRandom(0), which
/// makes every Rnd(min,max) call resolve to exactly `min` — every value below is hand-computed
/// against that fixed floor, not asserted against a range.
/// </summary>
public class AnnualTickHandlerTests
{
    private static Game BuildGame(params Planet[] planets)
    {
        var game = new Game(new Core.Galaxy.Galaxy(size: 20));
        game.Galaxy.Planets.AddRange(planets);
        return game;
    }

    [Test]
    public async Task YearIncrementsEachTick()
    {
        var game = BuildGame();
        var handler = new AnnualTickHandler(new FixedRandom(0));

        handler.RunAnnualTick(game);
        handler.RunAnnualTick(game);

        await Assert.That(game.Year).IsEqualTo(2);
    }

    [Test]
    public async Task EfficiencyClampedAt100ForIndependentWorld()
    {
        var planet = new Planet {
            Location = new Coordinate(0, 0),
            Owner = Empire.Independent,
            Efficiency = 100,
            Class = WorldClass.ClassM,
            TechLevel = TechLevel.Warp,
        };
        var game = BuildGame(planet);
        // Independent-owned worlds roll Rnd(0,1) regardless of current efficiency (UPDATE.PAS:1012-1015);
        // FixedRandom(1) forces Inc=1, pushing 100+1=101 into the Math.Min(100,...) clamp.
        var handler = new AnnualTickHandler(new FixedRandom(1));

        handler.RunAnnualTick(game);

        await Assert.That(planet.Efficiency).IsEqualTo(100);
    }

    [Test]
    public async Task PopulationGrowsExponentiallyBelowBasePop()
    {
        // Class=ClassM (MaxPop=4800), Tech=Warp (BasePop=700): Population(500) < BasePop(700), so
        // UpdatePopulation takes the exponential branch: increase = 128*500/4800 = 13.33 -> round 13.
        // No RNG in this branch; ample Cargo.Supplies avoids UseUpFood perturbing the result.
        var planet = new Planet {
            Location = new Coordinate(0, 0),
            Owner = new Empire { Name = "Test" },
            Population = 500,
            Class = WorldClass.ClassM,
            TechLevel = TechLevel.Warp,
        };
        planet.Cargo.Supplies = 200;
        var game = BuildGame(planet);
        var handler = new AnnualTickHandler(new FixedRandom(0));

        handler.RunAnnualTick(game);

        await Assert.That(planet.Population).IsEqualTo(513);
    }

    [Test]
    public async Task PopulationGrowsLinearlyAboveBasePop()
    {
        // Population(5000) > BasePop[Gate](3000) but < MaxPop[Paradise](5000 exactly, so not the
        // over-max branch): UpdatePopulation takes the linear branch: increase = BasePop/100 = 30.
        var planet = new Planet {
            Location = new Coordinate(0, 0),
            Owner = new Empire { Name = "Test" },
            Population = 5000,
            Class = WorldClass.Paradise,
            TechLevel = TechLevel.Gate,
        };
        planet.Cargo.Supplies = 2000;
        var game = BuildGame(planet);
        var handler = new AnnualTickHandler(new FixedRandom(0));

        handler.RunAnnualTick(game);

        await Assert.That(planet.Population).IsEqualTo(5030);
    }

    [Test]
    public async Task StarvationReducesPopulationAndRaisesRevolutionIndex()
    {
        // Population(1000) > BasePop[Warp](700) -> linear growth to 1007 (UpdatePopulation).
        // Cargo.Supplies=0 -> UseUpFood starves: foodNeeded=ThgLmt(1007/100*25)=251, lack=251,
        // starve=min(251/6, 1007/10)=min(41,100)=41 -> Population=1007-41=966.
        // revInc=min((int)(TechAdj[Warp]=13 * 41/10.0=4.1)=53, 45)=45 -> RevolutionIndex=45.
        // UpdateRevolution then applies ChangeRevIndex(0 + Rnd(-5,2)=-5) -> RevolutionIndex=40.
        var planet = new Planet {
            Location = new Coordinate(0, 0),
            Owner = new Empire { Name = "Test" },
            Population = 1000,
            Class = WorldClass.ClassM,
            TechLevel = TechLevel.Warp,
        };
        planet.Cargo.Supplies = 0;
        var game = BuildGame(planet);
        var handler = new AnnualTickHandler(new FixedRandom(0));

        handler.RunAnnualTick(game);

        await Assert.That(planet.Population).IsEqualTo(966);
        await Assert.That(planet.RevolutionIndex).IsEqualTo(40);
    }

    [Test]
    public async Task WorldRebelsWhenRevolutionIndexExceedsThreshold()
    {
        // Population(2350) > MaxPop[Barren](2340) -> Rnd(-10,10)=-10 -> Population=2340 (UpdatePopulation).
        // Cargo.Supplies=1000 comfortably covers foodNeeded=585 -> no starvation.
        // UpdateRevolution: ChangeRevIndex(0 + Rnd(-5,2)=-5) drops RevolutionIndex from 95 to 90.
        // Military=0 (no Legions/NinjaLegions) never exceeds optimumMilitary -> troop branch skipped.
        // Rebellion trigger: RevolutionIndex(90)>75 and Rnd(1,100)=1<90 and Type<>Capital -> Rebellion fires.
        // Inside Rebellion: rebels=max(1,ThgLmt(sqrt(2340)*65))=3144, chanceToEndRebel=0 (military=0),
        // Rnd(1,100)=1 is not < 0 -> world rebels (not suppressed):
        //   Owner->Independent, Type->Independent, SelfSufficiency->5/5/5/5,
        //   ChangeRevIndex(-Rnd(40,50)=-40) -> RevolutionIndex=50, Cargo.Legions=rebels=3144,
        //   Empire.TotalRevolutionIndex scratch += Rnd(5,10)=5.
        // Tail: Population unchanged (military=0), Efficiency: UpdateEfficiency(80, bracket 76-90,
        // Rnd(0,3)=0) leaves Efficiency=80, then Rebellion's Math.Max(0,80-Rnd(5,15)=5)=75.
        var owner = new Empire { Name = "Test" };
        var planet = new Planet {
            Location = new Coordinate(0, 0),
            Owner = owner,
            Population = 2350,
            Class = WorldClass.Barren,
            TechLevel = TechLevel.Warp,
            Efficiency = 80,
            RevolutionIndex = 95,
            Type = WorldType.Agricultural,
        };
        planet.Cargo.Supplies = 1000;
        var game = BuildGame(planet);
        game.Empires.Add(owner);
        var handler = new AnnualTickHandler(new FixedRandom(0));

        handler.RunAnnualTick(game);

        await Assert.That(planet.Owner).IsSameReferenceAs(Empire.Independent);
        await Assert.That(planet.Type).IsEqualTo(WorldType.Independent);
        await Assert.That(planet.RevolutionIndex).IsEqualTo(50);
        await Assert.That(planet.Population).IsEqualTo(2340);
        await Assert.That(planet.Efficiency).IsEqualTo(75);
        await Assert.That(planet.Cargo.Legions).IsEqualTo(3144);
        await Assert.That(planet.SelfSufficiency.Chemical).IsEqualTo(5);
        await Assert.That(planet.SelfSufficiency.Metal).IsEqualTo(5);
        await Assert.That(planet.SelfSufficiency.Supply).IsEqualTo(5);
        await Assert.That(planet.SelfSufficiency.Trillum).IsEqualTo(5);
        await Assert.That(owner.TotalRevolutionIndex).IsEqualTo(5);
    }

    [Test]
    public async Task SuppressedRebellionProducesNegativeTotalRevolutionIndex_SurvivesNextTick()
    {
        // Two planets under the same empire, each independently triggering a rebellion that gets
        // SUPPRESSED (large Cargo.Legions makes chanceToEndRebel high enough that "empire puts down
        // the rebellion" wins over "world rebels"). Each contributes -Rnd(1,5)=-1 to the scratch
        // accumulator, so Empire.TotalRevolutionIndex goes to -2 — the case that makes next tick's
        // RndVar(TotalRevIndex(Emp), 50) compute an inverted Rnd range (UPDATE.PAS:699): spread=
        // Trunc(-2*0.5)=-1, Rnd(-2-(-1), -2+(-1))=Rnd(-1,-3), which throws in Random.Next without
        // Rnd's "return Min if Max<=Min" guard (INT.PAS:114-115).
        var owner = new Empire { Name = "Test" };

        Planet MakePlanet(int x)
        {
            var planet = new Planet {
                Location = new Coordinate(x, 0),
                Owner = owner,
                Population = 1000,
                Class = WorldClass.Paradise,
                TechLevel = TechLevel.Gate,
                RevolutionIndex = 90,
                Type = WorldType.Agricultural,
            };
            planet.Cargo.Legions = 500;
            planet.Cargo.Supplies = 1000;
            return planet;
        }

        var game = BuildGame(MakePlanet(0), MakePlanet(1));
        game.Empires.Add(owner);
        var handler = new AnnualTickHandler(new FixedRandom(0));

        handler.RunAnnualTick(game);

        await Assert.That(owner.TotalRevolutionIndex).IsEqualTo(-2);
        await Assert.That(game.Galaxy.Planets[0].Owner).IsSameReferenceAs(owner);
        await Assert.That(game.Galaxy.Planets[1].Owner).IsSameReferenceAs(owner);

        // Must not throw despite RndVar(-2, 50) computing an inverted range next tick.
        handler.RunAnnualTick(game);
    }

    [Test]
    public async Task EmpireTotalRevolutionIndexResetsWithoutRebellionActivity()
    {
        // TotalRevolutionIndex is replaced each year from that year's rebellion activity alone
        // (UPDATE.PAS:1445 zeroes the scratch array; UpdateEmpire SETs, not adds, at the end).
        // A tick with no rebellion-triggering planet must reset a stale prior value to 0.
        var owner = new Empire { Name = "Test", TotalRevolutionIndex = 50 };
        var planet = new Planet {
            Location = new Coordinate(0, 0),
            Owner = owner,
            Population = 1000,
            Class = WorldClass.ClassM,
            TechLevel = TechLevel.Warp,
            RevolutionIndex = 0,
        };
        planet.Cargo.Supplies = 1000;
        var game = BuildGame(planet);
        game.Empires.Add(owner);
        var handler = new AnnualTickHandler(new FixedRandom(0));

        handler.RunAnnualTick(game);

        await Assert.That(owner.TotalRevolutionIndex).IsEqualTo(0);
    }
}
