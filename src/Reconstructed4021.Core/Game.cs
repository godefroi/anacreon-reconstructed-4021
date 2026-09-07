using System.Text.Json.Serialization;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core;

/// <summary>
/// Owns the empires in a game and whose turn it currently is — Pascal's Player is genuine
/// simulation state, not an incidental loop variable, so CurrentEmpire lives here rather than
/// being threaded through by whoever calls TurnEngine.
/// </summary>
public sealed class Game(Galaxy.Galaxy galaxy)
{
    public Galaxy.Galaxy Galaxy { get; } = galaxy;

    /// <summary>
    /// Port-only addition, no Pascal equivalent: a stable identity for this playthrough, shared by
    /// every save (manual or auto) taken from it -- lets a player's own saves/autosaves, and anything
    /// else keyed to a specific game session (e.g. <see cref="Turns.AnnualTickHandler"/>'s optional
    /// tech-research debug log), be correlated back to "the same game," across however many times it's
    /// saved, loaded, or the process itself is relaunched. Assigned fresh here so every construction
    /// path (a brand-new game, a real `.SAV` import) gets one for free with no extra call site; a JSON
    /// save written before this field existed has no `id` key at all, so
    /// <see cref="SaveFormat.GameJson.Deserialize"/> falls back to a fresh one there too -- that save's
    /// own sibling saves from before this feature just won't retroactively share an identity, which is
    /// the best any of them can do once written.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Not reflection-serializable as-is: <see cref="Empire"/> itself needs pre-allocated
    /// placeholder identity (its own <c>Capital</c>/<c>DefeatedBy</c>/visibility-set fields can
    /// forward-reference entities, and every entity's <c>Owner</c> back-references an
    /// <see cref="Empire"/>) — <see cref="SaveFormat.GameJson"/> reads/writes this list by hand
    /// instead of through the generic pass. See that file's own notes.
    /// </summary>
    [JsonIgnore]
    public List<Empire> Empires { get; } = [];

    /// <summary>
    /// Not reflection-serializable as-is (Empire-keyed, ITurnHandler has one real implementor) —
    /// <see cref="SaveFormat.GameJson"/> reads/writes this dictionary by hand instead of through the
    /// generic pass. See that file's own notes.
    /// </summary>
    [JsonIgnore]
    public Dictionary<Empire, ITurnHandler> TurnHandlers { get; } = new();

    /// <summary>
    /// Null until whoever assembles the game sets it to Empires[0] — Empires is populated after
    /// construction, not passed in up front, so the constructor can't default this itself.
    /// Same reason as <see cref="TurnHandlers"/>'s <c>[JsonIgnore]</c>: an <see cref="Empire"/>
    /// reference, and <see cref="SaveFormat.GameJson"/> hand-writes every one of those (alongside
    /// <see cref="Empires"/> itself) rather than routing them through automatic reflection.
    /// </summary>
    [JsonIgnore]
    public Empire? CurrentEmpire { get; set; }

    public int Year { get; set; }

    /// <summary>
    /// Which scenario this game started from (Environment section's ScenaFilename,
    /// ENVIRON.PAS:35) — real save metadata with no other home in this port's model.
    /// <see cref="Core.NewGame.ScenarioLoader"/> doesn't set this (a .SCN load has no equivalent
    /// concept of "this game's own save filename"); it's populated by
    /// <see cref="SaveFormat.SavGameLoader"/> on `.SAV` import.
    /// </summary>
    public string? ScenarioFilename { get; set; }

    /// <summary>
    /// Environment section's TimePerTurn/AutoSave/AsyncTurns/PauseActive/ReEnterGame
    /// (ENVIRON.PAS:38, 28-31) — real per-turn time-limit budget and UI/session settings that no
    /// system in this port reads yet (no turn-timer, no autosave, no interactive re-entry loop).
    /// Stored verbatim rather than discarded so a load-then-resave doesn't silently reset a real
    /// file's own values back to Pascal's declared defaults; <see cref="SaveFormat.SavGameLoader"/>
    /// populates these, <see cref="SaveFormat.SavGameWriter"/> writes them back out.
    /// </summary>
    public int TimePerTurn { get; set; } = 300;

    /// <inheritdoc cref="TimePerTurn"/>
    public bool AutoSave { get; set; } = true;

    /// <inheritdoc cref="TimePerTurn"/>
    public bool AsyncTurns { get; set; }

