using ThreeLn.Reconstruction4021.Core;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Turns;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Verifies AnnualTickHandler's UpdateRevolution/Rebellion pair (economy phase Commit 1) against
/// reference/verify/revolution.pas — a from-source, independently-transcribed re-read of
/// UPDATE.PAS:619-755, not a copy of this C# port. Excluded from the default `dotnet test` run
/// ([Explicit] + [Category("PascalGroundTruth")]) since it shells out to fpc; see PascalHarness.
///
/// revolution.pas's UpdateRevolutionScenario starts exactly at the point UpdateRevolution itself
/// starts — it does NOT run UpdatePopulation/UpdateEfficiency first, unlike RunAnnualTick, which
/// runs the whole UpdateWorld pipeline (RunProductionPipeline, UpdateEfficiency, UpdatePopulation,
/// UseUpFood, UpdateRevolution, in that order). So every case below tracks two population values:
/// PlanetPop (what the C# Planet starts the tick with) and HarnessPop (what UpdatePopulation will
/// have turned it into by the time UpdateRevolution actually runs, hand-computed the same way the
/// original hand-traced Commit-1 test did) — feeding PlanetPop straight to the harness would
/// silently double-apply the population adjustment. Efficiency needs no such split: every case
/// uses 80 or 100, both no-ops under UpdateEfficiency at FixedRandom(0) (only brackets &lt;=75
/// actually increment) — confirmed per case in the comments below, not assumed. Industry stays at
/// its zero default throughout, so RunProductionPipeline can't perturb Cargo/Population first.
/// </summary>
public class RevolutionPascalVerificationTests
{
    private static Game BuildGame(params Planet[] planets)
    {
        var game = new Game(new Core.Galaxy.Galaxy(size: 20));
        game.Galaxy.Planets.AddRange(planets);
        return game;
    }

    [Test, Explicit, Category("PascalGroundTruth")]
    public async Task FpcIsAvailable()
    {
        // Fails loudly rather than silently no-op'ing if this category is explicitly requested
        // without FreePascal installed.
        await Assert.That(PascalHarness.IsFpcAvailable)
            .IsTrue()
            .Because("PascalGroundTruth tests require fpc on PATH to compute ground truth");
    }

    private sealed record Case(
        int PlanetPop, WorldClass Class, TechLevel Tech, int HarnessPop,
        int Eff, int RevIndex, int Legions, int Ninja, WorldType Type, string Note);

    [Test, Explicit, Category("PascalGroundTruth")]
    public async Task UpdateRevolutionMatchesPascalAcrossACaseMatrix()
    {
        var cases = new[] {
            // Reproduces the hand-traced Commit-1 test WorldRebelsWhenRevolutionIndexExceedsThreshold,
            // as a sanity anchor. Pop(2350)>MaxPop[Barren](2340) -> Rnd(-10,10)=-10 -> HarnessPop=2340.
            // Eff=80 falls in UpdateEfficiency's <=90 bracket (Rnd(0,3)=0) -> no-op.
            new Case(2350, WorldClass.Barren, TechLevel.Warp, 2340, 80, 95, 0, 0, WorldType.Agricultural,
                "sanity anchor, matches WorldRebelsWhenRevolutionIndexExceedsThreshold"),

            // Pop(10)<75 -> Rnd(2,5)=2 -> HarnessPop=12, regardless of Class/Tech (that branch never
            // looks at MaxPop/BasePop). Eff=100 has no UpdateEfficiency bracket -> falls to the
            // default 0 increment -> no-op. Tiny population -> small sqrt(Pop) in Rebellion.
            new Case(10, WorldClass.EarthLike, TechLevel.Gate, 12, 100, 95, 0, 0, WorldType.Agricultural,
                "tiny population"),

            // Pop(50000)>MaxPop[EarthLike](4500) -> Rnd(-10,10)=-10 -> HarnessPop=49990. Eff=80 is
            // again a no-op (<=90 bracket). Legions=5000 makes Military(5000) exceed OptimumMilitary
            // (~1500 for this Population/Type) -> exercises the military-suppression branch the
            // roadmap flagged as faithful-but-never-tested (UPDATE.PAS:715-735), which then also
            // triggers (and this time suppresses) a Rebellion call.
            new Case(50000, WorldClass.EarthLike, TechLevel.Gate, 49990, 80, 95, 5000, 0, WorldType.Agricultural,
                "large population + military suppression + suppressed rebellion"),

            // Pop(50)<75 -> Rnd(2,5)=2 -> HarnessPop=52. RevIndex never exceeds 75 -> Rebellion never fires.
            new Case(50, WorldClass.EarthLike, TechLevel.Gate, 52, 100, 50, 0, 0, WorldType.Agricultural,
                "revolution index stays low, Rebellion never fires"),
        };

        var harnessArgs = cases
            .Select(c => $"{c.HarnessPop},{c.Eff},{c.RevIndex},0,0,{c.Legions},{c.Ninja},{(int)c.Type}")
            .Prepend("case")
            .ToArray();
        var output = PascalHarness.CompileAndRun("revolution", harnessArgs);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        await Assert.That(lines.Length).IsEqualTo(cases.Length)
            .Because("one output line per input case");

        for (var i = 0; i < cases.Length; i++) {
            var c = cases[i];
            var expected = ParseLine(lines[i]);

            var owner = new Empire { Name = "Test" };
            var planet = new Planet {
                Location = new Coordinate(0, 0),
                Owner = owner,
                Population = c.PlanetPop,
                Class = c.Class,
                TechLevel = c.Tech,
                Efficiency = c.Eff,
                RevolutionIndex = c.RevIndex,
                Type = c.Type,
            };
            planet.Cargo.Legions = c.Legions;
            planet.Cargo.NinjaLegions = c.Ninja;
            // UseUpFood runs between UpdatePopulation and UpdateRevolution; foodNeeded is
            // ThgLmt((Population/100)*25), which ThgLmt itself caps at MaxResources (9999) no
            // matter how large Population is — so Supplies=9999 guarantees no starvation for any
            // case here, keeping Population/RevolutionIndex exactly what the comments above say.
            planet.Cargo.Supplies = 9999;
            var game = BuildGame(planet);
            game.Empires.Add(owner);
            var handler = new AnnualTickHandler(new FixedRandom(0));

            handler.RunAnnualTick(game);

            var label = $"case {i} ({c.Note})";
            await Assert.That(planet.Cargo.Legions).IsEqualTo(int.Parse(expected["legions"])).Because(label);
            await Assert.That(planet.Cargo.NinjaLegions).IsEqualTo(int.Parse(expected["ninja"])).Because(label);
            await Assert.That(planet.Population).IsEqualTo(int.Parse(expected["population"])).Because(label);
            await Assert.That(planet.Efficiency).IsEqualTo(int.Parse(expected["efficiency"])).Because(label);
            await Assert.That(planet.RevolutionIndex).IsEqualTo(int.Parse(expected["revindex"])).Because(label);
            await Assert.That(owner.TotalRevolutionIndex).IsEqualTo(int.Parse(expected["total_rev_delta"])).Because(label);

            var rebelled = bool.Parse(expected["rebelled"]);
            await Assert.That(planet.Owner.IsIndependent).IsEqualTo(rebelled).Because(label);
        }
    }

    private static Dictionary<string, string> ParseLine(string line)
    {
        var fields = new Dictionary<string, string>();
        foreach (var part in line.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
            var eq = part.IndexOf('=');
            if (eq < 0)
                continue;
            fields[part[..eq]] = part[(eq + 1)..];
        }
        return fields;
    }
}
