using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.SaveFormat;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.LegacyNpe;

/// <summary>
/// This assembly's single <see cref="INpeHandlerProvider"/> — the composition root (this port's Tui/
/// top-level exe, and <c>Reconstructed4021.Tests</c>) constructs one instance and threads it through
/// <c>ScenarioLoader</c>/<c>GameJson</c>/<c>SavGameLoader</c>/<c>SavGameWriter</c>. Understands
/// Kingdom1/Kingdom2 only, matching <see cref="KingdomTurnHandler"/> being this assembly's one real
/// personality so far — see docs/ROADMAP.md's own disposition notes for Pirate/Berserker/Guardian/
/// Trader.
/// </summary>
public sealed class LegacyNpeProvider : INpeHandlerProvider
{
    /// <summary>For pure-value sub-objects with no entity references at all (<see cref="NpeCharacter"/>, <see cref="StateDeptRecord"/>) — safe to share statically since it carries no per-call state. Deliberately a separate instance from <c>GameJson</c>'s own like-named options rather than a shared Core constant: three lines, not worth a cross-assembly dependency to save typing.</summary>
    private static readonly JsonSerializerOptions PlainOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public bool Handles(NpeEmpireType type) => type is NpeEmpireType.Kingdom1 or NpeEmpireType.Kingdom2;

    public ITurnHandler CreateNew(Empire empire, NpeEmpireType type, Random random) =>
        new KingdomTurnHandler(empire, type, random);

    public IEnumerable<Empire> ReferencedEmpires(ITurnHandler handler) =>
        handler is KingdomTurnHandler kingdom ? kingdom.State.Keys : [];

    // ---- Native JSON save format ----

    public JsonNode CaptureJson(ITurnHandler handler, IJsonRefWriter refs)
    {
        var kingdom = (KingdomTurnHandler)handler;
        return new JsonObject {
            ["persona"] = JsonSerializer.SerializeToNode(kingdom.Persona, PlainOptions),
            ["state"] = WriteState(kingdom.State, refs),
            ["fleetStates"] = WriteFleetStates(kingdom.FleetStates, refs),
        };
    }

    private static JsonArray WriteState(IReadOnlyDictionary<Empire, StateDeptRecord> state, IJsonRefWriter refs)
    {
        var array = new JsonArray();

        foreach (var (empire, record) in state) {
            array.Add(new JsonObject {
                ["empireId"] = refs.EmpireId(empire),
                ["record"] = JsonSerializer.SerializeToNode(record, PlainOptions),
            });
        }

        return array;
    }

    // A KingdomTurnHandler's own FleetStates can genuinely hold a key for a fleet that's no longer in
    // Game.Galaxy.Fleets: NpeToolkit.EnforceNpeDataLinks (its own real pruning, NPEINTR.PAS's
    // EnforceNPEDataLinks) only runs at the start of that empire's own PlayTurn, so a Kingdom fleet
    // destroyed mid-turn by the human (combat, most directly) leaves a dangling entry until that
    // Kingdom's own next turn -- a real, live window, not a corrupted state, and GameJson.Serialize can
    // be called (via Save Game) at any point during the human's own turn, well inside it. Confirmed via
    // a real crash (KeyNotFoundException) hit live, not a hypothetical. Skipping a stale entry here
    // matches what EnforceNpeDataLinks would remove anyway on that empire's own next turn -- there's
    // nothing meaningful left to persist for a fleet that's already gone.
    private static JsonArray WriteFleetStates(IReadOnlyDictionary<Fleet, KingdomFleetState> fleetStates, IJsonRefWriter refs)
    {
        var array = new JsonArray();

        foreach (var (fleet, state) in fleetStates) {
            if (refs.FleetId(fleet) is not { } fleetId) {
                continue;
            }

            array.Add(new JsonObject {
                ["fleetId"] = fleetId,
                ["mission"] = state.Mission.ToString(),
                ["target"] = refs.EncodeObjectRef(state.Target),
                ["homeBase"] = refs.EncodeObjectRef(state.HomeBase),
                ["waiting"] = state.Waiting,
            });
        }

        return array;
    }

    public ITurnHandler RestoreJson(Empire empire, NpeEmpireType type, JsonNode data, IJsonRefReader refs, Random random)
    {
        var obj = data.AsObject();
        var persona = obj["persona"].Deserialize<NpeCharacter>(PlainOptions)!;
        var state = ReadState(obj["state"]!.AsArray(), refs);
        var fleetStates = ReadFleetStates(obj["fleetStates"]!.AsArray(), refs);

        return new KingdomTurnHandler(type, persona, state, fleetStates, random);
    }