    /// <inheritdoc cref="TimePerTurn"/>
    public bool PauseActive { get; set; } = true;

    /// <inheritdoc cref="TimePerTurn"/>
    public bool ReEnterGame { get; set; }

    /// <summary>
    /// Raw `.SAV` NPE-Data blobs for empires whose personality this port doesn't implement an
    /// <see cref="ITurnHandler"/> for (Pirate/Berserker/Guardian/Trader/unrecognized) — real
    /// scenarios routinely mix these with Kingdom empires (see `docs/ROADMAP.md`'s NPE reachability
    /// notes), so their state must round-trip opaquely rather than being silently
    /// dropped on `.SAV` write-back. Populated by <see cref="SaveFormat.SavGameLoader"/>; nothing
    /// reads the bytes themselves — this port has no representation of what's inside them. Same
    /// reason as <see cref="TurnHandlers"/>'s <c>[JsonIgnore]</c>: <see cref="SaveFormat.GameJson"/>
    /// handles this by hand.
    /// </summary>
    [JsonIgnore]
    public Dictionary<Empire, byte[]> UnimplementedNpeBlobs { get; } = new();

    /// <summary>
    /// `MessageList` (`MESS.PAS`) -- a flat, global list, not per-empire: real Pascal's own
    /// `GetMessages(Emp)` filters by <see cref="Message.Recipients"/> at call time rather than
    /// storing messages under their recipient. Same reason as <see cref="Empires"/>'s own
    /// <c>[JsonIgnore]</c>: <see cref="Message.Sender"/>/<see cref="Message.Recipients"/> are
    /// <see cref="Empire"/> references, so <see cref="SaveFormat.GameJson"/> reads/writes this by
    /// hand instead of through the generic pass.
    /// </summary>
    [JsonIgnore]
    public List<Message> Messages { get; } = [];

    /// <summary>
    /// SCENA.PAS's own WorldBackgroundIndex, parsed once at scenario load (<see cref="NewGame.ScenarioLoader"/>)
    /// rather than real Pascal's own "reopen and rescan the original .SCN file every single Close Up
    /// call" (<c>DisplayBackground</c> literally re-<c>Assign</c>s/<c>Reset</c>s the file each time) —
    /// same content, cheaper lookup, since this port's <see cref="Entities.Planet"/>/<see cref="Empire"/>
    /// references don't need reconstructing from a byte offset the way Pascal's own <c>IDNumber</c>s do.
    /// Not <c>[JsonIgnore]</c>d for the reference-cycle reasons <see cref="Turns.ITurnHandler"/>'s own
    /// dictionary is (<see cref="Entities.WorldBackgroundEntry.World"/> is an <see cref="Entities.ISectorObject"/>
    /// reference) — also matches real Pascal, which never persists this in a <c>.SAV</c> either, always
    /// re-deriving it from <see cref="ScenarioFilename"/> on demand. A game loaded via <c>.SAV</c>/native
    /// JSON (this port has no equivalent "re-open the original .SCN" step) simply has none, same as a
    /// hand-built fixture never parsed from a real scenario at all.
    /// </summary>
    [JsonIgnore]
    public List<Entities.WorldBackgroundEntry> WorldBackgroundIndex { get; } = [];

    /// <summary>
    /// SCENA.PAS's own <c>TEXT n</c>..<c>ENDTEXT</c> bodies, keyed by their number — raw lines,
    /// <c>[C:id]</c>/<c>[N:id]</c> placeholders unsubstituted until <see cref="FindWorldBackgroundText"/>
    /// resolves them for a specific viewer.
    /// </summary>
    [JsonIgnore]
    public Dictionary<int, IReadOnlyList<string>> BackgroundTexts { get; } = [];

    /// <summary>
    /// Deliberately <see cref="Types.EmpireStatus"/>-blind: cycles through every fixed slot,
    /// eliminated or not, matching real Pascal's own fixed per-empire array (only the *player* index
    /// cycles unconditionally; per-slot behavior, not iteration, is what's gated). All
    /// <see cref="Types.EmpireStatus"/>-awareness lives in <see cref="Turns.TurnEngine.AdvanceOneTurn"/>'s
    /// dispatch step instead — a skip-aware version here would break annual-tick wrap detection the
    /// moment <c>Empires[0]</c> itself becomes <see cref="Types.EmpireStatus.Eliminated"/>, since
    /// <see cref="IsFirstEmpire"/> checks that exact slot by reference.
    /// </summary>
    public Empire NextEmpire(Empire current)
    {
        var index = Empires.IndexOf(current);
        if (index < 0)
            throw new ArgumentException("Empire is not in this game's Empires list.", nameof(current));
        return Empires[(index + 1) % Empires.Count];
    }

