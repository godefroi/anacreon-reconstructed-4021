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

    private ListBox<SaveChoice> _list;
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

        if (key.Key == ConsoleKey.D && key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            DeleteSelected();
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.Enter:
                var game = GameJson.Deserialize(File.ReadAllText(_list.SelectedItem!.Path), _context.Random, _context.NpeProvider);
                if (game.CurrentEmpire is null)
                {
                    throw new InvalidOperationException("Save has no CurrentEmpire set -- nothing to drive.");
                }

                // TurnLoop.Start, not a direct GalaxyMapScreen: Reconstructed4021.Tui's own Program.cs
                // routes a loaded save through the exact same RunGame loop as a fresh game (its Load Game
                // branch and --load flag both call RunGame(loadedGame)) -- so the loaded empire's turn
                // start greeting and status report show again here too, same as they would on any other
                // entry into that empire's turn.
                NextScreen = TurnLoop.Start(game, _context);
                return;
            case ConsoleKey.Escape:
                NextScreen = _context.MakeTitleScreen();
                return;
        }
    }

    // Moves rather than deletes outright, so an accidental Ctrl+D is recoverable by hand -- deleted/
    // sits alongside saves/ and auto/, and LoadSaves' own Directory.GetFiles calls are non-recursive, so
    // it's never picked back up by the listing.
    private void DeleteSelected()
    {
        var path = _list.SelectedItem!.Path;
        var deletedDir = Path.Combine(_context.RepoRoot, "saves", "deleted");
        Directory.CreateDirectory(deletedDir);

        var destPath = Path.Combine(deletedDir, Path.GetFileName(path));
        if (File.Exists(destPath))
        {
            destPath = Path.Combine(deletedDir, $"{Path.GetFileNameWithoutExtension(path)}-{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(path)}");
        }

        File.Move(path, destPath);
        _list = new ListBox<SaveChoice>(_context.LoadSaves(), choice => choice.DisplayName);
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

        fb.DrawText(box.X + 1, box.Y + box.Height - 2, "Enter: choose   Ctrl+D: delete   Esc: back to main menu", Chrome.ContentFg, Chrome.ContentBg, maxWidth: box.Width - 2);
    }
}
