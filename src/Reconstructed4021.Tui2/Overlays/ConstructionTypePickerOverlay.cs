using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;


namespace Reconstructed4021.Tui2.Overlays;


// Build menu > New's own type picker (CONSTR.PAS: ConstructCommand's own menu of everything the
// empire currently has the technology for). Plain pick-one-and-close list, same Enter/Esc shape as
// HelpListOverlay/ObjectPickerOverlay.
internal sealed class ConstructionTypePickerOverlay : IOverlay
{
    private const int Width = 40;
    private readonly ListBox<ConstructionType> _list;
    private readonly Action<ConstructionType> _onChosen;

    public bool IsDismissed { get; private set; }

    public ConstructionTypePickerOverlay(IReadOnlyList<ConstructionType> types, Action<ConstructionType> onChosen)
    {
        _list = new ListBox<ConstructionType>(types, ConstructionCatalog.DisplayName);
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
        fb.DrawText(x + Math.Max(1, (width - 13) / 2), y, " Construction ", ConsoleColor.White, ConsoleColor.Black);
        _list.Draw(fb, x + 1, y + 1, width - 2, height - 2, ConsoleColor.Gray, ConsoleColor.Black, ConsoleColor.Black, ConsoleColor.Gray);
    }
}
