using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Npe;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.SaveFormat;

/// <summary>
/// `.SAV` file import (`LOADSAVE.PAS`'s `LoadGame`, `docs/SAV_FILE_FORMAT.md`). Builds this
/// port's real <see cref="Game"/>/<see cref="Galaxy.Galaxy"/>/entity object graph directly from
/// the on-disk bytes — see `docs/ROADMAP.md`'s save/load notes for why this is the priority
/// (real captured saves as ground truth/testing, user-facing import) versus the new native JSON
/// format being the actual long-term save format.
///
/// Real Pascal reads strictly sequentially with no seeking or index. Several sections reference
/// an empire by its on-disk ordinal before Empire Data (the section that actually names/activates
/// each of the 8 fixed slots) has been read — Environment's `Player`, Sector's mine
/// owner/`MineScout`, every entity's `Emp`/`ScoutedBy`/`KnownBy`. Resolved via 8 placeholder
/// <see cref="Empire"/> slots allocated up front, the same fixed-8-slot shape Pascal's own
/// `EmpireDataRecord` array has, just realized as real objects instead of an array index —
/// object identity is stable across the whole load, so an early reference to slot 3 still points
/// at the exact object Empire Data later fills in with a real name/tech/etc. Never added to
/// <see cref="Game.Empires"/> unless Empire Data marks that slot `InUse`: a not-`InUse` slot means
/// this exact placeholder is what a stray reference (a Kingdom's own `State` keys, `DefeatedBy`, a
/// `NewsItem`'s `OtherEmpire`/`Defender`, a minefield's owner/scouts) still resolves to — real
/// Pascal's own on-disk shape for such an empire, current game or a prior one, looks identical
/// either way, and this loader has no way to distinguish "genuinely never existed" from "existed,
/// now torn down" from the bytes alone (see <see cref="SavGameWriter"/>'s own remarks on why
/// <see cref="Types.EmpireStatus.Eliminated"/> is lossy through `.SAV` for the same reason).
/// </summary>
public sealed class SavGameLoader
{
    private readonly Empire[] _empireSlots = BuildPlaceholderSlots();

    /// <summary>
    /// Real object references by on-disk `IDNumber`, populated as each of Planets/Starbases/
    /// Fleets/Stargates/ConstructionSites is loaded. Every reference *outside* the Fleets section
    /// itself (`NameRecord.Coord` in Empire Data, `FleetDataRecord.TargetID`/`HomeBaseID` in NPE
    /// Data) comes strictly after all five, so the index is always complete by then. A `DestCOM`'s
    /// `Loc.ID` is the one exception — it's read *during* the Fleets section (`LoadFleets`), which
    /// precedes Stargates/ConstructionSites and can't see a same-section forward reference to a
    /// later fleet either — so <see cref="ReadCommandRecord"/> never resolves one against this
    /// dictionary directly; see <see cref="_pendingOrderDestinations"/>. Not populated for object
    /// types this port has no representation of (`Con`'s own type tag aside, that one IS
    /// `ConstructionSite` — the ones genuinely missing are `BlkHl`/`Plsr`/`WrmHl`/`Wndr`/`ArtObj`).
    /// Confirmed dead code in real Pascal, not just unexercised by this port's 13 reference saves --
    /// no creation routine for any of the five exists in either the 1.31 or 2.0 source tree
    /// (`docs/PASCAL_ARCHITECTURE_NOTES.md`'s own "Findings from porting" section), so no real
    /// `.SAV` file, from any scenario, could ever contain a reference to one.
    /// </summary>
    private readonly Dictionary<(SavObjectType, int), ISectorObject> _objectsById = new();

    /// <summary>
    /// `(fleet, order index, raw on-disk id)` for every `DestCOM` order whose `Loc.ID` named a real
    /// object — deferred rather than resolved inline in <see cref="ReadCommandRecord"/> because that
    /// runs mid-Fleets-section, before Stargates/ConstructionSites are loaded and before a
    /// same-section forward reference to a later fleet exists. Resolved by
    /// <see cref="ResolvePendingOrderDestinations"/> once every section has run — the same two-phase
    /// shape <see cref="SaveFormat.GameJson"/>'s own <c>Deserialize</c> uses for
    /// <c>FleetOrder.DestinationObject</c>, for the identical reason.
    /// </summary>
    private readonly List<(Fleet Fleet, int OrderIndex, SavIdNumber Id)> _pendingOrderDestinations = new();

    /// Per-slot `InUse`/`IsAPlayer`, populated by <see cref="LoadEmpireData"/> — NPE Data (Phase
    /// 7e) needs both to know which slots get an NPE blob at all (`EmpireActive(Emp) AND NOT
    /// EmpirePlayer(Emp)`, `docs/SAV_FILE_FORMAT.md`'s NPE Data section).
    private readonly bool[] _empireInUse = new bool[8];
    private readonly bool[] _empireIsPlayer = new bool[8];

    /// <summary>
    /// Drives whichever `ITurnHandler` a loaded NPE empire needs going forward (e.g.
    /// `KingdomTurnHandler`'s ongoing `PlayTurn` decisions) — not tied to anything in the save
    /// file itself. Real Pascal's own `.SAV` format has no `RandSeed` field either (confirmed:
    /// not in `docs/SAV_FILE_FORMAT.md`'s Environment section or anywhere else) — RNG continuity
    /// across a save/load was never part of the real format's contract, so a caller-supplied or
    /// fresh <see cref="Random"/> is exactly as faithful as real Pascal gets.
    /// </summary>
    private readonly Random _random;

    public SavGameLoader(Random? random = null) => _random = random ?? new Random();

    private static Empire[] BuildPlaceholderSlots()
    {
        var slots = new Empire[8];
        for (var i = 0; i < slots.Length; i++) {
            slots[i] = new Empire { Name = "" };
        }
        return slots;
    }

    /// Empire ordinal 0-7 = Empire1..Empire8 (this port's empire slots); 8 = Indep
    /// (`docs/SAV_FILE_FORMAT.md`'s Empire enum reference).
    private Empire ResolveEmpire(int ordinal) => ordinal == 8 ? Empire.Independent : _empireSlots[ordinal];

    /// <summary>
    /// Resolves a raw on-disk `IDNumber` to the real object it names, or null for an empty
    /// reference (`Index=0`) or an object type this port doesn't model.
    /// </summary>
    private ISectorObject? ResolveObject(SavIdNumber id) =>
        id.IsEmpty ? null : _objectsById.GetValueOrDefault((id.ObjectType, id.Index));

