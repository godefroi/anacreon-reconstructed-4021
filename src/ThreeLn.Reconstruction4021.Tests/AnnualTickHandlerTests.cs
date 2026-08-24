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
        // RunProductionPipeline first: Type=Agricultural (default) takes GetIndustrialDistribution's
        // raw-material-only branch with a nonzero Chemical optimum, but Cargo.Metals=0 can't afford
        // to grow it -> ReportResourceShortfall's first-shortfall-this-tick bump fires once (UPDATE.PAS's
        // ReportPlanetLack, UPDATE.PAS:971) -> RevolutionIndex=0+1=1.
        // Population(1000) > BasePop[Warp](700) -> linear growth to 1007 (UpdatePopulation).
        // Cargo.Supplies=0 -> UseUpFood starves: foodNeeded=ThgLmt(1007/100*25)=251, lack=251,
        // starve=min(251/6, 1007/10)=min(41,100)=41 -> Population=1007-41=966.
        // revInc=min((int)(TechAdj[Warp]=13 * 41/10.0=4.1)=53, 45)=45 -> RevolutionIndex=1+45=46.
        // UpdateRevolution then applies ChangeRevIndex(0 + Rnd(-5,2)=-5) -> RevolutionIndex=41.
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
        await Assert.That(planet.RevolutionIndex).IsEqualTo(41);
    }

}

