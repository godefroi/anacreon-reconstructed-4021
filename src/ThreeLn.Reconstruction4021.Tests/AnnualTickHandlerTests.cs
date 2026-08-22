using ThreeLn.Reconstruction4021.Core;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Turns;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests;

/// <summary>
/// Verifies Commit 1 of the economy phase: population growth, efficiency, and food consumption for
/// planets (UPDATE.PAS:UpdateWorld's planet branch, minus the production pipeline and tech
/// advancement, which are later commits). Revolution/rebellion has its own class,
/// AnnualTickHandlerRevolutionTests, since it's golden-file-backed rather than hand-traced. All
/// tests here use FixedRandom(0), which makes every Rnd(min,max) call resolve to exactly `min` —
/// every value below is hand-computed against that fixed floor, not asserted against a range. This
/// is safe to keep hardcoded (not golden-file-backed): none of UpdatePopulation/UpdateEfficiency/
/// UseUpFood involve a sqrt/pow cascade or other formula that's error-prone to hand-verify.
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

}

/// <summary>
/// Verifies revolution/rebellion for planets (UPDATE.PAS's UpdateRevolution/Rebellion pair, economy
/// phase Commit 1). MatchesGoldenFile checks the real Pascal arithmetic across a case matrix
/// (including the previously-untested military-suppression branch, UPDATE.PAS:715-735) against
/// reference/verify/golden/revolution.golden — computed by a real FreePascal run of
/// reference/verify/revolution.pas (GoldenFileTests), not hand-typed. revolution.pas's
/// UpdateRevolutionScenario starts exactly where UpdateRevolution itself starts — it does NOT run
/// UpdatePopulation/UpdateEfficiency first, unlike RunAnnualTick — so RevolutionCases tracks two
/// population values per case; see its doc comment. It DOES chain UpdateMilitary first (matching
/// UPDATE.PAS:1386-1388's real call order), since Commit 2c's landing means Cargo.Legions can already
/// differ from its starting value by the time UpdateRevolution reads it — see RevolutionCases's doc
/// comment for how that's modeled. The two tests below stay hardcoded: they assert cross-tick
/// bookkeeping behavior (an accumulator resets each year; an inverted Rnd range from a prior tick's
/// negative accumulator must not throw), not a single tick's Pascal arithmetic.
/// </summary>
public class AnnualTickHandlerRevolutionTests
{
    private static Game BuildGame(params Planet[] planets)
    {
        var game = new Game(new Core.Galaxy.Galaxy(size: 20));
        game.Galaxy.Planets.AddRange(planets);
        return game;
    }

    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles))]
    [MethodDataSource(typeof(PascalGroundTruth.RevolutionCases), nameof(PascalGroundTruth.RevolutionCases.AsDataSource))]
    public async Task MatchesGoldenFile(PascalGroundTruth.RevolutionCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("revolution.golden");

        var owner = new Empire { Name = "Test" };
        var planet = new Planet {
            Location = new Coordinate(0, 0),
            Owner = owner,
            Population = c.PlanetPop,
            Class = c.Class,
            TechLevel = c.Tech,
            Efficiency = c.Efficiency,
            RevolutionIndex = c.RevIndex,
            Type = c.Type,
        };
        planet.Cargo.Legions = c.Legions;
        planet.Cargo.NinjaLegions = c.Ninja;
        // UseUpFood runs between UpdatePopulation and UpdateRevolution; foodNeeded is
        // ThgLmt((Population/100)*25), which ThgLmt itself caps at MaxResources (9999) no matter how
        // large Population is — so Supplies=9999 guarantees no starvation for any case here.
        planet.Cargo.Supplies = 9999;
        var game = BuildGame(planet);
        game.Empires.Add(owner);
        var handler = new AnnualTickHandler(new FixedRandom(0));

        handler.RunAnnualTick(game);

        var expected = golden[c.Name];
        await Assert.That(planet.Cargo.Legions).IsEqualTo(int.Parse(expected["legions"]));
        await Assert.That(planet.Cargo.NinjaLegions).IsEqualTo(int.Parse(expected["ninja"]));
        await Assert.That(planet.Population).IsEqualTo(int.Parse(expected["population"]));
        await Assert.That(planet.Efficiency).IsEqualTo(int.Parse(expected["efficiency"]));
        await Assert.That(planet.RevolutionIndex).IsEqualTo(int.Parse(expected["revindex"]));
        await Assert.That(owner.TotalRevolutionIndex).IsEqualTo(int.Parse(expected["total_rev_delta"]));
        await Assert.That(planet.Owner.IsIndependent).IsEqualTo(bool.Parse(expected["rebelled"]));
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
/// AnnualTickHandler.RunProductionPipeline). MatchesGoldenFile checks the real Pascal arithmetic —
/// Pop=1000, Class=EthCls, and Tech=Gate cases deliberately exercise the sqrt/pow cascade in
/// GetIndustrialDistribution, infeasible to hand-trace reliably — against
/// reference/verify/golden/production.golden, computed by a real FreePascal run of
/// reference/verify/production.pas's FullPipeline (GoldenFileTests), not hand-typed. Cargo.Supplies,
/// Cargo.Ambrosia, and Cargo.Legions are excluded from that comparison: UseUpFood/UseUpAmbrosia/
/// UpdateMilitary all run later in the same tick and mutate them based on post-growth Population (and,
/// for Legions, world Type), which production.pas's ground truth (computed on the pre-growth
/// Population the pipeline actually sees, and not modeling UpdateMilitary at all) doesn't model — see
/// NinjaWorldAmbrosiaIsDrainedByUseUpAmbrosiaNotProduction for the one case that actually exercises
/// the Ambrosia gap. See production.pas's header comment for its one known deviation (the split
/// ProductionShips/ProductionCargo loops), which none of ProductionCases's cases trigger.
/// </summary>
public class AnnualTickHandlerProductionTests
{
    private static Game BuildGame(params Planet[] planets)
    {
        var game = new Game(new Core.Galaxy.Galaxy(size: 20));
        game.Galaxy.Planets.AddRange(planets);
        return game;
    }

    private static Planet MakePlanet(PascalGroundTruth.ProductionCase c, Empire owner)
    {
        var planet = new Planet {
            Location = new Coordinate(0, 0),
            Owner = owner,
            Class = c.Class,
            Type = c.Type,
            TechLevel = c.Tech,
            Efficiency = c.Efficiency,
            Population = c.Population,
            TrillumReserve = c.TrillumReserve,
            IsAddictedToAmbrosia = c.AmbAddict,
        };
        planet.Industry.Bioindustry = c.IndusBio;
        planet.Industry.Chemical = c.IndusChe;
        planet.Industry.Mining = c.IndusMin;
        planet.Industry.ShipyardGeneral = c.IndusSYG;
        planet.Industry.ShipyardJump = c.IndusSYJ;
        planet.Industry.ShipyardStarship = c.IndusSYS;
        planet.Industry.ShipyardTransport = c.IndusSYT;
        planet.Industry.Supply = c.IndusSup;
        planet.Industry.TrillumMining = c.IndusTri;
        planet.Cargo.Legions = c.CargoMen;
        planet.Cargo.NinjaLegions = c.CargoNnj;
        planet.Cargo.Ambrosia = c.CargoAmb;
        planet.Cargo.Chemicals = c.CargoChe;
        planet.Cargo.Metals = c.CargoMet;
        planet.Cargo.Supplies = c.CargoSup;
        planet.Cargo.Trillum = c.CargoTri;
        return planet;
    }

    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles))]
    [MethodDataSource(typeof(PascalGroundTruth.ProductionCases), nameof(PascalGroundTruth.ProductionCases.AsDataSource))]
    public async Task MatchesGoldenFile(PascalGroundTruth.ProductionCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("production.golden");
        var expected = golden[c.Name];

        var owner = new Empire { Name = "Test" };
        if (c.AllShipsUnlocked)
            owner.Technology.Ships.UnionWith(Enum.GetValues<ShipType>());
        var planet = MakePlanet(c, owner);
        var game = BuildGame(planet);
        game.Empires.Add(owner);
        var handler = new AnnualTickHandler(new FixedRandom(0));

        handler.RunAnnualTick(game);

        await Assert.That(planet.Industry.Bioindustry).IsEqualTo(int.Parse(expected["bio"]));
        await Assert.That(planet.Industry.Chemical).IsEqualTo(int.Parse(expected["che"]));
        await Assert.That(planet.Industry.Mining).IsEqualTo(int.Parse(expected["min"]));
        await Assert.That(planet.Industry.ShipyardGeneral).IsEqualTo(int.Parse(expected["syg"]));
        await Assert.That(planet.Industry.ShipyardJump).IsEqualTo(int.Parse(expected["syj"]));
        await Assert.That(planet.Industry.ShipyardStarship).IsEqualTo(int.Parse(expected["sys"]));
        await Assert.That(planet.Industry.ShipyardTransport).IsEqualTo(int.Parse(expected["syt"]));
        await Assert.That(planet.Industry.Supply).IsEqualTo(int.Parse(expected["sup"]));
        await Assert.That(planet.Industry.TrillumMining).IsEqualTo(int.Parse(expected["tri"]));

        await Assert.That(planet.Ships.Fighters).IsEqualTo(int.Parse(expected["fgt"]));
        await Assert.That(planet.Ships.HunterKillers).IsEqualTo(int.Parse(expected["hkr"]));
        await Assert.That(planet.Ships.Jumpships).IsEqualTo(int.Parse(expected["jmp"]));
        await Assert.That(planet.Ships.Jumptransports).IsEqualTo(int.Parse(expected["jtn"]));
        await Assert.That(planet.Ships.Penetrators).IsEqualTo(int.Parse(expected["pen"]));
        await Assert.That(planet.Ships.Starships).IsEqualTo(int.Parse(expected["ssp"]));
        await Assert.That(planet.Ships.Transports).IsEqualTo(int.Parse(expected["trn"]));

        await Assert.That(planet.Cargo.NinjaLegions).IsEqualTo(int.Parse(expected["cargonnj"]));
        await Assert.That(planet.Cargo.Chemicals).IsEqualTo(int.Parse(expected["cargoche"]));
        await Assert.That(planet.Cargo.Metals).IsEqualTo(int.Parse(expected["cargomet"]));
        await Assert.That(planet.Cargo.Trillum).IsEqualTo(int.Parse(expected["cargotri"]));
        await Assert.That(planet.TrillumReserve).IsEqualTo(int.Parse(expected["trillumreserve"]));
    }

    [Test]
    public async Task NinjaWorldAmbrosiaIsDrainedByUseUpAmbrosiaNotProduction()
    {
        // Same setup as the "NinjaProductionThrottledByScarceAmbrosia" golden case, checking the one
        // field production.pas can't ground-truth (see this class's header comment): Cargo.Ambrosia
        // ends the tick at 0, not because production touched it (golden's cargoamb stays 5, unchanged
        // from what this case starts with), but because UseUpAmbrosia (population upkeep, running
        // later in the same UpdateWorld) needs ThgLmt((1000/100)*11.5)=115 for 1000 population against
        // only 5 in stock -- nowhere near enough, so it's fully drained (halved-need branch: 115/2=57>5).
        var c = PascalGroundTruth.ProductionCases.All.Single(x => x.Name == "NinjaProductionThrottledByScarceAmbrosia");
        var owner = new Empire { Name = "Test" };
        var planet = MakePlanet(c, owner);
        var game = BuildGame(planet);
        game.Empires.Add(owner);
        var handler = new AnnualTickHandler(new FixedRandom(0));

        handler.RunAnnualTick(game);

        await Assert.That(planet.Cargo.Ambrosia).IsEqualTo(0);
    }
}

