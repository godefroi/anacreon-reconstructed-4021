using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Presentation;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Entities;

/// <summary>
/// What the Orders editor's help panel offers for a <c>DESTination</c> line: the places the viewer knows
/// about, ranked by distance, and a decode of what the line's destination currently points at. Names are
/// rare, so the unit of completion is the capital-relative <c>x,y</c> <see cref="FleetOrderCompiler"/>
/// already accepts; a name is used only when the viewer has given the object one.
/// </summary>
public static class FleetOrderCompletion
{
    /// <param name="Object">The world, starbase or stargate itself (the panel takes its glyph and owner colour from it).</param>
    /// <param name="Coordinates">Capital-relative <c>x,y</c>, the same form the compiler parses.</param>
    /// <param name="Name">The viewer's own name for it, or null.</param>
    /// <param name="InsertText">What a completion types after the command word: the name if it has one, else <paramref name="Coordinates"/>.</param>
    /// <param name="Population">Null unless the viewer owns or has scouted it, same rule as Close-Up.</param>
    public sealed record Candidate(ISectorObject Object, string Coordinates, string? Name, string InsertText, int? Population);

    /// <param name="Resolved">False when the destination text matches no name and no in-bounds coordinate (what Esc-compile would reject).</param>
    /// <param name="Target">The known object there, or null for empty space or something the viewer can't see.</param>
    /// <param name="Coordinates">Capital-relative position, null when unresolved.</param>
    public sealed record Decoded(bool Resolved, Candidate? Target, string? Coordinates);

    /// <summary>
    /// Known worlds, starbases and stargates whose coordinates (when <paramref name="argument"/> starts
    /// with a digit or '-') or name (otherwise) match, nearest to <paramref name="from"/> first. Only
    /// objects the viewer can see are listed, so an unscouted world never leaks.
    /// </summary>
    public static IReadOnlyList<Candidate> Candidates(Game game, Empire viewer, Coordinate from, string argument)
    {
        var query = argument.Trim();
        var byCoordinate = query.Length > 0 && (char.IsDigit(query[0]) || query[0] == '-');

        var result = new List<(Candidate Candidate, long Distance)>();
        foreach (var obj in Places(game.Galaxy)) {
            if (!Game.Visible(viewer, obj)) {
                continue;
            }

            var candidate = Describe(game, viewer, obj);
            var matches = query.Length == 0
                || (byCoordinate
                    ? candidate.Coordinates.StartsWith(query, StringComparison.Ordinal)
                    : candidate.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
            if (!matches) {
                continue;
            }

            long dx = obj.Location.X - from.X;
            long dy = obj.Location.Y - from.Y;
            result.Add((candidate, dx * dx + dy * dy));
        }

        return [.. result.OrderBy(r => r.Distance).ThenBy(r => r.Candidate.Coordinates, StringComparer.Ordinal).Select(r => r.Candidate)];
    }

    /// <summary>
    /// Where "near" is measured from for the line at <paramref name="row"/>: the nearest resolvable
    /// <c>DEST</c> above it, since orders chain (the fleet is heading there next), else the fleet's own position.
    /// </summary>
    public static Coordinate ReferencePoint(Game game, Empire viewer, Coordinate fleetLocation, IReadOnlyList<string> lines, int row)
    {
        for (var i = Math.Min(row, lines.Count) - 1; i >= 0; i--) {
            if (FleetOrderHelp.For(lines[i])?.Token != "DEST") {
                continue;
            }

            if (FleetOrderCompiler.ResolveDestination(game, viewer, FirstArgument(lines[i]), out var obj, out var position)) {
                return obj?.Location ?? position!.Value;
            }
        }

        return fleetLocation;
    }

    /// <summary>What a <c>DEST</c> line points at right now, or null when the line isn't a <c>DEST</c> with an argument.</summary>
    public static Decoded? Decode(Game game, Empire viewer, string line)
    {
        if (FleetOrderHelp.For(line)?.Token != "DEST") {
            return null;
        }

        var argument = FirstArgument(line);
        if (argument.Length == 0) {
            return null;
        }

        if (!FleetOrderCompiler.ResolveDestination(game, viewer, argument, out var obj, out var position)) {
            return new Decoded(false, null, null);
        }

        var origin = FleetOrderCompiler.Origin(game, viewer);
        var target = obj is not null && Game.Visible(viewer, obj) ? Describe(game, viewer, obj) : null;
        return new Decoded(true, target, RelativeCoordinate.Format(obj?.Location ?? position!.Value, origin));
    }

    /// <summary>The line with its argument replaced by <paramref name="candidate"/>, keeping the command word as typed.</summary>
    public static string Apply(string line, Candidate candidate)
    {
        var word = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return $"{word} {candidate.InsertText}";
    }

    /// <summary>The text after the command word, trimmed; what the filter matches against.</summary>
    public static string Argument(string line)
    {
        var trimmed = line.TrimStart();
        var space = trimmed.IndexOf(' ');
        return space < 0 ? "" : trimmed[(space + 1)..].Trim();
    }

    // The compiler reads only the first word after the command (ParseLine's parts[1]).
    private static string FirstArgument(string line) => Argument(line).Split(' ')[0];

    private static Candidate Describe(Game game, Empire viewer, ISectorObject obj)
    {
        var coordinates = RelativeCoordinate.Format(obj.Location, FleetOrderCompiler.Origin(game, viewer));
        // A name with a space can't be typed back as a destination (the compiler reads one word).
        var name = obj.Names.TryGetValue(viewer, out var n) && !n.Contains(' ') ? n : null;
        var population = obj is IEconomicWorld world && Game.ScoutedOrOwned(viewer, obj) ? world.Population : (int?)null;
        return new Candidate(obj, coordinates, name, name ?? coordinates, population);
    }

    private static IEnumerable<ISectorObject> Places(Galaxy.Galaxy galaxy)
    {
        foreach (var p in galaxy.Planets) yield return p;
        foreach (var s in galaxy.Starbases) yield return s;
        foreach (var g in galaxy.Stargates) yield return g;
    }
}