/// <summary>
/// Verifies revolution/rebellion for planets (UPDATE.PAS's UpdateRevolution/Rebellion pair, economy
/// phase Commit 1). MatchesGoldenFile checks the real Pascal arithmetic across a case matrix
/// (including the previously-untested military-suppression branch, UPDATE.PAS:715-735) against
/// reference/verify/golden/revolution.golden, computed patch-based (GoldenFileTests): the real,
/// only-minimally-touched UpdateWorld run against a hand-assembled Universe^
/// (reference/verify/runworld.pas's revolution domain), not a per-procedure transcription
/// — see RevolutionCases's doc comment for what that replaced, including a real production-pipeline
/// gap (ReportPlanetLack's RevolutionIndex bump) this migration caught. Every case uses Ninja=0
/// (hardcoded in the driver, matching every case here). The two tests below stay hardcoded: they
/// assert cross-tick bookkeeping behavior (an accumulator resets each year; an inverted Rnd range
/// from a prior tick's negative accumulator must not throw), not a single tick's Pascal arithmetic.
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

    // The tier cascade at the end of UpdateRevolution (UPDATE.PAS:737-753) is a mutually-exclusive
    // if/else-if chain, not one boolean condition -- these tests prove exactly one cascade headline
    // fires per call, at the right tier, and that RevolutionIndex>75 genuinely has two sub-outcomes
    // (Rebellion vs RebellionWarning4) rather than always calling Rebellion. Every case uses
    // Type=Outpost (0% of DATACNST.PAS's OptMilitary table) with zero Legions/NinjaLegions, which
    // collapses both the military-presence block's own Jitter(0,_) calls and Rebellion's own
    // military-driven chanceToEndRebel to an exactly-zero, FixedRandom-independent result (Rnd's own
    // degenerate-range guard, INT.PAS:114-115, returns Min outright when Max<=Min) -- so the only
    // FixedRandom-sensitive steps left are UpdateRevolution's own initial ChangeRevIndex(empRevAdj+
    // Rnd(-5,2)) decrease (also 0 here, since a fresh Empire's TotalRevIndex/RevolutionFactor are both
    // 0, making that Jitter's own range degenerate too) and the cascade's own Rnd(1,100) roll. Cases
    // read a planet's News for only the 6 cascade-specific headlines, ignoring unrelated noise from
    // other UpdateWorld steps (e.g. IndustryLacksMetals from a fresh planet's own industry growth).
    private static readonly HashSet<NewsType> _cascadeHeadlines = [
        NewsType.RebellionWarning1, NewsType.RebellionWarning2, NewsType.RebellionWarning3, NewsType.RebellionWarning4,
        NewsType.WorldRebelled, NewsType.RebellionSuppressed,
    ];

    private static Planet MakeCascadeTestPlanet(int revIndex, Empire owner)
    {
        var planet = new Planet {
            Location = new Coordinate(0, 0),
            Owner = owner,
            Population = 1000,
            Class = WorldClass.ClassM,
            TechLevel = TechLevel.Warp,
            Type = WorldType.Outpost, // OptMilitary[Outpost]=0% keeps the military-presence block inert.
            RevolutionIndex = revIndex,
        };
        planet.Cargo.Supplies = 9999; // avoid UseUpFood starvation, which would also bump RevolutionIndex.
        return planet;
    }

    [Test]
    public async Task RevIndex76_RollSucceeds_FiresRebellionNotWarning()
    {
        // FixedRandom(5): initial decrease delta=Rnd(-5,2)=0, so RevolutionIndex stays exactly 76
        // entering the cascade. Cascade roll Rnd(1,100)=6 < 76 -> Rebellion (chanceToEndRebel=0 there
        // too, so it's the "world rebels" outcome specifically, not "empire puts down the rebellion").
        var owner = new Empire { Name = "Test" };
        var planet = MakeCascadeTestPlanet(76, owner);
        var game = BuildGame(planet);
        game.Empires.Add(owner);

        new AnnualTickHandler(new FixedRandom(5)).RunAnnualTick(game);

        var cascadeNews = owner.News.Where(n => _cascadeHeadlines.Contains(n.Headline)).ToList();
        await Assert.That(cascadeNews.Select(n => n.Headline)).IsEquivalentTo([NewsType.WorldRebelled]);
        await Assert.That(planet.Owner).IsSameReferenceAs(Empire.Independent);
    }

    [Test]
    public async Task RevIndex76_RollFails_FiresWarningNotRebellion()
    {
        // FixedRandom(75): initial decrease delta=Rnd(-5,2)=70, so Start=6 -> RevolutionIndex=76
        // entering the cascade, same as the roll-succeeds case above. Cascade roll Rnd(1,100)=76 is
        // NOT <76 -> RebellionWarning4, and Rebellion never runs at all (owner stays unchanged).
        var owner = new Empire { Name = "Test" };
        var planet = MakeCascadeTestPlanet(6, owner);
        var game = BuildGame(planet);
        game.Empires.Add(owner);

        new AnnualTickHandler(new FixedRandom(75)).RunAnnualTick(game);

        var cascadeNews = owner.News.Where(n => _cascadeHeadlines.Contains(n.Headline)).ToList();
        await Assert.That(cascadeNews.Select(n => n.Headline)).IsEquivalentTo([NewsType.RebellionWarning4]);
        await Assert.That(planet.Owner).IsSameReferenceAs(owner);
    }

    [Test]
    public async Task RevIndex75_FallsThroughToWarning4NotTheAbove75Branch()
    {
        // 75 is not >75, so it takes the plain ">70" tier (also RebellionWarning4) instead of ever
        // reaching the roll -- proving the >75 branch's own boundary is exclusive, not inclusive.
        var owner = new Empire { Name = "Test" };
        var planet = MakeCascadeTestPlanet(75, owner);
        var game = BuildGame(planet);
        game.Empires.Add(owner);

        new AnnualTickHandler(new FixedRandom(5)).RunAnnualTick(game);

        var cascadeNews = owner.News.Where(n => _cascadeHeadlines.Contains(n.Headline)).ToList();
        await Assert.That(cascadeNews.Select(n => n.Headline)).IsEquivalentTo([NewsType.RebellionWarning4]);
    }

    [Test]
    public async Task RevIndex44_FiresWarning2Only()
    {
        // Mid-tier sanity check: 44 is >43 but not >66, so exactly RebellionWarning2 fires -- not
        // RebellionWarning1, RebellionWarning3, or more than one headline at once.
        var owner = new Empire { Name = "Test" };
        var planet = MakeCascadeTestPlanet(44, owner);
        var game = BuildGame(planet);
        game.Empires.Add(owner);

        new AnnualTickHandler(new FixedRandom(5)).RunAnnualTick(game);

        var cascadeNews = owner.News.Where(n => _cascadeHeadlines.Contains(n.Headline)).ToList();
        await Assert.That(cascadeNews.Select(n => n.Headline)).IsEquivalentTo([NewsType.RebellionWarning2]);
    }

    [Test]
    public async Task RevIndex30_FiresNoCascadeHeadline()
    {
        // 30 is not >30 -- below every tier's threshold, so the cascade fires nothing at all.
        var owner = new Empire { Name = "Test" };
        var planet = MakeCascadeTestPlanet(30, owner);
        var game = BuildGame(planet);
        game.Empires.Add(owner);

        new AnnualTickHandler(new FixedRandom(5)).RunAnnualTick(game);

        var cascadeNews = owner.News.Where(n => _cascadeHeadlines.Contains(n.Headline)).ToList();
        await Assert.That(cascadeNews).IsEmpty();
    }
}