    public bool IsFirstEmpire(Empire empire) =>
        Empires.Count > 0 && ReferenceEquals(Empires[0], empire);

    /// <summary>
    /// `GetMessages(Emp)` (`MESS.PAS:57-75`) -- filters <see cref="Messages"/> by
    /// <see cref="Message.Recipients"/> at call time, the same query real Pascal itself runs rather
    /// than storing a message under its recipient(s): a message can name several empires at once
    /// (or none of the caller), so there's no single owner to index by up front.
    /// </summary>
    public IEnumerable<Message> MessagesFor(Empire empire) => Messages.Where(m => m.Recipients.Contains(empire));

    /// <summary>
    /// Computed from <see cref="TurnHandlers"/>, which is itself <c>[JsonIgnore]</c>d (see its own
    /// remarks) — GameJson must not try to serialize this too, both because it's derived (nothing to
    /// persist) and because evaluating it requires every empire to already have a
    /// <see cref="TurnHandlers"/> entry, which isn't true for e.g. a freshly-constructed <see cref="Game"/>.
    /// </summary>
    [JsonIgnore]
    public bool AnyHumanPlayersRemain => Empires.Any(e => e.Status == EmpireStatus.Active && e.NpeType is null);

    /// <summary>
    /// AddGlobalNews (NEWS.PAS:230-243): broadcasts one news item to every empire that isn't in
    /// <paramref name="exclude"/> and has scouted <paramref name="source"/>. Pascal's own
    /// <c>EmpireActive(Emp)</c> conjunct is now enforced by the callee — <see cref="Empire.AddNews"/>'s
    /// own <see cref="EmpireStatus.Eliminated"/> guard — rather than by <see cref="Empires"/> list
    /// membership, since that's a permanent roster now (see <see cref="EmpireStatus"/>).
    /// </summary>
    public void AddGlobalNews(
        IEnumerable<Empire> exclude,
        ISectorObject source,
        NewsType headline,
        Empire? otherEmpire = null,
        int p1 = 0,
        int p2 = 0,
        int p3 = 0,
        Empire? defender = null)
    {
        var excluded = exclude as ICollection<Empire> ?? [.. exclude];

        foreach (var empire in Empires) {
            if (excluded.Contains(empire) || !Scouted(empire, source)) {
                continue;
            }

            empire.AddNews(headline, source, otherEmpire: otherEmpire, p1: p1, p2: p2, p3: p3, defender: defender);
        }
    }

    /// <summary>
    /// Scouted(Emp,Source) (PRIMINTR.PAS) — the stronger "currently sees" tier, as opposed to
    /// <see cref="Known"/>'s "has ever seen." Public: <see cref="Npe.NpeToolkit.DestroyAllFleetsInSector"/>
    /// reads this the same way <see cref="Known"/> already reads publicly, rather than
    /// hand-dispatching across the five <see cref="ISectorObject"/> kinds itself.
    /// </summary>
    public static bool Scouted(Empire empire, ISectorObject source) => source switch {
        Planet p => empire.Planets.Scouted.Contains(p),
        Starbase s => empire.Starbases.Scouted.Contains(s),
        Stargate g => empire.Stargates.Scouted.Contains(g),
        ConstructionSite c => empire.ConstructionSites.Scouted.Contains(c),
        Fleet f => empire.Fleets.Scouted.Contains(f),
        _ => false,
    };

    /// <summary>
    /// Known(Emp,ID) (PRIMINTR.PAS) — the weaker "has ever seen" tier, as opposed to
    /// <see cref="Scouted"/>'s "currently sees." Public: <see cref="Npe.NpeToolkit"/>'s targeting
    /// logic reads this the same way <see cref="AddGlobalNews"/> already reads
    /// <see cref="Scouted"/>, rather than every caller hand-dispatching across the five
    /// <see cref="ISectorObject"/> kinds itself.
    /// </summary>
    public static bool Known(Empire empire, ISectorObject source) => source switch {
        Planet p => empire.Planets.Known.Contains(p),
        Starbase s => empire.Starbases.Known.Contains(s),
        Stargate g => empire.Stargates.Known.Contains(g),
        ConstructionSite c => empire.ConstructionSites.Known.Contains(c),
        Fleet f => empire.Fleets.Known.Contains(f),
        _ => false,
    };

