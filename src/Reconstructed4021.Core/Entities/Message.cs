namespace Reconstructed4021.Core.Entities;

/// <summary>
/// `MessageRecord` (`MESS.PAS:23-33`) -- a player-to-player in-game message. `ReadBy` is
/// deliberately absent: real Pascal's own `LoadMessageData` never reads it back from disk
/// (`MESS.PAS:288-291` copies `Sender`/`Recipient`/`Read`/`Intercepted` from the loaded record but
/// never assigns `ReadBy`), so a message's per-recipient read-tracking bitset is genuine, confirmed
/// -in-source Pascal data loss across every real save/load boundary -- not a gap in this port to
/// fix, a real Pascal quirk to reproduce by not inventing state real Pascal itself never restores.
/// This has a real downstream consequence in the original game, not just a cosmetic gap: `ReadBy`
/// comes back as uninitialized heap garbage after a load, and `SetMessageRead`'s own completion
/// check (`ReadBy:=ReadBy+[Emp]; IF Recipient&lt;=ReadBy THEN Read:=True`) then operates on that
/// garbage the moment any recipient reads any message post-load -- `Read` itself round-trips
/// correctly as of the instant a file is loaded (it's a plain field, not derived), it's only later,
/// in-session `SetMessageRead` calls that can spuriously flip it. This port models `Read` as of
/// load time, same as real Pascal's own on-disk value.
/// </summary>
public sealed record Message(
    Empire Sender,
    IReadOnlySet<Empire> Recipients,
    bool Read,
    bool Intercepted,
    IReadOnlyList<string> Lines);
