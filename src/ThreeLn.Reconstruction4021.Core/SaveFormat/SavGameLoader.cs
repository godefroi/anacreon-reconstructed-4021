using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.SaveFormat;

/// <summary>
/// `.SAV` file import (`LOADSAVE.PAS`'s `LoadGame`, `docs/SAV_FILE_FORMAT.md`). Builds this
/// port's real <see cref="Game"/>/<see cref="Galaxy.Galaxy"/>/entity object graph directly from
/// the on-disk bytes — see `docs/ROADMAP.md` Phase 7's scope note for why this is the priority
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
/// <see cref="Game.Empires"/> unless Empire Data marks that slot `InUse` (see <see cref="Empire"/>
/// 's own doc comment: `Game.Empires` only ever holds real, in-use empires by construction).
/// </summary>
public sealed class SavGameLoader
{
    private readonly Empire[] _empireSlots = BuildPlaceholderSlots();

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

    public Game LoadGame(byte[] data)
    {
        var reader = new SavReader(data);

        LoadHeader(reader);
        var (year, playerOrdinal, scenarioFilename) = LoadEnvironment(reader);
        var galaxy = LoadSector(reader);

        var game = new Game(galaxy) {
            Year = year,
            ScenarioFilename = scenarioFilename,
            CurrentEmpire = ResolveEmpire(playerOrdinal),
        };

        LoadPlanets(reader, galaxy);
        LoadStarbases(reader, galaxy);
        LoadFleets(reader, galaxy);
        LoadStargates(reader, galaxy);
        LoadConstructionSites(reader, galaxy);

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
    /// redundant with `Game.CurrentEmpire`/`NextEmpire()` (see `docs/ROADMAP.md` Phase 7's scope
    /// note). `TimePerTurn`/`AutoSave`/`AsyncTurns`/`PauseActive`/`ReEnterGame` are UI/session
    /// settings with no effect anywhere in this port yet — also read and discarded.
    private static (int Year, int PlayerOrdinal, string ScenarioFilename) LoadEnvironment(SavReader reader)
    {
        var year = reader.ReadWord();
        var playerOrdinal = reader.ReadByte();
        reader.ReadBitSet(2); // EmpiresToMove -- discarded, see doc comment above.
        var scenarioFilename = reader.ReadPascalString(16);
        reader.Skip(2); // TimePerTurn
        reader.Skip(1); // AutoSave
        reader.Skip(1); // AsyncTurns
        reader.Skip(1); // PauseActive
        reader.Skip(1); // ReEnterGame

        return (year, playerOrdinal, scenarioFilename);
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

            index = reader.ReadWord();
        }
    }

    /// <summary>
    /// `LoadFleets` (`LOADSAVE.PAS:227-263`). Reproduces the real load-time quirk verbatim
    /// (`LOADSAVE.PAS:250-256`): if any single axis of `XY`/`Dest` is exactly 0, both coordinates
    /// reset to `(1,1)` — confirmed to fire on a per-component basis, not "both coordinates are
    /// (0,0)". `CommandRecord` order queues are read and discarded (`docs/ROADMAP.md` Phase 7's
    /// tracked gap — no in-memory representation exists yet). `NextOrder`/`OrderData` are Pascal's
    /// own legacy/superseded fields, already dead before this file was even written.
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

            reader.Skip(1); // NextOrder -- legacy, superseded by the CommandRecord queue below
            reader.Skip(6); // OrderData -- same legacy status
            reader.Skip(1); // NPEDataIndex -- Phase 6's Kingdom AI keys fleet state by Fleet reference, not this index
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

            var orderCount = reader.ReadWord();
            reader.Skip(orderCount * 5); // CommandRecord queue -- discarded, see doc comment above.

            index = reader.ReadWord();
        }
    }

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

            index = reader.ReadWord();
        }
    }
}
