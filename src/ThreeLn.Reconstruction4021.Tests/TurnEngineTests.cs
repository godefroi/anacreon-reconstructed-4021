using ThreeLn.Reconstruction4021.Core;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Turns;

namespace ThreeLn.Reconstruction4021.Tests;

/// <summary>
/// Verifies TurnEngine's call order and argument correctness against fakes — no real
/// economy/AI/movement/scouting behavior exists yet, so these tests are entirely about
/// sequencing, matching the verified Pascal control flow (ANACREON.PAS's UpdateTurn).
/// </summary>
public class TurnEngineTests
{
    private sealed class FakeTurnHandler(List<string> log, bool isHuman) : ITurnHandler
    {
        public bool IsHuman { get; } = isHuman;
        public void PlayTurn(Empire empire, Game game) => log.Add($"PlayTurn:{empire.Name}");
    }

    private sealed class ClassicAiTurnHandler(List<string> log) : ITurnHandler
    {
        public bool IsHuman => false;
        public void PlayTurn(Empire empire, Game game) => log.Add($"ClassicAi:{empire.Name}");
    }

    private sealed class AdvancedAiTurnHandler(List<string> log) : ITurnHandler
    {
        public bool IsHuman => false;
        public void PlayTurn(Empire empire, Game game) => log.Add($"AdvancedAi:{empire.Name}");
    }

    private sealed class FakeVisibilityHandler(List<string> log) : IVisibilityHandler
    {
        public void RefreshVisibility(Empire empire, Game game) => log.Add($"Visibility:{empire.Name}");
    }

    private sealed class FakeFleetMovementHandler(List<string> log) : IFleetMovementHandler
    {
        public void AdvanceFleets(Game game, Empire actingEmpire, Empire nextEmpire) =>
            log.Add($"AdvanceFleets:{actingEmpire.Name}->{nextEmpire.Name}");

        public void AdvanceStarbases(Game game, Empire empire) => log.Add($"AdvanceStarbases:{empire.Name}");
    }

    private sealed class FakeAnnualTickHandler(List<string> log) : IAnnualTickHandler
    {
        public int CallCount { get; private set; }

        public void RunAnnualTick(Game game)
        {
            CallCount++;
            log.Add("AnnualTick");
        }
    }

    private static (Game Game, TurnEngine Engine, FakeAnnualTickHandler Tick) Build(
        List<string> log, params (Empire Empire, ITurnHandler Handler)[] empires)
    {
        var game = new Game(new Core.Galaxy.Galaxy(size: 10));
        foreach (var (empire, handler) in empires)
        {
            game.Empires.Add(empire);
            game.TurnHandlers[empire] = handler;
        }

        var tick = new FakeAnnualTickHandler(log);
        var engine = new TurnEngine(new FakeVisibilityHandler(log), new FakeFleetMovementHandler(log), tick);
        return (game, engine, tick);
    }

    [Test]
    public async Task SequentialHandoff_OneHumanTwoAi_HumanFirst()
    {
        var human = new Empire { Name = "Human" };
        var ai2 = new Empire { Name = "AI2" };
        var ai3 = new Empire { Name = "AI3" };
        var log = new List<string>();
        var (game, engine, tick) = Build(log,
            (human, new FakeTurnHandler(log, isHuman: true)),
            (ai2, new FakeTurnHandler(log, isHuman: false)),
            (ai3, new FakeTurnHandler(log, isHuman: false)));
        game.CurrentEmpire = human;

        engine.AdvanceOneTurn(game);
        engine.AdvanceOneTurn(game);
        engine.AdvanceOneTurn(game);

        await Assert.That(string.Join("|", log)).IsEqualTo(string.Join("|",
        [
            "Visibility:Human", "PlayTurn:Human", "AdvanceFleets:Human->AI2", "AdvanceStarbases:AI2",
            "Visibility:AI2", "PlayTurn:AI2", "AdvanceFleets:AI2->AI3", "AdvanceStarbases:AI3",
            "Visibility:AI3", "PlayTurn:AI3", "AdvanceFleets:AI3->Human", "AdvanceStarbases:Human",
            "AnnualTick",
        ]));
        await Assert.That(tick.CallCount).IsEqualTo(1);
        await Assert.That(game.CurrentEmpire).IsSameReferenceAs(human);
    }