/// <summary>
/// Verifies Commit 2 of the economy phase: raw material and ship/cargo production for planets
/// (UPDATE.PAS's ProduceRawMaterial/GetIndustrialDistribution/UpdateIndustry/Production, called via
/// AnnualTickHandler.RunProductionPipeline). MatchesGoldenFile checks the real Pascal arithmetic —
/// Pop=1000, Class=EthCls, and Tech=Gate cases deliberately exercise the sqrt/pow cascade in
/// GetIndustrialDistribution, infeasible to hand-trace reliably — against
/// reference/verify/golden/production.golden, computed by a real FreePascal run of the real, patched
/// UpdateWorld (GoldenFileTests; see ProductionCases's doc comment for the harness bugs that migration
/// caught), not the old isolated FullPipeline transcription and not hand-typed.
///
/// Cargo.Supplies, Cargo.Ambrosia, Cargo.Legions, Cargo.Chemicals, and Cargo.Metals are excluded from
/// that comparison — each is mutated by a real UpdateWorld step this tick that RunAnnualTick either
/// runs on different (post-growth) state or doesn't run at all: UseUpFood/UseUpAmbrosia/UpdateMilitary
/// all act on post-growth Population (and, for Legions, world Type), and UpdateDefenses
/// (UPDATE.PAS:1278-1351) — not yet ported — draws down Cargo.Chemicals/Metals building defenses
/// toward a population-driven target. See NinjaWorldAmbrosiaIsDrainedByUseUpAmbrosiaNotProduction for
/// the one case that actually exercises the Ambrosia gap. None of ProductionCases's cases exercise a
/// defense type that would touch Cargo.Trillum the same way (UpdateDefenses's raw-material loop also
/// covers tri), so Cargo.Trillum stays asserted — revisit this exclusion list if a future case does.
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
/// reference/verify/golden/ambrosia.golden, computed patch-based (GoldenFileTests): the real,
/// only-minimally-touched UpdateWorld run against a hand-assembled Universe^
/// (reference/verify/runworld.pas's ambrosia domain), not a per-procedure transcription —
/// see AmbrosiaCases's doc comment for what that replaced. The two guard tests below stay hardcoded:
/// they assert control flow (a branch never taken, a value staying exactly at a boundary), not Pascal
/// arithmetic, so there's nothing for a golden file to add.
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

        // News spot-checks, by case name: the golden file itself only records final numeric state, not
        // News (see GoldenFileTests), so these confirm the right headline fires for the case its own
        // name identifies, without trying to hand-derive an exact Parm1.
        var headlines = owner.News.Select(n => n.Headline).ToList();
        switch (c.Name) {
            case "ShortageRiot":
                await Assert.That(headlines).Contains(NewsType.RiotDeaths);
                break;
            case "ShortageTechRegression":
                await Assert.That(headlines).Contains(NewsType.TechLevelRegressed);
                break;
        }
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
/// checks the real Pascal arithmetic against reference/verify/golden/military.golden, computed
/// patch-based (GoldenFileTests): the real, only-minimally-touched UpdateWorld run against a
/// hand-assembled Universe^ (reference/verify/runworld.pas's military domain), not a
/// per-procedure transcription — see MilitaryCases's doc comment for what that replaced.
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
/// (reference/verify/runworld.pas), not a per-procedure transcription — this is the one
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

/// <summary>
/// Verifies Commit 5a of the economy phase: empire-level tech research (UPDATE.PAS's
/// UpdateEmpire/NewTechLevel/GetChanceForNewTech/GetNewTech, called via AnnualTickHandler's
/// per-empire loop in RunAnnualTick). MatchesGoldenFile checks the real Pascal arithmetic — the
/// Trunc(percent*eff/100) lab-chance formula and GetNewTech's TechDev-membership pick — against
/// reference/verify/golden/empire.golden, computed by runworld.pas's empire domain calling
/// UpdateEmpire directly (see EmpireCases's doc comment for the 26-bit Technology encoding). Every
/// lab planet/starbase leaves Owner.Capital unset (null) — a defensive no-op for Commit 3's
/// UpdateTechLevel (see AnnualTickHandlerTechLevelTests.OwnedWorldWithNoCapitalIsANoOp), which also
/// runs during the same RunAnnualTick call, before the per-empire loop reads these worlds'
/// TechLevel; leaving it null is simpler than matching capital/lab TechLevel by hand and gives the
/// same guarantee (no drift) since GetChanceForNewTech's own lab classification never reads
/// Owner.Capital at all — only Empire.TechnologyLevel.
/// </summary>
public class AnnualTickHandlerEmpireTests
{
    private static Game BuildGame(Empire owner, List<Planet> planets, List<Starbase> starbases)
    {
        var game = new Core.Galaxy.Galaxy(size: 20);
        var result = new Game(game);
        result.Galaxy.Planets.AddRange(planets);
        result.Galaxy.Starbases.AddRange(starbases);
        result.Empires.Add(owner);
        return result;
    }

    private static Planet MakePlanet(PascalGroundTruth.EmpireLab lab, Empire owner, int x) => new() {
        Location = new Coordinate(x, 0),
        Owner = owner,
        Population = 10,
        Type = lab.Type,
        Class = lab.Class,
        TechLevel = lab.Tech,
        Efficiency = lab.Efficiency,
    };

    private static Starbase MakeStarbase(PascalGroundTruth.EmpireLab lab, Empire owner, int x) => new() {
        Location = new Coordinate(x, 0),
        Owner = owner,
        Kind = StarbaseKind.CommandBase, // non-complex: only Efficiency/TechLevel run unconditionally (Commit 4)
        Type = lab.Type,
        TechLevel = lab.Tech,
        Efficiency = lab.Efficiency,
    };