    private static Dictionary<Empire, StateDeptRecord> ReadState(JsonArray array, IJsonRefReader refs)
    {
        var state = new Dictionary<Empire, StateDeptRecord>();

        foreach (var entryNode in array) {
            var entry = entryNode!.AsObject();
            var empire = refs.Empire((int)entry["empireId"]!);
            state[empire] = entry["record"].Deserialize<StateDeptRecord>(PlainOptions)!;
        }

        return state;
    }

    private static Dictionary<Fleet, KingdomFleetState> ReadFleetStates(JsonArray array, IJsonRefReader refs)
    {
        var fleetStates = new Dictionary<Fleet, KingdomFleetState>();

        foreach (var entryNode in array) {
            var entry = entryNode!.AsObject();
            var fleet = refs.Fleet((int)entry["fleetId"]!);

            fleetStates[fleet] = new KingdomFleetState {
                Mission = Enum.Parse<NpeMissionType>((string)entry["mission"]!),
                Target = refs.DecodeObjectRef(entry["target"]),
                HomeBase = refs.DecodeObjectRef(entry["homeBase"]) as IEconomicWorld,
                Waiting = (int)entry["waiting"]!,
            };
        }

        return fleetStates;
    }

    // ---- `.SAV` NPE Data blob ----

    /// <summary>
    /// `Kingdom1DataRecord`/`Kingdom2DataRecord` (`NPETYPES.PAS:136-141`), inverse of <see cref="ReadSav"/>.
    /// `Midway`/`BlockX`/`BlockY` are confirmed dead/Pirate-only (<see cref="KingdomFleetState"/>'s own
    /// doc comment) -- always written zero. Only the fleets this Kingdom empire actually has AI state
    /// for get a real slot (`Index` byte = the fleet's own on-disk index); every other one of the 30
    /// fixed slots is all-zero (`Index=0` reads back as "unused", <see cref="ReadSav"/>'s own loop).
    /// `State` always emits exactly the 9 canonical ordinals (`Empire1..Empire8` then `Indep`)
    /// regardless of which ones this Kingdom's own dictionary happens to hold real data for -- matching
    /// real Pascal's own fixed-size array; a slot with no dictionary entry (never yet encountered as an
    /// enemy) writes an all-zero <see cref="StateDeptRecord"/>, which decodes as `PolicyType.None`/
    /// all-zero fields on the next load, not a crash or a wrong empire's data.
    /// </summary>
    public void WriteSav(SavWriter writer, ITurnHandler handler, ISavObjectIds objectIds, ISavEmpireSlots slots)
    {
        var kingdom = (KingdomTurnHandler)handler;

        var fleetSlots = new (int Index, KingdomFleetState State)[30];
        var next = 0;
        foreach (var (fleet, state) in kingdom.FleetStates) {
            if (next >= fleetSlots.Length) {
                throw new NotSupportedException("A Kingdom empire's own 30-fleet NPE-data slot table is full.");
            }
            fleetSlots[next] = (objectIds.IdOf(fleet).Index, state);
            next++;
        }

        foreach (var (index, state) in fleetSlots) {
            if (index == 0) {
                writer.WriteZeros(11);
                continue;
            }

            writer.WriteByte((byte)state.Mission);
            writer.WriteIdNumber(state.Target switch {
                null => new SavIdNumber(SavObjectType.Void, 0),
                ISectorObject sectorObject => objectIds.IdOf(sectorObject),
                _ => throw new NotSupportedException($"Unexpected KingdomFleetState.Target type {state.Target.GetType()}."),
            });
            writer.WriteIdNumber(state.HomeBase is { } homeBase ? objectIds.IdOf((ISectorObject)homeBase) : new SavIdNumber(SavObjectType.Void, 0));
            writer.WriteIdNumber(new SavIdNumber(SavObjectType.Void, 0)); // Midway
            writer.WriteByte((byte)state.Waiting);
            writer.WriteByte(0); // BlockX
            writer.WriteByte(0); // BlockY
            writer.WriteByte((byte)index);
        }

        for (var i = 0; i < 9; i++) {
            // Real Pascal's own State array is keyed by raw ordinal position regardless of whether
            // that ordinal is InUse (InitializeKingdom1NPE/2NPE seed all 8 unconditionally) -- an
            // empty slot (never assigned any empire, real or orphan) still gets an entry, just an
            // all-zero one, since there's no Empire object in this port's model to look one up by.
            var target = i == 8 ? Empire.Independent : slots.AnyEmpireAt(i);

            if (target is null || !kingdom.State.TryGetValue(target, out var record)) {
                writer.WriteZeros(12);
                continue;
            }

            writer.WriteByte((byte)record.Policy);
            writer.WriteByte((byte)record.AttackChance);
            writer.WriteLongInt((int)record.TotalMilitary);
            writer.WriteWord((ushort)record.Worlds);
            writer.WriteByte((byte)record.ThreatAssess);
            writer.WriteByte((byte)record.Aggressiveness);
            writer.WriteInteger((short)record.Balance);
        }

        var persona = kingdom.Persona;
        writer.WriteByte((byte)persona.ImperialistGene);
        writer.WriteByte((byte)persona.DefensiveGene);
        writer.WriteByte((byte)persona.OffensiveGene);
        writer.WriteByte((byte)persona.FactorGene);
        writer.WriteByte((byte)persona.RandomGene);
        writer.WriteByte((byte)persona.Defensive);
        writer.WriteByte((byte)persona.Offensive);
        writer.WriteByte((byte)persona.Techno);
        writer.WriteByte((byte)persona.Provoke);
        writer.WriteByte((byte)persona.Imperialist);
        writer.WriteByte((byte)persona.WorldPower);
        writer.WriteByte((byte)persona.Honorable);
        writer.WriteByte((byte)persona.SphereX);
        writer.WriteWord((ushort)persona.Clock);
        writer.WriteByte((byte)persona.Offset);
    }

