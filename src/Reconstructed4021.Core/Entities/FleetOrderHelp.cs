namespace Reconstructed4021.Core.Entities;

/// <summary>
/// Player-facing reference for the order language <see cref="FleetOrderCompiler"/> parses, shown in the
/// Orders editor's side panel. <see cref="Commands"/> is the single list of command words; the tests
/// compile each <see cref="Command.Token"/> to keep it in step with the compiler's own switch.
/// </summary>
public static class FleetOrderHelp
{
    /// <param name="Token">The 4-letter command word the compiler matches (case-insensitive).</param>
    /// <param name="Syntax">Command word with the optional-letter tail shown in lowercase, then arguments.</param>
    /// <param name="Summary">One line, shown in the command list.</param>
    /// <param name="Detail">Full explanation, shown when the cursor line starts with this command.</param>
    public sealed record Command(string Token, string Syntax, string Summary, string Detail);

    public static IReadOnlyList<Command> Commands { get; } = [
        new("DEST", "DESTination <name|x,y>", "Fly to a place",
            "Fly to a name you've given an object, or to x,y measured from your capital (e.g. 3,-2)."),
        new("TRAN", "TRANsfer <amount> <code>", "Load or unload at a world",
            "At one of your own worlds: positive loads onto the fleet, negative unloads to the world. Clamped to what's available and fits. <code> is a 3-letter resource or ship code."),
        new("REPE", "REPEat", "Start over",
            "Jump back to the first order. Takes effect once per turn, so two in a row can't spin."),
        new("WAIT", "WAIT", "Skip a turn",
            "Stop here until next turn, then carry on with the following order."),
        new("REFU", "REFUel", "Top up fuel",
            "At one of your own worlds: fill the tank from the world's trillum. (Port-only.)"),
        new("JOIN", "JOIN [OVER]", "Merge into the world here",
            "At one of your own worlds: fold the fleet into it, losing whatever exceeds the cap. JOIN OVER keeps the overflow in Holding fleets instead. Elsewhere, joins Holding fleets. (Port-only.)"),
    ];

    /// <summary>
    /// The command <paramref name="line"/> starts with, or null for a blank or unrecognised line. Matches
    /// on the first 4 characters like <c>ParseLine</c>, so a half-typed word like "DE" finds nothing
    /// until it reaches 4 characters.
    /// </summary>
    public static Command? For(string line)
    {
        var word = line.AsSpan().TrimStart();
        var end = word.IndexOf(' ');
        if (end >= 0) {
            word = word[..end];
        }

        if (word.Length > 4) {
            word = word[..4];
        }

        foreach (var c in Commands) {
            if (word.Equals(c.Token, StringComparison.OrdinalIgnoreCase)) {
                return c;
            }
        }

        return null;
    }
}
