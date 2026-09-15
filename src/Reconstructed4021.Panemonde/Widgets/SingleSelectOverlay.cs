namespace Reconstructed4021.Panemonde.Widgets;

// Plain pick-one-and-close list, generalized off Build > New's own construction-type picker
// (Reconstructed4021.Tui2) once Trade Technology needed the exact same shape twice more (an empire to
// trade with, then which technology to hand over). Fully generic -- no game-specific type baked in,
// same reason TextPromptOverlay lives here rather than in Tui2's own Overlays folder -- same Enter/Esc
// convention as that widget and ListBox<T> itself.
public sealed class SingleSelectOverlay<T> : IOverlay
{
    private const int Width = 40;

    private readonly string _title;
    private readonly ListBox<T> _list;
    private readonly Action<T> _onChosen;

    public bool IsDismissed { get; private set; }

    public SingleSelectOverlay(string title, IReadOnlyList<T> items, Func<T, string> format, Action<T> onChosen)
    {
        _title = title;
        _list = new ListBox<T>(items, format);
        _onChosen = onChosen;
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_list.HandleKey(key))
        {
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.Enter:
                IsDismissed = true;
                if (_list.SelectedItem is { } chosen)
                {
                    _onChosen(chosen);
                }

                break;
            case ConsoleKey.Escape:
                IsDismissed = true;
                break;
        }
    }

    public void Draw(FrameBuffer fb)
    {
        var width = Math.Min(Width, fb.Width);
        var height = Math.Min(_list.Items.Count + 2, Math.Max(3, fb.Height - 2));
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        var titleText = $" {_title} ";
        fb.DrawText(x + Math.Max(1, (width - titleText.Length) / 2), y, titleText, ConsoleColor.White, ConsoleColor.Black);
        _list.Draw(fb, x + 1, y + 1, width - 2, height - 2, ConsoleColor.Gray, ConsoleColor.Black, ConsoleColor.Black, ConsoleColor.Gray);
    }
}