    /// <summary>
    /// What the TUI's map/cursor/Close Up should treat as visible to <paramref name="empire"/>: Known,
    /// or owned outright. Real Pascal seeds <c>KnownBy:=[NewEmp]</c> the moment an object is created
    /// (INTRFACE.PAS:384-385, 419-420, 507-508), so an empire's own objects are always Known there by
    /// construction; this port has no equivalent creation hook (see <see cref="Turns.VisibilityHandler"/>'s
    /// own doc comment), so a freshly-owned object can otherwise sit un-Known behind
    /// <see cref="Turns.VisibilityHandler"/>'s 50%-roll discovery path until something scouts it. The
    /// ownership clause here restores that invariant at the read site instead, so the human's own
    /// worlds/fleets never vanish from their own map.
    /// </summary>
    public static bool Visible(Empire empire, ISectorObject source) =>
        Known(empire, source) || source.Owner == empire;

    /// <summary>
    /// Scouted, or owned outright — the full-detail analog of <see cref="Visible"/>'s "exists at all"
    /// tier, for redaction that needs to know whether the *viewer* gets real numbers (Close Up's
    /// field-by-field detail, CLSCOMM.PAS) rather than just whether an icon draws. Same
    /// ownership-fallback reasoning as <see cref="Visible"/>: real Pascal's <c>ScoutFleets</c>
    /// unconditionally Scouts every one of an empire's own fleets (<c>IF Emp=PlayerEmp THEN
    /// ScoutedBy:=ScoutedBy+[PlayerEmp]</c>), so an owned object is always Scouted there by
    /// construction; this port has no equivalent guarantee before <see cref="Turns.VisibilityHandler"/>
    /// has actually run once for that owner.
    /// </summary>
    public static bool ScoutedOrOwned(Empire empire, ISectorObject source) =>
        Scouted(empire, source) || source.Owner == empire;

    /// <summary>
    /// SCENA.PAS's DisplayBackground: the first <see cref="WorldBackgroundIndex"/> row (file order)
    /// whose <see cref="WorldBackgroundEntry.World"/> is <paramref name="world"/> and whose
    /// <see cref="WorldBackgroundEntry.Conditions"/> all pass for (<paramref name="conquer"/>, that
    /// world's current <see cref="ISectorObject.Owner"/>). Returns the matched <c>TEXT</c> block's
    /// lines, <c>[C:id]</c>/<c>[N:id]</c> placeholders substituted for <paramref name="viewer"/>, or
    /// null if nothing matched (real Pascal's own <c>Found=False</c>). <c>CLSCOMM.PAS</c>'s own
    /// <c>CloseUpCom</c> call always passes <c>conquer:false</c>; <c>ATTCOMM.PAS</c>'s post-conquest
    /// report (<c>conquer:true</c>) isn't wired to any caller yet (docs/OPEN_GAPS.md).
    /// </summary>
    public static IReadOnlyList<string>? FindWorldBackgroundText(Game game, ISectorObject world, Empire viewer, bool conquer)
    {
        var wantKind = conquer ? BackgroundConditionKind.ConqueredBy : BackgroundConditionKind.OwnedBy;

        foreach (var entry in game.WorldBackgroundIndex) {
            if (!ReferenceEquals(entry.World, world))
                continue;
            if (!entry.Conditions.All(c => c.Kind == wantKind && c.Empires.Contains(world.Owner)))
                continue;
            if (!game.BackgroundTexts.TryGetValue(entry.TextNumber, out var lines))
                continue;

            return lines.Select(line => SubstitutePlaceholder(game, viewer, line)).ToList();
        }

        return null;
    }

