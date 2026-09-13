using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;

namespace Reconstructed4021.Tui2;

// MAPWIND.PAS's GetMapObject/DISPLAY.PAS's DisplayMenu: 2+ objects at one cursor sector need a pick
// first (ExamineCursor's own 2+ branch) before Close Up can open on any one of them -- the first real
// second overlay-stack consumer (GalaxyMapScreen pushes this on top of itself, then this pushes a
// CloseUpOverlay on top of itself in turn once something's chosen).
internal sealed class ObjectPickerOverlay : IOverlay
{
    private const int Width = 45;
    private readonly ListBox<ISectorObject> _list;
    private readonly Empire _viewer;
    private readonly Action<ISectorObject> _onChosen;

    public bool IsDismissed { get; private set; }

    public ObjectPickerOverlay(IReadOnlyList<ISectorObject> objects, Empire viewer, Action<ISectorObject> onChosen)
    {
        _viewer = viewer;
        _onChosen = onChosen;
        _list = new ListBox<ISectorObject>(objects, Format);
    }

    private string Format(ISectorObject obj)
    {
        var name = obj.Names.GetValueOrDefault(_viewer) ?? CloseUpOverlay.DescribeLocation(obj, _viewer);
        return $"{name}  ({obj.Owner.Name})";
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
        _list.Draw(fb, x + 1, y + 1, width - 2, height - 2, ConsoleColor.Gray, ConsoleColor.Black, ConsoleColor.Black, ConsoleColor.Gray);
    }
}