    public Game LoadGame(byte[] data)
    {
        var reader = new SavReader(data);

        LoadHeader(reader);
        var (year, playerOrdinal, scenarioFilename, timePerTurn, autoSave, asyncTurns, pauseActive, reEnterGame) = LoadEnvironment(reader);
        var galaxy = LoadSector(reader);

        var game = new Game(galaxy) {
            Year = year,
            ScenarioFilename = scenarioFilename,
            CurrentEmpire = ResolveEmpire(playerOrdinal),
            TimePerTurn = timePerTurn,
            AutoSave = autoSave,
            AsyncTurns = asyncTurns,
            PauseActive = pauseActive,
            ReEnterGame = reEnterGame,
        };

        LoadPlanets(reader, galaxy);
        LoadStarbases(reader, galaxy);
        LoadFleets(reader, galaxy);
        LoadStargates(reader, galaxy);
        LoadConstructionSites(reader, galaxy);
        ResolvePendingOrderDestinations();
        LoadMessages(reader, game);
        LoadEmpireData(reader, game);
        LoadNewsData(reader, game);
        LoadNpeData(reader, game);

        // .SAV has no on-disk representation of a human's turn handler (there's no persisted AI
        // state to load -- HumanTurnHandler.PlayTurn is a no-op) -- mirrors ScenarioLoader.
        // RunCreatePlayerEmpire's own registration for a freshly created game.
        foreach (var empire in game.Empires) {
            if (empire.NpeType is null) {
                game.TurnHandlers[empire] = new HumanTurnHandler();
            }
        }

        return game;
    }

    /// <summary>
    /// `LoadHeader` (`LOADSAVE.PAS:62-71`). The signature is read and only sanity-checked, not
    /// stored — nothing in this port's model has a use for it once the file is confirmed to be a
    /// real Anacreon save. Version is read but not yet acted on (this port targets version 13
    /// only, matching `docs/SAV_FILE_FORMAT.md`'s own stated scope).
    /// </summary>
    private static void LoadHeader(SavReader reader)
    {
        var signature = reader.ReadRawString(32);
        var version = reader.ReadWord();

        if (!signature.StartsWith("Anacreon save file v1.3", StringComparison.Ordinal)) {
            throw new FormatException($"Not an Anacreon save file (signature was \"{signature}\").");
        }

        if (version != 13) {
            throw new FormatException($"Unsupported save file version {version} (this port only understands version 13).");
        }
    }

    /// `LoadEnvironment` (`ENVIRON.PAS:127-138`). `EmpiresToMove` is read and discarded — genuinely
    /// redundant with `Game.CurrentEmpire`/`NextEmpire()`, so there's nothing to store. The rest
    /// round-trip onto <see cref="Game"/>'s own like-named properties (see their doc comment).
    private static (int Year, int PlayerOrdinal, string ScenarioFilename, int TimePerTurn, bool AutoSave, bool AsyncTurns, bool PauseActive, bool ReEnterGame) LoadEnvironment(SavReader reader)
    {
        var year = reader.ReadWord();
        var playerOrdinal = reader.ReadByte();
        reader.ReadBitSet(2); // EmpiresToMove -- discarded, see doc comment above.
        var scenarioFilename = reader.ReadPascalString(16);
        var timePerTurn = reader.ReadWord();
        var autoSave = reader.ReadBoolean();
        var asyncTurns = reader.ReadBoolean();
        var pauseActive = reader.ReadBoolean();
        var reEnterGame = reader.ReadBoolean();

        return (year, playerOrdinal, scenarioFilename, timePerTurn, autoSave, asyncTurns, pauseActive, reEnterGame);
    }

    /// <summary>
    /// `LoadSector` (`GALAXY.PAS:88-99`). `SectorRecord.Obj`/`Flts` are read and discarded — fully
    /// redundant with what the Planets/Starbases/Stargates/ConstructionSites sections and each
    /// `Fleet`'s own <see cref="Entities.Fleet.Location"/> independently provide (confirmed by
    /// reading every real write site of `.Obj:=`/`Flts:=` in the 1.31 tree: fleets never occupy the
    /// `Obj` slot, only planets/starbases/stargates/construction sites do). `Special`'s low nibble
    /// (nebula type) and high nibble (mine-placing empire, sentinel `Ord(Indep)`=8 meaning "no
    /// mine" — `GALAXY.PAS`'s own `NoSRMField` constant) and `MineScout` are the only fields this
    /// port's <see cref="Galaxy.Galaxy"/> has anywhere to put, via its sparse nebula/minefield/
    /// mine-scouted-by dictionaries.
    /// </summary>
    private Galaxy.Galaxy LoadSector(SavReader reader)
    {
        var sizeOfGalaxy = reader.ReadWord();
        reader.ReadWord(); // Written twice; both copies hold the same value (GALAXY.PAS:81-82).

        var galaxy = new Galaxy.Galaxy(sizeOfGalaxy);

        for (var x = 0; x <= sizeOfGalaxy; x++) {
            for (var y = 0; y <= sizeOfGalaxy; y++) {
                var coordinate = new Galaxy.Coordinate(x, y);

                reader.ReadIdNumber(); // Obj -- discarded, see doc comment above.
                reader.ReadBitSet(1); // Flts -- discarded, same reason.
                var mineScout = reader.ReadBitSet(1);
                var special = reader.ReadByte();

                foreach (var scoutOrdinal in mineScout) {
                    galaxy.MarkMineScouted(ResolveEmpire(scoutOrdinal), coordinate);
                }

                var nebula = (NebulaType)(special & 0x0F);
                if (nebula != NebulaType.None) {
                    galaxy.SetNebula(coordinate, nebula);
                }

                var mineOwnerOrdinal = special >> 4;
                if (mineOwnerOrdinal != 8) { // 8 = Ord(Indep) = GALAXY.PAS's NoSRMField sentinel, "no mine."
                    galaxy.SetMine(coordinate, ResolveEmpire(mineOwnerOrdinal));
                }
            }
        }

        return galaxy;
    }