/// <summary>
/// Verifies Commit 2b of the economy phase: UseUpAmbrosia (UPDATE.PAS:1163-1276), addiction/
/// starvation-of-ambrosia effects on population, efficiency, tech level, and the addiction flag
/// itself. All planets use Type=Capital (Rebellion can never fire for a capital, UPDATE.PAS:751) and
/// Efficiency=100 (UpdateEfficiency's own switch has no bracket above 99, so it's a no-op regardless
/// of the RNG) to keep every other UpdateWorld step a deterministic no-op except the one under test
/// — Class=ClassM/Tech=Warp (or Tech=PreTech for the regression-guard case) are chosen so
/// UpdatePopulation's "current > basePop" branch applies, which needs no RNG either.
///
/// MatchesGoldenFile covers the cases with real Pascal arithmetic to check (addiction onset,
/// shortage-driven death/efficiency, riot, tech regression) against
/// reference/verify/golden/ambrosia.golden — computed by a real FreePascal run of
/// reference/verify/ambrosia.pas (AmbrosiaGoldenFileTests), not hand-typed, so these can never
/// silently agree with a shared mistake in both the golden values and the C# port. The two guard
/// tests below stay hardcoded: they assert control flow (a branch never taken, a value staying
/// exactly at a boundary), not Pascal arithmetic, so there's nothing for a golden file to add.
///
/// The industrial-sabotage random branch (UPDATE.PAS:1226-1234) is faithful to source but not
/// covered by any case here or in the golden file: Industry starts at 0 on every planet, and
/// RunProductionPipeline (which runs earlier in the same tick) grows it toward a distribution-derived
/// optimum by an amount that's infeasible to hand-trace on top of the shortage math, the same reason
/// AnnualTickHandlerProductionTests leans on the Pascal harness instead of hand-derivation. The
/// sabotage case is a straightforward Trunc(level*Rnd(0,20)/100) applied per industry type
/// (BioInd..TriInd, i.e. all of them) — add coverage if it's ever touched.
/// </summary>
public class AnnualTickHandlerAmbrosiaTests
{
    private static Game BuildGame(params Planet[] planets)
    {
        var game = new Game(new Galaxy(size: 20));
        game.Galaxy.Planets.AddRange(planets);
        return game;
    }

