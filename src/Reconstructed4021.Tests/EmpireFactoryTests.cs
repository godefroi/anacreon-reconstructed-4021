using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// PRIMINTR.PAS:982-1016 (CreateEmpire) + NEWGAME.PAS:1186-1259 (CreatePlayerEmpire/CreateNPEmpire's
/// tech-set union/intersect). Tech-level Bio (7) is used throughout since it's the exact level real
/// scenario files use (reference/scenarios/dos_131/*.SCN's CreatePlayerEmpire/CreateNPEmpire calls),
/// and its TechDev sets are hand-traceable from TechCatalog's own min-tech tables without needing the
/// production code under test to compute the expected value itself.
/// </summary>
public class EmpireFactoryTests
{
    [Test]
    public async Task NoExtraTechs_TechnologyEqualsTechDevAtPredecessorLevel()
    {
        var empire = EmpireFactory.CreateEmpire("Terra", password: null, isEmpress: false,
            TechLevel.Bio, restlessness: 0, centralModifier: false, foundingYear: 4000);

        // TechDev[Jump] (Pred(Bio)) by hand, from TechCatalog's own min-tech tables:
        await Assert.That(empire.Technology.Defenses).IsEquivalentTo([DefenseType.Gdm, DefenseType.IonCannon]);
        await Assert.That(empire.Technology.Ships).IsEquivalentTo([ShipType.Fighter, ShipType.Transport, ShipType.Jumpship, ShipType.Jumptransport]);
        await Assert.That(empire.Technology.Resources).IsEquivalentTo([CargoType.Supplies, CargoType.Legion, CargoType.Metals, CargoType.Chemicals, CargoType.Trillum]);
        await Assert.That(empire.Technology.Constructions).IsEmpty();
    }

    [Test]
    public async Task ExtraTech_AtOrBelowCurrentLevel_IsGranted()
    {
        // HunterKiller's own MinTech is Bio — already legal at Tech=Bio, so granting it as an
        // "extra" (as a scenario might, redundantly or not) survives the final TechDev[Tech] clamp.
        var empire = EmpireFactory.CreateEmpire("Terra", password: null, isEmpress: false,
            TechLevel.Bio, restlessness: 0, centralModifier: false, foundingYear: 4000,
            TechCatalog.Grant(ShipType.HunterKiller));

        await Assert.That(empire.Technology.Ships).Contains(ShipType.HunterKiller);
    }

    [Test]
    public async Task ExtraTech_AboveCurrentLevel_IsClampedAway()
    {
        // Minefield's MinTech is Starship, one level past Bio — NEWGAME.PAS's final
        // KnownTechs:=KnownTechs*TechDev[Tech] silently drops any extra beyond what's legal at this
        // empire's own starting level, real Pascal behavior, not a hypothetical edge case.
        var empire = EmpireFactory.CreateEmpire("Terra", password: null, isEmpress: false,
            TechLevel.Bio, restlessness: 0, centralModifier: false, foundingYear: 4000,
            TechCatalog.Grant(ConstructionType.Minefield));

        await Assert.That(empire.Technology.Constructions).IsEmpty();
    }

    [Test]
    public async Task CentralModifier_SetsLosesIfCapitalConquered()
    {
        var withCentral = EmpireFactory.CreateEmpire("Terra", password: null, isEmpress: false,
            TechLevel.Bio, restlessness: 0, centralModifier: true, foundingYear: 4000);
        var withoutCentral = EmpireFactory.CreateEmpire("Terra", password: null, isEmpress: false,
            TechLevel.Bio, restlessness: 0, centralModifier: false, foundingYear: 4000);

        await Assert.That(withCentral.LosesIfCapitalConquered).IsTrue();
        await Assert.That(withoutCentral.LosesIfCapitalConquered).IsFalse();
    }

    [Test]
    public async Task PlainFieldsCopyStraightThrough()
    {
        var empire = EmpireFactory.CreateEmpire("Terra", password: "hunter2", isEmpress: true,
            TechLevel.Warp, restlessness: 3, centralModifier: false, foundingYear: 4021);

        await Assert.That(empire.Name).IsEqualTo("Terra");
        await Assert.That(empire.Password).IsEqualTo("hunter2");
        await Assert.That(empire.IsEmpress).IsTrue();
        await Assert.That(empire.TechnologyLevel).IsEqualTo(TechLevel.Warp);
        await Assert.That(empire.RevolutionFactor).IsEqualTo(3);
        await Assert.That(empire.FoundingYear).IsEqualTo(4021);
        await Assert.That(empire.Capital).IsNull();
        await Assert.That(empire.TotalRevolutionIndex).IsEqualTo(0);
    }

    /// <summary>DATACNST.PAS:373-379's InitDefenseRecord — Fleets only, field-for-field.</summary>
    [Test]
    public async Task DefenseSettings_FleetsMatchInitDefenseRecord()
    {
        var empire = EmpireFactory.CreateEmpire("Terra", password: null, isEmpress: false,
            TechLevel.Bio, restlessness: 0, centralModifier: false, foundingYear: 4000);
        var fleets = empire.DefenseSettings.Fleets;

        await AssertShell(fleets.DeepSpace, fighters: 5, hunterKillers: 50, jumpships: 10, jumptransports: 0, penetrators: 15, starships: 0, transports: 0);
        await AssertShell(fleets.HighOrbit, fighters: 10, hunterKillers: 10, jumpships: 20, jumptransports: 0, penetrators: 30, starships: 50, transports: 0);
        await AssertShell(fleets.Orbit, fighters: 10, hunterKillers: 10, jumpships: 30, jumptransports: 0, penetrators: 30, starships: 30, transports: 0);
        await AssertShell(fleets.SubOrbit, fighters: 55, hunterKillers: 30, jumpships: 40, jumptransports: 0, penetrators: 25, starships: 20, transports: 0);
        await AssertShell(fleets.Ground, fighters: 20, hunterKillers: 0, jumpships: 0, jumptransports: 100, penetrators: 0, starships: 0, transports: 100);
    }