    /// <summary>
    /// `ScoutedBy`/`KnownBy` are separate independent `ScoutSet`s on disk (unlike this port's model,
    /// where <see cref="EntityVisibility{T}.MarkScouted"/> also implies Known) — reconstructed by
    /// applying Scouted first, then Known only for empires not already Scouted, so the
    /// Scouted-implies-Known invariant holds regardless of what the two on-disk sets happen to say.
    /// </summary>
    private void ApplyVisibility<T>(HashSet<int> scoutedBy, HashSet<int> knownBy, T entity, Func<Empire, EntityVisibility<T>> selector)
        where T : notnull
    {
        foreach (var ordinal in scoutedBy) {
            selector(ResolveEmpire(ordinal)).MarkScouted(entity);
        }

        foreach (var ordinal in knownBy) {
            if (!scoutedBy.Contains(ordinal)) {
                selector(ResolveEmpire(ordinal)).MarkKnown(entity);
            }
        }
    }

    private static void ReadShips(SavReader reader, ShipCounts ships)
    {
        foreach (var type in Enum.GetValues<ShipType>()) {
            ships[type] = reader.ReadWord();
        }
    }

    private static void ReadCargo(SavReader reader, CargoHold cargo)
    {
        foreach (var type in Enum.GetValues<CargoType>()) {
            cargo[type] = reader.ReadWord();
        }
    }

    private static void ReadDefenses(SavReader reader, DefenseCounts defenses)
    {
        foreach (var type in Enum.GetValues<DefenseType>()) {
            defenses[type] = reader.ReadWord();
        }
    }

    private static void ReadIndustry(SavReader reader, IndustryLevels industry)
    {
        foreach (var type in Enum.GetValues<IndustryType>()) {
            industry[type] = reader.ReadWord();
        }
    }

    /// ImpExp's 4 nibbles (confirmed via PRIMINTR.PAS's GetISSP/SetISSP, not assumed from the field
    /// name alone): bits 0-3 Chemical, 4-7 Metal (Mining), 8-11 Supply, 12-15 Trillum.
    private static void ApplySelfSufficiency(int impExp, SelfSufficiencySettings settings)
    {
        settings.Chemical = impExp & 0xF;
        settings.Metal = (impExp >> 4) & 0xF;
        settings.Supply = (impExp >> 8) & 0xF;
        settings.Trillum = (impExp >> 12) & 0xF;
    }

    /// <summary>
    /// `LoadPlanets` (`LOADSAVE.PAS:109-138`). Dense (`FOR i:=1 TO NoOfPlanets`) but that's just an
    /// on-disk detail — read the same sparse-terminated-by-0 loop shape as every other section,
    /// since a dense run is a special case of a sparse one with no gaps. `Special`'s only live bit
    /// is `AmbAddict` (ordinal 0 of `SpecialConditions`) — <see cref="Entities.Planet"/>'s own doc
    /// comment already confirms `Holocst`/`Plague`/`SelfSuff`/`Virgin` are dead. `Reserved`/`NextID`
    /// discarded — opaque/unused, per `docs/SAV_FILE_FORMAT.md`.
    /// </summary>
    private void LoadPlanets(SavReader reader, Galaxy.Galaxy galaxy)
    {
        var index = reader.ReadWord();

        while (index > 0) {
            var location = reader.ReadCoordinate();
            var ownerOrdinal = reader.ReadByte();
            var scoutedBy = reader.ReadBitSet(1);
            var knownBy = reader.ReadBitSet(1);
            var cls = (WorldClass)reader.ReadByte();
            var type = (WorldType)reader.ReadByte();
            var impExp = reader.ReadWord();
            var tech = (TechLevel)reader.ReadByte();
            var efficiency = reader.ReadByte();
            var revolutionIndex = reader.ReadByte();
            var special = reader.ReadBitSet(1);
            var population = reader.ReadWord();

            var planet = new Planet {
                Location = location,
                Owner = ResolveEmpire(ownerOrdinal),
                Class = cls,
                Type = type,
                TechLevel = tech,
                Efficiency = efficiency,
                RevolutionIndex = revolutionIndex,
                IsAddictedToAmbrosia = special.Contains(0), // AmbAddict
                Population = population,
            };
            ApplySelfSufficiency(impExp, planet.SelfSufficiency);

            ReadShips(reader, planet.Ships);
            ReadCargo(reader, planet.Cargo);
            ReadDefenses(reader, planet.Defenses);
            ReadIndustry(reader, planet.Industry);
            planet.TrillumReserve = reader.ReadWord();
            reader.Skip(16); // Reserved
            reader.ReadIdNumber(); // NextID -- unused linked-list field

            galaxy.Planets.Add(planet);
            ApplyVisibility(scoutedBy, knownBy, planet, e => e.Planets);
            _objectsById[(SavObjectType.Pln, index)] = planet;

            index = reader.ReadWord();
        }
    }

    /// <summary>
    /// `LoadStarbases` (`LOADSAVE.PAS:159-176`). No `Cls`/`TriReserve` fields on disk (unlike
    /// Planets) — matches <see cref="Entities.Starbase"/> having neither, so there's nothing to
    /// discard there. `STyp` is the full `TechnologyTypes` ordinal (confirmed: `TYPES.PAS:84`
    /// declares `StarbaseTypes = cmm..out`, a genuine Pascal subrange, which preserves the base
    /// enum's ordinals rather than renumbering from 0 — same convention `ConstrTypes`/
    /// `StargateTypes` use, offset -20 for this one).
    /// </summary>
    private void LoadStarbases(SavReader reader, Galaxy.Galaxy galaxy)
    {
        var index = reader.ReadWord();

        while (index > 0) {
            var location = reader.ReadCoordinate();
            var ownerOrdinal = reader.ReadByte();
            var scoutedBy = reader.ReadBitSet(1);
            var knownBy = reader.ReadBitSet(1);
            var kind = (StarbaseKind)(reader.ReadByte() - 20);
            var type = (WorldType)reader.ReadByte();
            var tech = (TechLevel)reader.ReadByte();
            var efficiency = reader.ReadByte();
            var revolutionIndex = reader.ReadByte();
            reader.ReadBitSet(1); // Special -- SetOfSpecialConditions, no live bit for a starbase.
            var population = reader.ReadWord();

            var starbase = new Starbase {
                Location = location,
                Owner = ResolveEmpire(ownerOrdinal),
                Kind = kind,
                Type = type,
                TechLevel = tech,
                Efficiency = efficiency,
                RevolutionIndex = revolutionIndex,
                Population = population,
            };

            ReadShips(reader, starbase.Ships);
            ReadCargo(reader, starbase.Cargo);
            ReadDefenses(reader, starbase.Defenses);
            ReadIndustry(reader, starbase.Industry);

            reader.Skip(1); // Move -- starbase movement isn't modeled through this field in this port
            var destination = reader.ReadCoordinate();
            // CreateStarbase (INTRFACE.PAS) initializes Dest:=XY for a non-moving base -- real
            // Pascal's Dest is never optional, so "not moving" is just Dest==XY on disk. This
            // port's own FleetMovementHandler.AdvanceStarbases instead represents "not moving" as
            // Destination=null (and nulls it out itself the moment it detects arrival) -- apply
            // that same arrived-detection here so a freshly loaded, non-moving starbase already
            // matches the steady state this port's own handler would converge to, rather than
            // momentarily violating "null means not moving" until the first turn runs.
            starbase.Destination = destination == location ? null : destination;
            starbase.Status = (FleetStatus)reader.ReadByte();

            reader.Skip(18); // Reserved
            reader.ReadIdNumber(); // NextID -- unused linked-list field

            galaxy.Starbases.Add(starbase);
            ApplyVisibility(scoutedBy, knownBy, starbase, e => e.Starbases);
            _objectsById[(SavObjectType.Base, index)] = starbase;

            index = reader.ReadWord();
        }
    }