    private static Planet MakeCapital(int population, TechLevel tech, Empire owner) => new() {
        Location = new Coordinate(0, 0),
        Owner = owner,
        Type = WorldType.Capital,
        Class = WorldClass.ClassM,
        TechLevel = tech,
        Efficiency = 100,
        Population = population,
    };

    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles))]
    [MethodDataSource(typeof(PascalGroundTruth.AmbrosiaCases), nameof(PascalGroundTruth.AmbrosiaCases.AsDataSource))]
    public async Task MatchesGoldenFile(PascalGroundTruth.AmbrosiaCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("ambrosia.golden");

        var owner = new Empire { Name = "Test" };
        var planet = MakeCapital(c.PlanetPop, c.Tech, owner);
        planet.Cargo.Supplies = 9999;
        planet.Cargo.Ambrosia = c.StartAmbrosia;
        planet.IsAddictedToAmbrosia = c.StartAddicted;
        var game = BuildGame(planet);
        game.Empires.Add(owner);
        var handler = new AnnualTickHandler(new FixedRandom(c.RngFixedValue));

        handler.RunAnnualTick(game);

        var expected = golden[c.Name];
        await Assert.That(planet.Population).IsEqualTo(int.Parse(expected["population"]));
        await Assert.That(planet.Efficiency).IsEqualTo(int.Parse(expected["efficiency"]));
        await Assert.That((int)planet.TechLevel).IsEqualTo(int.Parse(expected["techlevel"]));
        await Assert.That(planet.Cargo.Ambrosia).IsEqualTo(int.Parse(expected["ambrosia"]));
        await Assert.That(planet.IsAddictedToAmbrosia).IsEqualTo(bool.Parse(expected["addicted"]));
    }

    [Test]
    public async Task AmbrosiaShortageTechRegressionNeverDecrementsBelowPreTech()
    {
        // Tech=PreTech (BasePop=3): Pop(1000)>BasePop(3) still lands in the deterministic branch, but
        // PascalRound(3/100)=0 so growth is a no-op -> Population stays 1000. AmbNeeded=ThgLmt(10*11.5)
        // =115 against 50 in stock -> Lack=65, same Die=7 as the other shortage cases. FixedRandom(9)
        // again selects the tech-regression case, but UPDATE.PAS:1236's guard (Tech>PreTchLvl) must
        // stop it from stepping past the bottom of the enum.
        var owner = new Empire { Name = "Test" };
        var planet = MakeCapital(1000, TechLevel.PreTech, owner);
        planet.Cargo.Supplies = 9999;
        planet.Cargo.Ambrosia = 50;
        planet.IsAddictedToAmbrosia = true;
        var game = BuildGame(planet);
        game.Empires.Add(owner);
        var handler = new AnnualTickHandler(new FixedRandom(9));

        handler.RunAnnualTick(game);

        await Assert.That(planet.TechLevel).IsEqualTo(TechLevel.PreTech);
        await Assert.That(planet.Population).IsEqualTo(993);
        await Assert.That(planet.Efficiency).IsEqualTo(94);
    }

    [Test]
    public async Task NotAddictedWorldWithNoAmbrosiaCargoIsANoOp()
    {
        // Not addicted and Ambrosia=0 in stock -> the entire "not addicted" branch is guarded on
        // Ambrosia>0 (UPDATE.PAS:1254), so nothing happens: no addiction roll, cargo stays at 0.
        var owner = new Empire { Name = "Test" };
        var planet = MakeCapital(1000, TechLevel.Warp, owner);
        planet.Cargo.Supplies = 9999;
        var game = BuildGame(planet);
        game.Empires.Add(owner);
        var handler = new AnnualTickHandler(new FixedRandom(0));

        handler.RunAnnualTick(game);

        await Assert.That(planet.IsAddictedToAmbrosia).IsFalse();
        await Assert.That(planet.Cargo.Ambrosia).IsEqualTo(0);
        await Assert.That(planet.Population).IsEqualTo(1007);
        await Assert.That(planet.Efficiency).IsEqualTo(100);
    }
}

