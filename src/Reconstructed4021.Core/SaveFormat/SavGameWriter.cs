using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Npe;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.SaveFormat;

/// <summary>
/// This port's minimal `.SAV` write-back: `WriteGame(Game) → byte[]`, the exact section-by-section
/// mirror of <see cref="SavGameLoader"/>'s own `Load*` methods, in the same on-disk order. Built
/// purely to make `LoadGame` (`LOADSAVE.PAS`, unmodified) accept the result — see `docs/ROADMAP.md`'s
/// save/load notes for why this is a test-only verification tool, not a byte-faithful or
/// maintained save format (the native JSON format, <see cref="GameJson"/>, is that): no order-queue
/// reconstruction (none exists in this port), no message content (none exists either), no attempt to
/// reproduce a `Reserved`/pointer field's original garbage bytes (always zero here).
///
/// <see cref="Game.Empires"/> is a permanent roster now (see <see cref="Types.EmpireStatus"/>):
/// nothing this port's own elimination logic does ever removes an empire from it. That alone
/// doesn't eliminate <see cref="EmpireSlotIndex"/>'s orphan-discovery job, though — an empire that
/// was already not-InUse when the `.SAV` file being round-tripped was originally captured was never
/// added to <see cref="Game.Empires"/> in the first place (<see cref="SavGameLoader"/>'s own
/// placeholder gate, unrelated to and unchanged by this redesign), so a Kingdom's own `State` keys,
/// <see cref="Empire.DefeatedBy"/>, a <see cref="NewsItem"/>'s `OtherEmpire`/`Defender`, or a
/// minefield's owner/scouts can all still legitimately reference one — confirmed for real (not just
/// theorized) by <c>SavGameWriterAcceptanceTests</c> failing against an actual reference save's
/// minefield owner the one time the minefield loop was dropped on the assumption a permanent roster
/// made it redundant. <see cref="EmpireSlotIndex"/> is this format's own version of
/// <see cref="GameJson"/>'s `EntityIndex.EmpireId`, capped at exactly 8 slots (`Empire1..Empire8`;
/// Independent is ordinal 8, never a slot) — a real structural limit of the on-disk format itself,
/// matching the fact that real Pascal never has more than 8 empires exist in one game either. An
/// <see cref="Types.EmpireStatus.Eliminated"/> empire's own Empire Data slot is written exactly like
/// a genuinely unused or orphan-only slot (`InUse=false`, all zero) — real Pascal's own on-disk
/// shape for a destroyed empire already looks like that (confirmed by <see cref="SavGameLoader"/>:
/// an `!InUse` slot's other fields are never read into anything), so `.SAV` round-trip is
/// deliberately lossy for that state; <see cref="GameJson"/> is this port's lossless format for it.
/// </summary>
public static class SavGameWriter
{
    public static byte[] WriteGame(Game game)
    {
        var slots = new EmpireSlotIndex(game);
        var objectIds = new ObjectIdIndex(game.Galaxy);
        var visibility = new VisibilityIndex(game);
        var writer = new SavWriter();

        WriteHeader(writer);
        WriteEnvironment(writer, game, slots);
        WriteSector(writer, game.Galaxy, slots);
        WritePlanets(writer, game.Galaxy, slots, visibility);
        WriteStarbases(writer, game.Galaxy, slots, visibility);
        WriteFleets(writer, game.Galaxy, slots, visibility);
        WriteStargates(writer, game.Galaxy, slots, visibility);
        WriteConstructionSites(writer, game.Galaxy, slots, visibility);
        WriteMessages(writer);
        WriteEmpireData(writer, game, slots, objectIds);
        WriteNewsData(writer, game, slots, objectIds);
        WriteNpeData(writer, game, slots, objectIds);

        return writer.ToArray();
    }

    /// `SaveHeader` (`LOADSAVE.PAS:74-88`).
    private static void WriteHeader(SavWriter writer)
    {
        writer.WriteRawString("Anacreon save file v1.3\r\n\x1A      ", 32);
        writer.WriteWord(13);
    }

