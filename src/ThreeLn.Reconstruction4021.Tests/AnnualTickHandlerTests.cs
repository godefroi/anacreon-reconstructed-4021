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

/// <summary>
/// Verifies Commit 2 of the economy phase: raw material and ship/cargo production for planets
/// (UPDATE.PAS's ProduceRawMaterial/GetIndustrialDistribution/UpdateIndustry/Production, called via
/// AnnualTickHandler.RunProductionPipeline). Expected values here are NOT hand-derived — Pop=1000,
/// Class=EthCls, and Tech=Gate were deliberately chosen to fully exercise the sqrt/pow cascade in
/// GetIndustrialDistribution, which is infeasible to hand-trace reliably. Instead, every expected
/// value was produced by _ref/verify/verify.pas, a standalone FreePascal harness that transcribes
/// the literal DATACNST.PAS/INTRFACE.PAS/UPDATE.PAS/MISC.PAS tables and procedures byte-for-byte and
/// runs the identical call sequence RunProductionPipeline uses, with Rnd hardcoded to return its
/// lower bound (matching FixedRandom(0)'s semantics exactly). This is a from-source ground truth,
/// not a self-check against this C# port — see that file's header comment for its one known deviation.
/// </summary>
public class AnnualTickHandlerProductionTests
{
    private static Game BuildGame(params Planet[] planets)
    {
        var game = new Game(new Core.Galaxy.Galaxy(size: 20));
        game.Galaxy.Planets.AddRange(planets);
        return game;
    }

    [Test]
    public async Task TrillumProductionDrawsDownReservesWhenIndustryAlreadyDeveloped()
    {
        // ProduceRawMaterial runs before UpdateIndustry in RunProductionPipeline, so this result
        // depends only on the Industry level set here, not on anything GetIndustrialDistribution/
        // UpdateIndustry compute afterward for this tick.
        var owner = new Empire { Name = "Test" };
        var planet = new Planet {
            Location = new Coordinate(0, 0),
            Owner = owner,
            Class = WorldClass.EarthLike,
            Type = WorldType.TrillumMine,
            TechLevel = TechLevel.Gate,
            Efficiency = 100,
            Population = 1000,
            TrillumReserve = 500,
        };
        planet.Industry.TrillumMining = 100;
        planet.Cargo.Supplies = 1000;
        var game = BuildGame(planet);
        game.Empires.Add(owner);
        var handler = new AnnualTickHandler(new FixedRandom(0));

        handler.RunAnnualTick(game);

        await Assert.That(planet.Cargo.Trillum).IsEqualTo(238);
        await Assert.That(planet.TrillumReserve).IsEqualTo(498);
    }

    [Test]
    public async Task PreTechWorldOnlyProducesSuppliesEvenWithOtherIndustryDeveloped()
    {
        // TechDev[PreTchLvl] = [sup] only, so che/tri production is gated off entirely despite
        // Industry.Chemical/TrillumMining both being fully developed (100) — matching UPDATE.PAS:1359-1369.
        var owner = new Empire { Name = "Test" };
        var planet = new Planet {
            Location = new Coordinate(0, 0),
            Owner = owner,
            Class = WorldClass.EarthLike,
            Type = WorldType.TrillumMine,
            TechLevel = TechLevel.PreTech,
            Efficiency = 100,
            Population = 0, // kept tiny so UpdatePopulation/UseUpFood can't perturb Cargo.Supplies below
            TrillumReserve = 500,
        };
        planet.Industry.Chemical = 100;
        planet.Industry.TrillumMining = 100;
        planet.Industry.Supply = 100;
        var game = BuildGame(planet);
        game.Empires.Add(owner);
        var handler = new AnnualTickHandler(new FixedRandom(0));

        handler.RunAnnualTick(game);

        await Assert.That(planet.Cargo.Trillum).IsEqualTo(0);
        await Assert.That(planet.Cargo.Chemicals).IsEqualTo(0);
        await Assert.That(planet.Cargo.Supplies).IsEqualTo(122);
        await Assert.That(planet.TrillumReserve).IsEqualTo(500);
    }

