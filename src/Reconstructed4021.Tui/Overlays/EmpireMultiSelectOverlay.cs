using Reconstructed4021.Core.Entities;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;

namespace Reconstructed4021.Tui2.Overlays;

// SendMessageCommand's own GetEmpires (DESIGN.PAS:873-934): a checklist of every empire in the game,
// Space toggles the highlighted row, Enter confirms -- with nothing toggled, that falls back to
// whichever empire the cursor happens to be resting on (Pascal's own "IF Empires=[] THEN
// Empires:=[PotentialEmpire[GetMenuSelect(Menu)]]"), so a plain "highlight one, hit Enter" still works
// without ever pressing Space. Esc cancels with no callback, same convention as every other picker here.
internal sealed class EmpireMultiSelectOverlay : IOverlay
{
    private const int Width = 40;

    private readonly IReadOnlyList<Empire> _empires;
    private readonly HashSet<Empire> _selected = [];
    private readonly Action<IReadOnlySet<Empire>> _onChosen;
    private int _cursor;

    public bool IsDismissed { get; private set; }

    public EmpireMultiSelectOverlay(IReadOnlyList<Empire> empires, Action<IReadOnlySet<Empire>> onChosen)
    {
        _empires = empires;
        _onChosen = onChosen;
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                _cursor = _empires.Count == 0 ? 0 : (_cursor + _empires.Count - 1) % _empires.Count;
                return;
            case ConsoleKey.DownArrow:
                _cursor = _empires.Count == 0 ? 0 : (_cursor + 1) % _empires.Count;
                return;
            case ConsoleKey.Spacebar:
                if (_empires.Count > 0)
                {
                    var empire = _empires[_cursor];
                    if (!_selected.Remove(empire))
                    {
                        _selected.Add(empire);
                    }
                }

                return;
            case ConsoleKey.Enter:
                if (_empires.Count == 0)
                {
                    return;
                }

                IsDismissed = true;
                _onChosen(_selected.Count > 0 ? _selected : new HashSet<Empire> { _empires[_cursor] });
                return;
            case ConsoleKey.Escape:
                IsDismissed = true;
                return;
        }
    }

    public void Draw(FrameBuffer fb)
    {
        var width = Math.Min(Width, fb.Width);
        var height = Math.Min(_empires.Count + 4, Math.Max(4, fb.Height - 2));
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        fb.DrawText(x + Math.Max(1, (width - 15) / 2), y, " Send Message ", ConsoleColor.White, ConsoleColor.Black);

        for (var i = 0; i < _empires.Count && i < height - 4; i++)
        {
            var mark = _selected.Contains(_empires[i]) ? 'x' : ' ';
            var selected = i == _cursor;
            var text = $"[{mark}] {_empires[i].Name}";
            fb.DrawText(x + 1, y + 1 + i, text.PadRight(width - 2), selected ? ConsoleColor.Black : ConsoleColor.Gray, selected ? ConsoleColor.Gray : ConsoleColor.Black);
        }

        fb.DrawText(x + 1, y + height - 3, ",:move Space:toggle", ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
        fb.DrawText(x + 1, y + height - 2, "Enter:send  Esc:cancel", ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
    }
}