    /// <summary>
    /// `LoadFleets` (`LOADSAVE.PAS:227-263`). Reproduces the real load-time quirk verbatim
    /// (`LOADSAVE.PAS:250-256`): if any single axis of `XY`/`Dest` is exactly 0, both coordinates
    /// reset to `(1,1)` — confirmed to fire on a per-component basis, not "both coordinates are
    /// (0,0)". `CommandRecord` order queues are read into <see cref="Fleet.Orders"/> (see
    /// <see cref="ReadCommandRecord"/>) rather than discarded, and so is `NextOrder` -- a plain
    /// resume-index `Word`, not a pointer, that real Pascal's own `SaveFleets`/`LoadFleets`
    /// (`LOADSAVE.PAS:202,246`) blit as part of the whole live `FleetRecord`, so it round-trips
    /// correctly there. `OrderData` (the 6 bytes right after it) really is dead on load -- it's a
    /// serialized heap pointer from the save process, meaningless once reloaded into a new one --
    /// but `NextOrder` is real, live gameplay state <see cref="Turns.FleetMovementHandler.ExecuteFleetOrders"/>
    /// reads and writes every turn; skipping it would silently lose a fleet's resume point for any
    /// order queue saved mid-execution (e.g. sitting at a `WaitCOM` between two `DestCOM`s).
    /// </summary>
    private void LoadFleets(SavReader reader, Galaxy.Galaxy galaxy)
    {
        var index = reader.ReadWord();

        while (index > 0) {
            var location = reader.ReadCoordinate();
            var ownerOrdinal = reader.ReadByte();
            var scoutedBy = reader.ReadBitSet(1);

            var fleet = new Fleet { Location = location, Owner = ResolveEmpire(ownerOrdinal) };
            ReadShips(reader, fleet.Ships);
            ReadCargo(reader, fleet.Cargo);

            var destination = reader.ReadCoordinate();
            fleet.Status = (FleetStatus)reader.ReadByte();
            var fuelHigh = reader.ReadByte();
            var fuel = reader.ReadInteger();
            var knownBy = reader.ReadBitSet(1);

            fleet.NextOrder = reader.ReadByte();
            reader.Skip(6); // OrderData -- a serialized heap pointer, genuinely dead on load
            reader.Skip(1); // NPEDataIndex -- Kingdom AI keys fleet state by Fleet reference, not this index
            reader.Skip(8); // Reserved
            reader.ReadIdNumber(); // NextID -- unused linked-list field

            if (location.X == 0 || location.Y == 0 || destination.X == 0 || destination.Y == 0) {
                location = new Galaxy.Coordinate(1, 1);
                destination = new Galaxy.Coordinate(1, 1);
            }

            fleet.Location = location;
            // Same "null means not moving" translation as Starbase.Destination above -- real
            // Pascal's Dest is never optional, so arrival is just Location==Dest on disk.
            fleet.Destination = destination == location ? null : destination;
            fleet.Fuel = fuelHigh * 32767 + fuel;

            galaxy.Fleets.Add(fleet);
            ApplyVisibility(scoutedBy, knownBy, fleet, e => e.Fleets);
            _objectsById[(SavObjectType.Flt, index)] = fleet;

            var orderCount = reader.ReadWord();
            for (var i = 0; i < orderCount; i++) {
                ReadCommandRecord(reader, fleet);
            }

            index = reader.ReadWord();
        }
    }

    /// <summary>
    /// One `CommandRecord` (`ORDERS.PAS:47-53`, `docs/SAV_FILE_FORMAT.md`'s own worked example) --
    /// `Typ` plus its 4-byte variant, interpreted only for the two `Typ` values that actually use it
    /// (`DestCOM`/`TransCOM`); every other `Typ` has the variant skipped as garbage, matching real
    /// Pascal leaving it holding whatever the previous write left there. Appends directly to
    /// <paramref name="fleet"/>'s own <see cref="Fleet.Orders"/> rather than returning a value: a
    /// `DestCOM` naming a real object can't resolve <see cref="ResolveObject"/> yet (see
    /// <see cref="_pendingOrderDestinations"/>'s own doc comment), so this stashes a placeholder
    /// order plus its pending index instead.
    /// </summary>
    private void ReadCommandRecord(SavReader reader, Fleet fleet)
    {
        var type = (CommandType)reader.ReadByte();

        switch (type) {
            case CommandType.Destination: {
                var xy = reader.ReadCoordinate();
                var id = reader.ReadIdNumber();

                if (id.IsEmpty) {
                    fleet.Orders.Add(new FleetOrder(type, DestinationPosition: xy));
                } else {
                    _pendingOrderDestinations.Add((fleet, fleet.Orders.Count, id));
                    fleet.Orders.Add(new FleetOrder(type));
                }
                break;
            }

            case CommandType.Transfer: {
                var resourceOrdinal = reader.ReadByte();
                var amount = reader.ReadInteger();
                reader.Skip(1); // Trailing unused byte of the 4-byte variant (Res+Trns is only 3 bytes).
                var (ship, cargo) = ResolveTransferResource(resourceOrdinal);
                fleet.Orders.Add(new FleetOrder(type, TransferShip: ship, TransferCargo: cargo, TransferAmount: amount));
                break;
            }

            default:
                reader.Skip(4); // Variant unused for this command type -- leftover garbage bytes.
                fleet.Orders.Add(new FleetOrder(type));
                break;
        }
    }

