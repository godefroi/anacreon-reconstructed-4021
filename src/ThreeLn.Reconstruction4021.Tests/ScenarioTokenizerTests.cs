using ThreeLn.Reconstruction4021.Core.NewGame;

namespace ThreeLn.Reconstruction4021.Tests;

/// <summary>DFA.PAS's DFA1NextToken (the .SCN tokenizer), hand-traceable against its own state
/// machine — see ScenarioTokenizer's doc comment for the source procedure.</summary>
public class ScenarioTokenizerTests
{
    [Test]
    public async Task NextToken_SplitsOnWhitespace()
    {
        var t = new ScenarioTokenizer("Hello World");

        await Assert.That(t.NextToken()).IsEqualTo(("Hello", false));
        await Assert.That(t.NextToken()).IsEqualTo(("World", false));
    }

    [Test]
    public async Task NextToken_TreatsSemicolonToLineEndAsComment()
    {
        var t = new ScenarioTokenizer("Hello ; this is a comment\r\nWorld");

        await Assert.That(t.NextToken()).IsEqualTo(("Hello", false));
        await Assert.That(t.NextToken()).IsEqualTo(("World", false));
    }

    [Test]
    public async Task NextToken_QuotedTokenCanContainSpaces()
    {
        var t = new ScenarioTokenizer("\"This is a token\" Next");

        await Assert.That(t.NextToken()).IsEqualTo(("This is a token", false));
        await Assert.That(t.NextToken()).IsEqualTo(("Next", false));
    }

    [Test]
    public async Task NextToken_BackslashEscapesQuoteInsideQuotedToken()
    {
        var t = new ScenarioTokenizer("\"\\\"This is a token\\\"\"");

        await Assert.That(t.NextToken()).IsEqualTo(("\"This is a token\"", false));
    }

    [Test]
    public async Task NextToken_BackslashEscapesBackslash()
    {
        var t = new ScenarioTokenizer("\"This is a backslash (\\\\)\"");

        await Assert.That(t.NextToken()).IsEqualTo(("This is a backslash (\\)", false));
    }

    [Test]
    public async Task NextToken_UnquotedTokenCanContainPunctuation()
    {
        var t = new ScenarioTokenizer("!@#$%^&*");

        await Assert.That(t.NextToken()).IsEqualTo(("!@#$%^&*", false));
    }

    [Test]
    public async Task NextToken_EmptyQuotedStringIsSilentlySwallowed()
    {
        // Quote's own Ch='"' branch returns straight to Start (not Final) — an empty "" isn't a
        // zero-length token, it's swallowed like whitespace, so this call skips straight past it.
        var t = new ScenarioTokenizer("\"\" NextToken");

        await Assert.That(t.NextToken()).IsEqualTo(("NextToken", false));
    }

    [Test]
    public async Task NextToken_LineBreakImmediatelyInsideAnOpenQuoteIsAnError()
    {
        // The Quote state's own line-break check fires only on the character right after the
        // opening quote, before any real content has promoted it to the accumulating Token1 state.
        var t = new ScenarioTokenizer("\"\r\nrest");

        var (token, error) = t.NextToken();
        await Assert.That(error).IsTrue();
        await Assert.That(token).IsEqualTo("ERROR: Unexpected line break.");
    }

    [Test]
    public async Task NextToken_LineBreakInsideAnAlreadyStartedQuotedTokenEndsItCleanly()
    {
        // A real, faithfully-reproduced FSM asymmetry: once content has promoted Quote into the
        // accumulating Token1 state, Token1's own line-break check ends the token at Final, not an
        // error — only the Quote state's line-break case (the test above) is treated as an error.
        var t = new ScenarioTokenizer("\"unterminated\r\nrest");

        await Assert.That(t.NextToken()).IsEqualTo(("unterminated", false));
        await Assert.That(t.NextToken()).IsEqualTo(("rest", false));
    }

    [Test]
    public async Task NextToken_AtRealEndOfFileWithNoPendingTokenIsAnError()
    {
        var t = new ScenarioTokenizer("");

        var (token, error) = t.NextToken();
        await Assert.That(error).IsTrue();
        await Assert.That(token).IsEqualTo("ERROR: Unexpected end of file.");
    }

    [Test]
    public async Task NextToken_LastTokenInFileEndsCleanlyAtEof()
    {
        var t = new ScenarioTokenizer("LastToken");

        await Assert.That(t.NextToken()).IsEqualTo(("LastToken", false));
    }

    [Test]
    public async Task ReadLine_ReturnsUpToTerminatorAndAdvancesPastIt()
    {
        var t = new ScenarioTokenizer("first line\r\nsecond line\r\nthird");

        await Assert.That(t.ReadLine()).IsEqualTo("first line");
        await Assert.That(t.ReadLine()).IsEqualTo("second line");
        await Assert.That(t.ReadLine()).IsEqualTo("third");
        await Assert.That(t.AtEnd).IsTrue();
    }

    [Test]
    public async Task ReadLine_InterleavesWithNextTokenOnASharedCursor()
    {
        // Mirrors SkipDescriptions being invoked mid-token-stream (BEGINDESCRIPTION consumed as a
        // token, then whole lines skipped via ReadLn, then token parsing resumes right after).
        var t = new ScenarioTokenizer("BEGINDESCRIPTION\r\nsome text\r\nENDDESCRIPTION\r\nNEXTCOMMAND");

        await Assert.That(t.NextToken()).IsEqualTo(("BEGINDESCRIPTION", false));
        await Assert.That(t.ReadLine()).IsEqualTo("");
        await Assert.That(t.ReadLine()).IsEqualTo("some text");
        await Assert.That(t.ReadLine()).IsEqualTo("ENDDESCRIPTION");
        await Assert.That(t.NextToken()).IsEqualTo(("NEXTCOMMAND", false));
    }
}