    /// `SaveEnvironment` (`ENVIRON.PAS:143-159`). `EmpiresToMove`/`TimePerTurn`/`AutoSave`/
    /// `AsyncTurns`/`PauseActive`/`ReEnterGame` have no home in this port (`SavGameLoader`'s own
    /// doc comment) — written as real Pascal's own declared defaults (`ENVIRON.PAS`'s `CONST`
    /// section), harmless either way since `LoadEnvironment` discards all of them right back.
    /// `Player` has no "no one yet" sentinel in the real format (unlike every entity reference,
    /// which has `IDNumber`'s `Index=0`) -- a freshly built <see cref="NewGame.ScenarioLoader"/> game
    /// has real empires but hasn't picked whose turn it is yet, so <see cref="Game.CurrentEmpire"/>
    /// can genuinely be null here even though it never is for anything <see cref="SavGameLoader"/>
    /// itself produced (that always sets it from a real file's own `Player` byte). Falls back to the
    /// first real empire rather than failing outright -- any real empire is a structurally valid
    /// `Player` byte, and this writer's whole bar is "produces a file real Pascal accepts," not
    /// "faithfully reproduces a fact this port's model doesn't actually have yet."
    private static void WriteEnvironment(SavWriter writer, Game game, EmpireSlotIndex slots)
    {
        var player = game.CurrentEmpire ?? game.Empires.FirstOrDefault()
            ?? throw new InvalidOperationException("Cannot write a .SAV file with no empires at all.");

        writer.WriteWord((ushort)game.Year);
        writer.WriteByte((byte)slots.SlotOf(player));
        writer.WriteBitSet([], 2); // EmpiresToMove
        writer.WritePascalString(game.ScenarioFilename ?? "", 16);
        writer.WriteWord(300); // TimePerTurn
        writer.WriteBoolean(true); // AutoSave
        writer.WriteBoolean(false); // AsyncTurns
        writer.WriteBoolean(true); // PauseActive
        writer.WriteBoolean(false); // ReEnterGame
    }

    /// <summary>
    /// `SaveSector` (`GALAXY.PAS:75-88`). `Obj`/`Flts` are always written empty — real Pascal
    /// reads (`LoadSector`) discard them too (`SavGameLoader`'s own doc comment: fully redundant
    /// with the Planets/Starbases/Fleets/Stargates/ConstructionSites sections). `Special`'s
    /// "no mine" sentinel is `NoSRMField = Ord(Indep)*16` (`GALAXY.PAS:50`) — <em>not</em> zero;
    /// writing a plain zero byte for an unmined cell would decode as "mined by Empire1" on the next
    /// load (`Special shr 4` would read 0, `SavGameLoader.LoadSector`'s own `mineOwnerOrdinal != 8`
    /// check), a real, confirmed landmine caught empirically before this writer's first real-Pascal
    /// test run ever passed. `runload.pas`'s own `minedcellcount` checksum field exists specifically
    /// to keep this path covered by the real-Pascal acceptance test going forward, not just at the
    /// point this comment was written.
    /// </summary>
    private static void WriteSector(SavWriter writer, Galaxy.Galaxy galaxy, EmpireSlotIndex slots)
    {
        writer.WriteWord((ushort)galaxy.Size);
        writer.WriteWord((ushort)galaxy.Size);

        for (var x = 0; x <= galaxy.Size; x++) {
            for (var y = 0; y <= galaxy.Size; y++) {
                var coordinate = new Coordinate(x, y);

                writer.WriteIdNumber(new SavIdNumber(SavObjectType.Void, 0)); // Obj
                writer.WriteBitSet([], 1); // Flts

                var mineScoutOrdinals = galaxy.MineScoutedByData.TryGetValue(coordinate, out var scouts)
                    ? scouts.Select(slots.SlotOf)
                    : [];
                writer.WriteBitSet(mineScoutOrdinals, 1);

                var nebula = (int)galaxy.GetNebula(coordinate);
                var mineOwnerSlot = galaxy.GetMineOwner(coordinate) is { } owner ? slots.SlotOf(owner) : 8;
                writer.WriteByte((byte)(nebula | (mineOwnerSlot << 4)));
            }
        }
    }

    private static void WriteShips(SavWriter writer, ShipCounts ships)
    {
        foreach (var type in Enum.GetValues<ShipType>()) {
            writer.WriteWord((ushort)ships[type]);
        }
    }

    private static void WriteCargo(SavWriter writer, CargoHold cargo)
    {
        foreach (var type in Enum.GetValues<CargoType>()) {
            writer.WriteWord((ushort)cargo[type]);
        }
    }

    private static void WriteDefenses(SavWriter writer, DefenseCounts defenses)
    {
        foreach (var type in Enum.GetValues<DefenseType>()) {
            writer.WriteWord((ushort)defenses[type]);
        }
    }

    private static void WriteIndustry(SavWriter writer, IndustryLevels industry)
    {
        foreach (var type in Enum.GetValues<IndustryType>()) {
            writer.WriteWord((ushort)industry[type]);
        }
    }

    /// Inverse of `SavGameLoader.ApplySelfSufficiency` — same 4-nibble `ImpExp` packing.
    private static ushort EncodeSelfSufficiency(SelfSufficiencySettings settings) =>
        (ushort)(settings.Chemical | (settings.Metal << 4) | (settings.Supply << 8) | (settings.Trillum << 12));

    private static void WriteVisibility<T>(SavWriter writer, T entity, VisibilityIndex.Sets<T> sets) where T : notnull
    {
        writer.WriteBitSet(sets.ScoutedBy.TryGetValue(entity, out var scouted) ? scouted : [], 1);
        writer.WriteBitSet(sets.KnownBy.TryGetValue(entity, out var known) ? known : [], 1);
    }

