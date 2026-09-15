using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Tui2.Screens;


namespace Reconstructed4021.Tui2.NewGame;


// Everything after a scenario is chosen shares this: the header/scenario text, the context threaded
// down from TitleScreen, and how to build whatever screen comes next. Centralized here so
// ScenarioLoader.Load's construction (GalaxySetup + ScenarioLoader) lives in exactly one place rather
// than being duplicated wherever the last player happens to get confirmed.
internal sealed class NewGameFlow
{
    public ScenarioLoader.ScenarioHeader Header { get; }
    public string ScenarioText { get; }
    public NewGameContext Context { get; }

    public NewGameFlow(ScenarioLoader.ScenarioHeader header, string scenarioText, NewGameContext context)
    {
        Header = header;
        ScenarioText = scenarioText;
        Context = context;
    }

    // Program.cs's own "IF MinPlay<MaxPlay" branch: a scenario with only one possible player count
    // never prompts for it at all.
    public IScreen CreatePlayerCountOrFirstSetupScreen() => Header.MinPlayers < Header.MaxPlayers
        ? new PlayerCountScreen(Header, this)
        : CreatePlayerSetupScreen(Header.MinPlayers, []);

    public IScreen CreatePlayerSetupScreen(int playerCount, IReadOnlyList<ScenarioLoader.PlayerInfo> playersSoFar)
    {
        var playerNumber = playersSoFar.Count + 1;
        var suggestedName = ScenarioLoader.SuggestEmpireName(Context.Random, playersSoFar.Select(p => p.Name).ToList());
        return new PlayerSetupScreen(Header, playerNumber, playerCount, suggestedName, playersSoFar, this);
    }

    public IScreen CompleteSetup(IReadOnlyList<ScenarioLoader.PlayerInfo> players)
    {
        var setup = new GalaxySetup(Context.Random);
        var loader = new ScenarioLoader(setup, Context.Random, Context.NpeProvider);
        var game = loader.Load(ScenarioText, players);
        if (game.CurrentEmpire is null)
        {
            throw new InvalidOperationException("ScenarioLoader.Load must set Game.CurrentEmpire.");
        }

        // TurnLoop.Start, not a direct GalaxyMapScreen: matches Reconstructed4021.Tui's own RunGame,
        // which runs this same loop from turn 1 onward -- the starting empire could in principle be an
        // NPE needing a silent advance first, and even when it's a human (the only case this port's
        // single-human focus actually exercises), that human still gets the turn-start greeting and
        // status report before ever seeing the map, same as every subsequent turn.
        return TurnLoop.Start(game, Context);
    }
}
