using Reconstructed4021.Panemonde.Widgets;

namespace Reconstructed4021.Tests;

/// <summary>
/// <see cref="TextEditor"/>'s optional word-wrap mode (EDIT.PAS's own RMargin reflow, re-derived
/// from scratch per edit rather than transliterating ForwardWordWrap/BackwardWordWrap's own
/// incremental cascade -- see RewrapParagraphAt's own doc comment). A small margin keeps each case
/// legible; the non-wrap (fleet orders) path is unchanged and has its own coverage via
/// assets/tui-driver-scripts/fleet-orders-smoke-test.txt.
/// </summary>
public class TextEditorTests
{
    private static void Type(TextEditor editor, string text)
    {
        foreach (var ch in text)
        {
            editor.HandleKey(new ConsoleKeyInfo(ch, ConsoleKey.NoName, shift: false, alt: false, control: false));
        }
    }

    private static void Press(TextEditor editor, ConsoleKey key) =>
        editor.HandleKey(new ConsoleKeyInfo('\0', key, shift: false, alt: false, control: false));

    [Test]
    public async Task WordWrap_BreaksAtTheLastSeparatorWithinMargin()
    {
        var editor = new TextEditor("", wrapMargin: 10);
        Type(editor, "hello world");

        await Assert.That(editor.Lines).IsEquivalentTo(new[] { "hello ", "world" });
    }

    [Test]
    public async Task WordWrap_UnbrokenWordLongerThanMargin_HardBreaksAtMargin()
    {
        var editor = new TextEditor("", wrapMargin: 5);
        Type(editor, "abcdefgh");

        await Assert.That(editor.Lines).IsEquivalentTo(new[] { "abcde", "fgh" });
    }

    [Test]
    public async Task WordWrap_SeparatorLandsExactlyAtMarginPlusOne_LeavesATrailingEmptyLine()
    {
        var editor = new TextEditor("", wrapMargin: 5);
        Type(editor, "abcde "); // 6 chars: the (margin+1)th is itself the separator.

        await Assert.That(editor.Lines).IsEquivalentTo(new[] { "abcde ", "" });
    }

    [Test]
    public async Task WordWrap_BackspaceAcrossASoftWrapBoundary_DeletesTheSeparatorCharacter()
    {
        var editor = new TextEditor("", wrapMargin: 10);
        Type(editor, "hello world"); // -> ["hello ", "world"], cursor after the 'd'.
        Press(editor, ConsoleKey.Home);
        // Cursor is now at the start of the wrapped second line ("world"), column 0.

        Press(editor, ConsoleKey.Backspace);

        await Assert.That(editor.Lines).IsEquivalentTo(new[] { "helloworld" });
        await Assert.That(editor.CursorRow).IsEqualTo(0);
        await Assert.That(editor.CursorCol).IsEqualTo(5);
    }

    [Test]
    public async Task WordWrap_BackspaceAtARealParagraphBreak_MergesWithoutLosingAnyCharacter()
    {
        var editor = new TextEditor("", wrapMargin: 20);
        Type(editor, "abc");
        Press(editor, ConsoleKey.Enter);
        Type(editor, "def");
        Press(editor, ConsoleKey.Home);
        // Cursor is now at the start of the second (Enter-created) line, column 0.

        Press(editor, ConsoleKey.Backspace);

        await Assert.That(editor.Lines).IsEquivalentTo(new[] { "abcdef" }); // every original character survives.
        await Assert.That(editor.CursorRow).IsEqualTo(0);
        await Assert.That(editor.CursorCol).IsEqualTo(3);
    }
}