    /// `SavePlanets` (`LOADSAVE.PAS:92-107`) — dense (`FOR i:=1 TO NoOfPlanets`), matching real Pascal.
    private static void WritePlanets(SavWriter writer, Galaxy.Galaxy galaxy, EmpireSlotIndex slots, VisibilityIndex visibility)
    {
        for (var i = 0; i < galaxy.Planets.Count; i++) {
            var planet = galaxy.Planets[i];
            writer.WriteWord((ushort)(i + 1));

            writer.WriteCoordinate(planet.Location);
            writer.WriteByte((byte)slots.SlotOf(planet.Owner));
            WriteVisibility(writer, planet, visibility.Planets);
            writer.WriteByte((byte)planet.Class);
            writer.WriteByte((byte)planet.Type);
            writer.WriteWord(EncodeSelfSufficiency(planet.SelfSufficiency));
            writer.WriteByte((byte)planet.TechLevel);
            writer.WriteByte((byte)planet.Efficiency);
            writer.WriteByte((byte)planet.RevolutionIndex);
            writer.WriteBitSet(planet.IsAddictedToAmbrosia ? [0] : [], 1); // AmbAddict
            writer.WriteWord((ushort)planet.Population);
            WriteShips(writer, planet.Ships);
            WriteCargo(writer, planet.Cargo);
            WriteDefenses(writer, planet.Defenses);
            WriteIndustry(writer, planet.Industry);
            writer.WriteWord((ushort)planet.TrillumReserve);
            writer.WriteZeros(16); // Reserved
            writer.WriteIdNumber(new SavIdNumber(SavObjectType.Void, 0)); // NextID
        }

        writer.WriteWord(0);
    }

    /// `SaveStarbases` (`LOADSAVE.PAS:141-157`).
    private static void WriteStarbases(SavWriter writer, Galaxy.Galaxy galaxy, EmpireSlotIndex slots, VisibilityIndex visibility)
    {
        for (var i = 0; i < galaxy.Starbases.Count; i++) {
            var starbase = galaxy.Starbases[i];
            writer.WriteWord((ushort)(i + 1));

            writer.WriteCoordinate(starbase.Location);
            writer.WriteByte((byte)slots.SlotOf(starbase.Owner));
            WriteVisibility(writer, starbase, visibility.Starbases);
            writer.WriteByte((byte)(starbase.Kind + 20));
            writer.WriteByte((byte)starbase.Type);
            writer.WriteByte((byte)starbase.TechLevel);
            writer.WriteByte((byte)starbase.Efficiency);
            writer.WriteByte((byte)starbase.RevolutionIndex);
            writer.WriteBitSet([], 1); // Special -- no live bit for a starbase
            writer.WriteWord((ushort)starbase.Population);
            WriteShips(writer, starbase.Ships);
            WriteCargo(writer, starbase.Cargo);
            WriteDefenses(writer, starbase.Defenses);
            WriteIndustry(writer, starbase.Industry);

            writer.WriteByte(0); // Move
            // Dest==XY means "not moving" on disk (CreateStarbase's own convention) -- never (0,0),
            // which would falsely read back as a real destination unless it happens to equal XY.
            writer.WriteCoordinate(starbase.Destination ?? starbase.Location);
            writer.WriteByte((byte)starbase.Status);

            writer.WriteZeros(18); // Reserved
            writer.WriteIdNumber(new SavIdNumber(SavObjectType.Void, 0)); // NextID
        }

        writer.WriteWord(0);
    }

    /// <summary>
    /// `SaveFleets` (`LOADSAVE.PAS:188-225`). Always writes an empty order queue (`NoOfComs=0`) --
    /// no in-memory order-queue representation exists in this port (`docs/OPEN_GAPS.md`'s tracked
    /// gap). <see cref="Fleet.Destination"/> null must become `Dest:=XY`, never `(0,0)`:
    /// `LoadFleets`' own per-axis quirk (`LOADSAVE.PAS:250-256`) resets <em>both</em> `XY` and `Dest`
    /// to `(1,1)` the instant either coordinate has a zero component on either field, so writing a
    /// literal `(0,0)` "no destination" sentinel would silently relocate every stationary fleet on
    /// the very next load. `runload.pas`'s own `sumfleetx`/`sumfleety` checksum fields exist
    /// specifically to cover this path in the real-Pascal acceptance test -- see that driver's own
    /// doc comment.
    /// </summary>
    private static void WriteFleets(SavWriter writer, Galaxy.Galaxy galaxy, EmpireSlotIndex slots, VisibilityIndex visibility)
    {
        for (var i = 0; i < galaxy.Fleets.Count; i++) {
            var fleet = galaxy.Fleets[i];
            writer.WriteWord((ushort)(i + 1));

            writer.WriteCoordinate(fleet.Location);
            writer.WriteByte((byte)slots.SlotOf(fleet.Owner));
            writer.WriteBitSet(visibility.Fleets.ScoutedBy.TryGetValue(fleet, out var scouted) ? scouted : [], 1);
            WriteShips(writer, fleet.Ships);
            WriteCargo(writer, fleet.Cargo);
            writer.WriteCoordinate(fleet.Destination ?? fleet.Location);
            writer.WriteByte((byte)fleet.Status);

            var fuel = (int)fleet.Fuel;
            writer.WriteByte((byte)(fuel / 32767));
            writer.WriteInteger((short)(fuel % 32767));

            writer.WriteBitSet(visibility.Fleets.KnownBy.TryGetValue(fleet, out var known) ? known : [], 1);

            writer.WriteByte(0); // NextOrder
            writer.WriteZeros(6); // OrderData
            writer.WriteByte(0); // NPEDataIndex
            writer.WriteZeros(8); // Reserved
            writer.WriteIdNumber(new SavIdNumber(SavObjectType.Void, 0)); // NextID

            writer.WriteWord(0); // order-queue count -- always empty, see doc comment
        }

        writer.WriteWord(0);
    }

