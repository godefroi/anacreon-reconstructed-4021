using System.Text.Json.Nodes;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.SaveFormat;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Turns;

/// <summary>
/// The construction/persistence seam a computer-controlled NPE AI implementation plugs into Core
/// through, so Core never names a concrete personality type (<c>KingdomTurnHandler</c> and friends
/// live in the separate <c>Reconstructed4021.LegacyNpe</c> assembly, which references this one, not
/// the other way around — see docs/PORT_DESIGN.md). One provider instance is expected to answer for
/// every <see cref="NpeEmpireType"/> it recognizes; Core treats "no provider was supplied" and
/// "the supplied provider doesn't recognize this type" identically — the empire gets no
/// <see cref="ITurnHandler"/> at all (matching this port's existing "ai has no entry in
/// TurnHandlers" behavior for a personality nothing has been built for yet), or for `.SAV`/JSON
/// data specifically, an opaque, round-tripped-but-unplayable blob
/// (<see cref="Game.UnimplementedNpeBlobs"/>).
/// </summary>
public interface INpeHandlerProvider
{
    bool Handles(NpeEmpireType type);

    /// <summary>
    /// A brand-new empire's persona/state, seeded from the same RNG stream driving the rest of
    /// scenario load — <see cref="NewGame.ScenarioLoader"/>'s own InitializeNPE call
    /// (NEWGAME.PAS:1250, right after CreateEmpire).
    /// </summary>
    ITurnHandler CreateNew(Empire empire, NpeEmpireType type, Random random);

    /// <summary>The native-JSON save format's inverse pair — <paramref name="handler"/> is always one this provider itself created (<see cref="Handles"/> already gated the call).</summary>
    JsonNode CaptureJson(ITurnHandler handler, IJsonRefWriter refs);

    ITurnHandler RestoreJson(Empire empire, NpeEmpireType type, JsonNode data, IJsonRefReader refs, Random random);

    /// <summary>The `.SAV` NPE Data blob's inverse pair, for the personalities this provider understands well enough to read/write structured fields rather than opaque bytes.</summary>
    void WriteSav(SavWriter writer, ITurnHandler handler, ISavObjectIds objectIds, ISavEmpireSlots slots);

    ITurnHandler ReadSav(SavReader reader, Empire empire, NpeEmpireType type, ISavRefResolver resolver, Random random);

    /// <summary>
    /// Every empire <paramref name="handler"/>'s own private state references internally (e.g. a
    /// per-enemy diplomatic-memory dictionary keyed by empire) — including one that's otherwise
    /// unreachable from <see cref="Game.Empires"/>, a real "orphan" case (see
    /// <see cref="SaveFormat.SavGameWriter"/>'s own remarks on why). Used only for `.SAV`'s
    /// up-front empire-slot allocation, which can't discover orphans lazily the way the JSON format's
    /// own <see cref="IJsonRefWriter.EmpireId"/> does; returns empty for any <paramref name="handler"/>
    /// this provider didn't itself create.
    /// </summary>
    IEnumerable<Empire> ReferencedEmpires(ITurnHandler handler);
}
