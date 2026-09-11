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

    public bool Handles(NpeEmpireType type) => type is NpeEmpireType.Kingdom1 or NpeEmpireType.Kingdom2 or NpeEmpireType.Pirate or NpeEmpireType.Berserker;

    public ITurnHandler CreateNew(Empire empire, NpeEmpireType type, Random random) => type switch {
        NpeEmpireType.Pirate => new PirateTurnHandler(empire, random),
        NpeEmpireType.Berserker => new BerserkerTurnHandler(empire, random),
        _ => new KingdomTurnHandler(empire, type, random),
    };

    /// <summary>Pirate/Berserker's own AI state never references another empire directly (Target/HomeBase are always a Fleet, Starbase, or world, never an Empire) — no orphan-empire slots to reserve for either.</summary>
    public IEnumerable<Empire> ReferencedEmpires(ITurnHandler handler) =>
        handler is KingdomTurnHandler kingdom ? kingdom.State.Keys : [];

    // ---- Native JSON save format ----

    public JsonNode CaptureJson(ITurnHandler handler, IJsonRefWriter refs)
    {
        if (handler is PirateTurnHandler pirate) {
            return CapturePirateJson(pirate, refs);
        }
        if (handler is BerserkerTurnHandler berserker) {
            return CaptureBerserkerJson(berserker, refs);
        }

        var kingdom = (KingdomTurnHandler)handler;
        return new JsonObject {
            ["persona"] = JsonSerializer.SerializeToNode(kingdom.Persona, PlainOptions),
            ["state"] = WriteState(kingdom.State, refs),
            ["fleetStates"] = WriteFleetStates(kingdom.FleetStates, refs),
        };
    }

    private static JsonObject CapturePirateJson(PirateTurnHandler pirate, IJsonRefWriter refs)
    {
        var fleetStates = new JsonArray();
        foreach (var (fleet, state) in pirate.FleetStates) {
            if (refs.FleetId(fleet) is not { } fleetId) {
                continue;
            }

            fleetStates.Add(new JsonObject {
                ["fleetId"] = fleetId,
                ["mission"] = state.Mission.ToString(),
                ["target"] = refs.EncodeObjectRef(state.Target),
                ["waiting"] = state.Waiting,
                ["blockX"] = state.BlockX,
                ["blockY"] = state.BlockY,
            });
        }

        var huntingGround = new byte[400];
        for (var x = 0; x < 20; x++) {
            for (var y = 0; y < 20; y++) {
                huntingGround[x * 20 + y] = pirate.HuntingGround[x, y];
            }
        }

        return new JsonObject {
            ["fleetStates"] = fleetStates,
            ["huntingGround"] = Convert.ToBase64String(huntingGround),
            ["sheep"] = Convert.ToBase64String(pirate.Sheep),
        };
    }

    private static JsonObject CaptureBerserkerJson(BerserkerTurnHandler berserker, IJsonRefWriter refs)
    {
        var fleetStates = new JsonArray();
        foreach (var (fleet, state) in berserker.FleetStates) {
            if (refs.FleetId(fleet) is not { } fleetId) {
                continue;
            }

            fleetStates.Add(new JsonObject {
                ["fleetId"] = fleetId,
                ["mission"] = state.Mission.ToString(),
                ["target"] = refs.EncodeObjectRef(state.Target),
                ["homeBase"] = refs.EncodeObjectRef(state.HomeBase),
            });
        }

        var baseStates = new JsonArray();
        foreach (var (starbase, state) in berserker.BaseStates) {
            baseStates.Add(new JsonObject {
                ["baseRef"] = refs.EncodeObjectRef(starbase),
                ["mission"] = state.Mission.ToString(),
                ["target"] = refs.EncodeObjectRef(state.Target),
                ["count"] = state.Count,
            });
        }

        return new JsonObject {
            ["fleetStates"] = fleetStates,
            ["baseStates"] = baseStates,
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

        if (type == NpeEmpireType.Pirate) {
            return RestorePirateJson(obj, refs, random);
        }
        if (type == NpeEmpireType.Berserker) {
            return RestoreBerserkerJson(obj, refs, random);
        }

        var persona = obj["persona"].Deserialize<NpeCharacter>(PlainOptions)!;
        var state = ReadState(obj["state"]!.AsArray(), refs);
        var fleetStates = ReadFleetStates(obj["fleetStates"]!.AsArray(), refs);

        return new KingdomTurnHandler(type, persona, state, fleetStates, random);
    }

    private static PirateTurnHandler RestorePirateJson(JsonObject obj, IJsonRefReader refs, Random random)
    {
        var fleetStates = new Dictionary<Fleet, PirateFleetState>();
        foreach (var entryNode in obj["fleetStates"]!.AsArray()) {
            var entry = entryNode!.AsObject();
            var fleet = refs.Fleet((int)entry["fleetId"]!);

            fleetStates[fleet] = new PirateFleetState {
                Mission = Enum.Parse<NpeMissionType>((string)entry["mission"]!),
                Target = refs.DecodeObjectRef(entry["target"]),
                Waiting = (int)entry["waiting"]!,
                BlockX = (int)entry["blockX"]!,
                BlockY = (int)entry["blockY"]!,
            };
        }

        var huntingGroundBytes = Convert.FromBase64String((string)obj["huntingGround"]!);
        var huntingGround = new byte[20, 20];
        for (var x = 0; x < 20; x++) {
            for (var y = 0; y < 20; y++) {
                huntingGround[x, y] = huntingGroundBytes[x * 20 + y];
            }
        }

        var sheep = Convert.FromBase64String((string)obj["sheep"]!);

        return new PirateTurnHandler(fleetStates, huntingGround, sheep, random);
    }

    private static BerserkerTurnHandler RestoreBerserkerJson(JsonObject obj, IJsonRefReader refs, Random random)
    {
        var fleetStates = new Dictionary<Fleet, BerserkerFleetState>();
        foreach (var entryNode in obj["fleetStates"]!.AsArray()) {
            var entry = entryNode!.AsObject();
            var fleet = refs.Fleet((int)entry["fleetId"]!);

            fleetStates[fleet] = new BerserkerFleetState {
                Mission = Enum.Parse<NpeMissionType>((string)entry["mission"]!),
                Target = refs.DecodeObjectRef(entry["target"]) as IEconomicWorld,
                HomeBase = refs.DecodeObjectRef(entry["homeBase"]) as IEconomicWorld,
            };
        }

        var baseStates = new Dictionary<Starbase, BerserkerBaseState>();
        foreach (var entryNode in obj["baseStates"]!.AsArray()) {
            var entry = entryNode!.AsObject();
            if (refs.DecodeObjectRef(entry["baseRef"]) is not Starbase starbase) {
                continue;
            }

            baseStates[starbase] = new BerserkerBaseState {
                Mission = Enum.Parse<BaseMissionType>((string)entry["mission"]!),
                Target = refs.DecodeObjectRef(entry["target"]) as IEconomicWorld,
                Count = (int)entry["count"]!,
            };
        }

        return new BerserkerTurnHandler(fleetStates, baseStates, random);
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
        if (handler is PirateTurnHandler pirate) {
            WritePirateSav(writer, pirate, objectIds);
            return;
        }
        if (handler is BerserkerTurnHandler berserker) {
            WriteBerserkerSav(writer, berserker, objectIds);
            return;
        }

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
    /// PirateDataRecord (`NPETYPES.PAS:127-132`, 739 bytes): `FleetData` (30 x 11), `HuntingGround`
    /// (400, row-major x then y matching Pascal's `ARRAY[1..20,1..20] OF Byte` layout), `Sheep` (9,
    /// dead — see <see cref="PirateTurnHandler.Sheep"/>'s own doc comment). `HomeBaseID`/`Midway` are
    /// dead for Pirate (unlike Kingdom, where `HomeBaseID` is real) — always written as Void, matching
    /// <see cref="PirateFleetState"/>'s own doc comment.
    /// </summary>
    private static void WritePirateSav(SavWriter writer, PirateTurnHandler pirate, ISavObjectIds objectIds)
    {
        var fleetSlots = new (int Index, PirateFleetState State)[30];
        var next = 0;
        foreach (var (fleet, state) in pirate.FleetStates) {
            if (next >= fleetSlots.Length) {
                throw new NotSupportedException("A Pirate empire's own 30-fleet NPE-data slot table is full.");
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
                _ => throw new NotSupportedException($"Unexpected PirateFleetState.Target type {state.Target.GetType()}."),
            });
            writer.WriteIdNumber(new SavIdNumber(SavObjectType.Void, 0)); // HomeBaseID -- dead for Pirate
            writer.WriteIdNumber(new SavIdNumber(SavObjectType.Void, 0)); // Midway -- dead
            writer.WriteByte((byte)state.Waiting);
            writer.WriteByte((byte)state.BlockX);
            writer.WriteByte((byte)state.BlockY);
            writer.WriteByte((byte)index);
        }

        for (var x = 0; x < 20; x++) {
            for (var y = 0; y < 20; y++) {
                writer.WriteByte(pirate.HuntingGround[x, y]);
            }
        }

        writer.WriteBytes(pirate.Sheep);
    }

    /// <summary>
    /// BerserkerDataRecord (`NPETYPES.PAS:145-150`, 930 bytes): `FleetData` (30 × 11, same
    /// `FleetDataRecord` shape as Kingdom/Pirate — unlike Pirate, `HomeBaseID` is real here, matching
    /// <see cref="BerserkerFleetState"/>'s own doc comment), then `BaseData`. Unlike `FleetData`,
    /// `BaseData` (`ARRAY[1..MaxNoOfStarbases] OF BaseDataRecord` — `NPETYPES.PAS:98`) has no
    /// compaction/`Index` field of its own: `MaxNoOfStarbases` (100) is the whole galaxy's total
    /// starbase-id space, so `BaseData[i]` is directly the on-disk starbase #`i`'s own slot, real
    /// Pascal indexing it as `BaseData[BaseID.Index]` everywhere rather than scanning for a used
    /// slot the way `FleetData`'s 30-slot cap forces. `BaseDataRecord` itself is 5 bytes: `Mission`
    /// byte, `TargetID` `IDNumber` (2 bytes), `Count` `Word` (2 bytes). A dead 50-word `Spare` block
    /// (100 bytes) follows both, written as zero.
    /// </summary>
    private static void WriteBerserkerSav(SavWriter writer, BerserkerTurnHandler berserker, ISavObjectIds objectIds)
    {
        var fleetSlots = new (int Index, BerserkerFleetState State)[30];
        var next = 0;
        foreach (var (fleet, state) in berserker.FleetStates) {
            if (next >= fleetSlots.Length) {
                throw new NotSupportedException("A Berserker empire's own 30-fleet NPE-data slot table is full.");
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
            writer.WriteIdNumber(state.Target is { } target ? objectIds.IdOf(target) : new SavIdNumber(SavObjectType.Void, 0));
            writer.WriteIdNumber(state.HomeBase is { } homeBase ? objectIds.IdOf(homeBase) : new SavIdNumber(SavObjectType.Void, 0));
            writer.WriteIdNumber(new SavIdNumber(SavObjectType.Void, 0)); // Midway -- confirmed dead
            writer.WriteByte(0); // Waiting -- confirmed dead for Berserker
            writer.WriteByte(0); // BlockX -- Pirate-only
            writer.WriteByte(0); // BlockY -- Pirate-only
            writer.WriteByte((byte)index);
        }

        var baseByIndex = new Dictionary<int, BerserkerBaseState>();
        foreach (var (starbase, state) in berserker.BaseStates) {
            baseByIndex[objectIds.IdOf(starbase).Index] = state;
        }

        for (var i = 1; i <= 100; i++) {
            if (!baseByIndex.TryGetValue(i, out var state)) {
                writer.WriteZeros(5);
                continue;
            }

            writer.WriteByte((byte)state.Mission);
            writer.WriteIdNumber(state.Target is { } target ? objectIds.IdOf(target) : new SavIdNumber(SavObjectType.Void, 0));
            writer.WriteWord((ushort)state.Count);
        }

        writer.WriteZeros(100); // Spare[1..50]: Word -- dead
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
        if (type == NpeEmpireType.Pirate) {
            return ReadPirateSav(reader, resolver, random);
        }
        if (type == NpeEmpireType.Berserker) {
            return ReadBerserkerSav(reader, resolver, random);
        }

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

    /// <summary>See <see cref="WritePirateSav"/> for the byte layout this mirrors.</summary>
    private static PirateTurnHandler ReadPirateSav(SavReader reader, ISavRefResolver resolver, Random random)
    {
        var fleetStates = new Dictionary<Fleet, PirateFleetState>();

        for (var i = 0; i < 30; i++) {
            var mission = (NpeMissionType)reader.ReadByte();
            var targetId = reader.ReadIdNumber();
            reader.ReadIdNumber(); // HomeBaseID -- dead for Pirate
            reader.ReadIdNumber(); // Midway -- dead
            var waiting = reader.ReadByte();
            var blockX = reader.ReadByte();
            var blockY = reader.ReadByte();
            var index = reader.ReadByte();

            if (index == 0) {
                continue;
            }

            if (resolver.ResolveObject(new SavIdNumber(SavObjectType.Flt, index)) is not Fleet fleet) {
                continue;
            }

            fleetStates[fleet] = new PirateFleetState {
                Mission = mission,
                Target = resolver.ResolveObject(targetId),
                Waiting = waiting,
                BlockX = blockX,
                BlockY = blockY,
            };
        }

        var huntingGround = new byte[20, 20];
        for (var x = 0; x < 20; x++) {
            for (var y = 0; y < 20; y++) {
                huntingGround[x, y] = reader.ReadByte();
            }
        }

        var sheep = reader.ReadBytes(9);

        return new PirateTurnHandler(fleetStates, huntingGround, sheep, random);
    }

    /// <summary>See <see cref="WriteBerserkerSav"/> for the byte layout this mirrors.</summary>
    private static BerserkerTurnHandler ReadBerserkerSav(SavReader reader, ISavRefResolver resolver, Random random)
    {
        var fleetStates = new Dictionary<Fleet, BerserkerFleetState>();

        for (var i = 0; i < 30; i++) {
            var mission = (NpeMissionType)reader.ReadByte();
            var targetId = reader.ReadIdNumber();
            var homeBaseId = reader.ReadIdNumber();
            reader.ReadIdNumber(); // Midway -- confirmed dead
            reader.Skip(1); // Waiting -- confirmed dead for Berserker
            reader.Skip(1); // BlockX -- Pirate-only
            reader.Skip(1); // BlockY -- Pirate-only
            var index = reader.ReadByte();

            if (index == 0) {
                continue;
            }

            if (resolver.ResolveObject(new SavIdNumber(SavObjectType.Flt, index)) is not Fleet fleet) {
                continue;
            }

            fleetStates[fleet] = new BerserkerFleetState {
                Mission = mission,
                Target = resolver.ResolveObject(targetId) as IEconomicWorld,
                HomeBase = resolver.ResolveObject(homeBaseId) as IEconomicWorld,
            };
        }

        var baseStates = new Dictionary<Starbase, BerserkerBaseState>();

        for (var i = 1; i <= 100; i++) {
            var mission = (BaseMissionType)reader.ReadByte();
            var targetId = reader.ReadIdNumber();
            var count = reader.ReadWord();

            if (mission == BaseMissionType.None) {
                continue;
            }
            if (resolver.ResolveObject(new SavIdNumber(SavObjectType.Base, (byte)i)) is not Starbase starbase) {
                continue;
            }

            baseStates[starbase] = new BerserkerBaseState {
                Mission = mission,
                Target = resolver.ResolveObject(targetId) as IEconomicWorld,
                Count = count,
            };
        }

        reader.Skip(100); // Spare[1..50]: Word -- dead

        return new BerserkerTurnHandler(fleetStates, baseStates, random);
    }
}