    /// `SaveStargates` (`LOADSAVE.PAS:301-317`). `LinkedTo` null means "not yet linked" -- `Limbo`
    /// (0,0) on disk, the opposite convention from Fleet/Starbase's `Destination` (compare this
    /// method's own doc comment on <see cref="WriteFleets"/>): a stargate's "unset" sentinel really
    /// is the zero coordinate, so writing it plainly is correct here specifically.
    private static void WriteStargates(SavWriter writer, Galaxy.Galaxy galaxy, EmpireSlotIndex slots, VisibilityIndex visibility)
    {
        for (var i = 0; i < galaxy.Stargates.Count; i++) {
            var stargate = galaxy.Stargates[i];
            writer.WriteWord((ushort)(i + 1));

            writer.WriteCoordinate(stargate.Location);
            writer.WriteByte((byte)slots.SlotOf(stargate.Owner));
            WriteVisibility(writer, stargate, visibility.Stargates);
            writer.WriteByte((byte)(stargate.Kind + 24));
            writer.WriteCoordinate(stargate.LinkedTo ?? default);
            writer.WriteIdNumber(new SavIdNumber(SavObjectType.Void, 0)); // NextID
        }

        writer.WriteWord(0);
    }

    /// `SaveConstr` (`LOADSAVE.PAS:348-364`).
    private static void WriteConstructionSites(SavWriter writer, Galaxy.Galaxy galaxy, EmpireSlotIndex slots, VisibilityIndex visibility)
    {
        for (var i = 0; i < galaxy.ConstructionSites.Count; i++) {
            var site = galaxy.ConstructionSites[i];
            writer.WriteWord((ushort)(i + 1));

            writer.WriteCoordinate(site.Location);
            writer.WriteByte((byte)slots.SlotOf(site.Owner));
            WriteVisibility(writer, site, visibility.ConstructionSites);
            writer.WriteByte((byte)(site.Building + 19));
            writer.WriteByte((byte)site.YearsToCompletion);
            writer.WriteIdNumber(new SavIdNumber(SavObjectType.Void, 0)); // NextID
        }

        writer.WriteWord(0);
    }

    /// `SaveMessageData` (`MESS.PAS`). No in-memory message concept exists in this port
    /// (`SavGameLoader.LoadMessages`' own doc comment) -- nothing to write, ever.
    private static void WriteMessages(SavWriter writer) => writer.WriteByte(0);

    private static void WriteShellDefensePlan(SavWriter writer, ShellDefensePlan plan)
    {
        foreach (var shell in Enum.GetValues<ShellPosition>()) {
            var distribution = plan[shell];
            foreach (var ship in Enum.GetValues<ShipType>()) {
                writer.WriteByte((byte)distribution[ship]);
            }
        }
    }

    private static void WriteDefenseSettings(SavWriter writer, DefenseSettings settings)
    {
        WriteShellDefensePlan(writer, settings.Fleets);
        WriteShellDefensePlan(writer, settings.Starbases);
    }

    /// Inverse of `SavGameLoader.ReadProbes` -- 10 fixed slots, only the leading
    /// `ProbesInTransit.Count` are `PInTrans`; the rest are zeroed/`Ready` (status 0), matching how a
    /// probe with no in-flight identity of its own (`Empire.ProbesInTransit`'s own doc comment) has
    /// no better slot to occupy than "the next free one."
    private static void WriteProbes(SavWriter writer, IReadOnlyList<Coordinate> inTransit)
    {
        for (var i = 0; i < Empire.MaxProbesInTransit; i++) {
            if (i < inTransit.Count) {
                writer.WriteCoordinate(inTransit[i]);
                writer.WriteByte(1); // PInTrans
            } else {
                writer.WriteCoordinate(default);
                writer.WriteByte(0); // Ready
            }
        }
    }

    private static void WriteNameRecord(SavWriter writer, LocationBookmark bookmark)
    {
        writer.WritePascalString(bookmark.Name, 8);
        writer.WriteCoordinate(bookmark.Location);
        writer.WriteIdNumber(new SavIdNumber(SavObjectType.Void, 0)); // ID -- always the raw-XY form, see SavGameLoader.ReadNameRecord's own doc comment
        writer.WriteZeros(4); // Next
    }

