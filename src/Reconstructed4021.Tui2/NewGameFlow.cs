using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Panemonde;

namespace Reconstructed4021.Tui2;

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
        var player = game.CurrentEmpire ?? throw new InvalidOperationException("ScenarioLoader.Load must set Game.CurrentEmpire.");
        return new GalaxyMapScreen(game, player, Context);
    }
}
