# Anacreon .SAV File Format

Binary save file format for Anacreon v1.31 (Turbo Pascal), save format version 13.

This document describes the **on-disk byte layout**, not the Pascal data structures used to
produce it. Where the two differ (e.g. a field that is 2 bytes in the file because Turbo
Pascal picked a smaller storage type than its declared range suggests), the byte layout wins.
Every section below cites the exact Pascal source file and line so the layout can be
re-derived or re-checked against `reference/DOSAnacreonSource131/`.

Every claim in this document was checked against real save files in `reference/saves/`, by
writing a byte-exact parser and walking it from offset 0 to EOF:

| File | Scenario | What it added |
|---|---|---|
| `INTRO_1.SAV` | INTRO, turn 1 | baseline: header, environment, sector, planets, empire data, Kingdom2 NPE |
| `IMPERIUM_1.SAV` | IMPERIUM, turn 1 | all 8 empire slots as active players |
| `AFTERMAT_1.SAV` | AFTERMAT, turn 1 | a second independent multiplayer configuration |
| `GAUNTLET_1.SAV` | GAUNTLET, turn 1 | 10 populated `StarbaseRecord`s; a `PirateDataRecord` NPE |
| `PRINCES_1.SAV` | PRINCES (hand-fixed, see below), turn 1 | another multiplayer configuration |
| `INTRO_2.SAV` | INTRO, after ~2 played turns | real `FleetRecord`s (9 active fleets) and `NameRecord`s (player used the in-game "Name" command on two fleets) — this is what caught the string-tail-garbage issue below |
| `Confront_1.SAV` | Confront (`pack_1`), fresh start | a `GuardianNPE` and a `BerserkerNPE` blob, both active |
| `Confront_2.SAV` | Confront, a few turns in | a populated `ConstrRecord` (player-built outpost) and 64 active fleets |
| `FLEET_ORDERS.SAV` | (edited from an existing save) | a fleet with a real, non-empty `CommandRecord` queue, and a sent in-game message (`MessageRecord`) |