    /// <summary>Encodes/decodes Empire.Technology as the same 26-bit mask runworld.pas's empire domain
    /// uses (see EmpireCases's doc comment) — deliberately independent of TechCatalog's own entry
    /// ordering, not shared with it, so a bug in that ordering can't hide on both sides of the
    /// golden-file comparison. Internal (not private): EmpireFactoryTests reuses this same pair rather
    /// than duplicating a third independent encoder/decoder.</summary>
    internal static void ApplyTechnologyBitmask(UnlockedTechnology tech, int mask)
    {
        var bit = 0;
        foreach (var d in Enum.GetValues<DefenseType>()) { if (((mask >> bit) & 1) != 0) tech.Defenses.Add(d); bit++; }
        foreach (var s in Enum.GetValues<ShipType>()) { if (((mask >> bit) & 1) != 0) tech.Ships.Add(s); bit++; }
        foreach (var c in Enum.GetValues<CargoType>()) { if (((mask >> bit) & 1) != 0) tech.Resources.Add(c); bit++; }
        foreach (var k in Enum.GetValues<ConstructionType>()) { if (((mask >> bit) & 1) != 0) tech.Constructions.Add(k); bit++; }
    }

    internal static int ComputeTechnologyBitmask(UnlockedTechnology tech)
    {
        var bit = 0;
        var mask = 0;
        foreach (var d in Enum.GetValues<DefenseType>()) { if (tech.Defenses.Contains(d)) mask |= 1 << bit; bit++; }
        foreach (var s in Enum.GetValues<ShipType>()) { if (tech.Ships.Contains(s)) mask |= 1 << bit; bit++; }
        foreach (var c in Enum.GetValues<CargoType>()) { if (tech.Resources.Contains(c)) mask |= 1 << bit; bit++; }
        foreach (var k in Enum.GetValues<ConstructionType>()) { if (tech.Constructions.Contains(k)) mask |= 1 << bit; bit++; }
        return mask;
    }

    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles))]
    [MethodDataSource(typeof(PascalGroundTruth.EmpireCases), nameof(PascalGroundTruth.EmpireCases.AsDataSource))]
    public async Task MatchesGoldenFile(PascalGroundTruth.EmpireCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("empire.golden");

        var owner = new Empire { Name = "Test", TechnologyLevel = c.TechLevel };
        ApplyTechnologyBitmask(owner.Technology, c.TechnologyBitmask);

        var planets = new List<Planet>();
        if (c.Planet1 is not null)
            planets.Add(MakePlanet(c.Planet1, owner, x: 0));
        if (c.Planet2 is not null)
            planets.Add(MakePlanet(c.Planet2, owner, x: 1));

        var starbases = new List<Starbase>();
        if (c.Starbase is not null)
            starbases.Add(MakeStarbase(c.Starbase, owner, x: 2));

        var game = BuildGame(owner, planets, starbases);
        var handler = new AnnualTickHandler(new FixedRandom(c.RngFixedValue));

        handler.RunAnnualTick(game);

        var expected = golden[c.Name];
        await Assert.That((int)owner.TechnologyLevel).IsEqualTo(int.Parse(expected["techlevel"]));
        await Assert.That(ComputeTechnologyBitmask(owner.Technology)).IsEqualTo(int.Parse(expected["technology"]));
    }

    [Test]
    public async Task TechLevelAdvanceSweepOnlyBumpsCapitalAndUniversityPlanets()
    {
        // Same scenario as EmpireCases.TechLevelAdvanceRollSucceeds, plus a third, non-lab planet
        // (Agricultural) at the same starting TechLevel, to prove the post-advance sweep
        // (UPDATE.PAS:412-421) only bumps Capital/University-type planets, not every owned planet.
        // Can't fit a third planet into the Pascal CLI's fixed-width case shape, so this stays
        // hardcoded rather than golden-file-backed.
        var owner = new Empire { Name = "Test", TechnologyLevel = TechLevel.PreTech };
        owner.Technology.Resources.Add(CargoType.Supplies); // TechSet = TechDev[PreTech] exactly

        var capital = new Planet {
            Location = new Coordinate(0, 0), Owner = owner, Population = 10,
            Type = WorldType.Capital, Class = WorldClass.ClassM, TechLevel = TechLevel.PreTech, Efficiency = 100,
        };
        capital.Cargo.Supplies = 9999;
        var university = new Planet {
            Location = new Coordinate(1, 0), Owner = owner, Population = 10,
            Type = WorldType.University, Class = WorldClass.ClassM, TechLevel = TechLevel.PreTech, Efficiency = 100,
        };
        university.Cargo.Supplies = 9999;
        var bystander = new Planet {
            Location = new Coordinate(2, 0), Owner = owner, Population = 10,
            Type = WorldType.Agricultural, Class = WorldClass.ClassM, TechLevel = TechLevel.PreTech, Efficiency = 100,
        };
        bystander.Cargo.Supplies = 9999;

        var game = BuildGame(owner, [capital, university, bystander], []);
        var handler = new AnnualTickHandler(new FixedRandom(0));

        handler.RunAnnualTick(game);

        await Assert.That(owner.TechnologyLevel).IsEqualTo(TechLevel.Primitive);
        await Assert.That(capital.TechLevel).IsEqualTo(TechLevel.Primitive);
        await Assert.That(university.TechLevel).IsEqualTo(TechLevel.Primitive);
        await Assert.That(bystander.TechLevel).IsEqualTo(TechLevel.PreTech);
    }

    [Test]
    public async Task FractionalLabChanceTruncatesNotRounds()
    {
        // Every EmpireCases.MatchesGoldenFile case uses Efficiency=100, where
        // Trunc(percent*eff/100) is always an exact whole-number pass-through of the percent
        // constant and can't distinguish a real Trunc from an accidental Round — not a golden-file
        // case for the reason EmpireCases's doc comment gives (Efficiency=100 can't discriminate,
        // and the growth mismatch between this test's full RunAnnualTick and runworld.pas's direct
        // UpdateEmpire call makes a shared Efficiency field impossible for a non-100 starting value).
        //
        // Population=10 starts Efficiency at 58 (the "<=75" UpdateEfficiency band); FixedRandom(10)
        // forces that band's Rnd(2,5) to 2+10=12, growing Efficiency to 58+12=70 before NewTechLevel
        // ever reads it — the same 70 confirmed by directly invoking a real, freshly-compiled
        // runworld.exe with Efficiency=70 fed straight in (bypassing UpdateEfficiency entirely, since
        // runworld.pas's empire domain calls UpdateEmpire directly): TechIncUnv*70/100 = 10.5,
        // Trunc -> 10. FixedRandom(10) forces the accept gate to Rnd(1,100)=11, and 11>10 rejects —
        // the empire's Technology set is left untouched. Had Trunc been wrongly Round (10.5->11), the
        // same roll would succeed (11<=11) and add Supplies instead.
        var owner = new Empire { Name = "Test", TechnologyLevel = TechLevel.PreTech };
        var university = new Planet {
            Location = new Coordinate(0, 0), Owner = owner, Population = 10,
            Type = WorldType.University, Class = WorldClass.ClassM, TechLevel = TechLevel.PreTech, Efficiency = 58,
        };
        university.Cargo.Supplies = 9999;

        var game = BuildGame(owner, [university], []);
        var handler = new AnnualTickHandler(new FixedRandom(10));

        handler.RunAnnualTick(game);

        await Assert.That(university.Efficiency).IsEqualTo(70);
        await Assert.That(owner.TechnologyLevel).IsEqualTo(TechLevel.PreTech);
        await Assert.That(ComputeTechnologyBitmask(owner.Technology)).IsEqualTo(0);
    }
}