    /// <summary>
    /// SCENA.PAS's ParseLine: at most one <c>[C:id]</c>/<c>[N:id]</c> marker per line (the first '['
    /// through the first ']'), replaced with a relative coordinate or a short object name — real
    /// Pascal only ever scans for one bracket pair per line, never loops for a second, so neither
    /// does this. A marker whose id doesn't resolve is dropped rather than left as raw <c>[X:...]</c>
    /// text, matching Pascal's own <c>IDMatch</c> leaving <c>ID:=EmptyQuadrant</c> on failure rather
    /// than throwing — a malformed marker in scenario prose shouldn't crash the game.
    /// </summary>
    private static string SubstitutePlaceholder(Game game, Empire viewer, string line)
    {
        var open = line.IndexOf('[');
        var close = open < 0 ? -1 : line.IndexOf(']', open);
        if (open < 0 || close < 0)
            return line;

        var marker = line[open..(close + 1)];
        if (marker.Length < 5 || marker[2] != ':')
            return line;

        var kind = marker[1];
        if (kind is not ('C' or 'N') || !TryParseTypeIndex(marker[3..^1], out var objType, out var index))
            return line;

        var target = ResolveWorldReference(game.Galaxy, objType, index);
        if (target is null)
            return line.Remove(open, marker.Length);

        var replacement = kind == 'C'
            ? CoordinateName(game, viewer, target.Location)
            : target.Names.GetValueOrDefault(viewer) ?? DescribeBackgroundKind(target);

        return line.Remove(open, marker.Length).Insert(open, replacement);
    }

    /// <summary>
    /// PRIMINTR.PAS's GetCoordName: a location's coordinates relative to <paramref name="viewer"/>'s
    /// own capital (X same sign, Y flipped since screen-down is universe-"south"). Matches
    /// GalaxyView's own cursor-readout formula (its <c>CursorCoordinateText</c>), which is relative
    /// to the map's own origin rather than a general <see cref="Empire"/> — kept separate rather than
    /// shared, since that origin also falls back to the galaxy's center with no capital, a UI-only
    /// concern this query doesn't need.
    /// </summary>
    private static string CoordinateName(Game game, Empire viewer, Galaxy.Coordinate location)
    {
        var origin = viewer.Capital?.Location ?? new Galaxy.Coordinate(game.Galaxy.Size / 2, game.Galaxy.Size / 2);
        return $"{location.X - origin.X},{origin.Y - location.Y}";
    }

    /// <summary>
    /// <c>ObjectName</c>'s fallback when nothing has named this object — mirrors the TUI's own
    /// <c>CloseUpWindow.DescribeKind</c>/<c>GameShell.ObjectListItem</c> convention, kept as a
    /// separate small copy here rather than shared (Core can't reference the Tui project, and
    /// <c>[N:id]</c> is never actually exercised by any committed scenario — confirmed, no real .SCN
    /// uses it — so this exists for file-format completeness, not to serve real content).
    /// </summary>
    private static string DescribeBackgroundKind(ISectorObject obj) => obj switch {
        Planet p => p.Type.ToString(),
        Starbase s => s.Kind.ToString(),
        Stargate g => g.Kind.ToString(),
        ConstructionSite c => $"{c.Building} site",
        Fleet => "Fleet",
        _ => "Unknown",
    };

    /// <summary>"type:index" (SCENA.PAS's own colon-packed token) split into its two integers, or false if it isn't one.</summary>
    internal static bool TryParseTypeIndex(string text, out int objType, out int index)
    {
        var colon = text.IndexOf(':');
        if (colon >= 0 && int.TryParse(text[..colon], out objType) && int.TryParse(text[(colon + 1)..], out index))
            return true;

        (objType, index) = (0, 0);
        return false;
    }

    /// <summary>
    /// IDMatch (SCENA.PAS): a "type:index" reference (ObjectTypes ordinals — Pln=2, Base=3) to the
    /// real object <see cref="NewGame.ScenarioLoader"/> created at that 1-based Pascal index, or null
    /// for any other type or an out-of-range index. Only Pln/Base are supported — confirmed the only
    /// two types any real WorldBackgroundIndex row or <c>[C:id]</c>/<c>[N:id]</c> marker ever
    /// references, and the only two the TUI's own CloseUpWindow lays out background text for anyway.
    /// </summary>
    internal static ISectorObject? ResolveWorldReference(Galaxy.Galaxy galaxy, int objType, int index) => objType switch {
        2 when index >= 1 && index <= galaxy.Planets.Count => galaxy.Planets[index - 1],
        3 when index >= 1 && index <= galaxy.Starbases.Count => galaxy.Starbases[index - 1],
        _ => null,
    };
}