    /// <summary>
    /// `Kingdom1DataRecord`/`Kingdom2DataRecord` (`NPETYPES.PAS:136-141`, 454 bytes): `FleetData`
    /// (30 × `FleetDataRecord`, 11 bytes each), `State` (9 × `StateDeptRecord`, 12 bytes each,
    /// `Empire1..Empire8` then `Indep`), `Persona` (`NPECharacterRecord`, 16 bytes) — field order
    /// confirmed directly against `NPETYPES.PAS`, not assumed from the format doc's summary alone.
    /// A `FleetDataRecord` with `Index=0` is an unused slot (Kingdom tracks at most 30 fleets at
    /// once, not one slot per real fleet) — skipped, same for a nonzero `Index` that doesn't
    /// resolve to a real fleet (shouldn't happen for valid data). `Midway`/`BlockX`/`BlockY` are
    /// confirmed dead/Pirate-only per <see cref="KingdomFleetState"/>'s own doc comment — discarded.
    /// </summary>
    public ITurnHandler ReadSav(SavReader reader, Empire empire, NpeEmpireType type, ISavRefResolver resolver, Random random)
    {
        var fleetStates = new Dictionary<Fleet, KingdomFleetState>();

        for (var i = 0; i < 30; i++) {
            var mission = (NpeMissionType)reader.ReadByte();
            var targetId = reader.ReadIdNumber();
            var homeBaseId = reader.ReadIdNumber();
            reader.ReadIdNumber(); // Midway -- confirmed dead
            var waiting = reader.ReadByte();
            reader.Skip(1); // BlockX -- Pirate-only
            reader.Skip(1); // BlockY -- Pirate-only
            var index = reader.ReadByte();

            if (index == 0) {
                continue;
            }

            if (resolver.ResolveObject(new SavIdNumber(SavObjectType.Flt, index)) is not Fleet fleet) {
                continue;
            }

            fleetStates[fleet] = new KingdomFleetState {
                Mission = mission,
                Target = resolver.ResolveObject(targetId),
                HomeBase = resolver.ResolveObject(homeBaseId) as IEconomicWorld,
                Waiting = waiting,
            };
        }

        var state = new Dictionary<Empire, StateDeptRecord>();
        for (var i = 0; i < 9; i++) {
            var policy = (PolicyType)reader.ReadByte();
            var attackChance = reader.ReadByte();
            var totalMilitary = reader.ReadLongInt();
            var worlds = reader.ReadWord();
            var threatAssess = reader.ReadByte();
            var aggressiveness = reader.ReadByte();
            var balance = reader.ReadInteger();

            state[resolver.ResolveEmpire(i)] = new StateDeptRecord {
                Policy = policy,
                AttackChance = attackChance,
                TotalMilitary = totalMilitary,
                Worlds = worlds,
                ThreatAssess = threatAssess,
                Aggressiveness = aggressiveness,
                Balance = balance,
            };
        }

        var persona = new NpeCharacter {
            ImperialistGene = reader.ReadByte(),
            DefensiveGene = reader.ReadByte(),
            OffensiveGene = reader.ReadByte(),
            FactorGene = reader.ReadByte(),
            RandomGene = reader.ReadByte(),
            Defensive = reader.ReadByte(),
            Offensive = reader.ReadByte(),
            Techno = reader.ReadByte(),
            Provoke = reader.ReadByte(),
            Imperialist = reader.ReadByte(),
            WorldPower = reader.ReadByte(),
            Honorable = reader.ReadByte(),
            SphereX = reader.ReadByte(),
            Clock = reader.ReadWord(),
            Offset = reader.ReadByte(),
        };

        return new KingdomTurnHandler(type, persona, state, fleetStates, random);
    }
}