    [Test]
    public async Task FullProductionPipelineForCapitalTypeWorld()
    {
        // Exercises the entire pipeline in composed order, including GetIndustrialDistribution's
        // Gamma/Beta cascade (Capital has a principal industry, so it takes the non-PI-less branch)
        // and Production building all seven ship types from one developed ShipyardGeneral level.
        var owner = new Empire { Name = "Test" };
        owner.Technology.Ships.UnionWith(Enum.GetValues<ShipType>());
        var planet = new Planet {
            Location = new Coordinate(0, 0),
            Owner = owner,
            Class = WorldClass.EarthLike,
            Type = WorldType.Capital,
            TechLevel = TechLevel.Gate,
            Efficiency = 100,
            Population = 1000,
            TrillumReserve = 5000,
        };
        planet.Industry.ShipyardGeneral = 100;
        planet.Industry.TrillumMining = 100;
        planet.Cargo.Chemicals = 5000;
        planet.Cargo.Metals = 5000;
        planet.Cargo.Supplies = 5000;
        planet.Cargo.Trillum = 5000;
        var game = BuildGame(planet);
        game.Empires.Add(owner);
        var handler = new AnnualTickHandler(new FixedRandom(0));

        handler.RunAnnualTick(game);

        await Assert.That(planet.Industry.Bioindustry).IsEqualTo(0);
        await Assert.That(planet.Industry.Chemical).IsEqualTo(10);
        await Assert.That(planet.Industry.Mining).IsEqualTo(13);
        await Assert.That(planet.Industry.ShipyardGeneral).IsEqualTo(115);
        await Assert.That(planet.Industry.ShipyardJump).IsEqualTo(0);
        await Assert.That(planet.Industry.ShipyardStarship).IsEqualTo(0);
        await Assert.That(planet.Industry.ShipyardTransport).IsEqualTo(0);
        await Assert.That(planet.Industry.Supply).IsEqualTo(10);
        await Assert.That(planet.Industry.TrillumMining).IsEqualTo(50);

        await Assert.That(planet.Ships.Fighters).IsEqualTo(113);
        await Assert.That(planet.Ships.HunterKillers).IsEqualTo(21);
        await Assert.That(planet.Ships.Jumpships).IsEqualTo(37);
        await Assert.That(planet.Ships.Jumptransports).IsEqualTo(21);
        await Assert.That(planet.Ships.Penetrators).IsEqualTo(16);
        await Assert.That(planet.Ships.Starships).IsEqualTo(8);
        await Assert.That(planet.Ships.Transports).IsEqualTo(42);

        await Assert.That(planet.Cargo.Chemicals).IsEqualTo(4863);
        await Assert.That(planet.Cargo.Metals).IsEqualTo(4211);
        await Assert.That(planet.Cargo.Trillum).IsEqualTo(5217);
        await Assert.That(planet.TrillumReserve).IsEqualTo(4998);
    }

    [Test]
    public async Task NinjaProductionThrottledByScarceAmbrosiaButAmbrosiaNeverDeducted()
    {
        // Preserves UPDATE.PAS:896-916's asymmetric raw-material handling: Ambrosia's requirement is
        // checked (and throttles ninja production when scarce) but only che/met/sup/tri are ever
        // actually subtracted from cargo. Ambrosia stays at 5 despite gating production down to 5.
        var owner = new Empire { Name = "Test" };
        var planet = new Planet {
            Location = new Coordinate(0, 0),
            Owner = owner,
            Class = WorldClass.EarthLike,
            Type = WorldType.NinjaWorld,
            TechLevel = TechLevel.Gate,
            Efficiency = 100,
            Population = 1000,
            TrillumReserve = 5000,
        };
        planet.Industry.Bioindustry = 100;
        planet.Cargo.Chemicals = 5000;
        planet.Cargo.Metals = 5000;
        planet.Cargo.Supplies = 5000;
        planet.Cargo.Trillum = 5000;
        planet.Cargo.Ambrosia = 5;
        var game = BuildGame(planet);
        game.Empires.Add(owner);
        var handler = new AnnualTickHandler(new FixedRandom(0));

        handler.RunAnnualTick(game);

        await Assert.That(planet.Industry.Bioindustry).IsEqualTo(97);
        await Assert.That(planet.Industry.Chemical).IsEqualTo(21);
        await Assert.That(planet.Industry.Mining).IsEqualTo(6);
        await Assert.That(planet.Industry.Supply).IsEqualTo(10);
        await Assert.That(planet.Industry.TrillumMining).IsEqualTo(6);

        await Assert.That(planet.Cargo.NinjaLegions).IsEqualTo(5);
        await Assert.That(planet.Cargo.Ambrosia).IsEqualTo(5);
        await Assert.That(planet.Cargo.Chemicals).IsEqualTo(4998);
    }
}