/// <summary>
/// Verifies Commit 2c of the economy phase: UpdateMilitary (UPDATE.PAS:606-617), the growth of a
/// world's military (Cargo.Legions) toward the population/type-derived optimum. MatchesGoldenFile
/// checks the real Pascal arithmetic against reference/verify/golden/military.golden, computed by a
/// real FreePascal run of reference/verify/military.pas (GoldenFileTests), not hand-typed.
/// military.pas's UpdateMilitaryScenario starts exactly where UpdateMilitary itself starts — it does
/// NOT run UpdatePopulation first, unlike RunAnnualTick — so MilitaryCases tracks two population
/// values per case; see its doc comment.
/// </summary>
public class AnnualTickHandlerMilitaryTests
{
    private static Game BuildGame(params Planet[] planets)
    {
        var game = new Game(new Core.Galaxy.Galaxy(size: 20));
        game.Galaxy.Planets.AddRange(planets);
        return game;
    }

    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles))]
    [MethodDataSource(typeof(PascalGroundTruth.MilitaryCases), nameof(PascalGroundTruth.MilitaryCases.AsDataSource))]
    public async Task MatchesGoldenFile(PascalGroundTruth.MilitaryCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("military.golden");

        var owner = new Empire { Name = "Test" };
        var planet = new Planet {
            Location = new Coordinate(0, 0),
            Owner = owner,
            Population = c.PlanetPop,
            Class = c.Class,
            TechLevel = c.Tech,
            Efficiency = 100,
            Type = c.Type,
        };
        planet.Cargo.Legions = c.Legions;
        planet.Cargo.Supplies = 9999;
        var game = BuildGame(planet);
        game.Empires.Add(owner);
        var handler = new AnnualTickHandler(new FixedRandom(c.RngFixedValue));

        handler.RunAnnualTick(game);

        var expected = golden[c.Name];
        await Assert.That(planet.Cargo.Legions).IsEqualTo(int.Parse(expected["legions"]));
    }
}