    [Test]
    public async Task Bootstrap_FirstEmpireIsAi_DoesNotFireAnnualTickPrematurely()
    {
        var ai1 = new Empire { Name = "AI1" };
        var human = new Empire { Name = "Human" };
        var log = new List<string>();
        var (game, engine, tick) = Build(log,
            (ai1, new FakeTurnHandler(log, isHuman: false)),
            (human, new FakeTurnHandler(log, isHuman: true)));
        game.CurrentEmpire = ai1;

        engine.AdvanceOneTurn(game);

        await Assert.That(log).Contains("PlayTurn:AI1");
        await Assert.That(tick.CallCount).IsEqualTo(0);
        await Assert.That(game.CurrentEmpire).IsSameReferenceAs(human);
    }

    [Test]
    public async Task SingleEmpireGame_AnnualTickFiresEveryCall()
    {
        var human = new Empire { Name = "Human" };
        var log = new List<string>();
        var (game, engine, tick) = Build(log, (human, new FakeTurnHandler(log, isHuman: true)));
        game.CurrentEmpire = human;

        engine.AdvanceOneTurn(game);
        engine.AdvanceOneTurn(game);
        engine.AdvanceOneTurn(game);

        await Assert.That(tick.CallCount).IsEqualTo(3);
    }

    [Test]
    public async Task DifferentHandlerImplementations_DispatchPerEmpire()
    {
        var log = new List<string>();
        var classic = new Empire { Name = "Classic" };
        var advanced = new Empire { Name = "Advanced" };
        var (game, engine, _) = Build(log,
            (classic, new ClassicAiTurnHandler(log)),
            (advanced, new AdvancedAiTurnHandler(log)));
        game.CurrentEmpire = classic;

        engine.AdvanceOneTurn(game);
        engine.AdvanceOneTurn(game);

        await Assert.That(log).Contains("ClassicAi:Classic");
        await Assert.That(log).Contains("AdvancedAi:Advanced");
    }

    [Test]
    public async Task MultiYearCadence_AnnualTickFiresOncePerRound_NotPerEmpire()
    {
        var human = new Empire { Name = "Human" };
        var ai = new Empire { Name = "AI" };
        var log = new List<string>();
        var (game, engine, tick) = Build(log,
            (human, new FakeTurnHandler(log, isHuman: true)),
            (ai, new FakeTurnHandler(log, isHuman: false)));
        game.CurrentEmpire = human;

        for (var i = 0; i < 4; i++)
            engine.AdvanceOneTurn(game);

        await Assert.That(tick.CallCount).IsEqualTo(2);
    }

    [Test]
    public async Task MissingHandler_ThrowsKeyNotFoundException()
    {
        var human = new Empire { Name = "Human" };
        var ai = new Empire { Name = "AI" };
        var log = new List<string>();
        var game = new Game(new Core.Galaxy.Galaxy(size: 10));
        game.Empires.Add(human);
        game.Empires.Add(ai);
        game.TurnHandlers[human] = new FakeTurnHandler(log, isHuman: true);
        // ai has no entry in TurnHandlers.
        var engine = new TurnEngine(new FakeVisibilityHandler(log), new FakeFleetMovementHandler(log), new FakeAnnualTickHandler(log));
        game.CurrentEmpire = ai;

        await Assert.That(() => engine.AdvanceOneTurn(game)).ThrowsExactly<KeyNotFoundException>();
    }

    [Test]
    public async Task CurrentEmpireUnset_ThrowsInvalidOperationException()
    {
        var human = new Empire { Name = "Human" };
        var log = new List<string>();
        var (game, engine, _) = Build(log, (human, new FakeTurnHandler(log, isHuman: true)));

        await Assert.That(() => engine.AdvanceOneTurn(game)).ThrowsExactly<InvalidOperationException>();
    }
}
