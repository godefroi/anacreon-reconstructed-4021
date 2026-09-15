namespace Reconstructed4021.Core.Entities;

/// <summary>
/// `MessageRecord` (`MESS.PAS:23-33`) -- a player-to-player in-game message. `Read` is the one field
/// real Pascal actually persists (`MESS.PAS:288-291`'s `LoadMessageData` copies
/// `Sender`/`Recipient`/`Read`/`Intercepted` from the loaded record) -- it's true once every empire in
/// `Recipients` has read the message (`SetMessageRead`'s own `IF Recipient&lt;=ReadBy THEN Read:=True`),
/// so <see cref="Turns.AnnualTickHandler"/>'s end-of-year `DeleteReadMessages` never removes a message
/// a later multi-recipient reader hasn't gotten to yet.
///
/// `ReadBy` (the live per-recipient tracking set that `Read` is computed from) is deliberately
/// runtime-only, not a positional/serialized member: real Pascal's own `LoadMessageData` never reads
/// it back from disk at all (only `Read` itself round-trips), so on-disk `ReadBy` state is discarded at
/// every real save/load boundary -- a confirmed-in-source Pascal quirk, not a gap in this port to fix.
/// A freshly loaded message therefore starts with an empty `ReadBy` regardless of its `Read` value,
/// same as real Pascal's own post-load garbage-`ReadBy`-but-correct-`Read` split, just without the
/// "garbage" part: nothing here reads `ReadBy` before some recipient in the new session actually opens
/// the message, so an empty set is observably identical to real Pascal's own uninitialized one for
/// every case that matters (a message already `Read=true` from a prior session never needs its `ReadBy`
/// consulted again).
/// </summary>
public sealed record Message(
    Empire Sender,
    IReadOnlySet<Empire> Recipients,
    bool Read,
    bool Intercepted,
    IReadOnlyList<string> Lines)
{
    public HashSet<Empire> ReadBy { get; } = [];
}
