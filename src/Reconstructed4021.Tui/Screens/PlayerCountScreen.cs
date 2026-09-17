using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;
using Reconstructed4021.Tui.NewGame;
using Reconstructed4021.Tui.Shared;


namespace Reconstructed4021.Tui.Screens;


// NEWGAME.PAS:1425-1470 (GetNoOfPlayers). Only reached when a scenario's MinPlayers < MaxPlayers --
// see NewGameFlow.CreatePlayerCountOrFirstSetupScreen, matching real Pascal's own "IF MinPlay<MaxPlay"
// branch (its NoChoice counterpart never prompts at all).
internal sealed class PlayerCountScreen : IScreen
{
    private readonly ScenarioLoader.ScenarioHeader _header;
    private readonly NewGameFlow _flow;
    private readonly Starfield _starfield = new();
    private readonly TextInputField _field;
    private string _error = "";

    public IScreen? NextScreen { get; private set; }

    public PlayerCountScreen(ScenarioLoader.ScenarioHeader header, NewGameFlow flow)
    {
        _header = header;
        _flow = flow;
        _field = new TextInputField(header.MinPlayers.ToString(), maxLength: 2);
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)
        {
            NextScreen = _flow.Context.MakeTitleScreen();
            return;
        }

        if (key.Key == ConsoleKey.Enter)
        {
            if (int.TryParse(_field.Text, out var count) && count >= _header.MinPlayers && count <= _header.MaxPlayers)
            {
                NextScreen = _flow.CreatePlayerSetupScreen(count, []);
            }
            else
            {
                _error = $"Please enter a number between {_header.MinPlayers} and {_header.MaxPlayers}.";
            }

            return;
        }

        _field.HandleKey(key);
    }

    public void Update(TimeSpan elapsed) => _starfield.Update(elapsed);

    public void Draw(FrameBuffer fb)
    {
        var box = Chrome.Draw(fb, _header.Title, _starfield);
        fb.DrawText(box.X + 1, box.Y + 1, $"How many players ({_header.MinPlayers}-{_header.MaxPlayers}) ? ", Chrome.ContentFg, Chrome.ContentBg);
        _field.Draw(fb, box.X + 1, box.Y + 2, 10, TextInputField.DefaultFg, TextInputField.DefaultBg);
        fb.DrawText(box.X + 1, box.Y + 4, _error, Chrome.ContentFg, Chrome.ContentBg);
        fb.DrawText(box.X + 1, box.Y + box.Height - 2, "Enter: confirm   Esc: back to main menu", Chrome.ContentFg, Chrome.ContentBg);
    }
}