    /// See <see cref="_pendingOrderDestinations"/>'s own doc comment for why this can't run inline
    /// in <see cref="ReadCommandRecord"/>. A `DestCOM` naming an object type this port has no
    /// representation for (`BlkHl`/`Plsr`/`WrmHl`/`Wndr`/`ArtObj` -- confirmed dead code in real
    /// Pascal, see `_objectsById`'s own doc comment) resolves to null here, same as everywhere else
    /// <see cref="ResolveObject"/> is used.
    private void ResolvePendingOrderDestinations()
    {
        foreach (var (fleet, orderIndex, id) in _pendingOrderDestinations) {
            fleet.Orders[orderIndex] = fleet.Orders[orderIndex] with { DestinationObject = ResolveObject(id) };
        }
    }

    /// `ResourceTypes` ordinal (`ORDERS.PAS`'s own `GetResourceType`: `fgt..tri`, 5-18) split into
    /// this port's existing `ShipType`(5-11)/`CargoType`(12-18) ordinal offsets -- the same shared
    /// ordinal space `_technologyByOrdinal` already decodes for tech grants, just restricted to the
    /// subrange a real `TRANSFER` order can actually name. Neither is set for an out-of-range ordinal
    /// (0-4: `NoRes`/defenses) -- not producible by real Pascal's own `GetResourceType`, and no
    /// reference save exercises `TransCOM` at all to confirm what such a byte would even mean here.
    private static (ShipType? Ship, CargoType? Cargo) ResolveTransferResource(int ordinal) => ordinal switch {
        >= 5 and <= 11 => ((ShipType?)(ordinal - 5), null),
        >= 12 and <= 18 => (null, (CargoType?)(ordinal - 12)),
        _ => (null, null),
    };

    /// `LoadStargates` (`LOADSAVE.PAS:282-296`). `GTyp` is the full `TechnologyTypes` ordinal
    /// (confirmed by `docs/SAV_FILE_FORMAT.md`'s own `STARGATE_DONE.SAV` example: `GTyp=24` for
    /// `gte`), offset -24. `Dest` is a plain `XYCoord`, not an `IDNumber` — `Limbo` (0,0) means
    /// "not yet linked to anything," matching <see cref="Entities.Stargate.LinkedTo"/>'s own null.
    private void LoadStargates(SavReader reader, Galaxy.Galaxy galaxy)
    {
        var index = reader.ReadWord();

        while (index > 0) {
            var location = reader.ReadCoordinate();
            var ownerOrdinal = reader.ReadByte();
            var scoutedBy = reader.ReadBitSet(1);
            var knownBy = reader.ReadBitSet(1);
            var kind = (StargateKind)(reader.ReadByte() - 24);
            var destination = reader.ReadCoordinate();
            reader.ReadIdNumber(); // NextID -- unused linked-list field

            var stargate = new Stargate {
                Location = location,
                Owner = ResolveEmpire(ownerOrdinal),
                Kind = kind,
                LinkedTo = destination == default ? null : destination,
            };

            galaxy.Stargates.Add(stargate);
            ApplyVisibility(scoutedBy, knownBy, stargate, e => e.Stargates);
            _objectsById[(SavObjectType.Gate, index)] = stargate;

            index = reader.ReadWord();
        }
    }

    /// `LoadConstr` (`LOADSAVE.PAS:321-337`). `CTyp` is the full `TechnologyTypes` ordinal
    /// (confirmed by `docs/SAV_FILE_FORMAT.md`'s own `Confront_2.SAV` example: `CTyp=23` for
    /// `out`), offset -19.
    private void LoadConstructionSites(SavReader reader, Galaxy.Galaxy galaxy)
    {
        var index = reader.ReadWord();

        while (index > 0) {
            var location = reader.ReadCoordinate();
            var ownerOrdinal = reader.ReadByte();
            var scoutedBy = reader.ReadBitSet(1);
            var knownBy = reader.ReadBitSet(1);
            var building = (ConstructionType)(reader.ReadByte() - 19);
            var yearsToCompletion = reader.ReadByte();
            reader.ReadIdNumber(); // NextID -- unused linked-list field

            var site = new ConstructionSite {
                Location = location,
                Owner = ResolveEmpire(ownerOrdinal),
                Building = building,
                YearsToCompletion = yearsToCompletion,
            };

            galaxy.ConstructionSites.Add(site);
            ApplyVisibility(scoutedBy, knownBy, site, e => e.ConstructionSites);
            _objectsById[(SavObjectType.Con, index)] = site;

            index = reader.ReadWord();
        }
    }

    /// <summary>
    /// `LoadMessageData` (`MESS.PAS:252-317`). `ReadBy` is skipped, not resolved into anything --
    /// see <see cref="Message"/>'s own doc comment for why real Pascal's own loader never restores
    /// it either. Read before Empire Data (`LoadEmpireData`), same as every other early empire-
    /// ordinal reference in this file (this class's own doc comment) -- `ResolveEmpire` returns the
    /// same placeholder object identity regardless of load order.
    /// </summary>
    private void LoadMessages(SavReader reader, Game game)
    {
        var messageCount = reader.ReadByte();

        for (var i = 0; i < messageCount; i++) {
            var senderOrdinal = reader.ReadByte();
            var recipientOrdinals = reader.ReadBitSet(1);
            reader.ReadBitSet(1); // ReadBy -- discarded, see Message's own doc comment.
            var read = reader.ReadBoolean();
            var intercepted = reader.ReadBoolean();
            reader.Skip(2); // MesText.NoOfLines -- redundant with the NoOfLines byte read below.
            reader.Skip(4); // MesText.FirstLine -- pointer, discarded.
            reader.Skip(4); // MesText.LastLine -- pointer, discarded.
            reader.Skip(4); // Next -- pointer, discarded; read order already is list order.
            reader.Skip(4); // Prev -- pointer, discarded; read order alone still rebuilds the list.

            var lineCount = reader.ReadByte();
            var lines = new List<string>(lineCount);
            for (var j = 0; j < lineCount; j++) {
                lines.Add(reader.ReadPascalString(80));
            }

            var recipients = recipientOrdinals.Select(ResolveEmpire).ToHashSet();
            game.Messages.Add(new Message(ResolveEmpire(senderOrdinal), recipients, read, intercepted, lines));
        }
    }

    private static void ReadDefenseSettings(SavReader reader, DefenseSettings settings)
    {
        ReadShellDefensePlan(reader, settings.Fleets);
        ReadShellDefensePlan(reader, settings.Starbases);
    }