    /// <summary>
    /// InitDefenseRecord (DATACNST.PAS:373-379) only initializes ShellDefDist (Fleets) — Pascal's own
    /// typed constant omits StarbaseDefDist entirely, defaulting it to all zeros. Not a port gap.
    /// </summary>
    [Test]
    public async Task DefenseSettings_StarbasesStayAllZero()
    {
        var empire = EmpireFactory.CreateEmpire("Terra", password: null, isEmpress: false,
            TechLevel.Bio, restlessness: 0, centralModifier: false, foundingYear: 4000);

        await AssertShell(empire.DefenseSettings.Starbases.DeepSpace, 0, 0, 0, 0, 0, 0, 0);
        await AssertShell(empire.DefenseSettings.Starbases.HighOrbit, 0, 0, 0, 0, 0, 0, 0);
        await AssertShell(empire.DefenseSettings.Starbases.Orbit, 0, 0, 0, 0, 0, 0, 0);
        await AssertShell(empire.DefenseSettings.Starbases.SubOrbit, 0, 0, 0, 0, 0, 0, 0);
        await AssertShell(empire.DefenseSettings.Starbases.Ground, 0, 0, 0, 0, 0, 0, 0);
    }

    /// <summary>
    /// TechLevel.PreTech has no predecessor — Pred(PreTchLvl) is an out-of-range TechDev index in
    /// real Pascal (a range-check error), not a well-defined empty set, and no real scenario file
    /// ever creates a player/NPE empire at that level (reference/scenarios/dos_131/*.SCN's
    /// CreatePlayerEmpire/CreateNPEmpire calls are all Bio or PreGate) — this can't be golden-file
    /// verified against real Pascal output (it would crash the harness), so it's hardcoded instead,
    /// same precedent as EmpireCases's own fractional-efficiency case. EmpireFactory's guard skips the
    /// TechDev[Pred(Tech)] union entirely at this level rather than modeling undefined behavior — the
    /// only technology a fresh PreTech empire could ever have is whatever's granted as an "extra".
    /// </summary>
    [Test]
    public async Task LowestLevel_HasNoPredecessorTechs()
    {
        var empire = EmpireFactory.CreateEmpire("Terra", password: null, isEmpress: false,
            TechLevel.PreTech, restlessness: 0, centralModifier: false, foundingYear: 4000);

        await Assert.That(empire.Technology.Defenses).IsEmpty();
        await Assert.That(empire.Technology.Ships).IsEmpty();
        await Assert.That(empire.Technology.Resources).IsEmpty();
        await Assert.That(empire.Technology.Constructions).IsEmpty();
    }

    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles))]
    [MethodDataSource(typeof(PascalGroundTruth.EmpireFactoryCases), nameof(PascalGroundTruth.EmpireFactoryCases.AsDataSource))]
    public async Task MatchesGoldenFile(PascalGroundTruth.EmpireFactoryCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("empirecreate.golden");

        var mask = c.ExtraTechsMask;
        var extraTechs = new UnlockedTechnology();
        AnnualTickHandlerEmpireTests.ApplyTechnologyBitmask(extraTechs, mask);
        var extras = extraTechs.Defenses.Select(TechCatalog.Grant)
            .Concat(extraTechs.Ships.Select(TechCatalog.Grant))
            .Concat(extraTechs.Resources.Select(TechCatalog.Grant))
            .Concat(extraTechs.Constructions.Select(TechCatalog.Grant))
            .ToArray();

        var empire = EmpireFactory.CreateEmpire("Terra", password: null, isEmpress: false,
            c.TechLevel, restlessness: 0, centralModifier: false, foundingYear: 4000, extras);

        var expected = golden[c.Name];
        await Assert.That((int)empire.TechnologyLevel).IsEqualTo(int.Parse(expected["techlevel"]));
        await Assert.That(AnnualTickHandlerEmpireTests.ComputeTechnologyBitmask(empire.Technology)).IsEqualTo(int.Parse(expected["technology"]));
    }

    private static async Task AssertShell(ShipDistribution shell, int fighters, int hunterKillers, int jumpships, int jumptransports, int penetrators, int starships, int transports)
    {
        await Assert.That(shell.Fighters).IsEqualTo(fighters);
        await Assert.That(shell.HunterKillers).IsEqualTo(hunterKillers);
        await Assert.That(shell.Jumpships).IsEqualTo(jumpships);
        await Assert.That(shell.Jumptransports).IsEqualTo(jumptransports);
        await Assert.That(shell.Penetrators).IsEqualTo(penetrators);
        await Assert.That(shell.Starships).IsEqualTo(starships);
        await Assert.That(shell.Transports).IsEqualTo(transports);
    }
}
