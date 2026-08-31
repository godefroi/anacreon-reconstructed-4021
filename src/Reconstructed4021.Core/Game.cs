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
}