    private static void ReadShellDefensePlan(SavReader reader, ShellDefensePlan plan)
    {
        foreach (var shell in Enum.GetValues<ShellPosition>()) {
            var distribution = plan[shell];
            foreach (var ship in Enum.GetValues<ShipType>()) {
                distribution[ship] = reader.ReadByte();
            }
        }
    }

    /// `ProbeRecord`'s 10 fixed slots collapse to just the in-transit destinations
    /// (`Empire.ProbesInTransit`'s own doc comment) — only `Status=PInTrans` (ordinal 1) slots
    /// contribute; `Ready`/`AtDest`/`Lost` probes carry no state this port's model keeps.
    private static List<Coordinate> ReadProbes(SavReader reader)
    {
        var inTransit = new List<Coordinate>();

        for (var i = 0; i < 10; i++) {
            var destination = reader.ReadCoordinate();
            var status = reader.ReadByte();
            if (status == 1) { // PInTrans
                inTransit.Add(destination);
            }
        }

        return inTransit;
    }

    /// <summary>
    /// Pascal's single `TechnologyTypes` ordinal space (`NoRes,LAM..dis`, `TYPES.PAS:63-85`) —
    /// mirrors `ScenarioLoader`'s own `_technologyTypeGrants` decode table exactly (same source
    /// declaration order), kept as its own small transcription here rather than shared across the
    /// two unrelated file-format parsing boundaries (`.SCN` vs `.SAV`). Serves both the Empire
    /// Data `Technology` bitset (<see cref="ApplyTechnology"/>) and News's `EmpireGainedTechnology`
    /// `TechGrant` decode (<see cref="ResolveTechGrant"/>) — one raw ordinal, two consumers.
    /// </summary>
    private static readonly (Action<UnlockedTechnology> Grant, TechCatalog.TechGrantIdentity Identity)?[] _technologyByOrdinal = BuildTechnologyByOrdinal();

    private static (Action<UnlockedTechnology>, TechCatalog.TechGrantIdentity)?[] BuildTechnologyByOrdinal()
    {
        var table = new (Action<UnlockedTechnology>, TechCatalog.TechGrantIdentity)?[27];

        void SetDefense(int ordinal, DefenseType type) => table[ordinal] = (TechCatalog.Grant(type), new TechCatalog.TechGrantIdentity(TechCategory.Defense, (int)type));
        void SetShip(int ordinal, ShipType type) => table[ordinal] = (TechCatalog.Grant(type), new TechCatalog.TechGrantIdentity(TechCategory.Ship, (int)type));
        void SetCargo(int ordinal, CargoType type) => table[ordinal] = (TechCatalog.Grant(type), new TechCatalog.TechGrantIdentity(TechCategory.Cargo, (int)type));
        void SetConstruction(int ordinal, ConstructionType type) => table[ordinal] = (TechCatalog.Grant(type), new TechCatalog.TechGrantIdentity(TechCategory.Construction, (int)type));

        SetDefense(1, DefenseType.Lam);
        SetDefense(2, DefenseType.DefenseSatellite);
        SetDefense(3, DefenseType.Gdm);
        SetDefense(4, DefenseType.IonCannon);
        SetShip(5, ShipType.Fighter);
        SetShip(6, ShipType.HunterKiller);
        SetShip(7, ShipType.Jumpship);
        SetShip(8, ShipType.Jumptransport);
        SetShip(9, ShipType.Penetrator);
        SetShip(10, ShipType.Starship);
        SetShip(11, ShipType.Transport);
        SetCargo(12, CargoType.Legion);
        SetCargo(13, CargoType.NinjaLegion);
        SetCargo(14, CargoType.Ambrosia);
        SetCargo(15, CargoType.Chemicals);
        SetCargo(16, CargoType.Metals);
        SetCargo(17, CargoType.Supplies);
        SetCargo(18, CargoType.Trillum);
        SetConstruction(19, ConstructionType.Minefield);
        SetConstruction(20, ConstructionType.CommandBase);
        SetConstruction(21, ConstructionType.Fortress);
        SetConstruction(22, ConstructionType.IndustrialComplex);
        SetConstruction(23, ConstructionType.Outpost);
        SetConstruction(24, ConstructionType.Gate);
        SetConstruction(25, ConstructionType.WarpLink);
        SetConstruction(26, ConstructionType.Disrupter);

        return table;
    }

    private static void ApplyTechnology(HashSet<int> ordinals, UnlockedTechnology unlocked)
    {
        foreach (var ordinal in ordinals) {
            if (ordinal is >= 0 and < 27) {
                _technologyByOrdinal[ordinal]?.Grant(unlocked);
            }
        }
    }

    private static TechCatalog.TechGrantIdentity? ResolveTechGrant(int ordinal) =>
        ordinal is >= 0 and < 27 ? _technologyByOrdinal[ordinal]?.Identity : null;

    /// <summary>
    /// `NameRecord` (`DATASTRC.PAS:152-157`) — `Coord` is a `Location` union exactly like
    /// `CommandRecord`'s `DestCOM` variant (see `docs/SAV_FILE_FORMAT.md`'s own worked example):
    /// a raw coordinate when nothing occupies that cell, or a resolved object reference when
    /// something does. Parsed here without resolving `id` against a live object yet — the raw
    /// tuple is stashed and resolved after this slot's own `InUse` gate is known (see
    /// <see cref="LoadEmpireData"/>), same two-step shape as everything else that byte stream reads
    /// unconditionally but only acts on for a real slot.
    /// </summary>
    private static (string Name, Coordinate Xy, SavIdNumber Id) ReadRawNameRecord(SavReader reader)
    {
        var name = reader.ReadPascalString(8);
        var xy = reader.ReadCoordinate();
        var id = reader.ReadIdNumber();
        reader.Skip(4); // Next -- pointer, discarded; read order already is list order

        return (name, xy, id);
    }