    /// Inverse of `SavGameLoader`'s `_technologyByOrdinal` table -- same raw `TechnologyTypes`
    /// ordinal space (`NoRes,LAM..dis`), checking containment instead of granting.
    private static readonly Func<UnlockedTechnology, bool>?[] _technologyContainsByOrdinal = BuildTechnologyContainsByOrdinal();

    private static Func<UnlockedTechnology, bool>?[] BuildTechnologyContainsByOrdinal()
    {
        var table = new Func<UnlockedTechnology, bool>?[27];

        void SetDefense(int ordinal, DefenseType type) => table[ordinal] = t => t.Defenses.Contains(type);
        void SetShip(int ordinal, ShipType type) => table[ordinal] = t => t.Ships.Contains(type);
        void SetCargo(int ordinal, CargoType type) => table[ordinal] = t => t.Resources.Contains(type);
        void SetConstruction(int ordinal, ConstructionType type) => table[ordinal] = t => t.Constructions.Contains(type);

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

    private static IEnumerable<int> EncodeTechnology(UnlockedTechnology technology)
    {
        for (var ordinal = 0; ordinal < _technologyContainsByOrdinal.Length; ordinal++) {
            if (_technologyContainsByOrdinal[ordinal]?.Invoke(technology) == true) {
                yield return ordinal;
            }
        }
    }

    /// <summary>
    /// `SaveEmpireData` (`LOADSAVE.PAS:412-449`). Always all 8 slots: an <see cref="EmpireStatus.Active"/>
    /// or <see cref="EmpireStatus.PendingElimination"/> <see cref="Game.Empires"/> member gets its
    /// full record; every other slot (genuinely unused, an orphan only <see cref="EmpireSlotIndex"/>
    /// knows about, or an <see cref="EmpireStatus.Eliminated"/> member) gets `InUse=false` and an
    /// otherwise all-zero record -- exactly what real Pascal's own on-disk shape already looks like,
    /// since nothing there ever reads a slot's other fields once `InUse` goes false (see this class's
    /// own doc comment). This makes `.SAV` round-trip deliberately lossy for
    /// <see cref="EmpireStatus.Eliminated"/>: name/tech/capital/etc. don't survive a write-back,
    /// matching Pascal exactly -- <see cref="SaveFormat.GameJson"/> is this port's lossless format for
    /// that state, `.SAV` never was one. The trailing `NoOfNames` byte is unconditional too -- real
    /// `SaveEmpireData`'s own loop has no `InUse` gate anywhere, it always writes the 183-byte record
    /// then a name count (0 for an empty/orphan/eliminated slot, `Names` being `Nil`) for all 8 slots.
    /// Missing that byte for an empty slot was a real, confirmed bug: it silently shifted every
    /// section after Empire Data by one byte per empty slot, caught only once real Pascal ran out of
    /// file partway through NPE Data on a save with fewer than 8 real empires (a 5-empire file, so 3
    /// missing bytes).
    /// </summary>
    private static void WriteEmpireData(SavWriter writer, Game game, EmpireSlotIndex slots, ObjectIdIndex objectIds)
    {
        for (var slot = 0; slot < 8; slot++) {
            var empire = slots.RealEmpireAt(slot);

            if (empire is null or { Status: EmpireStatus.Eliminated }) {
                writer.WriteZeros(183);
                writer.WriteByte(0); // NoOfNames
                continue;
            }

            writer.WriteBoolean(true); // InUse
            writer.WriteBoolean(empire.NpeType is null); // IsAPlayer
            writer.WritePascalString(empire.Name, 32);
            writer.WritePascalString(empire.Password ?? "", 8);
            writer.WriteZeros(2); // TimeLeft

            if (empire.DefeatedBy is { } conqueror) {
                writer.WriteIdNumber(new SavIdNumber(SavObjectType.Void, (byte)slots.SlotOf(conqueror)));
            } else if (empire.Capital is { } capital) {
                writer.WriteIdNumber(objectIds.IdOf((ISectorObject)capital));
            } else {
                writer.WriteIdNumber(new SavIdNumber(SavObjectType.Void, 0));
            }

            WriteDefenseSettings(writer, empire.DefenseSettings);
            WriteProbes(writer, empire.ProbesInTransit);

            writer.WriteZeros(4); // Names
            writer.WriteZeros(4); // LastName

            writer.WriteInteger((short)empire.TotalRevolutionIndex);
            writer.WriteByte((byte)empire.TechnologyLevel);
            writer.WriteBitSet(EncodeTechnology(empire.Technology), 4);
            writer.WriteBoolean(empire.IsEmpress);
            writer.WriteInteger((short)empire.RevolutionFactor);
            writer.WriteWord((ushort)empire.FoundingYear);
            writer.WriteBitSet(empire.LosesIfCapitalConquered ? [0] : [], 1); // CentralEMD
            writer.WriteZeros(14); // Reserved

            writer.WriteByte((byte)empire.Bookmarks.Count);
            foreach (var bookmark in empire.Bookmarks) {
                WriteNameRecord(writer, bookmark);
            }
        }
    }

    /// <summary>
    /// `SaveNewsData` (`NEWS.PAS:331-361`). Real content, not zeroed: `Parm1-3` plus whichever of
    /// `Subject`/`Position` and `OtherEmpire`/`TechGrant` a given <see cref="NewsItem"/> carries are
    /// exactly what real Pascal's own on-disk fields hold (`SavGameLoader.LoadNewsData`'s own doc
    /// comment: `Loc1`/`ID`/`Parm1-3` are the only fields a headline was ever written from, and
    /// `OtherEmpire`/`TechGrant` are just two of those parms re-interpreted) -- cheap to round-trip
    /// faithfully and worth it, unlike Messages (nothing to reconstruct there at all). Only the 8 real
    /// slot ordinals get an empire's own news; an orphan or unused slot writes `count=0` -- a real
    /// save's own on-disk shape always has exactly 8 slots regardless of `InUse`, but this port has no
    /// News list to draw from except <see cref="Game.Empires"/>'s own real members.
    /// </summary>
    private static void WriteNewsData(SavWriter writer, Game game, EmpireSlotIndex slots, ObjectIdIndex objectIds)
    {
        for (var slot = 0; slot < 8; slot++) {
            var empire = slots.RealEmpireAt(slot);
            if (empire is null) {
                writer.WriteWord(0);
                continue;
            }

            writer.WriteWord((ushort)empire.News.Count);
            foreach (var item in empire.News) {
                writer.WriteByte((byte)item.Headline);

                if (item.Position is { } position) {
                    writer.WriteCoordinate(position);
                    writer.WriteIdNumber(new SavIdNumber(SavObjectType.Void, 0));
                } else {
                    writer.WriteCoordinate(default);
                    writer.WriteIdNumber(item.Subject is { } subject ? objectIds.IdOf(subject) : new SavIdNumber(SavObjectType.Void, 0));
                }

                var (parm1, parm2, parm3) = (item.Parm1, item.Parm2, item.Parm3);
                writer.WriteInteger((short)parm1);
                writer.WriteInteger((short)parm2);
                writer.WriteInteger((short)parm3);
                writer.WriteZeros(4); // Next
            }
        }
    }

    /// <summary>
    /// `SaveNPEData` (`LOADSAVE.PAS:467-479`), two parts, matching `SavGameLoader.LoadNpeData`'s own
    /// structure in reverse. Part 1: the fixed 9-entry `NPEDataArray` type-tag table (`Data` pointer
    /// always written empty). Part 2: for each real, non-player empire slot, its variant blob --
    /// Kingdom via <see cref="WriteKingdomBlob"/>, everything else (Pirate/Berserker/Guardian/Trader)
    /// passed through verbatim from <see cref="Game.UnimplementedNpeBlobs"/>, which is exactly why
    /// that dictionary exists on the read side: this writer never needs to understand a personality
    /// it hasn't ported an <see cref="Turns.ITurnHandler"/> for.
    /// </summary>
    private static void WriteNpeData(SavWriter writer, Game game, EmpireSlotIndex slots, ObjectIdIndex objectIds)
    {
        for (var slot = 0; slot < 8; slot++) {
            var empire = slots.RealEmpireAt(slot);
            var typ = empire?.NpeType switch {
                NpeEmpireType.Pirate => 1,
                NpeEmpireType.Kingdom1 => 2,
                NpeEmpireType.Kingdom2 => 3,
                NpeEmpireType.Berserker => 4,
                NpeEmpireType.Guardian => 5,
                NpeEmpireType.Trader => 6,
                _ => 0,
            };
            writer.WriteByte((byte)typ);
            writer.WriteZeros(4); // Data
        }

        writer.WriteByte(0); // Independent's own NPEDataArray entry -- always NoNPE
        writer.WriteZeros(4);

        for (var slot = 0; slot < 8; slot++) {
            var empire = slots.RealEmpireAt(slot);
            if (empire is null || empire.NpeType is null || empire.Status == EmpireStatus.Eliminated) {
                continue; // Not EmpireActive AND NOT EmpirePlayer -- no blob at all, matching the read side.
            }

            switch (empire.NpeType) {
                case NpeEmpireType.Kingdom1 or NpeEmpireType.Kingdom2:
                    WriteKingdomBlob(writer, game, empire, slots, objectIds);
                    break;

                default:
                    writer.WriteBytes(RequireBlob(game, empire));
                    break;
            }
        }
    }

    private static byte[] RequireBlob(Game game, Empire empire) =>
        game.UnimplementedNpeBlobs.TryGetValue(empire, out var blob)
            ? blob
            : throw new InvalidOperationException(
                $"Empire '{empire.Name}' has NpeType {empire.NpeType} but no entry in Game.UnimplementedNpeBlobs " +
                "and no KingdomTurnHandler -- nothing to write for its NPE Data blob.");

    /// <summary>
    /// `Kingdom1DataRecord`/`Kingdom2DataRecord` (`NPETYPES.PAS:136-141`), inverse of
    /// `SavGameLoader.LoadKingdomBlob`. `Midway`/`BlockX`/`BlockY` are confirmed dead/Pirate-only
    /// (that method's own doc comment) -- always written zero. Only the fleets this Kingdom empire
    /// actually has AI state for get a real slot (`Index` byte = the fleet's own on-disk index);
    /// every other one of the 30 fixed slots is all-zero (`Index=0` reads back as "unused",
    /// `SavGameLoader`'s own loop). `State` always emits exactly the 9 canonical ordinals
    /// (`Empire1..Empire8` then `Indep`) regardless of which ones this Kingdom's own dictionary
    /// happens to hold real data for -- matching real Pascal's own fixed-size array; a slot with no
    /// dictionary entry (never yet encountered as an enemy) writes an all-zero
    /// <see cref="StateDeptRecord"/>, which decodes as `PolicyType.None`/all-zero fields on the next
    /// load, not a crash or a wrong empire's data.
    /// </summary>
    private static void WriteKingdomBlob(SavWriter writer, Game game, Empire empire, EmpireSlotIndex slots, ObjectIdIndex objectIds)
    {
        var handler = (KingdomTurnHandler)game.TurnHandlers[empire];

        var fleetSlots = new (int Index, Fleet Fleet, KingdomFleetState State)[30];
        var next = 0;
        foreach (var (fleet, state) in handler.FleetStates) {
            if (next >= fleetSlots.Length) {
                throw new NotSupportedException("A Kingdom empire's own 30-fleet NPE-data slot table is full.");
            }
            fleetSlots[next] = (objectIds.IdOf(fleet).Index, fleet, state);
            next++;
        }

        foreach (var (index, _, state) in fleetSlots) {
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

            if (target is null || !handler.State.TryGetValue(target, out var record)) {
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

        var persona = handler.Persona;
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
    /// Per-kind on-disk `(SavObjectType, 1-based index)` for every <see cref="ISectorObject"/> --
    /// list position plus one, matching the dense-by-list-order write in `WritePlanets` etc. Built up
    /// front since <see cref="Empire.Capital"/>, <see cref="NewsItem.Subject"/>, and a Kingdom's own
    /// `Target`/`HomeBase` all reference an object before its own section is necessarily written.
    /// </summary>
    private sealed class ObjectIdIndex
    {
        private readonly Dictionary<ISectorObject, SavIdNumber> _ids = new();

        public ObjectIdIndex(Galaxy.Galaxy galaxy)
        {
            Add(galaxy.Planets, SavObjectType.Pln);
            Add(galaxy.Starbases, SavObjectType.Base);
            Add(galaxy.Fleets, SavObjectType.Flt);
            Add(galaxy.Stargates, SavObjectType.Gate);
            Add(galaxy.ConstructionSites, SavObjectType.Con);
        }

        private void Add<T>(IReadOnlyList<T> list, SavObjectType type) where T : ISectorObject
        {
            for (var i = 0; i < list.Count; i++) {
                _ids[list[i]] = new SavIdNumber(type, (byte)(i + 1));
            }
        }

        public SavIdNumber IdOf(ISectorObject obj) => _ids[obj];
    }

    /// <summary>
    /// Per-real-empire scouted/known sets, inverted from each empire's own <see cref="Empire.Planets"/>
    /// etc (the perspective real Pascal's per-entity `ScoutedBy`/`KnownBy` bitsets encode). Orphan
    /// empires never contribute here -- nothing in this port ever calls
    /// <see cref="EntityVisibility{T}.MarkKnown"/>/<see cref="EntityVisibility{T}.MarkScouted"/> on one
    /// (they have no home in <see cref="Game.Empires"/> to be iterated from), so any historical
    /// visibility they might once have granted is a real, acceptable gap under this phase's
    /// no-fidelity-beyond-acceptance bar.
    /// </summary>
    private sealed class VisibilityIndex
    {
        public readonly Sets<Planet> Planets;
        public readonly Sets<Starbase> Starbases;
        public readonly Sets<Fleet> Fleets;
        public readonly Sets<Stargate> Stargates;
        public readonly Sets<ConstructionSite> ConstructionSites;

        public VisibilityIndex(Game game)
        {
            Planets = Build(game.Empires, e => e.Planets);
            Starbases = Build(game.Empires, e => e.Starbases);
            Fleets = Build(game.Empires, e => e.Fleets);
            Stargates = Build(game.Empires, e => e.Stargates);
            ConstructionSites = Build(game.Empires, e => e.ConstructionSites);
        }

        private static Sets<T> Build<T>(List<Empire> empires, Func<Empire, EntityVisibility<T>> selector) where T : notnull
        {
            var scoutedBy = new Dictionary<T, HashSet<int>>();
            var knownBy = new Dictionary<T, HashSet<int>>();

            for (var ordinal = 0; ordinal < empires.Count; ordinal++) {
                var visibility = selector(empires[ordinal]);

                foreach (var entity in visibility.Known) {
                    if (!knownBy.TryGetValue(entity, out var set)) {
                        knownBy[entity] = set = [];
                    }
                    set.Add(ordinal);
                }

                foreach (var entity in visibility.Scouted) {
                    if (!scoutedBy.TryGetValue(entity, out var set)) {
                        scoutedBy[entity] = set = [];
                    }
                    set.Add(ordinal);
                }
            }

            return new Sets<T>(scoutedBy, knownBy);
        }

        public readonly record struct Sets<T>(Dictionary<T, HashSet<int>> ScoutedBy, Dictionary<T, HashSet<int>> KnownBy) where T : notnull;
    }

    /// <summary>See this class's own doc comment for the orphan-discovery rationale.</summary>
    private sealed class EmpireSlotIndex
    {
        /// Every empire (real or orphan) assigned a slot, indexed by slot -- <see cref="AnyEmpireAt"/>'s
        /// backing store. A Kingdom's own `State` dictionary needs this: real Pascal's fixed 9-entry
        /// array is keyed by raw ordinal regardless of whether that ordinal is `InUse`, so ordinal *i*
        /// can have a real <see cref="Npe.StateDeptRecord"/> to write even when no real empire (and no
        /// orphan either) ever occupied slot *i* at all -- see <see cref="WriteKingdomBlob"/>, which
        /// falls back to an all-zero record rather than indexing this array, for that exact case.
        private readonly Empire?[] _anyBySlot = new Empire?[8];

        /// Real <see cref="Game.Empires"/> members only, indexed by slot -- null for a slot that's
        /// either genuinely unused or occupied only by an orphan (see <see cref="RealEmpireAt"/>).
        private readonly Empire?[] _realBySlot = new Empire?[8];
        private readonly Dictionary<Empire, int> _slotOf = new();
        private int _nextFreeSlot;

        public EmpireSlotIndex(Game game)
        {
            foreach (var empire in game.Empires) {
                var slot = Assign(empire);
                _realBySlot[slot] = empire;
            }

            // Game.Empires being a permanent roster now (see Types.EmpireStatus) only covers an empire
            // eliminated during *this* port's own runtime -- it does nothing for an empire that was
            // already not-InUse when the .SAV file being round-tripped was originally captured.
            // SavGameLoader's own placeholder gate (LoadEmpireData's "if (!inUse) continue") is
            // unrelated to and unchanged by this redesign: a not-InUse slot's placeholder Empire is
            // never added to Game.Empires, so any of the four references below can still legitimately
            // point at one -- confirmed for real by SavGameWriterAcceptanceTests failing against
            // an actual reference save's minefield owner the one time this was tried without them.
            foreach (var handler in game.TurnHandlers.Values.OfType<KingdomTurnHandler>()) {
                foreach (var empire in handler.State.Keys) {
                    Register(empire);
                }
            }

            foreach (var empire in game.Empires) {
                if (empire.DefeatedBy is { } conqueror) {
                    Register(conqueror);
                }

                foreach (var news in empire.News) {
                    if (news.OtherEmpire is { } otherEmpire) {
                        Register(otherEmpire);
                    }
                    if (news.Defender is { } defender) {
                        Register(defender);
                    }
                }
            }

            foreach (var owner in game.Galaxy.MinefieldData.Values) {
                Register(owner);
            }
            foreach (var scouts in game.Galaxy.MineScoutedByData.Values) {
                foreach (var scout in scouts) {
                    Register(scout);
                }
            }
        }

        private int Assign(Empire empire)
        {
            if (_nextFreeSlot >= 8) {
                throw new NotSupportedException("The .SAV format supports at most 8 empires (Empire1..Empire8).");
            }

            var slot = _nextFreeSlot++;
            _slotOf[empire] = slot;
            _anyBySlot[slot] = empire;
            return slot;
        }

        /// Orphans (discovered here, never in <see cref="Game.Empires"/>) get a slot number for
        /// <see cref="SlotOf"/>/<see cref="AnyEmpireAt"/> to resolve consistently, but deliberately
        /// never populate <see cref="_realBySlot"/> -- <see cref="RealEmpireAt"/> must keep returning
        /// null for that slot so Empire Data/News/NPE Data all write it as "unused," matching real
        /// Pascal's own on-disk shape for a defeated-and-removed empire (this class's own doc comment).
        private void Register(Empire empire)
        {
            if (empire.IsIndependent || _slotOf.ContainsKey(empire)) {
                return;
            }

            Assign(empire);
        }

        /// <c>8</c> for <see cref="Empire.Independent"/>, matching <see cref="SavGameLoader"/>'s own convention.
        public int SlotOf(Empire empire) => empire.IsIndependent ? 8 : _slotOf[empire];

        /// The real (in <see cref="Game.Empires"/>) empire occupying this slot, or null if it's unused or an orphan-only slot.
        public Empire? RealEmpireAt(int slot) => _realBySlot[slot];

        /// Whichever empire (real or orphan) occupies this slot, or null if truly unused -- see <see cref="WriteKingdomBlob"/>.
        public Empire? AnyEmpireAt(int slot) => _anyBySlot[slot];
    }
}