/// <summary>
/// Verifies Commit 3 of the economy phase: UpdateTechLevel (UPDATE.PAS:1032-1072), tech-level
/// advancement/regression toward an owned world's empire's capital (or a 1-in-50 independent drift
/// for unowned worlds). MatchesGoldenFile checks the real Pascal arithmetic against
/// reference/verify/golden/techlevel.golden, computed patch-based (GoldenFileTests): the real,
/// only-minimally-touched UpdateWorld run against a hand-assembled Universe^
/// (reference/verify/patch-based/runworld.pas), not a per-procedure transcription — this is the one
/// domain where the ground truth includes a real GetCapital/GetTech lookup and Emp=Indep check rather
/// than CapitalTech/IsIndependent standing in for them. Unlike Revolution/MilitaryCase,
/// UpdateTechLevel's formula never reads Population, so every case here uses a tiny Population(10)
/// that always takes UpdatePopulation's "&lt;75" branch — whatever that grows to under a given
/// RngFixedValue doesn't matter, since only TechLevel is asserted. The one guard test below (no
/// capital set) stays hardcoded: it's a defensive no-op for incomplete test/setup state Pascal's real
/// GetCapital can't produce, not Pascal arithmetic for a golden file to add.
/// </summary>
public class AnnualTickHandlerTechLevelTests
{
    private static Game BuildGame(params Planet[] planets)
    {
        var game = new Game(new Core.Galaxy.Galaxy(size: 20));
        game.Galaxy.Planets.AddRange(planets);
        return game;
    }