    /// <summary>
    /// `LoadEmpireData` (`LOADSAVE.PAS:368-410`). Writes into the same 8 placeholder
    /// <see cref="Empire"/> objects every earlier section already resolved references against
    /// (see this class's own doc comment) — only `InUse` slots get added to
    /// <see cref="Game.Empires"/>. <c>TimeLeft</c> has no session/turn-clock concept in this port
    /// yet — read and discarded, same tracked-gap treatment as the Environment section's UI
    /// fields. <c>Names</c>/<c>LastName</c> are the linked list's head/tail pointers — garbage on
    /// disk, discarded; the real list follows immediately as `NameRecord × NoOfNames`. Each raw
    /// record resolving to a live object goes onto that object's own <see cref="ISectorObject.Names"/>
    /// (this port's own storage shape — see that property's own remarks); one that doesn't (a bare
    /// coordinate, or an on-disk reference nothing here can resolve) falls back to this empire's own
    /// <see cref="Empire.Bookmarks"/>, same as a genuinely coordinate-only name always did.
    /// </summary>
    private void LoadEmpireData(SavReader reader, Game game)
    {
        for (var slot = 0; slot < 8; slot++) {
            var empire = _empireSlots[slot];

            var inUse = reader.ReadBoolean();
            var isAPlayer = reader.ReadBoolean();
            var name = reader.ReadPascalString(32);
            var password = reader.ReadPascalString(8);
            reader.Skip(2); // TimeLeft
            var capitalId = reader.ReadIdNumber();

            ReadDefenseSettings(reader, empire.DefenseSettings);
            var probesInTransit = ReadProbes(reader);

            reader.Skip(4); // Names -- pointer, discarded
            reader.Skip(4); // LastName -- pointer, discarded

            var totalRevIndex = reader.ReadInteger();
            var techLevel = (TechLevel)reader.ReadByte();
            var technology = reader.ReadBitSet(4);
            var isAnEmpress = reader.ReadBoolean();
            var revFactor = reader.ReadInteger();
            var founding = reader.ReadWord();
            var modifiers = reader.ReadBitSet(1);
            reader.Skip(14); // Reserved

            var nameCount = reader.ReadByte();
            var rawNames = new List<(string Name, Coordinate Xy, SavIdNumber Id)>();
            for (var i = 0; i < nameCount; i++) {
                rawNames.Add(ReadRawNameRecord(reader));
            }

            _empireInUse[slot] = inUse;
            _empireIsPlayer[slot] = isAPlayer;

            if (!inUse) {
                continue; // Placeholder stays inert -- never added to Game.Empires (see this class's own doc comment on the 8 placeholder slots).
            }

            empire.Name = name;
            empire.Password = password.Length == 0 ? null : password;

            // Human-defeat sentinel (ATTACK.PAS:1120-1131's ConquerEmpire): ObjTyp=Void with
            // Index the conqueror's own raw empire ordinal -- even Index=0 (Empire1) is a real,
            // meaningful value here, a different meaning of "0" than IDNumber's usual "no object"
            // convention (an InUse empire's Capital is never legitimately EmptyQuadrant
            // otherwise -- every active empire has a real capital until this exact defeat path).
            // Always decodes to PendingElimination, never Eliminated: an Eliminated empire is
            // not-InUse on disk and never reaches this branch at all (see this class's own remarks
            // on the placeholder-slot doc comment above).
            if (capitalId.ObjectType == SavObjectType.Void) {
                empire.Capital = null;
                empire.Status = EmpireStatus.PendingElimination;
                empire.DefeatedBy = ResolveEmpire(capitalId.Index);
            } else {
                empire.Capital = ResolveObject(capitalId) as IEconomicWorld;
            }

            empire.ProbesInTransit.AddRange(probesInTransit);
            empire.TotalRevolutionIndex = totalRevIndex;
            empire.TechnologyLevel = techLevel;
            ApplyTechnology(technology, empire.Technology);
            empire.IsEmpress = isAnEmpress;
            empire.RevolutionFactor = revFactor;
            empire.FoundingYear = founding;
            empire.LosesIfCapitalConquered = modifiers.Contains(0); // CentralEMD

            foreach (var (rawName, xy, id) in rawNames) {
                if (!id.IsEmpty && ResolveObject(id) is { } target) {
                    target.Names[empire] = rawName;
                } else {
                    empire.Bookmarks.Add(new LocationBookmark { Name = rawName, Location = xy });
                }
            }

            game.Empires.Add(empire);
        }
    }

    /// <summary>
    /// Headlines confirmed (by reading the exact real Pascal `AddNews` call site, not guessed from
    /// this port's own parameter names) to carry another empire's raw ordinal in `Parm1` —
    /// `Ord(Player)`/`Integer(Player)` in every case. Deliberately small: only the headlines this
    /// phase's own reference-save ground truth actually exercises are confirmed here; every other
    /// headline falls back to <see cref="LoadNewsData"/>'s generic decode (raw `Parm1-3`, no
    /// `OtherEmpire`) rather than a guessed mapping.
    /// </summary>
    private static readonly Dictionary<NewsType, int> _otherEmpireInParm = new() {
        [NewsType.FleetDestroyedByLams] = 1, // ATTACK.PAS:1664 (LAMDs)
        [NewsType.FleetDamagedByLams] = 1, // ATTACK.PAS:1669 (LAMDm)
        [NewsType.ProbeDestroyedByYou] = 1, // INTRFACE.PAS:1329 (PCap)
    };

    /// <summary>Same confirmed-not-guessed discipline as <see cref="_otherEmpireInParm"/>: `EmpireGainedTechnology`'s `Parm1` is `Ord(NewTech)` (`UPDATE.PAS:399`, NCapTech) -- the full `TechnologyTypes` ordinal of one newly-granted item.</summary>
    private static readonly Dictionary<NewsType, int> _techGrantInParm = new() {
        [NewsType.EmpireGainedTechnology] = 1,
    };