/// <summary>
/// Verifies Commit 4 of the economy phase: the starbase branch of UpdateWorld (UPDATE.PAS:1392-1430)
/// — UpdateEfficiency/UpdateTechLevel running unconditionally, the rest of the pipeline (including
/// SupplyLink/SurplusLink, UPDATE.PAS:517-604) gated on Kind == IndustrialComplex. All tests use
/// FixedRandom(0) and Cargo.Chemicals (rather than Metals) as the SupplyLink/SurplusLink test
/// resource — UpdateIndustry only ever consumes Metals to grow industry from zero, so Chemicals stays
/// untouched by anything except the two Link procedures themselves when Cargo.Metals starts at 0 (its
/// default), letting these tests skip GetIndustrialDistribution's sqrt/pow cascade entirely rather
/// than hand-tracing it. MatchesGoldenFile covers SupplyLink/SurplusLink's own arithmetic
/// patch-based (reference/verify/golden/starbase.golden, via runworld.pas's starbase domain) — the one
/// part of this class with real Pascal-transcription risk; every other test here (eligibility,
/// distance, Kind-gating, Rebellion) is pure C#-side filtering logic checked directly, no separate
/// Pascal formula to cross-check.
/// </summary>
public class AnnualTickHandlerStarbaseTests
{
    private static Game BuildGame(Starbase starbase, params Planet[] planets)
    {
        var game = new Game(new Core.Galaxy.Galaxy(size: 20));
        game.Galaxy.Starbases.Add(starbase);
        game.Galaxy.Planets.AddRange(planets);
        return game;
    }

    private static Starbase MakeComplex(Coordinate location, Empire owner) => new() {
        Location = location,
        Owner = owner,
        Kind = StarbaseKind.IndustrialComplex,
        Type = WorldType.BaseStarbase,
        TechLevel = TechLevel.Warp,
        Efficiency = 100,
        Population = 0,
    };

