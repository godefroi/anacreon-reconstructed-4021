using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;
using Reconstructed4021.Tui.NewGame;
using Reconstructed4021.Tui.Shared;


namespace Reconstructed4021.Tui.Screens;


// NEWGAME.PAS:1517-1632 (InputEmpireName): name entry, then a gender pick. Real Pascal's own gender
// prompt is already a separate popup over the main box (a small bordered box revealed on top) -- kept
// here as an internal state flip within one screen rather than a second chained IScreen, since nothing
// else needs to interrupt between the two steps. Real Pascal's next step in this same procedure is a
// password, entered twice to confirm -- dropped, same as Reconstructed4021.Tui's own port: its only
// purpose is protecting each human's turn in hot-seat multiplayer, and nothing yet cycles through more
// than one human's turn.
internal sealed class PlayerSetupScreen : IScreen
{
    private enum Step { Name, Gender }

    private readonly ScenarioLoader.ScenarioHeader _header;
    private readonly int _playerNumber;
    private readonly int _playerCount;
    private readonly string _suggestedName;
    private readonly IReadOnlyList<ScenarioLoader.PlayerInfo> _playersSoFar;
    private readonly NewGameFlow _flow;
    private readonly Starfield _starfield = new();
    private readonly TextInputField _nameField;
    private Step _step = Step.Name;
    private bool _isEmpress;

    public IScreen? NextScreen { get; private set; }

    public PlayerSetupScreen(ScenarioLoader.ScenarioHeader header, int playerNumber, int playerCount, string suggestedName,
        IReadOnlyList<ScenarioLoader.PlayerInfo> playersSoFar, NewGameFlow flow)
    {
        _header = header;
        _playerNumber = playerNumber;
        _playerCount = playerCount;
        _suggestedName = suggestedName;
        _playersSoFar = playersSoFar;
        _flow = flow;
        _nameField = new TextInputField(suggestedName, maxLength: 32);
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        // Esc always abandons the whole New Game flow, regardless of which step this screen is on --
        // matching PlayerSetupWindow's own KeyDown, where the Esc check runs unconditionally after the
        // gender-popup-specific M/F check.
        if (key.Key == ConsoleKey.Escape)
        {
            NextScreen = _flow.Context.MakeTitleScreen();
            return;
        }

        if (_step == Step.Name)
        {
            if (key.Key == ConsoleKey.Enter)
            {
                _step = Step.Gender;
                return;
            }

            _nameField.HandleKey(key);
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
            case ConsoleKey.RightArrow:
                _isEmpress = !_isEmpress;
                return;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                Confirm(_isEmpress);
                return;
        }

        switch (char.ToUpperInvariant(key.KeyChar))
        {
            case 'M':
                Confirm(isEmpress: false);
                return;
            case 'F':
                Confirm(isEmpress: true);
                return;
        }
    }

    private void Confirm(bool isEmpress)
    {
        var name = string.IsNullOrWhiteSpace(_nameField.Text) ? _suggestedName : _nameField.Text.Trim();
        var players = _playersSoFar.Append(new ScenarioLoader.PlayerInfo(name, Password: null, isEmpress)).ToList();
        NextScreen = players.Count < _playerCount
            ? _flow.CreatePlayerSetupScreen(_playerCount, players)
            : _flow.CompleteSetup(players);
    }

    public void Update(TimeSpan elapsed) => _starfield.Update(elapsed);

    public void Draw(FrameBuffer fb)
    {
        var box = Chrome.Draw(fb, _header.Title, _starfield);
        fb.DrawText(box.X + 1, box.Y + 1, $"Name of player empire #{_playerNumber} : ", Chrome.ContentFg, Chrome.ContentBg);
        _nameField.Draw(fb, box.X + 1, box.Y + 2, 32, TextInputField.DefaultFg, TextInputField.DefaultBg);

        if (_step == Step.Gender)
        {
            DrawGenderPopup(fb, box);
        }

        fb.DrawText(box.X + 1, box.Y + box.Height - 2,
            "Enter: confirm name   M/F, arrows+Enter: pick gender   Esc: back to main menu", Chrome.ContentFg, Chrome.ContentBg);
    }

    private void DrawGenderPopup(FrameBuffer fb, Chrome.Box box)
    {
        // NEWGAME.PAS's own OpenWindow(20,12,50,7,ThinBRD,'',C.CommWind,C.SYSWBorder,...): a
        // single-line-bordered popup (border color SYSWBorder=7 -> LightGray on Black), content
        // CommWind=15 -> White on Black.
        const int width = 34;
        const int height = 5;
        var x = box.X + ((box.Width - width) / 2);
        var y = box.Y + 6;

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        fb.DrawText(x + 1, y + 1, "Are you male or female? (M/F)", ConsoleColor.White, ConsoleColor.Black);
        fb.DrawText(x + 1, y + 2, _isEmpress ? "  Male   [ Female ]" : "[ Male ]   Female", ConsoleColor.White, ConsoleColor.Black);
    }
}
