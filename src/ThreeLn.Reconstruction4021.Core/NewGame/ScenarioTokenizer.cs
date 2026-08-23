namespace ThreeLn.Reconstruction4021.Core.NewGame;

/// <summary>
/// Ports DFA.PAS's DFA1NextToken character state machine verbatim (states Start/Comment/Quote/Slash1/
/// Slash2/Token1/Token2/Final, same transitions), plus the whole-line reads LoadScenario's own
/// SkipDescriptions/SkipText need (Pascal's ReadLn) — both share one cursor into the same underlying
/// text, since a real .SCN file interleaves token-based commands with line-based BEGINTEXT/
/// BEGINDESCRIPTION blocks and the cursor has to stay in sync across the switch.
///
/// Faithfully reproduces one real quirk rather than "fixing" it: an empty quoted token ("") returns
/// the Quote state to Start instead of ending a token, so it's silently swallowed as if it were
/// whitespace and parsing continues looking for the next real token — see DFA1NextToken's own Quote
/// case, which only promotes to the token-accumulating Token1 state on the first non-quote character.
/// </summary>
public sealed class ScenarioTokenizer(string content)
{
    private const char EofCh = '\x1A';
    private const char ReturnCh = '\r';
    private const char LineFeedCh = '\n';
    private const char TabCh = '\t';

    private int _position;

    public bool AtEnd => _position >= content.Length;

    private char Read()
    {
        if (_position >= content.Length)
            return EofCh;
        return content[_position++];
    }

    private static bool IsWhiteSpace(char ch) => ch is EofCh or ReturnCh or LineFeedCh or TabCh or ' ';

    private enum State { Start, Comment, Quote, Slash1, Slash2, Token1, Token2, Final }

    /// <summary>DFA1NextToken. Returns (token, error) — on error, token holds the diagnostic message
    /// text a caller would embed in its own ScenaError call (DFA.PAS's own convention).</summary>
    public (string Token, bool Error) NextToken()
    {
        var state = State.Start;
        var token = "";
        var error = false;

        while (state != State.Final && !error) {
            var ch = Read();
            switch (state) {
                case State.Start:
                    if (ch == ';') state = State.Comment;
                    else if (ch == '"') state = State.Quote;
                    else if (ch == '\\') state = State.Slash2;
                    else if (ch == EofCh) { error = true; token = "ERROR: Unexpected end of file."; }
                    else if (!IsWhiteSpace(ch)) { state = State.Token2; token += ch; }
                    break;

                case State.Comment:
                    if (ch is ReturnCh or LineFeedCh) state = State.Start;
                    else if (ch == EofCh) { error = true; token = "ERROR: Unexpected end of file."; }
                    break;

                case State.Quote:
                    if (ch == '"') state = State.Start;
                    else if (ch == '\\') state = State.Slash1;
                    else if (ch is EofCh or ReturnCh or LineFeedCh) { error = true; token = "ERROR: Unexpected line break."; }
                    else { state = State.Token1; token += ch; }
                    break;

                case State.Slash1:
                    if (ch is EofCh or LineFeedCh or ReturnCh) { error = true; token = "ERROR: Unexpected line break."; }
                    else { state = State.Token1; token += ch; }
                    break;

                case State.Slash2:
                    if (ch is EofCh or LineFeedCh or ReturnCh) { error = true; token = "ERROR: Unexpected line break."; }
                    else { state = State.Token2; token += ch; }
                    break;

                case State.Token1:
                    if (ch == '"' || ch is LineFeedCh or ReturnCh or EofCh) state = State.Final;
                    else if (ch == '\\') state = State.Slash1;
                    else token += ch;
                    break;

                case State.Token2:
                    if (IsWhiteSpace(ch)) state = State.Final;
                    else if (ch == '\\') state = State.Slash2;
                    else token += ch;
                    break;
            }
        }

        return (token, error);
    }

    /// <summary>Pascal's ReadLn(SF, Line) — everything up to (not including) the next line terminator,
    /// cursor advanced past it. Returns "" once already at end, matching EoF(SF) staying true.</summary>
    public string ReadLine()
    {
        var start = _position;
        while (_position < content.Length && content[_position] is not (ReturnCh or LineFeedCh))
            _position++;
        var line = content[start.._position];

        if (_position < content.Length && content[_position] == ReturnCh)
            _position++;
        if (_position < content.Length && content[_position] == LineFeedCh)
            _position++;

        return line;
    }
}