    private static Planet MakeRawMaterialPlanet(Coordinate location, Empire owner, int chemicals) => new() {
        Location = location,
        Owner = owner,
        Class = WorldClass.ClassM,
        Type = WorldType.Agricultural,
        Population = 0,
        Cargo = { Chemicals = chemicals },
    };

    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles))]
    [MethodDataSource(typeof(PascalGroundTruth.StarbaseCases), nameof(PascalGroundTruth.StarbaseCases.AsDataSource))]
    public async Task MatchesGoldenFile(PascalGroundTruth.StarbaseCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("starbase.golden");

        var owner = new Empire { Name = "Test" };
        var starbase = MakeComplex(new Coordinate(5, 5), owner);
        starbase.Cargo.Chemicals = c.StarbaseChemicals;
        var neighbor = MakeRawMaterialPlanet(new Coordinate(5, 6), owner, chemicals: c.NeighborChemicals);
        var game = BuildGame(starbase, neighbor);
        game.Empires.Add(owner);
        var handler = new AnnualTickHandler(new FixedRandom(c.RngFixedValue));

        handler.RunAnnualTick(game);

        var expected = golden[c.Name];
        await Assert.That(starbase.Cargo.Chemicals).IsEqualTo(int.Parse(expected["starbaseChe"]));
        await Assert.That(neighbor.Cargo.Chemicals).IsEqualTo(int.Parse(expected["neighborChe"]));
    }

    [Test]
    public async Task NeighborAtOrBelow250TransfersNothing()
    {
        var owner = new Empire { Name = "Test" };
        var starbase = MakeComplex(new Coordinate(5, 5), owner);
        var neighbor = MakeRawMaterialPlanet(new Coordinate(5, 6), owner, chemicals: 250);
        var game = BuildGame(starbase, neighbor);
        game.Empires.Add(owner);
        var handler = new AnnualTickHandler(new FixedRandom(0));

        handler.RunAnnualTick(game);

        // Cargo[che](250)>250 is false -> the strict ">" boundary, not ">=".
        await Assert.That(starbase.Cargo.Chemicals).IsEqualTo(0);
        await Assert.That(neighbor.Cargo.Chemicals).IsEqualTo(250);
    }

    [Test]
    public async Task IneligibleNeighborsAreSkipped()
    {
        var owner = new Empire { Name = "Test" };
        var otherOwner = new Empire { Name = "Other" };
        var starbase = MakeComplex(new Coordinate(5, 5), owner);
        var wrongEmpire = MakeRawMaterialPlanet(new Coordinate(5, 6), otherOwner, chemicals: 1000);
        var wrongType = MakeRawMaterialPlanet(new Coordinate(6, 5), owner, chemicals: 1000);
        wrongType.Type = WorldType.Capital;
        var game = BuildGame(starbase, wrongEmpire, wrongType);
        game.Empires.Add(owner);
        game.Empires.Add(otherOwner);
        var handler = new AnnualTickHandler(new FixedRandom(0));

        handler.RunAnnualTick(game);

        await Assert.That(starbase.Cargo.Chemicals).IsEqualTo(0);
        await Assert.That(wrongEmpire.Cargo.Chemicals).IsEqualTo(1000);
        await Assert.That(wrongType.Cargo.Chemicals).IsEqualTo(1000);
    }

    [Test]
    public async Task OnlyChebyshevDistance1NeighborsParticipate()
    {
        var owner = new Empire { Name = "Test" };
        var starbase = MakeComplex(new Coordinate(5, 5), owner);
        var tooFar = MakeRawMaterialPlanet(new Coordinate(7, 5), owner, chemicals: 1000); // distance 2
        var sameSector = MakeRawMaterialPlanet(new Coordinate(5, 5), owner, chemicals: 1000); // distance 0
        var game = BuildGame(starbase, tooFar, sameSector);
        game.Empires.Add(owner);
        var handler = new AnnualTickHandler(new FixedRandom(0));

        handler.RunAnnualTick(game);

        await Assert.That(starbase.Cargo.Chemicals).IsEqualTo(0);
        await Assert.That(tooFar.Cargo.Chemicals).IsEqualTo(1000);
        await Assert.That(sameSector.Cargo.Chemicals).IsEqualTo(1000);
    }

    [Test]
    public async Task NonComplexKindSkipsEconomyPipelineButStillAdvancesEfficiency()
    {
        var owner = new Empire { Name = "Test" };
        var starbase = new Starbase {
            Location = new Coordinate(5, 5),
            Owner = owner,
            Kind = StarbaseKind.CommandBase,
            Type = WorldType.Base,
            Efficiency = 50,
            Population = 1000,
        };
        starbase.Cargo.Chemicals = 500;
        starbase.Industry.Mining = 10;
        var game = BuildGame(starbase);
        game.Empires.Add(owner);
        var handler = new AnnualTickHandler(new FixedRandom(0));

        handler.RunAnnualTick(game);

        // UpdateEfficiency runs unconditionally: Efficiency(50) is in the "<=50" bracket -> Rnd(3,8)=3.
        await Assert.That(starbase.Efficiency).IsEqualTo(53);
        // Everything gated on Kind==IndustrialComplex is untouched for a CommandBase.
        await Assert.That(starbase.Population).IsEqualTo(1000);
        await Assert.That(starbase.Cargo.Chemicals).IsEqualTo(500);
        await Assert.That(starbase.Industry.Mining).IsEqualTo(10);
        await Assert.That(starbase.RevolutionIndex).IsEqualTo(0);
    }

    [Test]
    public async Task RebellionOnAComplexGoesIndependentButStaysAComplex()
    {
        // OptMilitary[Outpost]=0% keeps UpdateMilitary/UpdateRevolution's military figure at exactly 0
        // (never touching Cargo.Legions before Rebellion), so Rebellion's own chanceToEndRebel term
        // (proportional to that same military figure) is also exactly 0 -- deterministically taking
        // the "world rebels" branch instead of "empire puts down rebellion" regardless of RNG.
        var owner = new Empire { Name = "Test" };
        var starbase = new Starbase {
            Location = new Coordinate(5, 5),
            Owner = owner,
            Kind = StarbaseKind.IndustrialComplex,
            Type = WorldType.Outpost,
            TechLevel = TechLevel.PreTech,
            Efficiency = 100,
            Population = 1500,
            RevolutionIndex = 90,
        };
        starbase.Cargo.Supplies = 9999; // keeps UseUpFood from starving Population before Rebellion reads it
        var game = BuildGame(starbase);
        game.Empires.Add(owner);
        var handler = new AnnualTickHandler(new FixedRandom(0));

        handler.RunAnnualTick(game);

        // RevIndex: 90 -5 (UpdateRevolution's decrease, Rnd(-5,2)=-5) = 85 -> Rebellion fires
        // (85>75, Rnd(1,100)=1<85, Type<>Capital). rebels=Max(1,(int)(Sqrt(1500)*65))=2517;
        // chanceToEndRebel=0/Sqrt(2517)*1.414213=0, so Rnd(1,100)=1 is not <0 -> "world rebels".
        await Assert.That(starbase.Owner).IsEqualTo(Empire.Independent);
        await Assert.That(starbase.Type).IsEqualTo(WorldType.Independent);
        await Assert.That(starbase.Kind).IsEqualTo(StarbaseKind.IndustrialComplex);
        await Assert.That(starbase.Cargo.Legions).IsEqualTo(2517);
    }
}

