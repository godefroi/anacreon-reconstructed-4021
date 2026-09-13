using Reconstructed4021.Core.SaveFormat;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;

namespace Reconstructed4021.Tui2;

// Main menu > Load Game: lists the *.json saves under saves/ (and saves/auto/, GameShell's own
// autosave subdirectory). Real Pascal has no equivalent to browse here either (LOADSAVE.PAS just
// prompts for a hardcoded filename) -- same rationale as ScenarioPickerScreen's own doc comment.
public sealed class SaveGamePickerScreen : IScreen
{
    public sealed record SaveChoice(string Path, string DisplayName);

    private readonly ListBox<SaveChoice> _list;
    private readonly NewGameContext _context;
    private readonly Starfield _starfield = new();

    public IScreen? NextScreen { get; private set; }

    public SaveGamePickerScreen(IReadOnlyList<SaveChoice> saves, NewGameContext context)
    {
        _list = new ListBox<SaveChoice>(saves, choice => choice.DisplayName);
        _context = context;
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_list.Items.Count == 0)
        {
            if (key.Key == ConsoleKey.Escape)
            {
                NextScreen = _context.MakeTitleScreen();
            }

            return;
        }

        if (_list.HandleKey(key))
        {
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.Enter:
                var game = GameJson.Deserialize(File.ReadAllText(_list.SelectedItem!.Path), _context.Random, _context.NpeProvider);
                var player = game.CurrentEmpire ?? throw new InvalidOperationException("Save has no CurrentEmpire set -- nothing to drive.");
                NextScreen = new GalaxyMapScreen(game, player, _context);
                return;
            case ConsoleKey.Escape:
                NextScreen = _context.MakeTitleScreen();
                return;
        }
    }

    public void Update(TimeSpan elapsed) => _starfield.Update(elapsed);

    public void Draw(FrameBuffer fb)
    {
        var box = Chrome.Draw(fb, "Load Game", _starfield);

        if (_list.Items.Count == 0)
        {
            fb.DrawText(box.X + 1, box.Y + 1, "No saved games found.", Chrome.ContentFg, Chrome.ContentBg);
        }
        else
        {
            _list.Draw(fb, box.X + 1, box.Y + 1, box.Width - 2, box.Height - 3,
                Chrome.ContentFg, Chrome.ContentBg, ConsoleColor.Black, Chrome.ContentFg);
        }

        fb.DrawText(box.X + 1, box.Y + box.Height - 2, "Enter: choose   Esc: back to main menu", Chrome.ContentFg, Chrome.ContentBg);
    }
}