See [Verification](#verification) for what each file did and didn't exercise, and what's still
untested.

The conversion tool is `scripts/savtool.ps1` (`to-json` / `to-sav` / `check` subcommands; run
via `pwsh scripts/savtool.ps1 <command> ...`). An equivalent `scripts/savtool.py` also exists.
Both were checked with a byte-identical round trip (`.SAV` → JSON → `.SAV`) against every file
in the table above.

## Conventions

- All multi-byte integers are **little-endian** (native x86 byte order; Turbo Pascal never
  byte-swaps).
- The file is read/written with `BlockRead`/`BlockWrite` on a file opened via `Reset(SF,1)` /
  `Rewrite(SF,1)` (`ReadVariable`/`WriteVariable`, `reference/DOSAnacreonSource131/DOS2.PAS:80-102`)
  — a **raw memory copy**, `SizeOf(Var)` bytes, with no framing, chunking, or byte-swapping of
  its own. This is why the on-disk layout is exactly the in-memory Pascal record layout: there
  is no serialization step to add or remove padding.
- **No inter-field padding.** Turbo Pascal packs record fields back-to-back with no alignment
  padding (there is no `{$A+}` alignment directive in effect anywhere in this codebase).
  Confirmed empirically: `PlanetRecord`'s declared field sizes sum to exactly 89 bytes, and
  parsing 50 planets at that stride from the reference file lands on a clean, strictly
  increasing `1..50` index sequence terminated by a `0` word.
- **Ordinal/subrange types are stored in the smallest type that fits their declared range** —
  this is a real Turbo Pascal compiler optimization, not a simplification of this document.
  Concretely: `Coordinate = 0..MaxSizeOfGalaxy` (`0..100`, `GALAXY.PAS:20`) fits in a `Byte`, so
  `XYCoord` (`GALAXY.PAS:22-24`) is **2 bytes**, not 4 — a draft of this document once assumed
  `Word` fields here and was wrong. Enumerated types with ≤256 members are 1 byte. Sets are
  sized to the number of bits needed to cover their base type's ordinal range, rounded up to a
  whole byte (see [Sets](#sets) below).
- **Pointers are 4 bytes** (Turbo Pascal real-mode far pointers: 2-byte offset + 2-byte segment),
  for both the generic `Pointer` type and typed `^T` pointers. Several saved records contain
  pointer fields (linked-list `Next`, `NPEDataRecord.Data`) that are written as part of a
  whole-record `BlockWrite` — **the actual DOS-session heap address is written to disk as
  garbage bytes.** They are meaningless on read and are never dereferenced by `LoadGame`; the
  loader always reconstructs the real pointers from the flat data (see notes per-section
  below). Treat them as 4 opaque bytes.

### Strings

Turbo Pascal `STRING[N]` is a fixed `N+1`-byte buffer: 1 length byte followed by `N` character
slots (only the first `Length` are meaningful; the rest are leftover heap/stack bytes, not
zeroed). **Whether the length byte is written to the file depends on exactly what was passed to
`WriteVariable`, not on some general rule:**

- Passed as a **whole variable** (`WriteVariable(SF, S, SizeOf(S))`) — the length byte **is**
  included. This is the common case: `ScenaFilename` in the Environment section
  (`ENVIRON.PAS:151`), `EmpireName`/`Pass` inside `EmpireDataRecord`, `Name` inside
  `NameRecord`, and message/news lines.
- Passed starting at the **first character** (`WriteVariable(SF, S[1], N)`) — the length byte is
  **skipped**, and exactly `N` raw character bytes are written with no length prefix at all.
  The **only** place this happens is the file signature in the header
  (`LOADSAVE.PAS:84`, using `Sign[1]`). This is the origin of "strings aren't length-prefixed"
  as a rule of thumb — it's true for exactly one field in the whole format.

So: **default to length-byte + fixed buffer; the header signature is the sole exception.**

The unused tail bytes are not reliably zero — confirmed in a real played save
(`reference/saves/INTRO_2.SAV`), where a `NameRecord.Name` of `"F1"` (a player-named fleet) has
genuine non-zero heap garbage in its remaining 6 buffer bytes. A fresh, just-generated save
(`INTRO_1.SAV`) happens to have all-zero tails instead, because `InitializeUniverse`
(`LOADSAVE.PAS:513-514`) zero-fills the whole in-memory universe before anything is written into
it — but that's a property of freshly-initialized memory, not of the file format, and a
converter that assumes zero tails will not reproduce a real save byte-for-byte.
`scripts/savtool.ps1`/`.py` handle this by keeping each string as `{text, tail_hex}` rather than
just the decoded text.

### Sets

Turbo Pascal `SET OF T` is a bitset sized to `ceil(bits / 8)` bytes, where `bits` is the number
of ordinal values in `T`'s declared range (bit 0 = lowest ordinal). Sizes used in this format:

| Set type | Base range | Members | Bytes |
|---|---|---|---|
| `ScoutSet` | `Empire1..Empire8` | 8 | 1 |
| `EmpireSet` | `Empire` (`Empire1..Indep`) | 9 | 2 |
| `SetOfSpecialConditions` | `SpecialConditions` | 5 | 1 |
| `SetOfEmpireModifiers` | `EmpireModifiers` | 7 | 1 |
| `TechnologySet` | `TechnologyTypes` | 27 | 4 |

(`TYPES.PAS:107-108,118,165-166`)

### Enums

Enumerated types are stored as their 0-based ordinal in the smallest type that fits (always 1
byte for every enum in this format — none has more than 256 members). See
[Enum reference](#enum-reference) at the end of this document for full member lists and
ordinals.

---

## File Layout

```
[Header] [Environment] [Sector] [Planets] [Starbases] [Fleets]
[Stargates] [Constructions] [Messages] [Empire Data] [News] [NPE Data]
```

Driven by `LoadGame`/`SaveGame`, `LOADSAVE.PAS:572-714`.

---

## Header — 34 bytes

`SaveHeader`/`LoadHeader`, `LOADSAVE.PAS:62-88`.

| Offset | Size | Field | Notes |
|---|---|---|---|
| 0–31 | 32 | Signature | Raw bytes, **no length prefix** (see [Strings](#strings)): `"Anacreon save file v1.3\r\n\x1A"` + 6 spaces. Constant `SFSignature`, `LOADSAVE.PAS:54`. |
| 32–33 | 2 | Version (Word) | `CurrentSFVersion = 13`, `LOADSAVE.PAS:55`. This document describes version 13 only. |

---

## Environment — 28 bytes

`SaveEnvironment`/`LoadEnvironment`, `ENVIRON.PAS:125-159`. Confirmed byte-for-byte against
`INTRO_1.SAV` (year 4021, scenario `INTRO.SCN`, etc.).

| Offset | Size | Field | Type | Notes |
|---|---|---|---|---|
| 0–1 | 2 | Year | Word | |
| 2 | 1 | Player | Empire (enum) | Current player empire |
| 3–4 | 2 | EmpiresToMove | EmpireSet | Set of empires still to move this turn |
| 5–21 | 17 | ScenaFilename | `String16` | Length byte + 16 chars, e.g. `INTRO.SCN` |
| 22–23 | 2 | TimePerTurn | Word | Seconds |
| 24 | 1 | AutoSave | Boolean | |
| 25 | 1 | AsyncTurns | Boolean | |
| 26 | 1 | PauseActive | Boolean | |
| 27 | 1 | ReEnterGame | Boolean | |

---

## Sector — variable, `4 + 5×(SizeOfGalaxy+1)²` bytes

`SaveSector`/`LoadSector`, `GALAXY.PAS:75-105`.

```
[SizeOfGalaxy: Word] [SizeOfGalaxy: Word]   (same value, written twice — not X and Y)
[Row 0: SectorRecord × (SizeOfGalaxy+1)]
[Row 1: SectorRecord × (SizeOfGalaxy+1)]
...
[Row SizeOfGalaxy: SectorRecord × (SizeOfGalaxy+1)]
```

The galaxy is always square; the field is written twice for historical reasons but both
copies hold the same value (`GALAXY.PAS:81-82`). There are `SizeOfGalaxy + 1` rows (0-indexed,
inclusive), each `SizeOfGalaxy + 1` records wide.

**SectorRecord — 5 bytes** (`GALAXY.PAS:32-38`):

| Offset | Size | Field | Type | Notes |
|---|---|---|---|---|
| 0–1 | 2 | Obj | IDNumber | Object occupying this sector (see [IDNumber](#idnumber-2-bytes)) |
| 2 | 1 | Flts | ScoutSet | Empires with a fleet here |
| 3 | 1 | MineScout | ScoutSet | Empires that know about a mine here |
| 4 | 1 | Special | Byte | Low nibble = nebula type (`NebulaTypes`); high nibble = mine placed by empire index |

---

## Planets — variable, `2 + N×91` bytes

`SavePlanets`/`LoadPlanets`, `LOADSAVE.PAS:92-137`. **Empirically confirmed**: 50 planets,
strictly sequential indices 1–50, `PlanetRecord` = 89 bytes.

```
[Index: Word] [PlanetRecord] [Index: Word] [PlanetRecord] ... [0: Word]
```

Unlike every other indexed section, planets are written **densely**: `FOR i:=1 TO NoOfPlanets`
(`LOADSAVE.PAS:97`), so indices are always exactly `1, 2, ..., NoOfPlanets`, not sparse.

**PlanetRecord — 89 bytes** (`DATASTRC.PAS:23-48`):

| Offset | Size | Field | Type | Notes |
|---|---|---|---|---|
| 0–1 | 2 | XY | XYCoord | Location |
| 2 | 1 | Emp | Empire | Owner |
| 3 | 1 | ScoutedBy | ScoutSet | |
| 4 | 1 | KnownBy | ScoutSet | |
| 5 | 1 | Cls | WorldClass | 21-member enum |
| 6 | 1 | Typ | WorldTypes | 21-member enum |
| 7–8 | 2 | ImpExp | Integer (`SelfSuffConditions`) | Import/export bitmask per raw material |
| 9 | 1 | Tech | TechLevel | 11-member enum |
| 10 | 1 | Eff | Index (0–100) | Efficiency % |
| 11 | 1 | RevIndex | Index (0–100) | Revolution index |
| 12 | 1 | Special | SetOfSpecialConditions | |
| 13–14 | 2 | Pop | Word (`Population`, 0–9999) | |
| 15–28 | 14 | Ships | ShipArray: 7 × Word | Indexed by `ShipTypes` = `fgt,hkr,jmp,jtn,pen,ssp,trn` |
| 29–42 | 14 | Cargo | CargoArray: 7 × Word | Indexed by `CargoTypes` = `men,nnj,amb,che,met,sup,tri` |
| 43–50 | 8 | Defns | DefnsArray: 4 × Word | Indexed by `DefnsTypes` = `LAM,def,GDM,ion` |
| 51–68 | 18 | Indus | IndusArray: 9 × Word | Indexed by `IndusTypes` (see [enum reference](#industypes)) |
| 69–70 | 2 | TriReserve | Word | Trillum reserves |
| 71–86 | 16 | Reserved | opaque bytes | `ARRAY[1..16] OF Byte`, not otherwise used at save time |
| 87–88 | 2 | NextID | IDNumber | Unused linked-list field (see [notes](#idnumber-2-bytes)) |

---

## Starbases — variable, `2 + N×92` bytes

`SaveStarbases`/`LoadStarbases`, `LOADSAVE.PAS:141-184`. Written **sparsely**: only indices in
`SetOfActiveStarbases` are emitted (`LOADSAVE.PAS:146-147`), so index gaps are normal. Bounded
by `MaxNoOfStarbases = 100`. **Empirically confirmed**: `GAUNTLET_1.SAV` has 10 populated
starbases at this exact 90-byte stride, parsing cleanly through to the terminator.

```
[Index: Word] [StarbaseRecord] ... [0: Word]
```

**StarbaseRecord — 90 bytes** (`DATASTRC.PAS:54-79`):

| Offset | Size | Field | Type | Notes |
|---|---|---|---|---|
| 0–1 | 2 | XY | XYCoord | |
| 2 | 1 | Emp | Empire | |
| 3 | 1 | ScoutedBy | ScoutSet | |
| 4 | 1 | KnownBy | ScoutSet | |
| 5 | 1 | STyp | StarbaseTypes | 4-member enum: `cmm,frt,cmp,out` |
| 6 | 1 | Typ | WorldTypes | |
| 7 | 1 | Tech | TechLevel | |
| 8 | 1 | Eff | Index | |
| 9 | 1 | RevIndex | Index | |
| 10 | 1 | Special | SetOfSpecialConditions | |
| 11–12 | 2 | Pop | Word | |
| 13–26 | 14 | Ships | ShipArray | |
| 27–40 | 14 | Cargo | CargoArray | |
| 41–48 | 8 | Defns | DefnsArray | |
| 49–66 | 18 | Indus | IndusArray | |
| 67 | 1 | Move | Byte | Years until the starbase moves |
| 68–69 | 2 | Dest | XYCoord | Destination |
| 70 | 1 | Status | FleetStatus | 4-member enum |
| 71–88 | 18 | Reserved | opaque bytes | `ARRAY[1..18] OF Byte` |
| 89–90 | 2 | NextID | IDNumber | |

---

## Fleets — variable

`SaveFleets`/`LoadFleets`, `LOADSAVE.PAS:188-278`. Written sparsely over
`1..MaxNoOfFleets (240)`, indices in `SetOfActiveFleets` only.

```
[Index: Word] [FleetRecord] [OrderCount: Word] [CommandRecord × OrderCount] ... [0: Word]
```

**Empirically confirmed**: `INTRO_2.SAV` (the same INTRO save after ~2 played turns) has 9
active fleets (indices 232–240) at this exact 57-byte stride, all with an order count of `0`
(direct moves don't populate the compiled order queue — see the note below). `FLEET_ORDERS.SAV`
confirms a real, non-empty `CommandRecord` queue: fleet 240 has 4 queued orders (`DestCOM`,
`WaitCOM`, `DestCOM`, `RepeatCOM`) from typing `DESTINATION 1,1`, a wait command, `DESTINATION
2,2`, and `REPEAT` — see [CommandRecord](#commandrecord) below for the full decode.

**The order-count word is always written, even when it's zero** (`LOADSAVE.PAS:207-219`): the
`IF FleetNextStatement(FltID)>0` branch writes the real count, the `ELSE` branch explicitly
writes `0`. A parser that skips the count word for orderless fleets will desync on the very
first one.

**FleetRecord — 57 bytes** (`DATASTRC.PAS:86-111`):

| Offset | Size | Field | Type | Notes |
|---|---|---|---|---|
| 0–1 | 2 | XY | XYCoord | Current location |
| 2 | 1 | Emp | Empire | |
| 3 | 1 | ScoutedBy | ScoutSet | |
| 4–17 | 14 | Ships | ShipArray | |
| 18–31 | 14 | Cargo | CargoArray | |
| 32–33 | 2 | Dest | XYCoord | |
| 34 | 1 | Status | FleetStatus | |
| 35 | 1 | FuelHigh | Byte | High byte of fuel (fuel = `FuelHigh*MaxInt + Fuel`) |
| 36–37 | 2 | Fuel | Integer | Low part of fuel |
| 38 | 1 | KnownBy | ScoutSet | |
| 39 | 1 | NextOrder | Byte | Legacy in-memory order-queue index; superseded by the `CommandRecord` list that follows and not meaningful on load |
| 40–45 | 6 | OrderData | opaque bytes | `ARRAY[1..6] OF Byte`, same legacy status as `NextOrder` |
| 46 | 1 | NPEDataIndex | Byte | |
| 47–54 | 8 | Reserved | opaque bytes | `ARRAY[1..8] OF Byte` |
| 55–56 | 2 | NextID | IDNumber | |

**Load-time quirk, not a format rule**: if either coordinate of `XY` or `Dest` is `0` after
load, `LoadFleets` resets both to `(1,1)` (`LOADSAVE.PAS:250-256`). This happens after reading,
so a `.SAV` can legitimately contain `(0,0)` on disk — a converter should not "fix" it, since
that would not match what the original file bytes said.

**CommandRecord — 5 bytes** (variant record, `ORDERS.PAS:37-53`):

| Offset | Size | Field | Notes |
|---|---|---|---|
| 0 | 1 | Typ | `CommandTypes`: `NoCOM,DestCOM,TransCOM,RepeatCOM,AbortCOM,SweepCOM,StopCOM,WaitCOM` |
| 1–4 | 4 | (variant) | Overlaid union, selected by `Typ`: `DestCOM` uses `Loc: Location` (`XYCoord`+`IDNumber`, all 4 bytes used, `ORDERS.PAS:212-213`); `TransCOM` uses `Res: ResourceTypes` (1 byte) + `Trns: Integer` (2 bytes) — 3 bytes used, 1 trailing byte unused/garbage (`ORDERS.PAS:206-208`); other `Typ` values don't use the variant at all, and it's left holding whatever the previous write put there |

**Empirically confirmed** (`FLEET_ORDERS.SAV`, fleet 240): typing `DESTINATION 1,1` then `DESTINATION
2,2` produced two `DestCOM` records with `variant_hex` `000002bc` and `00000282`. Both decode as
`XY = Limbo (0,0)` + `ID = {objType: 2 (Pln), index: 188}` and `{2, 130}` respectively — **not**
the literal typed coordinates. This is correct, confirmed game behavior, not a parsing bug:

- Destination coordinates typed as `X,Y` are **relative to the issuing empire's capital**, not
  absolute galaxy coordinates: `AbsoluteX(X) = X + CapX`, `AbsoluteY(Y) = CapY - Y`
  (`PRIMINTR.PAS:1118-1138`). The capital here is planet 1 at absolute `(18, 21)`, so `1,1` → 
  `(19, 20)` and `2,2` → `(20, 19)`.
- `GetLocation` (`PRIMINTR.PAS:1530-1573`) then calls `GetObject` on that absolute coordinate
  (`PRIMINTR.PAS:202-206`, a direct read of `SectorRecord.Obj`). **If an object already occupies
  that cell, its `IDNumber` is stored in `Loc.ID` and `Loc.XY` is left at its `Limbo` sentinel
  `(0,0)`** (`GALAXY.PAS:49`); only when the cell is empty does `Loc.XY` get the literal
  coordinate and `Loc.ID` stay at `EmptyQuadrant` (`ObjTyp: Void, Index: 0`, `TYPES.PAS:176`).
  Reading the raw `SectorRecord.Obj` at `(19, 20)` and `(20, 19)` directly from this save's Sector
  section gives exactly `(2, 188)` and `(2, 130)` — real planets happened to be sitting at both
  resolved absolute coordinates, even though `(1, 1)` and `(2, 2)` themselves (as literal galaxy
  coordinates) are empty.

Net effect: a `DestCOM` order's variant is either a raw destination coordinate or a resolved
object reference, decided per-order by what's actually at the target cell at compile time — both
halves of the `Location` union round-trip correctly either way, so no special-casing is needed in
a converter beyond preserving the 4 bytes verbatim.

---

## Stargates — variable, `2 + N×12` bytes

`SaveStargates`/`LoadStargates`, `LOADSAVE.PAS:282-317`. Sparse over `1..MaxNoOfStargates (50)`.
Zero gates in every reference save so far (gate tech is expensive to reach); layout below is
derived, not independently exercised.

```
[Index: Word] [StargateRecord] ... [0: Word]
```

**StargateRecord — 10 bytes** (`DATASTRC.PAS:117-127`, called `GateRecord` in its own end
comment):

| Offset | Size | Field | Type | Notes |
|---|---|---|---|---|
| 0–1 | 2 | XY | XYCoord | |
| 2 | 1 | Emp | Empire | |
| 3 | 1 | ScoutedBy | ScoutSet | |
| 4 | 1 | KnownBy | ScoutSet | |
| 5 | 1 | GTyp | StargateTypes | 3-member enum: `gte,lnk,dis` |
| 6–7 | 2 | Dest | XYCoord | Destination gate's location |
| 8–9 | 2 | NextID | IDNumber | |

---

## Constructions — variable, `2 + N×11` bytes

`SaveConstr`/`LoadConstr`, `LOADSAVE.PAS:321-364`. Sparse over `1..MaxNoOfConstrSites (50)`.

**Empirically confirmed**: `Confront_2.SAV` has one active construction site (index 50) after
the player started building an outpost — `CTyp = 23`, which is `out` (Outpost) at its absolute
`TechnologyTypes` ordinal (`TYPES.PAS:66`, not the 0-based position within the `ConstrTypes`
subrange), and `Emp = 0` (`Empire1`, the player). Both match what was actually built and by whom.

```
[Index: Word] [ConstrRecord] ... [0: Word]
```

**ConstrRecord — 9 bytes** (`DATASTRC.PAS:133-144`):

| Offset | Size | Field | Type | Notes |
|---|---|---|---|---|
| 0–1 | 2 | XY | XYCoord | |
| 2 | 1 | Emp | Empire | Constructing empire |
| 3 | 1 | ScoutedBy | ScoutSet | |
| 4 | 1 | KnownBy | ScoutSet | |
| 5 | 1 | CTyp | ConstrTypes | 8-member enum: `SRM,cmm,frt,cmp,out,gte,lnk,dis` |
| 6 | 1 | TimeToCompletion | Byte | Years remaining |
| 7–8 | 2 | NextID | IDNumber | |

---

## Messages — variable

`SaveMessageData`/`LoadMessageData`, `MESS.PAS:252-367`.

**Empirically confirmed**: `FLEET_ORDERS.SAV` has one real in-game message, sent from Empire1
(the player) to Empire3 (`Recipient` set contains index `2`), one line of text (`"yo, this is a
message"`, correctly decoded from its `LineStr`), `Read`/`Intercepted` both false, and a
plausible non-null `MesText.FirstLine`/`LastLine` pointer pair (garbage on disk as documented,
but present and non-zero as expected for a populated message rather than an empty one).

```
[NoOfMessages: Byte]
[MessageRecord] [NoOfLines: Byte] [Line: LineStr]×NoOfLines
[MessageRecord] [NoOfLines: Byte] [Line: LineStr]×NoOfLines
...
```

**MessageRecord — 23 bytes**, written whole including its pointer fields (`MESS.PAS:344`,
`MessageRecord` declared `MESS.PAS:23-33`):

| Offset | Size | Field | Type | Notes |
|---|---|---|---|---|
| 0 | 1 | Sender | Empire | |
| 1 | 1 | Recipient | ScoutSet | |
| 2 | 1 | ReadBy | ScoutSet | |
| 3 | 1 | Read | Boolean | |
| 4 | 1 | Intercepted | Boolean | |
| 5–6 | 2 | MesText.NoOfLines | Word | From embedded `TextStructure` (`TEXTSTRC.PAS:26-30`); redundant with the `NoOfLines` byte written separately just after this record |
| 7–10 | 4 | MesText.FirstLine | opaque (pointer) | Live heap pointer, garbage on disk, ignored on load |
| 11–14 | 4 | MesText.LastLine | opaque (pointer) | Same |
| 15–18 | 4 | Next | opaque (pointer) | Same — the linked list is rebuilt from read order |
| 19–22 | 4 | Prev | opaque (pointer) | Same |

Each line is a `LineStr` (`STRING[80]`) written whole: **81 bytes**, length byte included
(`MESS.PAS:299,305,359`).

---

## Empire Data — fixed 8 empires, each variable-length

`SaveEmpireData`/`LoadEmpireData`, `LOADSAVE.PAS:368-449`. **Empirically confirmed** against
`INTRO_1.SAV`: `EmpireDataRecord` = 183 bytes exactly, decoding to the real, sane empire names
(`Player_empire`, `Trantor`, `Lazarus`, `Freberon`, `First Sun`), matching capital planet
indices (1–5), matching founding year (4021, equal to the Environment section's `Year`), and 3
correctly-inactive trailing empire slots. `IMPERIUM_1.SAV` additionally confirms all 8 slots
active as players simultaneously (`InUse`/`IsAPlayer` both true in every slot), and
`INTRO_2.SAV` confirms a non-empty `Names` list (`NameRecord`, below).

For each of `Empire1..Empire8`, in order:

```
[EmpireDataRecord] [NoOfNames: Byte] [NameRecord × NoOfNames]
```

**EmpireDataRecord — 183 bytes** (`DATASTRC.PAS:177-204`):

| Offset | Size | Field | Type | Notes |
|---|---|---|---|---|
| 0 | 1 | InUse | Boolean | |
| 1 | 1 | IsAPlayer | Boolean | |
| 2–34 | 33 | EmpireName | `String32` | Length byte + 32 chars |
| 35–43 | 9 | Pass | `String8` | Length byte + 8 chars |
| 44–45 | 2 | TimeLeft | Integer | |
| 46–47 | 2 | Capital | IDNumber | |
| 48–117 | 70 | DefenseSettings | DefenseRecord | See below |
| 118–147 | 30 | Probe | ProbeArray: 10 × ProbeRecord | See below |
| 148–151 | 4 | Names | opaque (pointer) | Head of the name linked list — garbage on disk; the real list follows immediately as `NameRecord × NoOfNames` |
| 152–155 | 4 | LastName | opaque (pointer) | Same |
| 156–157 | 2 | TotalRevIndex | Integer | |
| 158 | 1 | TechnologyLevel | TechLevel | |
| 159–162 | 4 | Technology | TechnologySet | 27-bit set, see [Sets](#sets) |
| 163 | 1 | IsAnEmpress | Boolean | |
| 164–165 | 2 | RevFactor | Integer | |
| 166–167 | 2 | Founding | Word | Year founded |
| 168 | 1 | Modifiers | SetOfEmpireModifiers | |
| 169–182 | 14 | Reserved | opaque bytes | `ARRAY[1..14] OF Byte` |

**DefenseRecord — 70 bytes** (`DATASTRC.PAS:165-168`): two back-to-back
`DefenseDistributionArray` (shell defense, then starbase defense), each `ARRAY[ShellPos,
ShipTypes] OF Index` = 5 shells × 7 ship types × 1 byte = 35 bytes.

**ProbeRecord — 3 bytes** (`DATASTRC.PAS:170-173`): `Dest: XYCoord` (2) + `Status: ProbeStatus`
(1, 4-member enum). `ProbeArray` is 10 of these = 30 bytes.

**NameRecord — 17 bytes**, written whole including its pointer field
(`DATASTRC.PAS:152-157`, write site `LOADSAVE.PAS:441`):

| Offset | Size | Field | Type | Notes |
|---|---|---|---|---|
| 0–8 | 9 | Name | `String8` | Length byte + 8 chars |
| 9–12 | 4 | Coord.XY | XYCoord | From embedded `Location` (`GALAXY.PAS:26-29`) |
| 11–12 | 2 | Coord.ID | IDNumber | |
| 13–16 | 4 | Next | opaque (pointer) | Garbage on disk; list order on disk **is** list order — no reconstruction needed beyond walking `NoOfNames` records |

**Empirically confirmed**: `INTRO_2.SAV`'s player empire has `NoOfNames = 2` (two fleets named
`"F1"`/`"F2"` via the in-game "Name" command), decoding to legible names, correct `Coord.ID`
values pointing at real fleet indices (240 and 239, both present in that save's `fleets`
section), and a real non-null `Next` pointer chaining the first record to the second. This is
also where the string-tail-garbage byte, noted under [Strings](#strings), was caught: `Name`'s
unused buffer bytes are non-zero heap leftovers here, not zero.

---

## News — fixed 8 empires, each variable-length

`SaveNewsData`/`LoadNewsData`, `NEWS.PAS:266-362`. `INTRO_1.SAV` has 0 news items for every
empire, so only the per-empire count word was exercised.

For each of `Empire1..Empire8`, in order:

```
[NoOfItems: Word] [NewsRecord × NoOfItems]
```

**NewsRecord — 15 bytes**, written whole including its pointer field (`NEWS.PAS:113-122`, write
site `NEWS.PAS:356`):

| Offset | Size | Field | Type | Notes |
|---|---|---|---|---|
| 0 | 1 | Headline | NewsTypes | Large enum (~90 members), see `NEWS.PAS:21-109` |
| 1–2 | 2 | Loc1.XY | XYCoord | From embedded `Location` |
| 3–4 | 2 | Loc1.ID | IDNumber | |
| 5–6 | 2 | Parm1 | Integer | Meaning depends on `Headline` |
| 7–8 | 2 | Parm2 | Integer | |
| 9–10 | 2 | Parm3 | Integer | |
| 11–14 | 4 | Next | opaque (pointer) | Garbage on disk; list is rebuilt from read order |

---

## NPE Data

`SaveNPEData`/`LoadNPEData`, `LOADSAVE.PAS:453-479`. Two parts:

### 1. NPEData array — fixed 45 bytes

Written as one raw blit of the whole global array (`LOADSAVE.PAS:473`,
`NPETypes: NPEData: NPEDataArray`, `NPETYPES.PAS:161`).

```
[NPEDataRecord × 9]   (indexed Empire1..Empire8, then Indep — Indep is always NoNPE)
```

**NPEDataRecord — 5 bytes** (`NPETYPES.PAS:29-32`):

| Offset | Size | Field | Type | Notes |
|---|---|---|---|---|
| 0 | 1 | Typ | NPEmpireTypes | `NoNPE,PirateNPE,Kingdom1NPE,Kingdom2NPE,BerserkerNPE,GuardianNPE,TraderNPE` |
| 1–4 | 4 | Data | opaque (pointer) | Garbage on disk; the *actual* per-empire NPE data is the flat blob described below, keyed by array position, not by this pointer |

### 2. Per-empire NPE blobs

For each `Empire1..Empire8` where `EmpireActive(Emp) AND NOT EmpirePlayer(Emp)`, in empire
order, one variant-specific blob follows immediately — **no index or length prefix**; which
empires get a blob, and which variant, is entirely determined by `EmpireDataRecord.InUse` /
`.IsAPlayer` (already parsed) and `NPEDataRecord.Typ` (already parsed, part 1 above). This is
the one section of the format where you cannot parse a record without already knowing state
from two earlier sections.

Dispatch (`NPE.PAS:100-111`): `PirateNPE` and unrecognized/`TraderNPE` values use the pirate
layout; `Kingdom1NPE` and `Kingdom2NPE` share the kingdom layout; `BerserkerNPE` and
`GuardianNPE` each have their own.

Each variant's `Save*NPE` procedure (`NPE01.PAS:481-491`, `NPE02.PAS:49-59`,
`NPE03.PAS:129-139`, `NPE04.PAS:538-548`) does a single raw `BlockWrite` of the whole in-memory
struct — same whole-record-including-pointers pattern as elsewhere.

**Empirically confirmed**: `INTRO_1.SAV` has 4 active non-player empires, all `Kingdom2NPE`
(454 bytes each = 1,816 bytes), and `1,816` bytes is exactly what remains between the end of
the `NPEData` array and EOF. This is the strongest single check in this document — it means
every earlier section, all the way back to the file header, was parsed at the exact right
stride, with zero drift, across the entire 10,396-byte file. `GAUNTLET_1.SAV` additionally
confirms `PirateDataRecord` (739 bytes) the same way, alongside more `Kingdom2NPE` blobs.
`Confront_1.SAV`/`Confront_2.SAV` (from `reference/scenarios/pack_1/Confront.scn`, which declares
both `CreateNPEmpire 4` and `6`) confirm the last two variants the same way: a `GuardianDataRecord`
(430 bytes) and a `BerserkerDataRecord` (930 bytes), both parsing to the exact byte count
remaining before EOF. The Guardian's `FleetData` in both saves happens to hold a repeating
`0x20 0x17` byte pattern rather than sane-looking field values — that's real, unaltered file
content (confirmed via byte-identical round trip, not a decode artifact), most likely
leftover/uninitialized heap memory for a Guardian that hasn't acted yet, the same
non-zeroed-memory behavior documented under [Strings](#strings).

**FleetDataRecord — 11 bytes** (`NPETYPES.PAS:82-90`), shared by all four variants as
`FleetDataArray = ARRAY[1..NoOfFleetsPerEmpire(30)]`, 330 bytes:

| Offset | Size | Field | Type |
|---|---|---|---|
| 0 | 1 | Mission | MissionTypes (17-member enum) |
| 1–2 | 2 | TargetID | IDNumber |
| 3–4 | 2 | HomeBaseID | IDNumber |
| 5–6 | 2 | Midway | IDNumber |
| 7 | 1 | Waiting | Byte |
| 8 | 1 | BlockX | Byte |
| 9 | 1 | BlockY | Byte |
| 10 | 1 | Index | Byte |

**PirateDataRecord — 739 bytes** (`NPETYPES.PAS:128-132`): `FleetData` (330) +
`HuntingGround: ARRAY[1..20,1..20] OF Byte` (400, `MaxNoOfBlocks = MaxSizeOfGalaxy DIV 5 = 20`)
+ `Sheep: ARRAY[Empire] OF Index` (9). Also used for `TraderNPE` and any unrecognized type
(`NPE.PAS:108-109`).

**Kingdom1DataRecord — 454 bytes** (`NPETYPES.PAS:136-141`): `FleetData` (330) +
`State: StateDeptArray` (108: 9 empires × 12-byte `StateDeptRecord`, `NPETYPES.PAS:108-120`) +
`Persona: NPECharacterRecord` (16, `NPETYPES.PAS:35-52`). Used for both `Kingdom1NPE` and
`Kingdom2NPE`.

`StateDeptRecord` — 12 bytes: `Policy` (1, `PolicyTypes`) + `AttackChance` (1, `Index`) +
`TotalMilitary` (4, `LongInt`) + `Worlds` (2, `Word`) + `ThreatAssess` (1, `Index`) +
`Aggressiveness` (1, `Index`) + `Balance` (2, `Integer`).

`NPECharacterRecord` — 16 bytes: 13 `Index` (1-byte) fields (`ImpGene, DefGene, OffGene,
FactorGene, RandomGene, Defensive, Offensive, Techno, Provoke, Imperialist, WorldPower,
Honorable, SphereX`) + `Clock` (2, `Word`) + `Offset` (1, `Byte`).

**BerserkerDataRecord — 930 bytes** (`NPETYPES.PAS:146-150`): `FleetData` (330) +
`BaseData: ARRAY[1..100] OF BaseDataRecord` (500) + `Spare: ARRAY[1..50] OF Word` (100).

`BaseDataRecord` — 5 bytes (`NPETYPES.PAS:93-97`): `Mission` (1, `BaseMissionTypes`) +
`TargetID` (2, `IDNumber`) + `Count` (2, `Word`).

**GuardianDataRecord — 430 bytes** (`NPETYPES.PAS:154-158`): `FleetData` (330) +
`Spare: ARRAY[1..50] OF Word` (100).

---

## IDNumber — 2 bytes

`TYPES.PAS:133-136`. Appears throughout as a 2-byte object reference.

| Offset | Size | Field | Type | Notes |
|---|---|---|---|---|
| 0 | 1 | ObjTyp | ObjectTypes | 12-member enum, see [reference](#objecttypes) |
| 1 | 1 | Index | Byte | Index into the relevant array (planet/starbase/fleet/etc.); `0` conventionally means "no object" |

`NextID`/`Link` fields (in `PlanetRecord`, `StarbaseRecord`, `FleetRecord`, `StargateRecord`,
`ConstrRecord`) are named for an in-memory linked-list use that the save format doesn't
actually exercise — sections are flat arrays keyed by their own index word, not linked lists —
so these fields are written but not meaningfully consumed on load. Preserve them verbatim
rather than trying to interpret them.

---

## Verification

Every offset and size in this document was checked by writing a sequential byte parser and
running it against real save files, with each step gating the next. `INTRO_1.SAV` did the
initial pass:

1. **Header + Environment**: decoded to a legible year (4021), scenario name (`INTRO.SCN`),
   and flags — confirms the whole preamble byte-for-byte.
2. **Sector**: `SizeOfGalaxy = 21`, `22×22` grid of 5-byte records — arithmetic only, not an
   independent structural check.
3. **Planets**: **the load-bearing check.** Tried candidate `PlanetRecord` sizes and required a
   *strictly sequential* `1..N` index sequence terminated by `0` (planets are saved densely, so
   any wrong stride desyncs the index sequence within 1–2 records). Only 89 bytes worked,
   yielding 50 planets. This is what caught the `XYCoord` size error in the original draft.
4. **Empire Data**: decoded all 8 slots at a fixed 183-byte stride and got 5 legible, correct
   empire names, matching capital→planet-index references, and a founding year matching step 1
   — strong independent confirmation, including of the 4-byte pointer size (`Names`/`LastName`).
5. **News**: all-zero item counts for all 8 empires — terminator only, record layout derived.
6. **NPE Data**: decoded the 9-entry type array, found 4 active `Kingdom2NPE` empires, computed
   `4 × 454 = 1,816` bytes, and that value **exactly** matched the number of bytes remaining to
   EOF. This closes the loop on the entire file.

Everything else in `INTRO_1.SAV` (starbases, fleets, stargates, constructions, messages, names)
was empty — a fresh scenario start, saved before any turn was played. Eight more real saves,
generated by actually starting different scenarios and playing turns, closed every remaining gap
but one:

- **`GAUNTLET_1.SAV`**: 10 populated `StarbaseRecord`s at the derived 90-byte stride, parsing
  clean to the terminator — confirms starbases. Also has a `PirateDataRecord` NPE alongside more
  `Kingdom2NPE` ones, confirming the pirate NPE variant.
- **`IMPERIUM_1.SAV`**: all 8 empire slots active as players simultaneously — confirms the
  full-occupancy case of Empire Data that a 1-5-player scenario can't exercise.
- **`AFTERMAT_1.SAV`**, **`PRINCES_1.SAV`**: additional independent multiplayer configurations;
  no new sections, but more `EmpireDataRecord` variety (`AFTERMAT` needed a real fix — see
  below).
- **`INTRO_2.SAV`** (INTRO after ~2 played turns): 9 active `FleetRecord`s at the derived
  57-byte stride — confirms fleets. Also has 2 `NameRecord`s (the player named two fleets
  in-game), which confirmed the record layout **and** caught a real bug in the converter
  scripts: `NameRecord.Name`'s unused string-buffer bytes are non-zero heap garbage in a real
  played session, not zero as `INTRO_1.SAV` happened to have (see [Strings](#strings)). Both
  `savtool` scripts were fixed to preserve those bytes (`{text, tail_hex}`) instead of
  zero-padding them.
- **`Confront_1.SAV`**, **`Confront_2.SAV`** (Confront scenario, `pack_1`): confirm the last two
  NPE variants, `GuardianDataRecord` and `BerserkerDataRecord` (see [NPE Data](#npe-data)).
  `Confront_2.SAV` (a few turns in) also has a populated `ConstrRecord` — the player's outpost
  under construction — confirming that section (see [Constructions](#constructions)).
- **`FLEET_ORDERS.SAV`** (hand-edited from an existing save): a fleet with a real, non-empty
  `CommandRecord` queue (4 orders from typing `DESTINATION 1,1`, a wait, `DESTINATION 2,2`,
  `REPEAT`) and a sent in-game message. Decoding the order queue's variant bytes initially looked
  wrong — they didn't contain the literal typed coordinates — until cross-referencing
  `PRIMINTR.PAS`'s `AbsoluteX`/`AbsoluteY` functions revealed that typed destination coordinates
  are relative to the issuing empire's capital, and `GetLocation` resolves the computed absolute
  coordinate to whatever object already occupies that cell (storing its `IDNumber` instead of the
  raw coordinate) if one is present. Reading the save's own Sector section directly at the two
  resolved absolute coordinates confirmed real planets sit there, with `IDNumber`s matching the
  decoded order bytes exactly. See [CommandRecord](#commandrecord) and [Messages](#messages) for
  the full decode.

**A scenario-file bug found along the way, not a format bug**: `PRINCES.SCN` as shipped fails to
load (`ERROR: Unknown command "3500"`) because its one `CreateStarbase` block has an extra
stray numeric field that `CreateBase` (`NEWGAME.PAS:1027-1098`) doesn't expect — every value
after it reads one position early, and the desync compounds silently until the SDF command loop
eventually lands on a stray number instead of a keyword. Comparing against `ARRONAX.SCN`'s
`CreateStarbase` block (which lines up perfectly, comments matching exactly) confirmed this.
`PRINCES_1.SAV` was generated from a locally-edited copy with that stray field removed.

**Still unconfirmed** — no reference save has one yet:

- `StargateRecord` (needs gate tech, expensive to reach; none of the nine reference saves have a
  stargate)

Every other record type in this document has now been independently confirmed against at least
one real save, not merely derived from the Pascal declarations.

`scripts/savtool.ps1 check` (and the Python equivalent) round-trips every file in
`reference/saves/` byte-for-byte end to end — parse to JSON, rebuild from JSON, compare to the
original — which is the strongest confirmation available for a section short of loading it in
the actual DOS game and cross-checking field values on screen. All nine reference saves pass in
both scripts.

---

## Enum reference

All enums are part of the shared `TechnologyTypes` ordinal space unless noted, so their
sub-ranges (`ShipTypes`, `CargoTypes`, etc.) share ordinals with it (`TYPES.PAS:63-85`):

```
0 NoRes   1 LAM   2 def   3 GDM   4 ion   5 fgt   6 hkr   7 jmp   8 jtn
9 pen    10 ssp  11 trn  12 men  13 nnj  14 amb  15 che  16 met  17 sup
18 tri   19 SRM  20 cmm  21 frt  22 cmp  23 out  24 gte  25 lnk  26 dis
```

| Sub-range | Ordinals | Members |
|---|---|---|
| `ResourceTypes` | 0–18 | `NoRes..tri` (19) |
| `AttackTypes` | 0–13 | `NoRes..nnj` (14) |
| `ShipTypes` | 5–11 | `fgt,hkr,jmp,jtn,pen,ssp,trn` (7) |
| `CargoTypes` | 12–18 | `men,nnj,amb,che,met,sup,tri` (7) |
| `DefnsTypes` | 1–4 | `LAM,def,GDM,ion` (4) |
| `RawMTypes` | 15–18 | `che,met,sup,tri` (4) |
| `ConstrTypes` | 19–26 | `SRM,cmm,frt,cmp,out,gte,lnk,dis` (8) |
| `StarbaseTypes` | 20–23 | `cmm,frt,cmp,out` (4) |
| `StargateTypes` | 24–26 | `gte,lnk,dis` (3) |

### ObjectTypes

`TYPES.PAS:128` — 12 members: `Void,Con,Pln,Base,Gate,BlkHl,Plsr,WrmHl,Flt,DestFlt,Wndr,ArtOBJ`
(ordinals 0–11).

### IndusTypes

`TYPES.PAS:103-104` — independent 9-member enum (not part of `TechnologyTypes`):
`BioInd,CheInd,MinInd,SYGInd,SYJInd,SYSInd,SYTInd,SupInd,TriInd` (ordinals 0–8).

### Empire

`TYPES.PAS:51-53` — 9 members: `Empire1,Empire2,Empire3,Empire4,Empire5,Empire6,Empire7,
Empire8,Indep` (ordinals 0–8).

### Other small enums

| Enum | Members | Source |
|---|---|---|
| `TechLevel` | `PreTchLvl,PrimitLvl,PreAtmLvl,AtomicLvl,PreWrpLvl,WrpTchLvl,JmpTchLvl,BioTchLvl,StrTchLvl,PreGteLvl,GteTchLvl` (11) | `TYPES.PAS:88-90` |
| `WorldClass` | `AmbCls,ArdCls,ArtCls,BarCls,ClsJ,ClsK,ClsL,ClsM,DrtCls,EthCls,FstCls,GsGCls,HLfCls,IceCls,JngCls,OcnCls,ParCls,PsnCls,RnsCls,UndCls,VlcCls` (21) | `TYPES.PAS:93-95` |
| `WorldTypes` | `AgrTyp,AmbTyp,BseTyp,BseSTyp,CapTyp,CheTyp,IndTyp,JmpTyp,JmpSTyp,MinTyp,NnjTyp,OutTyp,RawTyp,RawSTyp,StrTyp,StrSTyp,TrnTyp,TrnSTyp,RsrTyp,TerTyp,TriTyp` (21) | `TYPES.PAS:98-100` |
| `SpecialConditions` | `AmbAddict,Holocst,Plague,SelfSuff,Virgin` (5) | `TYPES.PAS:107` |
| `EmpireModifiers` | `CentralEMD,Reserved1EMD..Reserved6EMD` (7) | `TYPES.PAS:110-117` |
| `FleetStatus` | `FReady,FInTrans,FInactive,FLost` (4) | `TYPES.PAS:124` |
| `ProbeStatus` | `PReady,PInTrans,PAtDest,PLost` (4) | `TYPES.PAS:125` |
| `NebulaTypes` | `NoNeb,Nebula,DarkNebula,DenseNebula` (4) | `TYPES.PAS:130` |
| `CommandTypes` | `NoCOM,DestCOM,TransCOM,RepeatCOM,AbortCOM,SweepCOM,StopCOM,WaitCOM` (8) | `ORDERS.PAS:37-45` |
| `NPEmpireTypes` | `NoNPE,PirateNPE,Kingdom1NPE,Kingdom2NPE,BerserkerNPE,GuardianNPE,TraderNPE` (7) | `NPETYPES.PAS:21-27` |
| `MissionTypes` | 17 members, `NoMSN..SupplyTrnMSN` | `NPETYPES.PAS:54-71` |
| `BaseMissionTypes` | `NoBMS,DefendBMS,AttackBMS,FindHomeBMS,RefuelBMS,WaitForAttackBMS,WanderAroundBMS` (7) | `NPETYPES.PAS:73-80` |
| `PolicyTypes` | `NoPLT,NeutralPLT,DefendPLT,HarassPLT,PreemptPLT,ConflictPLT,WarPLT` (7) | `NPETYPES.PAS:100-106` |
| `NewsTypes` | ~90 members | `NEWS.PAS:21-109` |