    /// <summary>
    /// `LoadNewsData` (`NEWS.PAS:266-297`). `Loc1` decodes the same way as every other `Location`
    /// union in this format (`NameRecord.Coord`, `CommandRecord`'s `DestCOM` variant): an object
    /// reference when `ID` is populated, a bare coordinate otherwise — this is a per-item decision
    /// baked into the bytes themselves (whichever field the original `AddNews` call filled),
    /// not a per-headline one. `OtherEmpire`/`TechGrant` are the two exceptions with real,
    /// per-headline meaning beyond that, decoded via the two confirmed tables above; every other
    /// headline keeps `Parm1-3` as plain ints with no further interpretation, matching
    /// <see cref="NewsItem"/>'s own shape for whatever this port has no real call site for (e.g.
    /// `MessageReceived`, since no in-memory message concept exists — see
    /// <see cref="LoadMessages"/>).
    /// </summary>
    private void LoadNewsData(SavReader reader, Game game)
    {
        for (var slot = 0; slot < 8; slot++) {
            var count = reader.ReadWord();

            for (var i = 0; i < count; i++) {
                var headline = (NewsType)reader.ReadByte();
                var xy = reader.ReadCoordinate();
                var id = reader.ReadIdNumber();
                var parm1 = reader.ReadInteger();
                var parm2 = reader.ReadInteger();
                var parm3 = reader.ReadInteger();
                reader.Skip(4); // Next -- pointer, discarded; read order already is list order

                if (!_empireInUse[slot]) {
                    continue; // Placeholder slot -- nothing real to attach this to.
                }

                ISectorObject? subject = null;
                Coordinate? position = null;
                if (id.IsEmpty) {
                    position = xy;
                } else {
                    subject = ResolveObject(id);
                }

                Empire? otherEmpire = _otherEmpireInParm.TryGetValue(headline, out var empireParm)
                    ? ResolveEmpire(SelectParm(empireParm, parm1, parm2, parm3))
                    : null;

                TechCatalog.TechGrantIdentity? techGrant = _techGrantInParm.TryGetValue(headline, out var techParm)
                    ? ResolveTechGrant(SelectParm(techParm, parm1, parm2, parm3))
                    : null;

                ResourceKind? resource = ResourceKind.LegacyParmSlot.TryGetValue(headline, out var resourceParm)
                    ? ResourceKind.FromOrdinal(SelectParm(resourceParm, parm1, parm2, parm3))
                    : null;

                _empireSlots[slot].AddNews(headline, subject, position, otherEmpire, techGrant, parm1, parm2, parm3, resource: resource);
            }
        }
    }

    private static int SelectParm(int index, int parm1, int parm2, int parm3) => index switch {
        1 => parm1,
        2 => parm2,
        _ => parm3,
    };

    /// <summary>
    /// `LoadNPEData` (`LOADSAVE.PAS:453-465`), two parts. Part 1: the fixed 9-entry `NPEDataArray`
    /// (`Empire1..Empire8` + `Indep`, `Indep` always `NoNPE`) — just the type tag per slot,
    /// `Data`'s pointer discarded. Part 2: for each `Empire1..Empire8` where `EmpireActive(Emp)
    /// AND NOT EmpirePlayer(Emp)` (`docs/SAV_FILE_FORMAT.md`'s own dispatch note — no index/length
    /// prefix, order and presence entirely determined by state already parsed in Empire Data),
    /// one variant-specific blob: Kingdom1/Kingdom2 share one real, interpreted layout (via the
    /// `KingdomTurnHandler` construct-from-saved-state seam); Pirate/Berserker/Guardian/Trader/
    /// unrecognized (`NPE.PAS:100-111`'s own dispatch: unrecognized values fall through to the
    /// Pirate layout, same as `TraderNPE`) have no `ITurnHandler` in this port at all yet, so their
    /// blobs are kept as opaque bytes in <see cref="Game.UnimplementedNpeBlobs"/> rather than
    /// dropped.
    /// </summary>
    private void LoadNpeData(SavReader reader, Game game)
    {
        var npeTypes = new NpeEmpireType?[9];
        for (var i = 0; i < 9; i++) {
            var typ = reader.ReadByte();
            reader.Skip(4); // Data -- pointer, discarded

            npeTypes[i] = typ switch {
                1 => NpeEmpireType.Pirate,
                2 => NpeEmpireType.Kingdom1,
                3 => NpeEmpireType.Kingdom2,
                4 => NpeEmpireType.Berserker,
                5 => NpeEmpireType.Guardian,
                6 => NpeEmpireType.Trader,
                _ => null, // 0 = NoNPE; anything else is malformed and treated the same (no type recorded)
            };
        }

        for (var slot = 0; slot < 8; slot++) {
            if (!_empireInUse[slot] || _empireIsPlayer[slot]) {
                continue; // NPE Data blobs only exist for EmpireActive AND NOT EmpirePlayer slots.
            }

            var empire = _empireSlots[slot];
            var npeType = npeTypes[slot];
            empire.NpeType = npeType;

            switch (npeType) {
                case NpeEmpireType.Kingdom1 or NpeEmpireType.Kingdom2:
                    var (persona, state, fleetStates) = LoadKingdomBlob(reader);
                    game.TurnHandlers[empire] = new KingdomTurnHandler(npeType.Value, persona, state, fleetStates, _random);
                    break;

                case NpeEmpireType.Berserker:
                    game.UnimplementedNpeBlobs[empire] = reader.ReadBytes(930);
                    break;

                case NpeEmpireType.Guardian:
                    game.UnimplementedNpeBlobs[empire] = reader.ReadBytes(430);
                    break;

                default:
                    // Pirate, Trader, and unrecognized/malformed values all share the pirate
                    // layout (NPE.PAS:108-109's own dispatch), including when npeType is null
                    // here because the raw byte was out of range -- real data never hits that,
                    // but the layout choice still matches what real Pascal would do with it.
                    game.UnimplementedNpeBlobs[empire] = reader.ReadBytes(739);
                    break;
            }
        }
    }

    /// <summary>
    /// `Kingdom1DataRecord`/`Kingdom2DataRecord` (`NPETYPES.PAS:136-141`, 454 bytes): `FleetData`
    /// (30 × `FleetDataRecord`, 11 bytes each), `State` (9 × `StateDeptRecord`, 12 bytes each,
    /// `Empire1..Empire8` then `Indep`), `Persona` (`NPECharacterRecord`, 16 bytes) — field order
    /// confirmed directly against `NPETYPES.PAS`, not assumed from the format doc's summary alone.
    /// A `FleetDataRecord` with `Index=0` is an unused slot (Kingdom tracks at most 30 fleets at
    /// once, not one slot per real fleet) — skipped, same for a nonzero `Index` that doesn't
    /// resolve to a real fleet (shouldn't happen for valid data). `Midway`/`BlockX`/`BlockY` are
    /// confirmed dead/Pirate-only per `KingdomFleetState`'s own doc comment — discarded.
    /// </summary>
    private (NpeCharacter Persona, Dictionary<Empire, StateDeptRecord> State, Dictionary<Fleet, KingdomFleetState> FleetStates) LoadKingdomBlob(SavReader reader)
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

            if (_objectsById.GetValueOrDefault((SavObjectType.Flt, index)) is not Fleet fleet) {
                continue;
            }

            fleetStates[fleet] = new KingdomFleetState {
                Mission = mission,
                Target = ResolveObject(targetId),
                HomeBase = ResolveObject(homeBaseId) as IEconomicWorld,
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

            state[ResolveEmpire(i)] = new StateDeptRecord {
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

        return (persona, state, fleetStates);
    }
}
