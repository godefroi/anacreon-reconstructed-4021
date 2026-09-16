using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;

namespace Reconstructed4021.Tests;

/// <summary><see cref="ListBox{T}"/>'s own per-item foreground override (fgSelector) -- FleetOverlay's own per-empire row coloring.</summary>
public class ListBoxTests
{
    private static ConsoleColor FgAt(FrameBuffer fb, int x, int y) => fb.SnapshotBack()[(y * fb.Width) + x].Fg;

    [Test]
    public async Task Draw_NoSelector_EveryUnselectedRowUsesPlainFg()
    {
        var list = new ListBox<string>(["Alice", "Bob"], s => s);
        var fb = new FrameBuffer(20, 5, Stream.Null);

        list.Draw(fb, 0, 0, 10, 2, ConsoleColor.Gray, ConsoleColor.Black, ConsoleColor.White, ConsoleColor.Blue);

        await Assert.That(FgAt(fb, 0, 1)).IsEqualTo(ConsoleColor.Gray);
    }

    [Test]
    public async Task Draw_WithSelector_UnselectedRowUsesPerItemColor()
    {
        var list = new ListBox<string>(["Alice", "Bob"], s => s);
        var fb = new FrameBuffer(20, 5, Stream.Null);

        list.Draw(fb, 0, 0, 10, 2, ConsoleColor.Gray, ConsoleColor.Black, ConsoleColor.White, ConsoleColor.Blue,
            fgSelector: s => s == "Alice" ? ConsoleColor.Green : ConsoleColor.Red);

        await Assert.That(FgAt(fb, 0, 0)).IsEqualTo(ConsoleColor.White); // Alice is row 0, the selected row
        await Assert.That(FgAt(fb, 0, 1)).IsEqualTo(ConsoleColor.Red); // Bob, unselected -- its own color
    }

    [Test]
    public async Task Draw_WithSelector_SelectedRowStillUsesSelectedFgNotTheSelector()
    {
        var list = new ListBox<string>(["Alice", "Bob"], s => s);
        list.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        var fb = new FrameBuffer(20, 5, Stream.Null);

        list.Draw(fb, 0, 0, 10, 2, ConsoleColor.Gray, ConsoleColor.Black, ConsoleColor.White, ConsoleColor.Blue,
            fgSelector: _ => ConsoleColor.Green);

        // Bob is now selected (row 1) -- selectedFg wins over the selector even though Bob's own
        // selector color is set too, matching the "selection always overrides row-specific styling"
        // rule the Draw overload's own doc comment states.
        await Assert.That(FgAt(fb, 0, 1)).IsEqualTo(ConsoleColor.White);
        await Assert.That(FgAt(fb, 0, 0)).IsEqualTo(ConsoleColor.Green); // Alice, now unselected
    }
}