    private static Planet MakePlanet(TechLevel tech, Empire owner) => new() {
        Location = new Coordinate(0, 0),
        Owner = owner,
        Population = 10,
        Class = WorldClass.ClassM,
        TechLevel = tech,
        Efficiency = 100,
    };

    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles))]
    [MethodDataSource(typeof(PascalGroundTruth.TechLevelCases), nameof(PascalGroundTruth.TechLevelCases.AsDataSource))]
    public async Task MatchesGoldenFile(PascalGroundTruth.TechLevelCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("techlevel.golden");

        Empire owner;
        if (c.IsIndependent) {
            owner = Empire.Independent;
        } else {
            owner = new Empire { Name = "Test" };
            owner.Capital = new Planet {
                Location = new Coordinate(1, 1),
                Owner = owner,
                TechLevel = c.CapitalTech,
            };
        }
        var planet = MakePlanet(c.Tech, owner);
        planet.Cargo.Supplies = 9999;
        var game = BuildGame(planet);
        if (!c.IsIndependent)
            game.Empires.Add(owner);
        var handler = new AnnualTickHandler(new FixedRandom(c.RngFixedValue));

        handler.RunAnnualTick(game);

        var expected = golden[c.Name];
        await Assert.That((int)planet.TechLevel).IsEqualTo(int.Parse(expected["techlevel"]));
    }

    [Test]
    public async Task OwnedWorldWithNoCapitalIsANoOp()
    {
        // Owner.Capital is null (never set) -> UpdateTechLevel has nothing to compare against, so
        // it's a defensive no-op rather than throwing or defaulting to some arbitrary tech level.
        var owner = new Empire { Name = "Test" };
        var planet = MakePlanet(TechLevel.Warp, owner);
        planet.Cargo.Supplies = 9999;
        var game = BuildGame(planet);
        game.Empires.Add(owner);
        var handler = new AnnualTickHandler(new FixedRandom(0));

        handler.RunAnnualTick(game);

        await Assert.That(planet.TechLevel).IsEqualTo(TechLevel.Warp);
    }
}
