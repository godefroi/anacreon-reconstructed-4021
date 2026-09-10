using System.Text.Json.Nodes;
using Reconstructed4021.Core.Entities;

namespace Reconstructed4021.Core.SaveFormat;

/// <summary>
/// The narrow slice of <c>GameJson</c>'s own entity-id bookkeeping an <see cref="Turns.INpeHandlerProvider"/>
/// needs to write cross-references (a mission's <c>Target</c>/<c>HomeBase</c>, a per-enemy state
/// dictionary's key) without depending on <c>GameJson</c>'s private <c>EntityIndex</c> itself.
/// </summary>
public interface IJsonRefWriter
{
    int EmpireId(Empire empire);

    /// <summary>Null if <paramref name="fleet"/> is no longer in <see cref="Galaxy.Galaxy.Fleets"/> — a real, live window (see <see cref="Turns.INpeHandlerProvider"/> implementors' own remarks); callers should skip that entry rather than write a dangling id.</summary>
    int? FleetId(Fleet fleet);

    /// <summary>Encodes a planet/starbase/fleet/stargate/construction-site reference, or null for a null/no-longer-live target.</summary>
    JsonNode? EncodeObjectRef(object? target);
}

/// <summary>Read-side inverse of <see cref="IJsonRefWriter"/>.</summary>
public interface IJsonRefReader
{
    Empire Empire(int id);
    Fleet Fleet(int id);
    object? DecodeObjectRef(JsonNode? node);
}