/// <summary>
/// Verifies Commit 5b of the economy phase: construction-site countdown/completion (UPDATE.PAS's
/// UpdateConstruction, nested UseUpRawMaterial, called via AnnualTickHandler's construction loop in
/// RunAnnualTick) and entity creation on completion (ConstructStarbase/ConstructStargate).
/// MatchesGoldenFile checks the real Pascal arithmetic — UseUpRawMaterial's draw-down and, for the
/// two completion cases, GetOptimumIndus's sqrt/pow cascade — against
/// reference/verify/golden/construction.golden, computed by runworld.pas's construction domain
/// calling UpdateConstruction directly (see ConstructionCases's doc comment). Every test uses a game
/// with no planets/starbases at all, only the construction site and its fleets, so RunAnnualTick's
/// per-empire loop (NewTechLevel) is a guaranteed no-op (zero labs, same as
/// AnnualTickHandlerEmpireTests.ZeroLabsNoOp) and can't perturb anything this class checks.
/// </summary>
public class AnnualTickHandlerConstructionTests
{
    private static readonly Coordinate SiteLocation = new(5, 5);

    private static Game BuildGame(Empire owner, ConstructionSite site, List<Fleet> fleets)
    {
        var game = new Game(new Core.Galaxy.Galaxy(size: 20));
        game.Galaxy.ConstructionSites.Add(site);
        game.Galaxy.Fleets.AddRange(fleets);
        game.Empires.Add(owner);
        return game;
    }

