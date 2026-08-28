using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.SaveFormat;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests;

/// <summary>
/// Phase 7b (Header + Environment + Sector). Ground truth for `INTRO_1.SAV` cross-checked
/// against `scripts/savtool.py`'s own JSON parse of the same file (year 4021, player ordinal 0,
/// scenario "INTRO.SCN", `SizeOfGalaxy=21`, 90 sector cells with `Special=129` — nebula type 1,
/// no mine — and zero `MineScout` entries), not hand-derived from the format doc alone.
/// </summary>
public class SavGameLoaderTests
{
    private static byte[] LoadIntro1() =>
        File.ReadAllBytes(Path.Combine(PascalGroundTruth.PascalHarness.RepoRoot, "reference", "saves", "INTRO_1.SAV"));

    [Test]
    public async Task LoadGame_Intro1_ReadsEnvironment()
    {
        var game = new SavGameLoader().LoadGame(LoadIntro1());

        await Assert.That(game.Year).IsEqualTo(4021);
        await Assert.That(game.ScenarioFilename).IsEqualTo("INTRO.SCN");
    }

    [Test]
    public async Task LoadGame_Intro1_CurrentEmpireIsPlayerOrdinalZero()
    {
        // Environment.Player=0 -- resolves to empire slot 0 (Empire1), the same object identity
        // Empire Data will later populate with the real "Player_empire" name (Phase 7d).
        var game = new SavGameLoader().LoadGame(LoadIntro1());

        await Assert.That(game.CurrentEmpire).IsNotNull();
        await Assert.That(game.CurrentEmpire!.IsIndependent).IsFalse();
    }

    [Test]
    public async Task LoadGame_Intro1_GalaxyIsSizedFromSector()
    {
        var game = new SavGameLoader().LoadGame(LoadIntro1());

        await Assert.That(game.Galaxy.Size).IsEqualTo(21);
    }

    [Test]
    public async Task LoadGame_Intro1_ReconstructsNebulaeFromSpecialLowNibble()
    {
        var game = new SavGameLoader().LoadGame(LoadIntro1());

        // Confirmed via savtool.py: 90 real cells carry Special=129 (nebula type 1, no mine);
        // (4,4)/(4,5)/(4,6) are three of them.
        await Assert.That(game.Galaxy.GetNebula(new Coordinate(4, 4))).IsEqualTo(NebulaType.Nebula);
        await Assert.That(game.Galaxy.GetNebula(new Coordinate(4, 5))).IsEqualTo(NebulaType.Nebula);
        await Assert.That(game.Galaxy.GetNebula(new Coordinate(4, 6))).IsEqualTo(NebulaType.Nebula);

        // A cell with no special data at all -- the sentinel Special=128 (no nebula, no mine).
        await Assert.That(game.Galaxy.GetNebula(new Coordinate(0, 0))).IsEqualTo(NebulaType.None);

        var nebulaCount = Enumerable.Range(0, 22)
            .SelectMany(x => Enumerable.Range(0, 22).Select(y => new Coordinate(x, y)))
            .Count(c => game.Galaxy.GetNebula(c) != NebulaType.None);
        await Assert.That(nebulaCount).IsEqualTo(90);
    }

    [Test]
    public async Task LoadGame_Intro1_HasNoMinefields()
    {
        var game = new SavGameLoader().LoadGame(LoadIntro1());

        // A fresh scenario start has no player-placed mines anywhere -- every sector's high
        // nibble is the Ord(Indep)=8 "no mine" sentinel.
        var mined = Enumerable.Range(0, 22)
            .SelectMany(x => Enumerable.Range(0, 22).Select(y => new Coordinate(x, y)))
            .Count(c => game.Galaxy.GetMineOwner(c) is not null);
        await Assert.That(mined).IsEqualTo(0);
    }
}
