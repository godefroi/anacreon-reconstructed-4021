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
}