    private static Fleet MakeFleet(PascalGroundTruth.ConstructionFleet fleet, Empire owner) => new() {
        Location = SiteLocation,
        Owner = owner,
        Cargo = { Chemicals = fleet.Chemicals, Metals = fleet.Metals, Trillum = fleet.Trillum },
    };

    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles))]
    [MethodDataSource(typeof(PascalGroundTruth.ConstructionCases), nameof(PascalGroundTruth.ConstructionCases.AsDataSource))]
    public async Task MatchesGoldenFile(PascalGroundTruth.ConstructionCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("construction.golden");

        var owner = new Empire { Name = "Test", TechnologyLevel = c.OwnerTechLevel };
        var site = new ConstructionSite { Location = SiteLocation, Owner = owner, Building = c.Building, YearsToCompletion = c.YearsToCompletion };

        var fleets = new List<Fleet>();
        if (c.Fleet1 is not null)
            fleets.Add(MakeFleet(c.Fleet1, owner));
        if (c.Fleet2 is not null)
            fleets.Add(MakeFleet(c.Fleet2, owner));

        var game = BuildGame(owner, site, fleets);
        var handler = new AnnualTickHandler(new FixedRandom(c.RngFixedValue));

        handler.RunAnnualTick(game);

        var expected = golden[c.Name];
        var active = game.Galaxy.ConstructionSites.Contains(site);
        await Assert.That(Convert.ToInt32(active)).IsEqualTo(int.Parse(expected["active"]));
        // Only checked while active: Pascal keeps Constr[1].TimeToCompletion=0 readable after
        // completion, but the C# site is removed from the list entirely rather than zeroed in place.
        if (active)
            await Assert.That(site.YearsToCompletion).IsEqualTo(int.Parse(expected["timetocompletion"]));

        var fleet1 = fleets.Count > 0 ? fleets[0] : null;
        var fleet2 = fleets.Count > 1 ? fleets[1] : null;
        await Assert.That(fleet1?.Cargo.Chemicals ?? 0).IsEqualTo(int.Parse(expected["fleet1che"]));
        await Assert.That(fleet1?.Cargo.Metals ?? 0).IsEqualTo(int.Parse(expected["fleet1met"]));
        await Assert.That(fleet1?.Cargo.Trillum ?? 0).IsEqualTo(int.Parse(expected["fleet1tri"]));
        await Assert.That(fleet2?.Cargo.Chemicals ?? 0).IsEqualTo(int.Parse(expected["fleet2che"]));
        await Assert.That(fleet2?.Cargo.Metals ?? 0).IsEqualTo(int.Parse(expected["fleet2met"]));
        await Assert.That(fleet2?.Cargo.Trillum ?? 0).IsEqualTo(int.Parse(expected["fleet2tri"]));

        if (!active) {
            switch (c.Building) {
                case ConstructionType.Minefield:
                    await Assert.That(game.Galaxy.GetMineOwner(SiteLocation)).IsEqualTo(owner);
                    break;
                case ConstructionType.Gate or ConstructionType.WarpLink or ConstructionType.Disrupter:
                    await Assert.That(game.Galaxy.GetMineOwner(SiteLocation)).IsNull();
                    var stargate = game.Galaxy.Stargates.Single();
                    // Pascal's StargateTypes ordinals are gte=24,lnk=25,dis=26 (TechnologyTypes'
                    // own numbering); StargateKind.Gate=0 aligns with gte=24, so +24 converts.
                    await Assert.That(24 + (int)stargate.Kind).IsEqualTo(int.Parse(expected["stargatekind"]));
                    await Assert.That(stargate.LinkedTo).IsNull();
                    break;
                default:
                    await Assert.That(game.Galaxy.GetMineOwner(SiteLocation)).IsNull();
                    var starbase = game.Galaxy.Starbases.Single();
                    // Pascal's StarbaseTypes ordinals are cmm=20,frt=21,cmp=22,out=23 (TechnologyTypes'
                    // own numbering); StarbaseKind.CommandBase=0 aligns with cmm=20, so +20 converts.
                    await Assert.That(20 + (int)starbase.Kind).IsEqualTo(int.Parse(expected["starbasekind"]));
                    await Assert.That(starbase.Population).IsEqualTo(int.Parse(expected["starbasepop"]));
                    await Assert.That(starbase.Efficiency).IsEqualTo(int.Parse(expected["starbaseeff"]));
                    await Assert.That((int)starbase.Type).IsEqualTo(int.Parse(expected["starbasetype"]));
                    await Assert.That((int)starbase.TechLevel).IsEqualTo(int.Parse(expected["starbasetech"]));
                    await Assert.That(starbase.Industry.Bioindustry).IsEqualTo(int.Parse(expected["starbasebio"]));
                    await Assert.That(starbase.Industry.Chemical).IsEqualTo(int.Parse(expected["starbaseche"]));
                    await Assert.That(starbase.Industry.Mining).IsEqualTo(int.Parse(expected["starbasemin"]));
                    await Assert.That(starbase.Industry.ShipyardGeneral).IsEqualTo(int.Parse(expected["starbasesyg"]));
                    await Assert.That(starbase.Industry.ShipyardJump).IsEqualTo(int.Parse(expected["starbasesyj"]));
                    await Assert.That(starbase.Industry.ShipyardStarship).IsEqualTo(int.Parse(expected["starbasesys"]));
                    await Assert.That(starbase.Industry.ShipyardTransport).IsEqualTo(int.Parse(expected["starbasesyt"]));
                    await Assert.That(starbase.Industry.Supply).IsEqualTo(int.Parse(expected["starbasesup"]));
                    await Assert.That(starbase.Industry.TrillumMining).IsEqualTo(int.Parse(expected["starbasetri"]));
                    break;
            }
        }
    }

    [Test]
    public async Task OnlyFleetsAtSameLocationAndOwnerContribute()
    {
        // Pure C#-side LINQ filtering (Galaxy.Fleets.Where(location+owner match)), no separate
        // Pascal formula to cross-check — hardcoded rather than golden-file-backed. A fleet at a
        // different location and a fleet owned by a different empire both sit right next to a
        // genuinely-contributing fleet; only the third one's cargo should move.
        var owner = new Empire { Name = "Test" };
        var stranger = new Empire { Name = "Stranger" };
        var site = new ConstructionSite { Location = SiteLocation, Owner = owner, Building = ConstructionType.Minefield, YearsToCompletion = 2 };

        var wrongLocation = new Fleet { Location = new Coordinate(0, 0), Owner = owner };
        wrongLocation.Cargo.Chemicals = 500;
        var wrongOwner = new Fleet { Location = SiteLocation, Owner = stranger };
        wrongOwner.Cargo.Chemicals = 500;
        var contributor = new Fleet { Location = SiteLocation, Owner = owner };
        contributor.Cargo.Chemicals = 500;
        contributor.Cargo.Metals = 600;
        contributor.Cargo.Trillum = 100;

        var game = BuildGame(owner, site, [wrongLocation, wrongOwner, contributor]);
        game.Empires.Add(stranger);
        var handler = new AnnualTickHandler(new FixedRandom(0));

        handler.RunAnnualTick(game);

        await Assert.That(site.YearsToCompletion).IsEqualTo(1);
        await Assert.That(wrongLocation.Cargo.Chemicals).IsEqualTo(500);
        await Assert.That(wrongOwner.Cargo.Chemicals).IsEqualTo(500);
        await Assert.That(contributor.Cargo.Chemicals).IsEqualTo(500 - 110);
    }
}
