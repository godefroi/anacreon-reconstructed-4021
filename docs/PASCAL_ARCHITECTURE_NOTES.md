# Anacreon (DOS, Turbo Pascal) — Architecture Notes for a C# Port

Source of truth: `DOSAnacreonSource131\` (77+ `.PAS`/`.INC`/`.ASM` files, ~39,124 lines). Manual: `AnacreonManual.md` (~5,000 lines, read in full). This document is a **map**, not a transcription — it records what was actually read, cites `FILE.PAS:line` wherever a specific claim can be checked, and explicitly flags anything not yet reviewed in depth. Do not treat silence on a topic as "nothing there" — check the relevant "not reviewed" list first.

The file-system directory listing includes four files not mentioned in the original research brief: `DISPLAY.PAS`, `ATTNPE.PAS`, `NPE.PAS`, `OVERINIT.PAS`. All four are covered below (they matter: `ATTNPE.PAS` is the AI's attack driver, `NPE.PAS` is the always-resident AI dispatcher, `OVERINIT.PAS` sets up the overlay manager). Treat the file index (§7) as filesystem-derived, not brief-derived.

---

## 1. Overview

**Entry point & main loop** — `ANACREON.PAS` is the `PROGRAM` file. Its `BEGIN...END` block (ANACREON.PAS:374-419) is the entire game's outer loop:

```
Introduction;                          { splash, config load, help load, main menu init }
REPEAT
   Prologue(Player, ExitProgram);      { PROLOG.PAS: title menu, new/load game }
   IF NOT ExitProgram THEN
      REPEAT
         SetUpPlayer(Player, ExitGame);            { password gate, capital-lost check }
         IF EmpireActive(Player) AND NOT ExitGame THEN
            BEGIN
            IF NOT ReEnterGame THEN SetUpTurn(Player);  { scouting refresh, probes }
            PlayerTakesTurn(Player, ExitGame);           { PLAYTURN.PAS: the turn itself }
            IF NOT ExitGame THEN UpdateTurn(Player)      { advance to next empire }
                            ELSE ReEnterGame := TRUE;
            AutoBackup;
            END
         ELSE ... { empire destroyed / no more players } ...
      UNTIL AsyncTurns OR ExitGame;
UNTIL ExitProgram;
CleanUp;
```

Two turn-order modes coexist (`AsyncTurns: Boolean`, ENVIRON.PAS:30): **sequential** (default) cycles one empire at a time, calling `UpdateUniverse` (the annual tick) only when wrapping back to `Empire1` (ANACREON.PAS:232-237); **async** lets all empires queue their turns independently via `EmpiresToMove: EmpireSet` (ENVIRON.PAS:36) before the universe advances (ANACREON.PAS:260-291). This asymmetry is load-bearing, not cosmetic — see §3.

**Compilation / overlay model** — Turbo Pascal 4.0, built via `MK.BAT` → `tpc anacreon` using `TPC.CFG`: `/DOverlay` (defines the `Overlay` conditional symbol used everywhere as `{$IFDEF Overlay}{$F+}{$ENDIF}`), `/M` `/L` (map file), `/$R-,$S-,$V-` (range/stack/var-string checking off — the runtime trusts itself completely; a C# port gets these checks for free and should not disable equivalents). `OVERINIT.PAS` (17 lines, a unit with only an `IMPLEMENTATION`/init block, no interface) calls `OvrInit('ANACREON.OVR')`, `OvrSetBuf(40000)`, `OvrSetRetry(BufferSize DIV 3)` at program startup — this is what makes `ANACREON.OVR` (present in the source tree) a real overlay file, not a leftover.

Units marked `{$O UnitName}` in `ANACREON.PAS` (:48-64) — `PlayTurn, Prolog, TMA, Update, NewGame, AttComm, FltComm, Design, ClsComm, MscComm, NPE01, NPE02, NPE03, NPE04, NPEINTR` — are swappable overlays: DOS keeps only one resident at a time, paging others in from `ANACREON.OVR` on call. This is a pure memory-pressure artifact of 1990s DOS (640KB conventional memory); a C# port runs everything resident and should **not** try to preserve "overlay group" as a logical module boundary — the grouping reflects size/frequency-of-use tradeoffs on real hardware, not a designed subsystem seam. See §6 for the full list of DOS-specific mechanisms.

**Module shape at a glance:**
- **Data model** (resident, non-overlaid): `TYPES.PAS`, `DATASTRC.PAS`, `GALAXY.PAS`, `DATACNST.PAS`, `CDETYPES.PAS`, `NPETYPES.PAS`, `WNDTYPES.PAS`, `TEXTSTRC.PAS` — the shared vocabulary every other unit depends on. See §2.
- **Data-access facade**: `PRIMINTR.PAS` + `INTRFACE.PAS`, both declaring themselves (identical header comment) as "the interface between the Data Base and the rest of the program" — all other code is expected to go through these rather than touch `Universe`/`Sector` directly. The split is **dependency-driven, not purity-driven**: `PRIMINTR.PAS` (interface uses only Strg/Int/Types/Galaxy/DataStrc/DataCnst/Misc — no News, no EIO) holds low-level field get/put accessors (`GetResources`/`PutResources`, per-object simple-field get/set, fleet fuel/status accessors, the player-defined name/location linked-list subsystem) plus real game rules that happen not to need the higher-level units — e.g. `GetShipsKnown` fog-of-war-filters an independent world's reported ship types by its own unlocked-technology set, and `Scouted`/`Known` visibility-flag accessors live here too. `INTRFACE.PAS` (adds EIO/News/Mess to its uses, and its implementation additionally pulls in PrimIntr/Fleet/Orders/NPE — a strict one-way layering, confirmed: PRIMINTR never references INTRFACE) holds everything requiring those higher subsystems: the scouting/visibility *engine* (`Scout`, `ScoutFleets`, `ScoutObjects`, `ClearScoutSet`), object creation/destruction (`CreatePlanet`, `CreateStarbase`, `CreateStargate`, `Construction`, `DestroyEmpire`), the industrial-distribution economic model (`GetIndustrialDistribution`, `GetOptimumIndus`), and all formatted status/news-line generation (`GetWorldStatus`, `GetEmpireStatusLine`, `GetNewsLine`, etc.). A few quirks worth flagging before porting: `PutResources`'s catch-all `ELSE` branch zeroes the caller's input parameter instead of leaving it alone (apparently copy-pasted from `GetResources`'s symmetrical branch, where zeroing the out-parameter is correct); `ScoutObject` has no case for fleets (fleet visibility is instead fully recomputed every turn by `ScoutFleets`, not accumulated); and a source comment at `PRIMINTR.PAS:1407-1408` attributes a specific off-by-one fix ("Adam Luker fixed the Enemy240 bug") to a named contributor, confirming this tree carries post-release community patches (consistent with the existing project-memory note on v2-source caveats). See §7 file index for line-level pointers.
- **Game loop**: `PROLOG.PAS` (menu/session), `NEWGAME.PAS` (scenario interpreter), `PLAYTURN.PAS` (per-player turn), `UPDATE.PAS` (annual tick), `LOADSAVE.PAS` (persistence). See §3.
- **Subsystems**: fleets (`FLEET.PAS`/`FLTCOMM.PAS`), combat (`ATTACK.PAS`/`ATTCOMM.PAS`/`BATTLE.PAS`/`ATTNPE.PAS`/`BOMBER.PAS`), construction/economy/orders (`CONSTR.PAS`/`DESIGN.PAS`/`TRANSACT.PAS`/`ORDERS.PAS`/`CLSCOMM.PAS`/`MSCCOMM.PAS`), galaxy/scenario (`SCENA.PAS`/`ARTIFACT.PAS`/`RESOURCE.PAS`/`NAMES.PAS`/`MISC.PAS`), AI (`NPE.PAS` + `NPE00-04.PAS` + `NPEINTR.PAS`). See §4.
- **UI/presentation**: `WND.PAS`/`SWINDOWS.PAS`/`PULLDOWN.PAS`/`MENU.PAS`/`EDIT.PAS` (toolkit) plus per-window units (`MAPWIND.PAS` etc.) and `FASTSCR.ASM`. See §5.
- **Scripting VM**: `CDETYPES.PAS` (types) + `CODE.PAS` (interpreter) implement a small bytecode language (registers, if/case/while, create/destroy-object, display-text, win-game actions) used by `ARTIFACT.PAS` for scenario-scripted events, and referenced (but dormant — see §4) by `TRANSACT.PAS`.

**A key finding that affects every subsystem section below**: several manual-documented features exist only as dead/commented-out code in this v1.31 source — no victory conditions, no artifact scripting triggers wired into the main scenario loop, no holocaust command, no player-to-player transactions. Each occurrence is flagged in place; do not assume the manual's description of a feature matches this build's actual behavior without checking the citation.

---

## 2. Core Data Model

### 2.1 Primitive types and ranges (`TYPES.PAS`)

- `Resources = 0..9999` (`MaxResources`), `Index = 0..100`, `IndusIndex = 0..999`, `Population = 0..MaxResources` — TYPES.PAS:40-48. **The v1.31 source enforces this 9999 cap everywhere via `ThgLmt`** (MISC.PAS:98-109). **Correction:** an earlier draft of this document repeated a project-memory claim that v2 removes this cap; a full v1.31-vs-v2 diff (`docs/PASCAL_V1_VS_V2_DIFF.md`) checked this directly and found `ThgLmt`, `MaxResources`, and the per-type stacking-cap logic all byte-identical between versions — that claim did not hold up. See that document for the actual verified list of v2 gameplay/balance changes.
- `Empire = (Empire1,...,Empire8, Indep)` (TYPES.PAS:51-53) — 8 possible empires plus an `Indep` sentinel for unclaimed/no-owner. `Empire1` is ordinal 0, `Indep` is ordinal 8. `MaxNoOfEmpires: Empire = Empire8` (TYPES.PAS:56).
- `Directions = (NoDir,No,Ne,Ea,Se,So,Sw,We,Nw)` with `DirX`/`DirY` offset tables (DATACNST.PAS:561-564) — 8-directional (Chebyshev) movement grid.
- `TechnologyTypes` (TYPES.PAS:63-66): 27 values from `NoRes` through `dis`, subdivided by range into `ResourceTypes`, `ShipTypes` (`fgt..trn`: fighter, hunter-killer, jumpship, jumptransport, penetrator, starship("ssp"), transport), `RawMTypes`, `CargoTypes` (`men..tri`), `DefnsTypes` (`LAM..ion`), `AttackTypes` (`NoRes..nnj`), `ConstrTypes` (`SRM..dis`: SRM, command base "cmm", fortress "frt", industrial complex "cmp", outpost "out", gate "gte", warp link "lnk", disrupter "dis"), `StarbaseTypes` (`cmm..out`), `StargateTypes` (`gte..dis`).
- `TechLevel = (PreTchLvl,PrimitLvl,PreAtmLvl,AtomicLvl,PreWrpLvl,WrpTchLvl,JmpTchLvl,BioTchLvl,StrTchLvl,PreGteLvl,GteTchLvl)` — TYPES.PAS:88-90, 11 levels matching the manual's pre-tech→gate progression.
- `WorldClass` (21 values, TYPES.PAS:93-95) and `WorldTypes` (21 values, TYPES.PAS:98-100) — environment vs. economic-role, cross-referenced in §8's glossary table.
- `IndusTypes = (BioInd,CheInd,MinInd,SYGInd,SYJInd,SYSInd,SYTInd,SupInd,TriInd)` — TYPES.PAS:103-104: bio-tech labs, chemical plants, metal mines, and four distinct shipyard industries (general/jump/star/transport) plus food ("Sup") and trillum mining.
- `SpecialConditions = (AmbAddict, Holocst, Plague, SelfSuff, Virgin)` (TYPES.PAS:107) — **only `AmbAddict` is ever read by `UPDATE.PAS`'s annual tick** (confirmed by the turn-loop research pass); `Holocst`/`Plague`/`SelfSuff`/`Virgin` are declared but inert in this build's simulation step. Don't assume a live plague mechanic exists.
- `FleetTypes = (Standard,JumpFleet,HKFleet,Penetrator,AdvWrpFleet)`, `FleetStatus = (FReady,FInTrans,FInactive,FLost)` — TYPES.PAS:123-124.
- `ObjectTypes = (Void,Con,Pln,Base,Gate,BlkHl,Plsr,WrmHl,Flt,DestFlt,Wndr,ArtOBJ)` (TYPES.PAS:128) and `IDNumber = RECORD ObjTyp: ObjectTypes; Index: Byte END` (TYPES.PAS:133-136) — the universal "pointer" used throughout the codebase to refer to any map entity (planet, starbase, fleet, gate, construction site, artifact) by type+index rather than a native pointer. `EmptyQuadrant: IDNumber = (ObjTyp: Void; Index: 0)` (TYPES.PAS:176) is the sentinel "nothing here" value.
- Global mutable state (TYPES.PAS:178-190): `SetOfActiveFleets/Planets/Starbases/Gates/ConstructionSites` and their per-empire partitions (`SetOfFleetsOf[Empire]`, etc.) — Pascal `SET OF` bitmasks used everywhere as the "which slots are live" index, doubling as the per-empire ownership index. A C# port should decide once whether these become `HashSet<int>`, `BitArray`, or a query over the live entity collection — they are read constantly across every subsystem.

### 2.2 Galaxy / sector grid (`GALAXY.PAS`)

- `MaxSizeOfGalaxy = 100` (sectors per side); `XYCoord = RECORD x,y: 0..100 END`; `Location = RECORD XY: XYCoord; ID: IDNumber END` (name+coordinate pairing used by the naming system).
- `SectorRecord = RECORD Obj: IDNumber; Flts: ScoutSet; MineScout: ScoutSet; Special: Byte END` (GALAXY.PAS:32-38) — one entry per grid cell: what object (if any) occupies it, which empires have a fleet present, which empires know about a minefield there, and a packed `Special` byte (low nibble = `NebulaTypes`, high nibble = which empire's mine is placed, `NoSRMField = Ord(Indep)*16`).
- Storage: `Sector: SectorColumnSpine = ARRAY[Coordinate] OF SectorRowArrayPtr` (GALAXY.PAS:42-45) — a column of row-pointers, each row `GetMem`'d to exactly `(SizeOfGalaxy+1)` cells (GALAXY.PAS:127-134), not a full 101×101 allocation. This "spine of pointers, real size smaller than nominal declaration" idiom recurs elsewhere (artifact list, order buffers) — a C# port replaces each occurrence with a plain sized array/list, no spine needed.
- `Limbo: XYCoord = (x:0; y:0)` is the sentinel "no valid position" coordinate — but `MISC.PAS`'s `InGalaxy` (MISC.PAS:111-117) requires `x>0 AND y>0`, so row/column 0 is *also* structurally outside the galaxy even though `Coordinate = 0..100` allows it. Confirm this deliberately-dead border before "fixing" it in a port.
- Distance metric is **Chebyshev** (`max(|dx|,|dy|)`, `MISC.PAS:119-122`), not Euclidean — this governs sector-adjacency, scan range, and disrupter range checks throughout combat and exploration.

### 2.3 Universe record (`DATASTRC.PAS`)

```
UniverseRecord = RECORD
   Planet:  ARRAY[1..MaxNoOfPlanets=200]    OF PlanetRecord;
   Starbase: ARRAY[1..MaxNoOfStarbases=100] OF StarbaseRecord;
   Fleet:    ARRAY[1..MaxNoOfFleets=240]    OF FleetRecordPtr;   { pointers, not inline records }
   Stargate: ARRAY[1..MaxNoOfStargates=50]  OF StargateRecord;
   Constr:   ARRAY[1..MaxNoOfConstrSites=50] OF ConstrRecord;
   EmpireData: ARRAY[Empire] OF EmpireDataRecord;
END;
VAR Universe: ^UniverseRecord;   { one heap-allocated instance, `New`'d at unit init, DATASTRC.PAS:240 }
```

`MaxNoOfFleets = 240 = NoOfFleetsPerEmpire(30) * NoOfEmpires(8)` (TYPES.PAS:25-26) — note the Fleet-management research (§4) found `GetNextFleet` only enforces the *global* 240 cap; no code was found enforcing the per-empire 30 cap within `FLEET.PAS`/`FLTCOMM.PAS` — flag as an open question for whoever owns fleet-cap enforcement.

**`PlanetRecord`** (DATASTRC.PAS:23-48): `XY`, `Emp` (owner), `ScoutedBy`/`KnownBy: ScoutSet` (fog-of-war — scouted = exact info, known = knows-it-exists only), `Cls: WorldClass`, `Typ: WorldTypes`, `ImpExp: SelfSuffConditions` (import/export flags per raw material), `Tech: TechLevel`, `Eff: Index` (0-100% administrative efficiency), `RevIndex: Index` (0-100 dissatisfaction), `Special: SetOfSpecialConditions`, `Pop: Population`, `Ships: ShipArray`, `Cargo: CargoArray`, `Defns: DefnsArray`, `Indus: IndusArray` (per-industry development level), `TriReserve: Word` (remaining mineable trillum, finite per world), `Reserved: ARRAY[1..16] OF Byte` (padding — likely reflects a fixed on-disk record size budgeted with headroom), `NextID: IDNumber` (a link field — see fleet-record note below on whether such link fields are actually used).

**`StarbaseRecord`** (DATASTRC.PAS:54-79): same core fields as `PlanetRecord` plus `STyp: StarbaseTypes` and **mobility fields** `Move: Byte`, `Dest: XYCoord`, `Status: FleetStatus` — command bases and fortresses are *mobile* starbases (confirmed by `SBASE.PAS`'s `MovePlayerStarbases`/`MoveBase`/`GetNewBasePos`, which steps a starbase toward its `Dest` one cell/year, converting trillum to a flat 100-fuel-unit cost per move, SBASE.PAS:203-263).

**`FleetRecord`** (DATASTRC.PAS:86-111, accessed via `FleetRecordPtr` — fleets are the one entity stored as pointers, not inline array elements): `XY`, `Emp`, `ScoutedBy`, `Ships: ShipArray`, `Cargo: CargoArray`, `Dest: XYCoord`, `Status: FleetStatus`, **split fuel encoding** `FuelHigh: Byte` + `Fuel: Integer` reconstructed as `FuelHigh*MaxInt + Fuel` where Pascal `MaxInt=32767` (confirmed at `PRIMINTR.PAS:853-880`) — a non-power-of-two split a C# port must replicate bit-for-bit if it needs to load legacy saves, not "simplify" to one wide integer with different overflow semantics. `KnownBy: ScoutSet`, `NextOrder: Byte` + `OrderData: ARRAY[1..6] OF Byte` (the fleet's order-program state — see §4 fleets subsection: this is actually a live 4-byte far pointer + 2-byte length reinterpreted as raw bytes, not literal order data), `NPEDataIndex: Byte` (links a fleet to its owning AI's per-fleet mission record), `Reserved: ARRAY[1..8] OF Byte`, `NextID: IDNumber`. **Fleet-management research confirmed `NextID` and `Reserved` are never read or written by `FLEET.PAS`/`FLTCOMM.PAS`** — likely vestigial; check other subsystems before assuming dead everywhere.

**`StargateRecord`** (DATASTRC.PAS:117-127): `XY`, `Emp`, `ScoutedBy`, `KnownBy`, `GTyp: StargateTypes`, `Dest: XYCoord` (the paired gate's location), `NextID`.

**`ConstrRecord`** (DATASTRC.PAS:133-144): `XY`, `Emp`, `ScoutedBy`, `KnownBy`, `CTyp: ConstrTypes`, `TimeToCompletion: Byte` (counts down to 0, driving `UpdateConstruction` — see §3/§4), `NextID`.

**`EmpireDataRecord`** (DATASTRC.PAS:177-204): `InUse`/`IsAPlayer: Boolean`, `EmpireName: String32`, `Pass: String8`, `TimeLeft: Integer` (per-turn time-limit clock, seeded 25 min + 5 min/turn per the manual), `Capital: IDNumber`, `DefenseSettings: DefenseRecord` (empire-wide, **not per-planet** — confirmed by the construction/economy research: `GetDefenseSettings`/`SetDefenseSettings` key off `Player`, not a world ID), `Probe: ARRAY[1..10] OF ProbeRecord` (`NoOfProbesPerEmpire=10`, matching the manual's "10 probes/year"), `Names: NameRecordPtr`/`LastName` (linked list of player-defined location bookmarks — confirmed by the galaxy/scenario research to be simple user-typed labels, **not** a procedural name generator; `NAMES.PAS` contains no RNG-based naming despite a plausible-sounding hypothesis to the contrary), `TotalRevIndex: Integer`, `TechnologyLevel: TechLevel` + `Technology: TechnologySet` (empire-wide unlocked-tech bitset — a world can only build something once *both* its own tech level and its empire's discovered-technology set permit it, per the manual's "discovering new technologies" rule), `IsAnEmpress: Boolean`, `RevFactor: Integer`, `Founding: Word`, `Modifiers: SetOfEmpireModifiers` (currently only `CentralEMD` is meaningful: "empire lost if capital conquered" — confirmed live in `ATTACK.PAS`'s `ConquerEmpire`, §4).

**`DefenseRecord`** (DATASTRC.PAS:165-168): two `DefenseDistributionArray = ARRAY[ShellPos,ShipTypes] OF Index` tables (`ShellDefDist`, `StarbaseDefDist`) — percent-of-fleet-by-orbital-shell-by-ship-type, edited via `MSCCOMM.PAS`'s `DefenseCommand` (§4).

**`GlobalSetsRecord`** (DATASTRC.PAS:210-220) mirrors the `VAR`-declared active/owned sets from `TYPES.PAS` inside a record, aliased via `GlobalSets: GlobalSetsRecord ABSOLUTE SetOfActiveFleets` (DATASTRC.PAS:235) — a Turbo Pascal `ABSOLUTE` overlay letting the same memory be addressed either as loose globals or as one record (e.g. for bulk save/load). A C# port has no reason to preserve this dual-addressing trick; pick one representation.

### 2.4 Economy/combat balance tables (`DATACNST.PAS`)

This file has no types, only tuning constants and display-name tables — but it is the single most load-bearing "spreadsheet" in the codebase and every numeric-formula claim elsewhere in this document cites into it. Highlights (values themselves are transcribed in the file; this is a map of *what exists*, not a re-derivation):

- **TIP formula constants**: `K1=1.76, K2=10, K3=0.75` (DATACNST.PAS:24-26) — `TIP = K1*(Pop+K2)^K3 * TechAdj[Tech]/100`, computed in `MISC.PAS`'s `TotalProd`.
- **Absolute Production constants**: `K4=0.0` ("DO NOT" apply — turn-loop research confirmed it's a no-op additive term), `K5=2.0` (comment says "DO NOT Change" but turn-loop research found it is **never referenced** anywhere — the exponent is hardcoded via `Sqr()` in `UPDATE.PAS` instead), `K6=11000.0` (DATACNST.PAS:31-33).
- **Ambrosia**: `AmbrosiaAdj=1.45`, `DrugsPerBillion=11.5`, `ChanceToAddict=25`(%/yr), `AddictDeathCoeff=0.12`, `AddictEffCoeff=0.9`, `AddictRevICoeff=0.55` (DATACNST.PAS:39-45).
- **Supplies**: `SuppliesPerBillion=25` (DATACNST.PAS:50).
- **Tech advancement**: `TechIncCap=12`, `TechIncUnv=15`, `TechIncRns=5`, `TechIncUnvRns=17` (%/yr chance per lab type), `TechLvlInc=16` (DATACNST.PAS:56-61).
- **Display-name tables**: `TechnologyName`, `TypeName`, `ThingNames`, `IndusNames`, `ObjName`, `TypeStr`/`ClassStr`/`TechStr` (single-character map glyphs), `TechN` — these are the authoritative source strings for anything the C# UI needs to display for a Pascal enum value.
- **Combat tables**: `MPower[LAM..trn]` (military power 0-100 per ship/defense type), `CombatTable[AttackTypes,AttackTypes]` (destructive-power index, attacker row × defender column, "100 = destroys 1 unit"), `WeapEff[AttackTypes]`, `ShipValue[AttackTypes]`, `ProtecOffered[ShipTypes]`/`ProtecNeeded[ShipTypes]` (escort mechanics), `DefAdj`/`DefBuildRate[DefnsTypes]` — consumed almost entirely by `ATTACK.PAS` (§4); **note the combat research found three separate, non-interchangeable "military power" tables in the codebase** (`MPower` here, a distinct `CombatPower` local to `ATTACK.PAS`, and yet another local `MilitaryPower` const in `BATTLE.PAS`) — do not collapse them into one during a port.
- **Population/tech tables**: `BasePop[TechLevel]` (average population at 50% eff by tech, 3..3000), `TechAdj[TechLevel]` (25..100, TIP multiplier — **distinct from** a same-named but differently-valued procedure-local array inside `UPDATE.PAS`'s `UseUpFood`, a naming collision the turn-loop research flagged explicitly), `TechAdj2[TechLevel]` (12..100, production-adjustment multiplier), `MinSupInd[TechLevel]`, `MinTechLevel[IndusTypes]`, `MinTechForType[WorldTypes]`, `MinTechForClass[WorldClass]`.
- **World-class economics**: `TriResByClass[WorldClass]` (average trillum reserves seeded at world creation), `ClassIndAdj[WorldClass,IndusTypes]` (% efficiency per class per industry — the manual's "advantages/disadvantages" table, machine-readable form).
- **Military/defense population**: `MilitPer[TechLevel]` — **confirmed dead** (never referenced anywhere in the codebase per the turn-loop research grep) — and `OptMilitary[WorldTypes]` (which *is* live, driving `UpdateMilitary`'s optimum-garrison calc).
- **Tech unlock progression**: `TechDev[TechLevel]: TechnologySet` — the cumulative set of technologies available at each tech level, consumed constantly to gate what a world/empire can build.
- **Fleet/cargo tuning**: `InitDefenseRecord` (default shell-defense distribution), `CargoSpace[CargoTypes]` (units-per-transport-slot), `TrnAdj[ShipTypes]` (cargo capacity relative to a transport), `JumpCargoRatio=5`, `FuelCons[fgt..tri]` (fuel burn per unit per space moved, ×0.001), `FuelCap[ShipTypes]` (max fuel per ship, ×0.01), `FltMovementRate[FleetTypes] = (1,10,10,2,2)` (quadrants/year — Standard=1, JumpFleet=10, HKFleet=10, Penetrator=2, AdvWrpFleet=2, matching the manual's "1 sector/year warp, 10 sectors/year jump, 2 sectors/year penetrator" rules).
- **Industry/construction tuning**: `ThgAdj[IndusTypes][fgt..tri]` (relative output mix per industry), `RawM[ResourceTypes][CargoTypes]` (raw material cost per 100 units built), `NewIndRawN[IndusTypes]` (metal cost per 100 points of new industrial development), `TypeData[WorldTypes]: IndusRArray` (how a world type's industry auto-balances — the machine-readable form of the manual's "ID is calculated automatically based on type and class" rule), `PrincipalIndustry[WorldTypes]`, `DefaultISSP=$5555`/`MaxISSP=10`/`NormalISSP=5`, `ISSP[0..10]` (the 11 discrete self-sufficiency percentages: 1,10,25,50,75,100,150,200,300,400,500%), `YearsToBuild[ConstrTypes]`, `ConsCargoNeeded[ConstrTypes]: CargoArray` (material/year to build), `DefAdj`/`DefBuildRate[DefnsTypes]`.

### 2.5 Supporting small units

- `CDETYPES.PAS` (71 lines) — the artifact-scripting VM's type vocabulary: `VariableRecord` (tagged union of Bool/Scalar/Emp/ID/XY), `RegisterArray = ARRAY[0..9] OF VariableRecord` (10 general-purpose "registers"), `ActionTypes` (27 opcodes: control flow `IfCODE/CaseCODE/SwitchCODE/WhileCODE/ElseCODE/EndCODE`, plus `AssignACT, CreateACT, DestructACT, DisplayACT, GetArtifactCoordACT, GetObjectPowerACT, IsEqualACT/IsGreaterACT/IsLesserACT/IsNotEqualACT, MenuAddFleetsACT/MenuDisplayACT/MenuInitializeACT, AnyKeyACT, ClsACT, DebugDumpACT, NullACT, WinGameACT`), `ActionRecord = RECORD AType; Immediate: VariableRecord; Parm: ARRAY[1..4] OF Byte END`, `ActionArray = ARRAY[1..1000] OF ActionRecord` (a "program" of up to 1000 instructions). See §4 for how `ARTIFACT.PAS`/`CODE.PAS` use this.
- `NPETYPES.PAS` (165 lines) — the AI subsystem's type vocabulary; see §4's NPE subsection for full detail. Top-level: `NPEmpireTypes = (NoNPE,PirateNPE,Kingdom1NPE,Kingdom2NPE,BerserkerNPE,GuardianNPE,TraderNPE)`, `NPECharacterRecord` (personality "genes": `ImpGene/DefGene/OffGene/FactorGene/RandomGene` plus derived traits `Defensive/Offensive/Techno/Provoke/Imperialist/WorldPower/Honorable/SphereX`, all `Index`=0-100), `MissionTypes`/`BaseMissionTypes` (fleet/base AI orders), `PolicyTypes` + `StateDeptRecord` (per-other-empire diplomatic stance), and one data record per archetype (`PirateDataRecord`, `Kingdom1DataRecord`, `BerserkerDataRecord`, `GuardianDataRecord`).
- `WNDTYPES.PAS` (47 lines) — window-command constants (`OpenWCM`, `CloseWCM`, `ScrllUpWCM`, etc.) and `InputCommandTypes` (which status window/command a keypress maps to). Pure UI vocabulary — see §5.
- `TEXTSTRC.PAS` (265 lines) — a doubly-linked-list word-wrapped text buffer (`LineRecord`/`TextStructure`) with `InsertLine`/`DeleteLine`/`LoadText`/`SaveText`. Used for help-file display, scenario intro pages, and the fleet-orders text editor. Not game-logic; a generic reusable text-buffer utility.

---

## 3. Game Loop / Turn Processing

**Session flow**: `ANACREON.PAS`'s outer loop (§1) calls `Prologue` (`PROLOG.PAS:1327`) each time it needs a title-menu decision (new game, load game, quit, options, add/delete player). Selecting "New Game" routes through `PROLOG.PAS:1239 StartANewGame` → `NEWGAME.PAS:1898 StartNewGame` → `NEWGAME.PAS:1650 LoadScenario`, which drives a `.SCN` scenario file — this is a **text-scenario interpreter reading scripted commands**, not a from-scratch procedural galaxy generator (see §4 for the full command set and generation algorithm). Selecting "Load Game" routes through `PROLOG.PAS:774 ContinueOldGame` → `LOADSAVE.PAS:572 LoadGame`.

**Per-empire per-year cycle**, once a game is running:
1. `SetUpPlayer(Player, ExitGame)` (`PROLOG.PAS:397`) — password gate; checks whether the empire's capital was lost since last turn (`EmpireNews`, calls `DestroyEmpire` if so); shows an empire-status recap.
2. `SetUpTurn(Player)` (`ANACREON.PAS:183-196`) — clears and rebuilds the empire's scout set (`ClearScoutSet`, `ScoutFleets`, `ScoutObjects`), advances probes (`UpdateProbes`).
3. `PlayerTakesTurn(Player, ExitGame)` (`PLAYTURN.PAS:1031`) — the interactive turn: a menu-bar command loop (`GetCommand` → `TrapCommandErrors` → `GetParameters` → single big `CASE` dispatch to command handlers, `PLAYTURN.PAS:1072-1116`). **Every command executes immediately against the live `Universe` record** — there is no "commit phase" or transaction buffering for direct commands. The **only** genuinely deferred mechanism is the fleet-orders mini-language (`OrderCom`/`CancelOrdCom` write into `FleetRecord.NextOrder`/`OrderData`, resolved later during fleet movement — see §4). A per-turn wall-clock budget (`TimeLeft`, seeded 25 min + 5 min/turn per the manual) forces the loop to end when exhausted (`PLAYTURN.PAS:1141-1147`).
4. `UpdateTurn(Player)` (`ANACREON.PAS:211-291`) — advances to the next empire. In **sequential mode**, before advancing it calls `UpdateAllFleets(Player, NextEmpire(Player))` and `MovePlayerStarbases(NextEmpire(Player))` for a specific asymmetric subset (see below), then on wrapping back to `Empire1` calls `UpdateUniverse` (the annual tick, `UPDATE.PAS:1433`) and resets `EmpiresToMove`. In **async mode**, movement/update instead happens once *all* human empires have called `Game/Next Turn` and `EmpiresToMove=[]` (`ANACREON.PAS:260-287`), at which point every active empire's fleets/starbases update in a single pass before `UpdateUniverse` runs.

**The sequential-mode fleet-movement asymmetry is load-bearing, not incidental** (confirmed by both the fleet and turn-loop research passes): `UpdateAllFleets(Player,NextPlayer)` (`FLEET.PAS:861-905`) only moves (a) the **current** player's `JumpFleet`s not currently at a stargate, and (b) the **next** player's `Standard`/`HKFleet`/`Penetrator`/`AdvWrpFleet` fleets (plus anything at a stargate, plus that player's starbases). This is what implements the manual's "jumpfleets gain initiative, warpfleets gain initiative on the opposite side" fleet-initiative rule (see §8) at the turn-sequencing level, not inside combat resolution. **A naive "iterate every fleet, move whichever belongs to whoever's turn it is" port would silently break this rule.**

**Annual tick, `UpdateUniverse` (`UPDATE.PAS:1433-1488`)** — runs once per wrap to `Empire1` (sequential) or once per full round (async): increments `Year`; loops all planets → `UpdateWorld`; loops all starbases → `UpdateWorld` (shared code path); loops all active construction sites → `UpdateConstruction`; loops all active empires → `UpdateEmpire`; finally `DeleteReadMessages`. Fleet movement is **not** part of this procedure — it runs separately per §3's asymmetric schedule, immediately before `UpdateUniverse` is invoked. See §4 (economy subsection) for the full `UpdateWorld` formula breakdown.

**AI turn integration**: for non-player empires, `ImplementNPE(Emp)` (`NPE.PAS`, see §4) is called at the same point a human's `PlayerTakesTurn` would run — i.e. the AI gets one synchronous decision-making pass per empire per year, sharing the same `SetUpTurn`/movement/`UpdateUniverse` scaffolding as a human player, not a separate simulation path.

---

## 4. Major Subsystems

### 4.1 Fleets & Movement (`FLEET.PAS`, `FLTCOMM.PAS`)

**Division of labor**: `FLEET.PAS` (`UNIT Fleet`) is the core data-manipulation library — create/deploy/destroy, composition transfer, per-turn movement, fuel, order execution. `FLTCOMM.PAS` (`UNIT FltComm`) is the player-facing "Fleet Command" menu layer built on top of it (Launch/Abort/Transfer/Refuel/Probe/MineSweep/Orders commands) — this confirms the game's general `XxxCOMM.PAS` convention: such files pair a domain's UI/menu code with its command-execution entry points, while the actual state-mutating engine lives in a same-domain non-COMM unit.

**Lifecycle**: `GetNextFleet` (`FLEET.PAS:246-280`) scans `SetOfActiveFleets` **top-down** (`i:=MaxNoOfFleets; ...Dec(i)`) for a free slot — enforces only the *global* 240-fleet cap, not the per-empire 30 documented in `TYPES.PAS`. `DeployFleet` (`FLEET.PAS:391-435`) is the sole creation entry point, splitting ships/cargo off an existing planet/base/fleet via `ChangeCompositionOfFleet` (`FLEET.PAS:282-389`, the general-purpose ship/cargo re-splitter — this single procedure implements both "split a fleet" and "merge two fleets" depending on whether the source/target is a fleet or a ground object, auto-destroying either side reduced to zero ships). Destruction is a required two-step, enforced only by convention: `AbortFleet` (`FLEET.PAS:150-209`, dumps contents to a target) must be followed by `DestroyFleet` (`FLEET.PAS:211-244`, idempotent, frees the record and clears bitmasks) — nothing in the type system prevents skipping `DestroyFleet`.

**Movement/fuel** (`UpdateFleet`, `FLEET.PAS:646-859`, invoked selectively by `UpdateAllFleets` per §3's schedule): fuel burns once per call via `UseUpFuel` (`FLEET.PAS:660-703`, cost = `1 + Σships×FuelCons/1000 + Σcargo×FuelCons/1000`); insufficient fuel triggers an automatic, **recursive** (not looped) conversion of onboard trillum cargo to fuel one ton at a time until enough exists or cargo runs out, else the fleet goes `FInactive` with a `NoFuel` news event. Gates/fortresses grant an instant `Teleport` if within range; otherwise the fleet steps `FltMovementRate[FltTyp]` times toward `Dest` via `GetNewPos` (Chebyshev one-cell-at-a-time), checking enemy mines and disrupters mid-move for `JumpFleet`/`HKFleet` types only (`Standard`/`Penetrator`/`AdvWrpFleet` are immune to both in transit). Orders execute (`ExecuteFleetOrders`) only in the same call that brings the fleet to `FReady` — i.e. once per arrival, not every turn.

**Orders mini-language**: `FleetRecord.OrderData[1..6]` is not literal order bytes — it's a live 4-byte far pointer + 2-byte length (`OrderStructure`) reinterpreted via unchecked type-cast (`ORDERS.PAS:326`). A player edits free-text order lines (`DEST`, `TRAN`sfer, `REPE`at, `WAIT` — matching the manual exactly) which `CompileOrders`/`ParseLine` (`ORDERS.PAS:191-266`) turn into an `ARRAY OF CommandRecord`, heap-allocated and pointer-stashed into the 6-byte field. **This means orders are not literally save-game-serializable as raw memory** — a save/load path must reconstruct them from source text or a separate format (not located in this pass). Execution (`ExecuteFleetOrders`, `FLEET.PAS:562-617`) runs one command per call, stopping ("yielding") only at `DestCOM` (pauses until arrival) or `WaitCOM`; `RepeatCOM` loops the program counter back to 1 exactly once per invocation (`IgnoreRepeat` guard prevents same-call infinite loop). Three enum values (`AbortCOM, SweepCOM, StopCOM`) are declared in `CommandTypes` but never produced or consumed by the parser/executor in this file pair — dead enum members, possibly meant for a base/planet order system not covered here.

**Mobile starbases**: command bases and fortresses move via `SBASE.PAS`'s `MovePlayerStarbases`/`MoveBase`/`GetNewBasePos` (a Chebyshev step-toward-destination with obstacle/nebula avoidance, structurally similar to but separately implemented from `FLEET.PAS`'s fleet movement), consuming a flat 100 fuel-equivalent (trillum) per move regardless of distance stepped that year.

### 4.2 Combat (`ATTACK.PAS`, `ATTCOMM.PAS`, `BATTLE.PAS`, `ATTNPE.PAS`, `BOMBER.PAS`)

**Division of labor** (confirmed by full read of all five files): `ATTACK.PAS` is the actual combat-resolution engine and all state mutation (grouping, targeting, damage, casualties, retreat/surrender, conquest, holocaust) — no UI code. `ATTCOMM.PAS` is the player-facing command layer (`AttackCommand`/`AutoAttackCommand`, group-splitting UI, the manual round-loop, all screen drawing) calling into `ATTACK.PAS`'s primitives — it computes no damage itself. **`BATTLE.PAS` is a separate, smaller subsystem despite the name collision with `ATTACK.PAS`'s own internal `Battle` procedure** — it exports `CalcAttackRound`/`CalcMilitaryPower`, a simple aggregate-force-vs-aggregate-force model with its own local power table, used only by the (partly dead) bombing subsystem. `ATTNPE.PAS` is the AI's unattended battle driver, calling the same `ATTACK.PAS` primitives through a menu-free round loop.

**Setup**: `CalculateCombatData` (`ATTACK.PAS:344-383`) computes tech/class/base adjustment factors once per battle. `DefaultDistribution`/`DefaultGroup` (`ATTACK.PAS:1301-1367`) split the attacking fleet into up to 9 groups (`MaxNoOfGroups`), one per ship type, transports auto-loading ground troops (ninjas preferred over men). `GetEnemy` (`ATTACK.PAS:1369-1424`) distributes the defender's forces across the five orbital shells per `Defenses.ShellDefDist` percentages, with fixed shell assignments for `def`→Orbit, `GDM`/`ion`/`LAM`→SbOrb, `men`/`nnj`→Grnd.

**Per-round resolution** (shared by the player's `Engage` loop, `ATTCOMM.PAS:220-635`, and the AI's `FleetEngage`/`WorldEngage`, `ATTNPE.PAS:221-384`):
1. **Targeting** (`GetTargetArray`, `ATTACK.PAS:472-703`) — per-group priority weighted by `ShipValue`, boosted for transports nearing ground and starships in deep space, reduced by escort cover (see below); LAM/GDM pools are drawn down and counted as `Killed` **at allocation time, regardless of whether they actually hit** (`ATTACK.PAS:656-691`).
2. **Simultaneous damage** (`GroupAttack`/`EnemyAttack`, `ATTACK.PAS:705-808`) — `CombatTable[Attacker,Defender]` scaled by tech/class adjustments, gated by `InRangeOfDefense` (a per-shell weapon-range matrix matching the manual's shell-range rules), with a +50% casualty bonus for groups advancing through occupied fire.
3. **Casualty application** — proportional loss of carried ground troops when a transport group takes hits.
4. **Movement** (`AdvanceGroups`, `ATTACK.PAS:183-215`) — advancing/retreating groups shift one shell; **on reaching `Grnd`, a transport carrying `GAT>0` flips identity** (`Num`↔`GAT` swap, `Typ:=GATTyp`) — this is the actual mechanism by which invasion troops become a ground-combat group.
5. **End-of-round checks**: total wipeout → `AttDestroyedART`; else `EnemySurrenders` (`ATTACK.PAS:232-342`, **threshold-driven off a separate `CombatPower` table**, not raw damage, with distinct rule sets for fleet vs. ground targets gated by defender tech level and revolution index); the AI additionally forces retreat after round 30 if no non-transport combat ships remain (`FleetRetreats`, `ATTNPE.PAS:160-192`) — **the AI round loop has no iteration cap** and only exits via destruction/surrender/this round-30 retreat check, so a battle satisfying none of those could loop indefinitely.

**Resolution/conquest** (`ResolveAttack`, `ATTACK.PAS:1183-1299`): world conquest (`ConquerWorld`, `ATTACK.PAS:915-983`) transfers ownership, drops efficiency by `Rnd(10,20)`, adjusts revolution index by a lookup table, re-scouts; conquering an enemy **capital** additionally triggers `ConquerEmpire` (`ATTACK.PAS:985-1139`): small/far worlds auto-annex to the winner, large/loyal worlds may go independent, a new capital is chosen by tech-then-population, or (if the loser has the `CentralEMD` modifier) the empire is destroyed outright and *all* its worlds go independent.

**Escort/protection mechanics**: `TotalProtection`/`BuildPriority1` (`ATTACK.PAS:416-556`) reduce a group's attack-priority via `ProtecOffered`/`ProtecNeeded` — but **only while the group has no target yet assigned** (`Trg=NoRes`); a group loses its escort bonus the instant it picks a target. Cloaked hunter-killers get priority forced to 0 while cloaked and decloak permanently on their first attack, matching the manual's hunter-killer rules.

**Bombing** (`BOMBER.PAS`): `Bombing1stPhase` (`BOMBER.PAS:47-75`) is the only functional half — treats the incoming bomber fleet as the *defender* against the target's own defenses via `BATTLE.PAS`'s simple aggregate model, applying casualties/possible destruction to the bomber. **`Bombing2ndPhase` (the actual surface-bombing effect) is an empty stub** (`BOMBER.PAS:77-91`) with no caller found anywhere in the source tree — treat this subsystem as effectively unwired in this build. The functioning planet-destruction path for population/industry/tech damage is instead `ATTACK.PAS`'s `HolocaustEffectiveness`/`HolocaustWorld` (`ATTACK.PAS:1439-1621`) — port priority should go there, not `BOMBER.PAS`. Two likely bugs were flagged in this area: `WorldSurrenders`'s `Random(1)<=ChanceToSurrender` (`ATTACK.PAS:1497`) always evaluates true since integer `Random(1)` only ever returns 0; and `RevertTechnology` is called with a mismatched actual/formal parameter (`ATTACK.PAS:1596` vs `:1549`), making tech-reversion chance depend on death count rather than the intended effectiveness value. **Confirm both against actual play behavior before deciding whether to "fix" or faithfully port them.**

**Notable quirks for porting**: three non-interchangeable "military power" tables exist across the codebase (`DATACNST.PAS`'s `MPower`, `ATTACK.PAS`'s local `CombatPower`, `BATTLE.PAS`'s local `MilitaryPower`) — do not merge them. Combat RNG calls are interleaved with flavor-text/outcome selection using the same stream, so reordering calls during a port changes actual game outcomes, not just cosmetics.

### 4.3 Construction & Economy Commands (`CONSTR.PAS`, `DESIGN.PAS`, `TRANSACT.PAS`, `ORDERS.PAS`, `CLSCOMM.PAS`, `MSCCOMM.PAS`)

**Construction** (`CONSTR.PAS` UI + `INTRFACE.PAS`'s `Construction`/`DestroyConstruction` + `UPDATE.PAS`'s `UpdateConstruction` engine tick): a site is created at an empty sector with `TimeToCompletion:=YearsToBuild[ConsType]`. Each year, `UseUpRawMaterial` (`UPDATE.PAS:113-170`) sums cargo from every **player fleet physically parked on the site's sector** and greedily consumes up to `ConsCargoNeeded[CTyp]` per resource from those fleets (fleet-by-fleet, capped per fleet) — **construction is fed by mobile fleet cargo sitting on the site, not by any stockpile of the site's own**, confirmed independently by both the status-display code and the consumption code. If materials fall short, no time is consumed that year and a shortfall news event fires; otherwise `TimeToCompletion` decrements, and at 0 the finished object is built (`SRM`→minefield via `PutMine`; `cmm/frt/cmp/out`→`ConstructStarbase`; `gte/lnk/dis`→`ConstructStargate`). Aborting (`DestroyConstruction`, `INTRFACE.PAS:518-532`) has **no partial-refund logic** — the site and any accumulated progress simply vanish.

**"Design" is a misnomer for this file** — `DESIGN.PAS` contains no ship-design or industry-layout-design subsystem; its actual contents are `DesignateCommand` (re-assign a world's `WorldTypes`, the manual's "Designate" command — gated by tech level and world class, with several soft-confirm warnings for wasteful choices) plus an unrelated grab-bag: `LaunchLAM`, `ChangeISSPCom` (the real industry-tuning lever — edits the 4 raw-material industries' ISSP across `DATACNST.PAS`'s 11 discrete levels), `SellTechnology` (transfers one unlocked technology to another empire, **no cost charged** despite the "sell" name, restricted to the recipient's current tech band), `GrantIndependenceCommand`. **Player control over a world's industry mix is entirely indirect**: either via `DesignateCommand` (triggers a full re-distribution appropriate to the new type — the actual redistribution formula lives in `INTRFACE.PAS`'s `DesignateWorld`, not reviewed in this pass) or via `ChangeISSPCom`'s per-industry self-sufficiency dial. No command directly edits raw industrial-distribution percentages.

**Transactions are dormant**: `TRANSACT.PAS` implements a generic bytecode-dispatch hook (`Transaction(WorldID,FltID,Emp)` seeds three VM registers and calls `CodeInterpreter`) presumably for scenario-scripted merchant/trade events — but its only would-be caller, `TransactionCommand` in `MSCCOMM.PAS`, exists solely inside a Pascal block comment. **Treat this entire subsystem as inert scaffolding** unless a live caller turns up elsewhere.

**Orders queue** — see §4.1; `ORDERS.PAS` is the compiler/decompiler for the fleet-order mini-language, with no bound-check against its own declared `MaxNoOfOrders=100` in the append path (`AddOrders`, O(n) realloc-and-copy per call).

**Planet/fleet info commands** (`CLSCOMM.PAS`): `CloseUpCom`/`ProductionCom` are **read-only** displays — they recompute (for display purposes only) industrial distribution, optimum vs. actual industry levels, ship/cargo production-consumption, and defense build targets, masking most numeric fields to `????`/estimated bands for non-owned, merely-scouted worlds (the fog-of-war display convention — see §8's `HiLo`/`YesNo` banding rule from `MISC.PAS`).

**Empire-wide defense distribution & self-destruct** (`MSCCOMM.PAS`): `DefenseCommand` edits the **empire-wide** (not per-planet) shell-defense-distribution percentages with auto-normalization on invalid input (rows not summing to 100% get corrected, disallowed ground-ship placements folded into sub-orbit). `SelfDestructCommand` is restricted to bases (excluding industrial complexes) and stargates. **`HolocaustCommand`, `ArtifactCommand`, `TransactionCommand`, and their supporting menu wiring are all dead code (inside Pascal block comments) in this build** — the construction/economy research pass corrects an earlier assumption (based on a comment-blind grep) that these were live exports; only `SelfDestructCommand` and `DefenseCommand` are actually compiled into `MSCCOMM.PAS`'s interface.

### 4.4 Galaxy Generation, Scenarios, Artifacts, Exploration

**Scenario system** (`NEWGAME.PAS`) — confirmed to be a **text-scenario interpreter**, not an autonomous procedural generator. `LoadScenario` (`NEWGAME.PAS:1650`) parses a `.SCN` file's header (version, title, min/max players, galaxy size, planet count, difficulty, game-length bounds, first year, and — version-gated — a `Seed` field), shows introduction text pages, collects player names/passwords, seeds the RNG (`Seed=0` ⇒ `Randomize` from the clock; nonzero ⇒ deterministic `RandSeed:=Seed` — **reproducing a seeded scenario in C# requires bit-exact reimplementation of Turbo Pascal's specific LCG, not `System.Random`**), then runs a keyword-dispatch loop over scenario commands until `ENDSCENARIO`/EOF/error. Live commands in this build: `CLASSTABLE`, `TECHTABLE`, `CREATENEBULA`, `CREATERANDOMNEBULA`, `CREATESRMS`, `CREATEPLAYEREMPIRE`, `CREATENPEMPIRE`, `CREATERANDOMWORLDS`, `CREATEWORLD`, `CREATESTARBASE`, `CREATESTARGATE`, `DEFINEZONE`, `DEFINEXY`, `REPORT`, `SETTRILLUMRESERVES`. **`BEGINARTIFACTS`/`BEGINVICTORYCONDITIONS`/`BEGINTRANSACTIONS` are present only as comments in the dispatch loop — there is no victory-condition mechanism and no artifact/transaction scenario scripting reachable from the main scenario loop in this source revision**, a significant gap versus the manual's Appendix A description (which documents a fuller command set). No `.SCN` sample file exists anywhere in the source tree, so this reconstruction is entirely from the reader code, not a verified real file.

`CreateRandomWorlds` (`NEWGAME.PAS:1122-1163`) places worlds at random valid points (rejecting occupied/dense-nebula cells, up to 100 tries) within a named zone, resampling class/tech pairs from percentage tables (`ClassTable`/`TechTable` commands) until a tech-vs-`MinTechForClass` compatibility check passes (a rejection loop, not a true joint distribution — up to 100 tries, same pattern as the position search). Per-world stats (`CreateRndPlanet`→`SetUpWorld`, `NEWGAME.PAS:885-951`) roll efficiency `Rnd(40,60)`, population scaled off `BasePop[Tech]` and efficiency, and a militia-index-derived (`MI = Rnd(1,33)+Rnd(1,34)+Rnd(1,33)`, a triangular-biased sum of three uniform draws, **not** flat) ship/cargo/defense loadout, each further varied `±20%` via `RndVar`, with anything not yet unlocked by `TechDev[Tech]` zeroed out. `NebulaeBand`/`NebulaePatches` (`NEWGAME.PAS:1261-1333`) generate diagonal random-walk strips or clustered blobs for `CREATERANDOMNEBULA`. Named zones/points (`DefineZone`/`DefineXY`) and four coordinate forms (absolute, `Z:n` zone-random, `R:x,y` galaxy-random, `point:dx,dy` point-relative-random) support the manual's documented scenario-authoring coordinate syntax.

**Artifacts & the scripting VM** (`ARTIFACT.PAS` + `CODE.PAS`): confirmed — `ARTIFACT.PAS`'s `ActiveSituation`/`CloseUpSituation` (`ARTIFACT.PAS:257-302`) are the only two live trigger points (of four declared `SituationTypes`: `NoSIT, ActiveSIT, CloseUpSIT, UpdateSIT` — `UpdateSIT` has no confirmed trigger call site in this file), each building a fresh per-invocation register frame (`R[0]`=situation type, `R[1]`=artifact ID, `R[2]`=triggering empire) and calling `CodeInterpreter` against the artifact **type's** compiled `ActionArray`, while a separate persistent register array (`ArtifactRecord.V`) carries the specific **instance's** state across invocations. `ArtifactTypeRecord` (name, attribute flags like `MobileATT`/`UsableByOwnATT`/`HiddenToAllATT`, size, situations-handled set, compiled code) is the shared per-species definition; `ArtifactRecord` (type index, location, register state, link) is the per-instance data. The keyword vocabulary that would let a *scenario file* actually define new artifacts (`SituationKeyword`, `ActionKeyword`, `ActionParms` in `NEWGAME.PAS`, per `ARTIFACT.PAS`'s own header-comment cross-reference) is part of the same dead/commented-out scenario-scripting gap noted above.

**Naming** (`NAMES.PAS`): confirmed **not** a name-generator — `AddNameCommand`/`DeleteNameCommand` simply attach/remove a player-typed label to a `Location`. (A random-name suggestion table *does* exist, but it's `NEWGAME.PAS`'s 59-entry `RndEmpireName` table for empire naming at game start, not a general procedural generator, and not part of `NAMES.PAS`.)

**Resource/cargo accounting** (`RESOURCE.PAS`): `CargoSpaceAvail`/`SubtractCasualties`/`BalanceResources` — noted as maintaining its **own** cargo-capacity table, independently tuned from `MISC.PAS`'s `FleetCargoSpace`/`DATACNST.PAS`'s `CargoSpace`/`TrnAdj` — two parallel implementations of "how much can a fleet carry," a port should collapse to one authoritative model.

**Misc utilities** (`MISC.PAS`): geometry (`Distance`=Chebyshev, `SameXY`/`SameID`/`SameLocation` via raw record-to-integer casts — hard-codes byte layout, must become field-wise equality in C#), fleet/cargo arithmetic (`AddThings`/`SubThings`/`MoveThings`), `TotalProd` (the TIP formula), `FuelCapacity`/`FuelConsumption`/`FleetCargoSpace`, `MilitaryPower`, and the fog-of-war display-banding helpers `HiLo`/`YesNo` (turn scouted-estimate numbers into strings like `Lo-`/`Hi+`/`yes3`/`----` — a concrete, portable display rule, not just formatting).

### 4.5 The NPE (AI) Subsystem

**Real file-to-archetype mapping** (confirmed by full read of all seven files — do not assume NPE01=archetype-1 etc. by number): `NPE.PAS` is the always-resident dispatcher; `NPE01.PAS`→**Pirate**; `NPE02.PAS`→**both Kingdom1 and Kingdom2** (one shared implementation, `ImplementKingdom1NPE`, with two different gene/policy seed presets — Kingdom1 cautious/builder, Kingdom2 aggressive from turn one); `NPE03.PAS`→**Guardian**; `NPE04.PAS`→**Berserker**; `NPE00.PAS` is **not a shared utility layer** despite its name/number suggesting one — it's a **Kingdom-only** high-level policy layer (`ReviewNews`, `DefendEmpire`, `ImperialExpansion`, `WarCabinet`, `ExplorationAndProbing`), referenced only by `NPE02.PAS`; `NPEINTR.PAS` (largest single file, 1732 lines) is the genuinely shared AI utility layer used by all archetypes — fleet deployment/composition, target selection, region/base bookkeeping, per-mission implementations, diplomacy state. **`TraderNPE` has no implementation anywhere in the source** — `ImplementNPE`'s dispatch `CASE` (`NPE.PAS:64-85`) has an `ELSE` branch that silently routes both `NoNPE` and `TraderNPE` through the **pirate** code path. Treat "Trader" as an unimplemented archetype for porting purposes, not merely an undispatched one.

**Decision algorithm — `Balance` dominates the documented gene ladder.** `StateDepartment` (`NPEINTR.PAS:1460-1540`) short-circuits its entire trait-driven policy `CASE` the instant `Balance<0` (`ConflictPLT` if `Balance<-1`, else `PreemptPLT`) — only when `Balance>=0` does the gene/threat-driven ladder (`NeutralPLT→HarassPLT→PreemptPLT→ConflictPLT→WarPLT`) actually run. `Balance` is a simple revenge ledger: decremented on a lost battle, incremented on successfully conquering a target belonging to that enemy. So the dominant war-declaration input is "am I currently losing to this specific empire," with the manual-documented personality genes acting only as a tiebreaker in the non-losing case. Of the eight documented personality traits (`Defensive/Offensive/Techno/Provoke/Imperialist/WorldPower/Honorable/SphereX`), **`Techno` and `Honorable` are set at init but never read anywhere in the source tree** — confirmed dead despite `NPETYPES.PAS`'s doc comments describing their intended effect ("100=likes technology," "100=will not attack friends"). `Imperialist` is the only trait ever mutated post-init (`ModifyPersona`, gated by `RandomGene`). **A documentation/code inversion was found on `SphereX`**: comments describe higher `SphereX` as a *larger* expansion/probe radius, but the actual formulas (`NPE00.PAS:468,715`) make higher `SphereX` produce a *smaller* radius — a port following the comment literally would invert this archetype's behavior.

**Per-archetype behavior**: Pirate (NPE01) has no diplomacy or persona at all — two fleet-generation loops per turn (large raiders targeting rich weakly-defended worlds; smaller patrols to weighted random "hunting ground" cells). Kingdom1/Kingdom2 (NPE02+NPE00) is the only archetype using diplomacy/persona/world-redesignation machinery; the two differ only in their initial gene/policy seed, not in code path. Guardian (NPE03) is purely reactive and static: no fleets, no persona — just LAM-striking the highest-power enemy fleet within 5 sectors from every owned world/starbase with LAM stock. Berserker (NPE04)'s distinguishing mechanic is that **its starbases are the mobile roaming unit** (`SetFleetDestination` called directly on `Base`-type IDs), driven by a separate `BaseMissionTypes` state machine; conquered worlds have a 1-in-3 chance of being destroyed outright rather than annexed.

**Fleet/mission dispatch**: every archetype maintains its own 25-slot (`NoOfFleetsPerEmpire`) `FleetDataArray`, garbage-collected each turn (`EnforceNPEDataLinks`, `NPEINTR.PAS:1661-1689`) to clear slots whose fleet is no longer active. New fleets are always created through `DeployBattleFleet`/`DeployCargoFleet` (`NPEINTR.PAS:505-628`), which pick ship composition from one of six hardcoded per-mission `SequenceArray`s, spending a military-power budget ship-type by ship-type. `GetBestTarget` (`NPEINTR.PAS:715-775`) is the shared world-target scorer used by expansion, jump-attack, slow-attack, and raider retargeting alike: `value = Factor × (tech+1) × classValue × (pop/1000) / (100 + (defense+troops)/1000)`.

**Several likely bugs were flagged (not verified against a live run)**: `StateDeptReport` calls `GetCapital(Emp,...)` instead of `GetCapital(EnemyEmp,...)` inside its per-enemy loop, meaning the tech-based threat-assessment multiplier never actually fires (`NPEINTR.PAS:1609`); `BSRKDestroyWorld`'s industry-loss calc (`NPE04.PAS:178`) reduces to `max(X,X-k)=X`, i.e. industry is always fully zeroed rather than partially reduced; `DeploySlowAttack`'s fallback path deploys a jump-attack instead of a slow-attack mission when the nearest base is too weak (`NPEINTR.PAS:883`); `LoadPirateNPE`'s legacy version-migration branch never propagates its I/O error code to the caller (`NPE01.PAS:501-513`); `InitializeGuardianNPE` allocates its data block without zero-filling it, so uninitialized heap memory gets written verbatim to save files (`NPE03.PAS:159-172`, harmless today only because Guardian never touches that field). **Per existing project memory on the v2-source caveat**: the 9999/9000 per-type ship-stacking caps found in `NPEINTR.PAS:1362-1365,1443-1449` are this v1.31 build's original behavior (the v2 source's removal of a "9999 ship cap" is an un-opted-in change) — preserve them deliberately, don't silently drop them by reusing v2-derived logic.

DOS/porting notes specific to this subsystem: `NPEDataRecord.Data: Pointer` is reinterpreted per archetype via hard casts (`PirateDataPtr(DataPtr)` etc.) in every `Implement*NPE`/`Load*NPE`/`Save*NPE`/`CleanUp*NPE` — port to a proper discriminated union or one-implementation-per-archetype class hierarchy, not a reproduced cast. Save/load is raw `BlockRead`/`BlockWrite` of whole records, with a `Version<12` legacy migration path that hardcodes 25 fleet slots/empire and a manually-computed flat index — must be preserved if old saves need to load. `ImplementNPE` does direct `ClrScr`/`OpenWindow`/`WriteString` calls for a progress display inside the dispatcher (`NPE.PAS:70-84`) — needs factoring into a callback/progress-reporter interface for a headless C# port.

### 4.6 Save/Load & Persistence (`LOADSAVE.PAS`)

Flat sequential binary stream via untyped-file `BlockRead`/`BlockWrite` (wrapped as `ReadVariable`/`WriteVariable` in `DOS2.PAS`), no random access. Header: a signature string (`'Anacreon save file v1.3'...`) + version word (`CurrentSFVersion=13`) — **read but never validated** against expected values at load time (`LoadHeader` doesn't compare the signature; the version is only ever passed through to `LoadNPEData`). Section order (identical on save and load): Header → Environment → Sector (galaxy grid) → Planets → Starbases → Fleets → Stargates → Construction sites → Messages → EmpireData → News → NPE data.

Each section follows a "2-byte index + fixed record, repeated, sentinel index 0" pattern, **except Planets**, which unconditionally writes all `1..NoOfPlanets` with no active-set gating (every other section — starbases, gates, construction — does gate on its `SetOfActive*` set first). Fleets additionally serialize their variable-length order queue (count + `CommandRecord`s) inline. `EmpireData` skips `Indep` (reconstructed at load via `InitializeIndependentRecord`) and appends a variable-length linked list of player-defined `NameRecord`s. `LoadFleets` silently repairs zeroed/corrupt coordinates to `(1,1)` with no diagnostic (`LOADSAVE.PAS:250-256`).

**Error handling is weaker than it looks**: inside most `Save*`/`Load*` procedures, `Error:=ReadVariable(...)`/`WriteVariable(...)` is called repeatedly across many fields, but each assignment **overwrites** the previous result — only the *last* I/O call's outcome is ever surfaced to the caller, so a mid-section failure is silently swallowed unless it's also the section's final read/write. On failure, `SaveGame` deletes the partial output file (`Close`+`Erase`); `LoadGame`'s failure path only closes the file, leaving the in-memory `Universe` (already zeroed by `InitializeUniverse` at load start) in a blank state with no explicit recovery.

**Fuel encoding note repeated from §2.3**: a fleet's fuel is split `FuelHigh:Byte`/`Fuel:Integer` reconstructed as `FuelHigh*MaxInt(32767)+Fuel` — this exact non-power-of-two split must be preserved bit-for-bit if the C# port needs to read legacy save files.

---

## 5. UI / Presentation Layer

### 5.1 Window/menu toolkit (`WND.PAS`, `SWINDOWS.PAS`, `PULLDOWN.PAS`, `MENU.PAS`, `EDIT.PAS`, `COLORS.INC`, `BITPIC.INC`, `FASTSCR.ASM`)

**Window model** (`WND.PAS`): 1-based absolute screen coordinates over an 80×25 text display. `WindowRecord` stores an outer rect and an inner content rect (the border style drives the inset via lookup tables keyed by `BorderTypes`). A fixed pool of 20 window slots (`MaxNoOfWindows`) with an explicit z-order stack (`WindowStack`/`CurWind`). `OpenWindow` deactivates the current top (saving its screen contents), allocates a slot, pushes it, draws the border directly via `Mem[ScrSeg:Offset]` CP437 box-drawing writes, and clips CRT I/O to the interior via `Window()`. `CloseWindow` is a deliberately non-obvious anti-flicker composite: it redirects the global `VirtualScreen` pointer to a RAM buffer, replays every remaining stacked window's restore/save into that buffer, then blits the whole composited result to the real screen in one shot — a real behavior a naive rewrite would drop, not an accident. (A defect was flagged: `OpenWindow` advances the stack index before checking whether a free slot exists, so exhausting all 20 slots leaves a stale stack entry.)

**Menu/dispatch model**: `PULLDOWN.PAS` is the top menu bar — one `BarItem` per top-level menu, each heading a circular doubly-linked list of `MenuItem`s; `ActivateMenuBar` is fed one keystroke at a time and returns an integer `Comm` command code once a selection is made (arrow navigation plus mnemonic-key matching at both the bar and open-menu level). Only one bar and one open dropdown can exist at a time (single set of module-level globals). `MENU.PAS` is a structurally separate, simpler popup list widget (nil-terminated linked list, type-ahead search that does not wrap, per-instance window handle) used for generic selection lists (e.g. STAWIND/FLTWIND's sortable entity lists) — it returns a line number via `GetMenuSelect`, not a `Return` code like PULLDOWN. `EDIT.PAS` is the word-wrapping multi-line text editor widget (used for the fleet-orders source editor per §4.1); its wrap width (`RMargin`) is referenced but not defined in this file, so whether editing reflows/mutates stored text destructively is unconfirmed.

**`FASTSCR.ASM`** — hand-written 8088 assembly, Pascal far-call convention (callee cleans the stack). `WriteString` computes the video-memory offset from window-relative x,y plus the packed `WindMin` origin using shift-based multiplication (avoiding a slow 8088 `MUL`), then writes char+attribute word pairs via `STOSW` through an **indirect** pointer (`VirtualScreen`) — this indirection is exactly what `WND.PAS`'s `CloseWindow` exploits to redirect drawing into a RAM buffer. Two distinct CGA hardware workarounds exist: per-character horizontal/vertical-retrace polling ("snow" avoidance, gated by a `CheckSnow` flag) in `WriteString`/`WriteBlanks`, versus a screen-blanking-around-the-whole-block-move approach in the four `Scroll*` routines. **The scroll routines do not clear the row they vacate** — callers are contractually responsible for blanking it first (confirmed both in the ASM and in `MENU.PAS`'s call sites) — a cross-file behavioral contract a partial port could silently break.

**Embedded game logic found in the toolkit layer** (the one thing that must not get lost in a rewrite): `SWINDOWS.PAS` tracks each player's real-time countdown clock (`SecondsLeft:=Round(GetTimeLeft(Player)-ElapsedTime)`) with pause-time explicitly excluded from the deduction (`StopClock`/`StartClock`), though enforcement of a forced turn-end on timeout was not found in these files (see §3/§4's `PLAYTURN.PAS` citation for that). Alt+P/D/Z/N inject literal encoded command macros (`ProbeMacro`, `ProductionMacro`, `DesignateMacro`, `NameMacro`) directly into the input stream — real command semantics baked into a UI convenience. Digit keys are deliberately excluded from the generic command-key set and re-admitted only in the map/scan window (map navigation/zoom use digits specifically) — a keyboard-overload rule, not incidental. Function-key-to-window bindings (F1 Help, F3/F4 Status, F5/F6 Fleet, F7 News, F8 Empire, F9 Names, F10 Map/Scan) are a fixed, specific game convention worth preserving deliberately. `COLORS.INC`'s `ColorRecord` mixes generic UI chrome with a genuine domain taxonomy (`PlayerColor`/`PlayerFleetColor`/`EnemyColor`/`UnscoutedColor`/`NebulaColor`/`GridColor`) worth preserving as a concept list, not just a theme.

### 5.2 Per-feature windows (`MAPWIND.PAS`, `EMPWIND.PAS`, `FLTWIND.PAS`, `STAWIND.PAS`, `NWSWIND.PAS`, `NMSWIND.PAS`, `HLPWIND.PAS`, `NEWS.PAS`, `MESS.PAS`, `DISPLAY.PAS`)

Two premises from the original research plan were wrong and are corrected here: **`NMSWIND.PAS` is the Names window** (F9 — lists player-assigned location/fleet names), not a new-message notifier; `NWSWIND.PAS` is the actual News window (F7). And **diplomatic messaging and status-bar messaging are two entirely separate files**, neither a dedicated "message window": `MESS.PAS` is a pure data model (empire-to-empire messages with per-recipient read-tracking and interception mechanics), while status-bar/prompt lines (`WriteCommLine`, `WriteErrorMessage`) live in `DISPLAY.PAS`.

**`MAPWIND.PAS`** (largest UI file, 1193 lines) — the galaxy scan/map window. **The single most important rendering fact for a port**: one galaxy cell maps to *three* screen columns, because its `CellRecord` (player-fleet char+attr / world char+attr / enemy-fleet char+attr, 6 bytes) is laid out to match packed VGA text-mode memory and blitted directly — every stride/offset calculation in the file derives from this 3-column-per-cell packing, and a rewrite treating it as one-column-per-cell will get scrolling/cursor geometry wrong. Embedded logic beyond pure rendering: mine visibility packs nebula type and mine-owner into a single sector byte's nibbles; the coordinate grid is anchored to the *player's own capital*, not a global origin; every draw pass re-queries `Known`/`Scouted` visibility live rather than working from pre-filtered data; selecting a map cell serializes the choice into synthetic keystrokes pushed onto the input stream to hand off to command processing (a UI-to-dispatch bridge, not state mutation, but real coupling).

**`NEWS.PAS`** — the underlying event-log data model, not pure UI, and reviewed with corresponding rigor. `NewsRecord = RECORD Headline: NewsTypes; Loc1: Location; Parm1,Parm2,Parm3: Integer; Next: NewsRecordPtr END`, one singly-linked list per empire (no shared pool). `AddNews(Player,Head,Loc,P1,P2,P3)` no-ops for `Indep`/inactive empires and **silently drops the event if the heap is low** (no error, no fallback) — a port should decide whether to preserve or fix this. `AddGlobalNews` fans a news item out to every active empire that has `Scouted` the event's source object — i.e. broadcast news is itself gated by the scouting/visibility rule, not a flat empire list. The full `NewsTypes` enum (~80 values — production shortfalls, tech changes, rebellions/warnings at four escalating tiers, battle outcomes, ambrosia addiction events, mine/LAM/disrupter combat events, message-intercept events, holocaust events, etc.) was catalogued in full with its documented `(Loc,Parm...)` meaning per constant — see the source file for the complete list; a `LocalNews` subset of ~23 types is used by the News window to sort local-scope items after empire-wide ones. **Scope gap**: `NEWS.PAS` defines the event and its raw parameters, but the actual `Parm1/2/3`→display-text formatting (`GetNewsLine`) lives in the `PrimIntr`/`Intrface` facade layer, not here.

**Other windows** (lighter treatment, per the overall task's UI guidance): `EMPWIND.PAS`/`FLTWIND.PAS`/`STAWIND.PAS`/`NWSWIND.PAS`/`NMSWIND.PAS` share one structural pattern — a paged list over a data array, built by a per-window `Initialize*DataArray` that filters/sorts by ownership and `Scouted`/`Known` visibility, with an identical `Open`/`Close`/`Draw`/page-navigation shape across all four; in a C# port these collapse to one generic paged-list view plus a per-window data provider. `HLPWIND.PAS` is confirmed clean of embedded game logic — pure help-file paging with a hardcoded topic index. `DISPLAY.PAS` contains one real domain-rule check worth flagging: `InterpretObj` rejects player input referencing a destroyed base/gate by checking the live `SetOfActiveStarbases`/`SetOfActiveGates` sets, not just parsing syntax. `MESS.PAS`'s interception mechanic computes chance-to-intercept from distance (`Round((1/distance²)×150)`) per enemy planet and garbles intercepted text with randomized character runs; a max-5-intercepts-per-empire cap was found to be enforced loosely (checked once per outer-loop candidate, not after every increment inside the inner loop), and multi-recipient sends use only the last-processed recipient's capital for all interception distance rolls — both worth a decision (preserve vs. fix) during the port.

---

## 6. DOS/Pascal-Specific Concerns for Porting

| Mechanism | Where seen | Port decision needed |
|---|---|---|
| Overlay units (`{$O UnitName}`, `OvrInit`/`OvrSetBuf`/`OvrSetRetry`) | `ANACREON.PAS:48-64`, `OVERINIT.PAS` | Drop entirely — pure DOS memory-pressure artifact (640KB conventional memory). Do not preserve overlay grouping as a module boundary; it reflects size/frequency tradeoffs, not designed seams. |
| `{$F+}` far-call directive | pervasive, paired with `{$IFDEF Overlay}` | Drop — meaningless outside segmented memory. |
| Split/scaled fuel encoding (`FuelHigh:Byte` + `Fuel:Integer` = `FuelHigh*MaxInt+Fuel`, `MaxInt=32767`) | `DATASTRC.PAS:97-99`, `PRIMINTR.PAS:853-880` | If legacy saves must load: replicate the exact non-power-of-two split. Otherwise: collapse to one wide integer, but confirm no other code depends on the two-part representation's overflow behavior. |
| Fixed 9999 resource cap (`MaxResources`, `ThgLmt` saturation) | `TYPES.PAS:40`, `MISC.PAS:98-109`, used throughout | Per project memory, v2 source removes this cap as an *un-opted-in* change — preserve the v1.31 cap unless the port explicitly wants v2 behavior. |
| Turbo Pascal RNG (`Random`/`Randomize`/`RandSeed`) via `Rnd`/`RndVar` wrappers | `INT.PAS:108-131`, seeded `NEWGAME.PAS:1735-1738` | `System.Random` will **not** reproduce seeded scenarios bit-for-bit — needs a from-scratch reimplementation of Turbo Pascal's specific LCG if seed-reproducibility matters. |
| `Trunc` vs `Round` mixed per call site (Pascal `Round`=nearest, away-from-zero on ties; `Trunc`=toward zero) | throughout `UPDATE.PAS`, `ATTACK.PAS`, `NEWGAME.PAS` | Match per-call-site, not uniformly — `Math.Round`'s default banker's rounding differs from Pascal's on ties. |
| Untyped file I/O, `{$I-}`/`{$I+}` + `IOResult` | `LOADSAVE.PAS`, `ENVIRON.PAS`, `DOS2.PAS` | Maps to `Stream`+try/catch; note the "last-write-wins" error-swallowing bug in `LOADSAVE.PAS` — decide whether to preserve or silently fix. |
| Raw DOS interrupt calls ($21 AH=$2C clock read, $44 IOCTL device info, PSP/environment-block parsing) | `SYSTEM2.PAS:125-146`, `DOS2.PAS:268-296,168-234` | Replace with `DateTime.Now`, `System.Environment`, standard file APIs respectively. |
| Inline x86 ASM (`INLINE(...)` opcodes, `FASTSCR.ASM`) | `SYSTEM2.PAS:154-160` (`FillWord`), `FASTSCR.ASM` (video writes) | Replace with managed array-fill / console-buffer APIs. No inline ASM was found in the combat/fleet/economy subsystems — it's confined to low-level utility and screen-write code. |
| Direct text-mode video memory writes (`Mem[ScrSeg:offset]`) | `ATTCOMM.PAS`, `FLTCOMM.PAS`, likely all `*WIND.PAS` files | Full UI rewrite territory — but note some of these calls are physically embedded inside logic-bearing command files, not cleanly isolated; extract carefully. |
| Record-to-integer reinterpret casts for equality (`Integer(Coord1)=Integer(Coord2)`) | `MISC.PAS:124-146` | Must become explicit field-wise equality — hard-codes `XYCoord`/`IDNumber`/`Location` byte sizes that don't transfer to C#. |
| `ABSOLUTE` variable aliasing | `DATASTRC.PAS:235` (`GlobalSets ABSOLUTE SetOfActiveFleets`), `PROLOG.PAS:93-121` (`BitPicture ABSOLUTE Pic`) | No C# equivalent without `unsafe`/`MemoryMarshal`; pick one representation instead of dual-addressing. |
| Manual heap buffer management (`GetMem`/`FreeMem`/`Move`, realloc-by-copy) | `ORDERS.PAS:106-127` (order queue), `ARTIFACT.PAS:135-138` (type array) | Replace with `List<T>`/managed arrays; the "nominal oversized array, real allocation smaller" idiom (galaxy sector spine, artifact list) disappears entirely. |
| Live pointer stashed in a fixed-size byte array (`FleetRecord.OrderData[1..6]` as a 6-byte far-pointer-plus-length) | `ORDERS.PAS:326-335` | Not literally serializable — a save/load path must already reconstruct orders from a different representation; locate and document it before assuming the in-memory layout is the save format. |
| Pascal `SET OF` bitmask types used for membership + adjacency indexing | pervasive (`SetOfActiveFleets`, `TechnologySet`, `ScoutSet`, etc.) | `HashSet<T>`/`BitArray`/flags-enum depending on cardinality; every `IN`/set-union call site needs conversion. |
| `WITH ... DO` implicit field scoping, including cases where the `WITH`-block's record field silently shadows an outer same-named loop variable | throughout; concretely `LOADSAVE.PAS:127-131` etc. (loop var `Emp` shadowed by `PlanetRecord.Emp` inside `WITH`) | Must become fully-qualified field access — verify each `WITH` block for shadowing before mechanically flattening it, since the shadowed behavior may be intentional and non-obvious. |
| Variant records (Pascal `CASE` inside a `RECORD`) | `CdeTypes.VariableRecord`, `ORDERS.CommandRecord` | Port to a discriminated union / tagged class hierarchy, not an unsafe overlay. |
| Global mutable singletons (`Universe: ^UniverseRecord`, `Sector`, `Player`) referenced directly from every procedure, sometimes re-passed as same-named `VAR` parameters that alias the global | `ANACREON.PAS:378` `Player` aliasing confirmed | A C# port will want an explicit game-state/session object threaded through, rather than ambient global access — but note the original code's "shadow the global with a same-named parameter" idiom is intentional aliasing, not a bug, when auditing call sites during extraction. |

---

## 7. File Index

*One line per file. Files marked "not yet reviewed in depth" are named/sized from the directory listing and cross-references only — do not cite specifics about them without reading them first.*

| File | Lines | Purpose |
|---|---:|---|
| ANACREON.PAS | 419 | Program entry point; outer game loop, overlay init, turn-advance orchestration. §1, §3. |
| ANACREON.CNF | — | Runtime-written config file (scenario/save/help dirs, color) — data, not source. |
| ANACREON.OVR | — | Compiled overlay file loaded by the overlay manager at runtime — binary, not source. |
| ARTIFACT.PAS | 304 | Artifact type/instance definitions; dispatches `ActiveSituation`/`CloseUpSituation` into the `Code` VM. §4.4. |
| ATTACK.PAS | 1735 | Core combat resolution engine: grouping, targeting, damage, casualties, retreat/surrender, conquest, holocaust. §4.2. |
| ATTCOMM.PAS | 1748 | Player-facing attack command UI/menu layer over `ATTACK.PAS`. §4.2. |
| ATTNPE.PAS | 427 | AI's unattended battle driver (not in original brief; found via directory listing). §4.2. |
| BATTLE.PAS | 79 | Separate small aggregate-force combat model (`CalcAttackRound`/`CalcMilitaryPower`), used only by bombing. §4.2. |
| BITCOMP.PAS | 47 | Standalone offline authoring tool (`PROGRAM Compile1`) that packs 80-char bitmap lines into Word bitmasks — not linked into the runtime game. |
| BITPIC.INC | 64 | Bitmap/character-graphics include data for title-screen art (4 hardcoded monochrome glyph arrays). Cosmetic only. §5.1. |
| BOMBER.PAS | 93 | Strategic bombing: functional defense-vs-bomber-fleet phase; surface-bombing phase is an empty, uncalled stub. §4.2. |
| CDETYPES.PAS | 71 | Types for the artifact-scripting bytecode VM (registers, opcodes, action records). §2.5. |
| CLSCOMM.PAS | 813 | Read-only planet/fleet "close-up" and production-info display commands. §4.3. |
| CODE.PAS | 576 | Confirmed the `CodeInterpreter` VM loop for `CDETYPES.PAS`'s bytecode — dispatches control-flow opcodes recursively and falls through to `ExecuteAction` for the 17 action opcodes; 3 of those (`CreateACT`/`DestructACT`/`WinGameACT`) are documented but unimplemented (silent no-op). §4.4. |
| COLORS.INC | 167 | Three static named-role color palettes (color/B&W/mono) for the UI toolkit. §5.1. |
| CONSTR.PAS | 308 | Player UI for starting/aborting/checking construction sites. §4.3. |
| DATACNST.PAS | 568 | Economic/combat/production tuning constants and display-name tables — the game's "balance spreadsheet." §2.4. |
| DATASTRC.PAS | 241 | The `UniverseRecord` and all per-entity record types (Planet/Starbase/Fleet/Stargate/Constr/EmpireData). §2.3. |
| DEADCODE.PAS | 469 | Confirmed dead: opens directly on a `PROCEDURE` with no `UNIT`/`PROGRAM` header, not compilable as-is; its two largest procedures (`GetCapitals`, `NewNPERegion`) are referenced nowhere else in the source tree. |
| DFA.PAS | 170 | Confirmed: a genuine hand-rolled 7-state DFA tokenizer (`DFA1NextToken`) for scenario-script text (whitespace/quoted tokens, `;`-comments) — this is the tokenizer behind `.SCN` parsing referenced in §4.4. |
| DESIGN.PAS | 1062 | Misleadingly named — actually `DesignateCommand` (world re-typing) plus an unrelated grab-bag: LAM launch, ISSP editing, tech trading, granting independence. §4.3. |
| DISPLAY.PAS | 219 | General display utilities: status-bar/prompt lines, main command-window setup, an ID-picker menu wrapper, and player-input validation against live game state. §5.2. |
| DLIST.PAS | 63 | **Incomplete/broken**: a generic doubly-linked-list unit whose `AddListElement` procedure is truncated mid-declaration with no body — will not compile as shown; almost certainly dead/unused. |
| DOS2.PAS | 565 | DOS system-call library: shell-out, PSP/environment parsing, printer output, buffered file copy, load/save file-picker widget. §6. |
| EDIT.PAS | 574 | Word-wrapping multi-line text-editing widget over a linked-list text buffer, used by the fleet-orders source editor. §5.1. |
| EIO.PAS | 563 | Confirmed "Extended Input Output Library": console I/O over Crt/Dos — virtual-screen buffer with snow avoidance, keyboard input, screen save/restore, cursor shape; its `WriteString`/`WriteBlanks`/`Scroll*` primitives are external, linked from `FASTSCR.ASM`/`.OBJ`. |
| EMPWIND.PAS | 127 | Empire status window (F8) — thin open/close/draw wrapper delegating line text to the facade layer. §5.2. |
| ENVIRON.PAS | 190 | Config-file (ANACREON.CNF) load/save and in-session environment globals (Year, Player, TimePerTurn, AsyncTurns, etc.) with their own save/load hooks. §6. |
| FASTSCR.ASM | 762 | Hand-written 8088 assembly for hot-path text-mode screen writes and scrolling, with CGA snow-avoidance/blanking workarounds. §5.1/§6. |
| FASTSCR.OBJ | — | Compiled object file for FASTSCR.ASM — binary, not source. |
| FLEET.PAS | 908 | Core fleet data engine: create/deploy/destroy, composition transfer, movement, fuel, order execution. §4.1. |
| FLTCOMM.PAS | 933 | Player-facing fleet command UI (Launch/Abort/Transfer/Refuel/Probe/MineSweep/Orders). §4.1. |
| FLTWIND.PAS | 326 | Fleet status window (F5) — paged, sorted list of player fleets + mobile starbases + scouted/known enemy fleets. §5.2. |
| GALAXY.PAS | 140 | The sector grid: coordinates, per-cell object/scout/mine/nebula state, load/save. §2.2. |
| HLPWIND.PAS | 260 | Help window (F1) / help-file display — confirmed clean of embedded game logic. §5.2. |
| INT.PAS | 133 | Pure integer-math/RNG helper library (`Rnd`, `RndVar`, `Sgn`, `ISqrt`, etc.) — confirmed by full read. §6. |
| INTRFACE.PAS | 1660 | Data-access facade, higher-level half: scouting/visibility engine, object creation/destruction, industrial-distribution model, formatted status/news lines. Paired with `PRIMINTR.PAS` — full division of labor in §1/§6. |
| LOADSAVE.PAS | 716 | Save-game binary format: section order, per-section read/write, error handling. §4.6. |
| LOG.TXT | — | Build/dev log, not source. |
| LSORT.PAS | 495 | `LongInt`-indexed variant of `SORT.PAS`'s disk-paged QuickSort — confirmed unreferenced anywhere else in the tree (likely an unused Borland toolbox import). |
| MAPWIND.PAS | 1193 | Galaxy map window (F10) — largest UI file; 3-screen-columns-per-cell rendering model. §5.2. |
| MENU.PAS | 388 | Generic scrollable popup list-selection widget (distinct from `PULLDOWN.PAS`'s menu bar). §5.1. |
| MESS.PAS | 371 | Diplomatic empire-to-empire message data model: send/read-tracking/interception mechanics. Not a window/view. §5.2. |
| MISC.PAS | 324 | Grab-bag of geometry, fleet/cargo arithmetic, and the TIP production formula. §4.4. |
| MK.BAT / D.BAT | — | Build scripts (`tpc anacreon`, debugger launch) — confirm overlay/compile flags via `TPC.CFG`. §1. |
| MSCCOMM.PAS | 657 | Empire-wide defense-distribution editor and object self-destruct; several commands (Holocaust/Artifact/Transaction) are dead/commented-out. §4.3. |
| NAMES.PAS | 243 | Player-defined location-name bookmarks (add/delete/list) and a status-report printer — confirmed **not** a name generator. §4.4. |
| NEWGAME.PAS | 1963 | Scenario-file (`.SCN`) interpreter: header parse, world/nebula/empire generation commands, RNG seeding. Largest file in the codebase. §4.4. |
| NEWS.PAS | 366 | The news/event-log data model (`AddNews`/`EraseNews`, ~80-value `NewsTypes` enum) — game-logic-adjacent, not pure UI. §5.2. |
| NMSWIND.PAS | 240 | **Names window (F9)** — confirmed not a message notifier; lists player-assigned location/fleet names. §5.2. |
| NPE.PAS | 113 | AI top-level dispatcher (always resident, not overlaid) — `ImplementNPE(Empire)` entry point, routes by archetype. §4.5. |
| NPE00.PAS | 739 | Kingdom-only high-level AI policy layer (news review, defense, expansion, war-cabinet) — despite its name/number, not a shared utility file; only `NPE02.PAS` uses it. §4.5. |
| NPE01.PAS | 536 | Pirate archetype AI (overlaid). §4.5. |
| NPE02.PAS | 330 | Kingdom1 **and** Kingdom2 archetype AI, one shared implementation with two gene/policy seed presets (overlaid). §4.5. |
| NPE03.PAS | 196 | Guardian archetype AI — static, reactive, LAM-strikes only (overlaid). §4.5. |
| NPE04.PAS | 627 | Berserker archetype AI — mobile roaming starbases are the core mechanic (overlaid). §4.5. |
| NPEINTR.PAS | 1732 | Largest single AI file (overlaid) — the genuinely shared AI utility layer: fleet deployment, targeting, diplomacy state, mission implementations, used by every archetype. §4.5. |
| NPETYPES.PAS | 165 | AI subsystem's type vocabulary (archetypes, personality genes, missions, diplomatic state). §2.5. |
| NWSWIND.PAS | 276 | **News window (F7)** — paged list over the per-empire `NEWS.PAS` linked list. §5.2. |
| ORDERS.PAS | 351 | Fleet-orders mini-language compiler/decompiler (`DEST`/`TRAN`/`REPEAT`/`WAIT`). §4.1/4.3. |
| OVERINIT.PAS | 17 | Overlay-manager initialization (`OvrInit`/`OvrSetBuf`/`OvrSetRetry`) — not in original brief; found via directory listing. §1. |
| PLAYTURN.PAS | 1229 | Per-player interactive turn loop: command bar, parameter validation, dispatch. §3. |
| PROLOG.PAS | 1449 | Main menu, save/load orchestration, per-turn player setup/password gate. §3, §4.4. |
| PULLDOWN.PAS | 497 | Pulldown-menu-bar widget — top-level command dispatch, returns an integer command code. §5.1. |
| QSORT.PAS | 102 | In-place quicksort by leading Word key — **confirmed live**, used by `STAWIND.PAS`/`FLTWIND.PAS` to sort their entity lists. |
| REAL1.PAS | 24 | Single function `Expnt(Base,Exponent)` — real-number power function (no builtin `**` in Turbo Pascal), used by `MISC.PAS`. |
| RESOURCE.PAS | 110 | Cargo-space accounting and casualty subtraction for fleet resource arrays (own parallel cargo-capacity model vs. `MISC.PAS`). §4.4. |
| SBASE.PAS | 265 | Mobile-starbase movement (command bases/fortresses). §4.1. |
| SCENA.PAS | 344 | Scenario **text-database** reader (world-background flavor text keyed to ownership state) — not a scenario/map/victory-condition definer (that's `NEWGAME.PAS`). §4.4. |
| SORT.PAS | 493 | Borland "Database Toolbox" disk-paged non-recursive QuickSort for large record sets — confirmed unreferenced anywhere else in the tree (likely an unused Borland toolbox import, distinct from the live `QSORT.PAS`). |
| STAWIND.PAS | 322 | Star/planet status window (F3) — paged, sorted list of owned + scouted worlds. §5.2. |
| STRG.PAS | 196 | Foundational string-type definitions (`String6/8/16/24/32/50/64/128`, `LineStr`=80, `MaxStr`=255, all Pascal length-prefixed strings) plus string helpers (`Int2Str`, `AllUpCase`, `AdjustString`, etc.) — confirmed by full read, widely imported. |
| SWINDOWS.PAS | 429 | Game-specific window orchestration: global input dispatch, function-key routing, command macros, the per-player turn clock. §5.1. |
| SYSTEM2.PAS | 164 | Low-level system utilities: DOS clock read (per-turn timer), an inline-ASM `FillWord`, filename helpers. §6. |
| TEST.PAS | 12 | Confirmed scaffolding: standalone save/load smoke-test program, not in `ANACREON.PAS`'s uses clause. |
| TEST1.PAS | 43 | Confirmed scaffolding: standalone manual test harness for `BATTLE.PAS`'s `CalcAttackRound`, not in `ANACREON.PAS`'s uses clause (the function it tests is live game logic; the harness itself is not shipped). |
| TEXTSTRC.PAS | 265 | Generic doubly-linked word-wrapped text-buffer utility (help text, scenario intros, order editor). §2.5. |
| TMA.PAS | 145 | Confirmed: splash-screen logo (`TMALogo`, animated ASCII art) and an about/credits box (`AboutAnacreon`), called from the main program's `Introduction`. |
| TPC.CFG | — | Turbo Pascal compiler config: `/DOverlay`, range/stack/var-checking off. §1. |
| TRANSACT.PAS | 106 | Generic bytecode-dispatch hook for scenario-scripted world transactions — dormant, no live caller found. §4.3. |
| TYPES.PAS | 194 | Global primitive types, enums, and active-entity bitset globals. §2.1. |
| UPDATE.PAS | 1490 | Annual simulation tick: per-world production/population/tech/revolution, construction progress, empire tech advancement. §3, §4.3. |
| VIEWMAP.PAS | 52 | Confirmed: a standalone `PROGRAM` (not a unit, not linked into the main game), a developer/debug savegame-map viewer with keyboard panning — loads a save file directly and renders the galaxy for inspection outside normal play. |
| WND.PAS | 661 | Core window/widget manager: open/close/stack/border/modal-dialog primitives. §5.1. |
| WNDTYPES.PAS | 47 | Window-command constants and input-command-to-window mapping. §2.5. |
| License.txt | — | Not source. |

---

## 8. Manual-to-Code Glossary

Built from a full read of `AnacreonManual.md`. Pascal-identifier columns are filled in only where confirmed by an actual code read cited elsewhere in this document; a blank/"see §" entry means the mapping is inferred from naming convention only and should be verified before being relied on.

| Manual term | Pascal identifier(s) | Notes |
|---|---|---|
| Empire | `Empire = (Empire1..Empire8, Indep)` (TYPES.PAS:51) | `Indep` is the "unclaimed/independent" pseudo-empire, not a 9th player. |
| Capital | `WorldTypes.CapTyp`; `EmpireDataRecord.Capital: IDNumber` | Losing it triggers `ConquerEmpire` if the empire has the `CentralEMD` modifier (§4.2). |
| Fighter / fgt | `ShipTypes.fgt` | Warp drive, cheap, ground-attack capable. |
| Hunter-killer / hkr | `ShipTypes.hkr`, `FleetTypes.HKFleet` | Jump drive; cloaked unless attacking (`GroupRecord.Flg`, §4.2). |
| Jumpship / jmp | `ShipTypes.jmp` | 10 sectors/yr, `FltMovementRate[JumpFleet]=10`. |
| Jumptransport / jtn | `ShipTypes.jtn` | 1/5 transport cargo capacity (`TrnAdj[jtn]`). |
| Penetrator / pen | `ShipTypes.pen`, `FleetTypes.Penetrator` | Modified warp, 2 sectors/yr, sensor-jamming. |
| Starship / ssp / "str" | `ShipTypes.ssp` (manual abbreviates "str") | Slowest, most powerful. |
| Transport / trn | `ShipTypes.trn` | Full cargo capacity baseline. |
| Legion (men) | `CargoTypes.men` | 10,000 troops per legion per the manual; ground-assault/defense troops. |
| Ninja legion / nnj | `CargoTypes.nnj` | 5x normal legion strength per manual; requires ambrosia to produce (`NnjTyp` worlds). |
| Supplies / sup | `CargoTypes.sup`, `IndusTypes.SupInd` | Food/medicine; `SuppliesPerBillion=25` (DATACNST.PAS:50). |
| Trillum / tri | `CargoTypes.tri`, `IndusTypes.TriInd` | Fuel; `TriResByClass`, `FuelCons`/`FuelCap`. |
| Metals / met | `CargoTypes.met`, `IndusTypes.MinInd` | Ship/industry construction material. |
| Chemicals / che | `CargoTypes.che`, `IndusTypes.CheInd` | Ship/defense/ambrosia construction material. |
| Ambrosia / amb | `CargoTypes.amb`, `IndusTypes.BioInd`, `SpecialConditions.AmbAddict` | See §4.6/§2.4 for the addiction formula constants. |
| LAM | `DefnsTypes.LAM` | Long-range attack missile, range 5 sectors, base/capital-built. |
| GDM | `DefnsTypes.GDM` | Ground defense missile, orbit-range. |
| Ion cannon / ion | `DefnsTypes.ion` | Sub-orbit range, ground-based. |
| Defense satellite / def | `DefnsTypes.def` | High-orbit + orbit range. |
| Deep space / high orbit / orbit / sub-orbit / ground | `ShellPos = (DpSpc,HiOrb,Orbit,SbOrb,Grnd)` (TYPES.PAS:172) | The five combat shells; see §4.2 shell-range matrix (`InRangeOfDefense`). |
| Group (combat) | `GroupRecord`/`GroupArray` (ATTACK.PAS:57-68), max 9 (`MaxNoOfGroups`) | Matches manual's "no more than nine groups." |
| Standard battle configuration | `DefaultDistribution`/`DefaultGroup` (ATTACK.PAS:1301-1367) | |
| Retreat | `GroupStatus.GRtrt`, `AttackResultTypes.AttRetreatsART`, `FleetRetreats` (AI) | |
| Conquest | `ConquerWorld`, `ConquerEmpire`, `AttackResultTypes.DefConqueredART` (ATTACK.PAS:915-1139) | |
| Surrender | `EnemySurrenders`, `HoloResultTypes.WorldSurrendersHRT` | Threshold-driven, not damage-driven (§4.2). |
| ISSP | `ChangeISSPCom` (DESIGN.PAS:262-389), `DATACNST.PAS`'s `ISSP[0..10]` table | Industrial Self-Sufficiency Percentage; 11 discrete levels 1%-500%. |
| TIP (Total Industrial Potential/Production) | `TotalProd` (MISC.PAS:247), constants `K1,K2,K3` (DATACNST.PAS:24-26) | |
| Designate | `DesignateCommand` (DESIGN.PAS:655-859), `DesignateWorld` (INTRFACE.PAS) | |
| World class | `WorldClass` enum (TYPES.PAS:93-95), `ClassIndAdj`/`TriResByClass`/`MinTechForClass` (DATACNST.PAS) | 21 values. |
| World type | `WorldTypes` enum (TYPES.PAS:98-100), `TypeData`/`PrincipalIndustry`/`MinTechForType` (DATACNST.PAS) | 21 values. |
| Technology level | `TechLevel` enum (TYPES.PAS:88-90), `TechDev`/`TechAdj`/`TechAdj2`/`BasePop` (DATACNST.PAS) | 11 levels, pre-tech through gate. |
| Revolution index | `EmpireDataRecord`/`PlanetRecord.RevIndex: Index`, `UpdateRevolution`/`Rebellion` (UPDATE.PAS:619-713) | |
| Efficiency | `PlanetRecord.Eff: Index`, `UpdateEfficiency` (UPDATE.PAS:1007) | |
| Population | `PlanetRecord.Pop: Population`, `UpdatePopulation` (UPDATE.PAS:1074) | |
| Fleet order commands (Transfer/Destination/Repeat/Wait) | `CommandTypes` (`TransCOM`/`DestCOM`/`RepeatCOM`/`WaitCOM`), `ParseLine`/`CompileOrders` (ORDERS.PAS) | Matches manual's `TRANSFER`/`DESTINATION`/`REPEAT`/`WAIT` exactly. |
| Probe | `ProbeRecord`/`ProbeArray` (DATASTRC.PAS:170-175), `NoOfProbesPerEmpire=10` | |
| Nebula (normal / dark / dense) | `NebulaTypes = (NoNeb,Nebula,DarkNebula,DenseNebula)` (TYPES.PAS:130) | Dense blocks movement/placement; dark restricts scan to own sector. |
| Stargate / gate | `StargateTypes.gte`, `StargateRecord` | |
| Warp link | `StargateTypes.lnk` (per `ConstrTypes`) | |
| Disrupter | `StargateTypes.dis` | Slows enemy jumpfleets within 3 sectors (per manual; combat-research confirmed the range-3 check exists in fleet movement, §4.1). |
| SRM (minefield) | `ConstrTypes.SRM`, `Sector.Special` high nibble, `PutMine`/`EnemyMine` | |
| Outpost / command base / industrial complex / fortress | `ConstrTypes`/`StarbaseTypes` (`out,cmm,cmp,frt`) | Command bases and fortresses are mobile (§2.3, `SBASE.PAS`). |
| Pirate / Kingdom / Aggressor / Berserker / Guardian / Trader (AI archetypes) | `NPEmpireTypes` (`PirateNPE,Kingdom1NPE,Kingdom2NPE,BerserkerNPE,GuardianNPE,TraderNPE`) | Manual's Appendix A (lines 4170-4177) documents only 3 archetypes: **Pirate** ("sends battle fleets looking for undefended transport fleets" — confirmed match to `PirateNPE`/§4.5's NPE01 raider behavior), **Kingdom** ("defensive... will only attack if provoked" — maps to `Kingdom1NPE`/`Kingdom2NPE`, which §4.5 confirms share one code path differing only in initial gene/policy seed; the manual does not distinguish two Kingdom variants, so the 1/2 split is a code-only distinction), and **Aggressor** ("expand dominion over independent worlds, occasionally attacks without provocation" — an *inferred*, semantic-only match to `BerserkerNPE`, not a name match). **`GuardianNPE` and `TraderNPE` have no description anywhere in the manual's ~5,000 lines** — Guardian's purely-reactive LAM-striking behavior and Trader's complete non-implementation (§4.5: routed through the Pirate code path) are both confirmed by code alone, not manual cross-reference. Do not treat the manual as covering these two. |
| Victory conditions | *(none confirmed live)* | Manual describes end conditions (year/world/ship count) as scenario-configured "game ends" thresholds, but `NEWGAME.PAS`'s scenario loop has no live `BEGINVICTORYCONDITIONS` handling (§4.4) — likely player/GM-adjudicated in practice, not engine-enforced, in this build. |
| Artifact | `ArtifactRecord`/`ArtifactTypeRecord` (ARTIFACT.PAS), `CDETYPES.PAS` VM | Scripting hook exists in code; scenario-file authoring path for it is dead (§4.4). |

---

## 9. Findings from porting (dead code, quirks, real bugs)

Things discovered about the *original* Pascal source while building the C# port — not port design
decisions (see [`PORT_DESIGN.md`](PORT_DESIGN.md) for those) or implementation status (see
[`ROADMAP.md`](ROADMAP.md)). A living list, added to as each phase's own investigation turns something
up; light on detail where detail hasn't been derived. See also
[`QUESTIONS_FOR_GEORGE.md`](QUESTIONS_FOR_GEORGE.md) for the subset of these worth asking the original
author about directly.

### `ATTCOMM.PAS`/`FLTCOMM.PAS`/`ORDERS.PAS` are ~entirely DOS UI or a human order-compiler

Not simulation logic — confirmed while scoping Combat (Phase 5). The one thing worth keeping from
them: `ATTCOMM.PAS`'s `AutoAttackCommand` confirms the real non-interactive entry point every
non-human attack goes through is `ATTNPE.PAS`'s `NPEAttack(fleetId, targetId, intent, retreatIndex)`.

### `ATTNPE.PAS`'s "NPE" means "no player experience" (auto-resolved), not "AI decision logic"

It's the round-by-round resolution driver (`FleetEngage`/`WorldEngage`/`GroupEngage`) both a human's
auto-resolve and a computer empire's attack funnel through — no strategic targeting logic lives here.

### An earlier, simpler combat-resolution design predates the group/shell system

While scoping Phase 5 (Combat), `BATTLE.PAS` (`CalcAttackRound`/`CalcMilitaryPower`) and `BOMBER.PAS`
(`Bombing1stPhase`/`Bombing2ndPhase`) turned up as dead code — grepped for callers across the entire
1.31 source tree, and nothing outside those two files calls into either one. `Bombing2ndPhase`'s own
body is an empty stub (`BEGIN END`), and `Bombing1stPhase` (which does have a real body, calling into
`BATTLE.PAS`) is itself never called by anything.

The shape here is a much simpler, non-group-based "quick resolve" combat formula — a single
attacker-vs-defender military-power ratio, no orbital shells, no per-ship-type targeting — next to
the real, live combat engine (`ATTACK.PAS`'s group/shell system: `GroupRecord`s advancing through
`ShellPosition`s, `CombatTable`-driven per-type targeting, round-by-round resolution). Reads like an
earlier or alternate design for resolving an attack that was superseded once the group/shell system
was built, left in the tree rather than deleted. This is inference from dead code's shape, not
confirmed history (see `QUESTIONS_FOR_GEORGE.md`). Not ported — the live `ATTACK.PAS` engine is the
one this port implements.

### Probes may once have persisted at their destination instead of resolving instantly

`ProbeStatus` (`TYPES.PAS:125`) declares four states — `PReady`, `PInTrans`, `PAtDest`, `PLost` — but
the shipped 1.31 (and 2.0) behavior only ever reaches the first two. `UpdateProbes`
(`INTRFACE.PAS:1346-1359`) resolves an in-transit probe in a single call (scout, then straight back to
`PReady`); `PAtDest`/`PLost` are never assigned anywhere in either source tree, and `ProbesReturn`
(`INTRFACE.PAS:1361-1370`, the one procedure that reads `PAtDest`) is never called in either tree
either — checked both directly, not assumed from one.

The shape is suggestive: a design where a probe might take multiple turns to arrive, then sit at its
destination (continuing to scout, or awaiting recall) before returning, would need exactly this
four-state enum. Whether that was ever implemented, planned, or removed before either shipped source
tree isn't something source alone can answer — this is inference from dead code's shape, not a
confirmed history. Not ported (see `ROADMAP.md`'s Phase 3), and not planned unless it resurfaces as
something worth reviving deliberately.

### `Consolidate` (`ATTNPE.PAS`) is confirmed dead code

Declared, empty body, never called anywhere including from within its own unit — same treatment as
`BATTLE.PAS`/`BOMBER.PAS`. Not ported.

### `HolocaustCommand`/`HolocaustWorld`/`HolocaustEffectiveness` are confirmed dead code

A full nuclear-bombardment mechanic (`ATTACK.PAS`) — surrender chance, population deaths, industry
destruction, tech regression — with no way to reach it in a shipped build. `MSCCOMM.PAS`'s
`HolocaustCommand` (the only caller of `HolocaustWorld`/`HolocaustEffectiveness`) is wrapped in a
Pascal comment block, and so is its own forward interface declaration a few lines above it — the unit
couldn't even export the symbol. `PLAYTURN.PAS`'s command-dispatch entry for it
(`HoloCom : HolocaustCommand;`) sits inside a separate `(*ARTIFACTS ... *)` commented block alongside
`TransactionCommand`/`ArtifactCommand`, itself nested one level inside a live `{$IFNDEF Demo}` region
(so those three specifically are cut, not the whole surrounding command table). Identical in both the
1.31 and 2.0 source trees. `SelfDestructCommand`/`SelfDestructObject` and `LAMCom`/`LaunchLAM` sit
right next to the `(*ARTIFACTS*)` block in the same command table but *outside* it — real, reachable
commands, unlike Holocaust. Not ported (see `Core/Combat/CombatStandalone.cs`'s own doc comment); see
`docs/QUESTIONS_FOR_GEORGE.md` for the open question of why Holocaust specifically got bundled into
the same cut as the artifacts feature.

An earlier research pass (Phase 5 commit 5a's scoping) also claimed `SelfDestructObject` had no
caller anywhere in the source tree. That was wrong — missed by not checking `MSCCOMM.PAS`'s own
command procedures closely enough — and has since been corrected (`SelfDestructCommand` calls it
directly, MSCCOMM.PAS:519). Worth remembering as a reminder that "grepped every caller, found none" is
only as good as which files were actually searched.

### FreePascal's `Round` is banker's rounding, not Turbo Pascal's

`PascalMath.PascalRound` was originally implemented as half-away-from-zero (matching an assumption
about Turbo Pascal's own behavior); FreePascal's actual `Round` is half-to-even at exact `.5`
boundaries — confirmed directly against the ground-truth compiler (`Round(2.5)=2`, `Round(3.5)=4`,
`Round(-2.5)=-2`), not assumed. `GetEnemy`'s clean-percentage shell split (5%/10%/15% of a round ship
count, Phase 5d) was the first formula in the whole port to land exactly on a `.5` boundary, so the
wrong assumption went undetected through every earlier phase's own golden-file coverage until then.
See `PORT_DESIGN.md`'s randomness section for the fix and how the fallout was checked.

### Scenario golden-file testing can't be bit-exact, and why

`ScenarioLoaderGoldenTests` (loading a real `.SCN` file end to end through the C# `ScenarioLoader`)
only asserts fields that never involve a random draw. Every field derived from `Rnd()`/`RndVar()` —
planet coordinates, population, trillum, ships/cargo/defenses, world class/tech, nebula cell count,
even starbase population/efficiency — is deliberately excluded from exact-match comparison. This was
not the original design; it's the outcome of a real investigation, recorded here so nobody has to redo
it.

**What looked like memory corruption, and wasn't.** While chasing a small `sumpop`/`sumtri` mismatch
on 10 of 11 real `dos_131/*.SCN` files (Phase 2 commit 2e), a "fix" to `CreateRndPlanet`'s
`MI:=Round(MI*RndMilTechAdj[T])` (storing the intermediate `Real` in a variable before rounding, to
match C#'s `double` result) caused a much larger divergence in an unrelated file (`AFTERMAT.SCN`):
wrong coordinates, wrong nebula cell counts — fields with no floating-point involvement at all. This
looked exactly like memory corruption or an FPU register-stack bug, and was bisected as such for some
time: ruled out "any new local variable" (an unused one changed nothing), ruled out the optimizer
(`-O-` reproduced it identically), narrowed the divergence to exactly planet index 18 in that file
(the first `CreateRandomWorlds`-generated planet, right after the file's explicit `CreateWorld`
commands run out).

The real mechanism, once found, is mundane: `INT.PAS`'s `Rnd(Min,Max)` returns `Min` **without
drawing** whenever `Max<=Min`, and `RndVar`'s `temp1:=Trunc(Value*(Variation/100))` truncates to `0`
for many small `Value`s at 20% variation — so a ±1 change in `MI` can flip several of `SetUpWorld`'s
18 `RndVar` calls between "skip the draw" and "take it." Every world/scenario in this codebase shares
**one** PRNG stream, so changing how many draws one planet consumes reshuffles every subsequent draw
for every remaining planet and the nebula generator. That's not corruption; it's the ordinary,
expected behavior of a shared-stream PRNG when anything upstream changes the draw count.

**Why this can't be fixed by matching floating-point precision.** The immediate cause of the `MI`/
`Pop` mismatch is that fpc's default i386 codegen keeps chained `Real` expressions in the x87 FPU's
80-bit extended-precision register stack until a value is explicitly stored, while C#'s `double` is
always a strict 64-bit IEEE754 value — so the same borderline expression (one whose exact mathematical
result sits very close to an integer) can `Trunc`/`Round` to a different integer in each language.

Forcing fpc to compile the ground-truth harness with `-CfSSE2` (strict 64-bit double, matching C#) was
tried as a fix (`reference/verify/build.ps1`, `PatchHarness.cs` — this flag is now permanent). Verified
via `git diff --stat` on the regenerated golden files that it changed none of the 13 golden domains
that existed at the time of the switch — but that's a snapshot, not a guarantee: the flag changes
float semantics harness-wide, so a future domain with its own borderline `Real` expression will get
different ground truth under it than it would have under fpc's default x87 mode. It's kept anyway as a
deliberate baseline choice, not a no-op: aligning the harness with the only precision C# has removes a
whole class of future "which precision does this quirky expression round under" questions, even
though it doesn't and can't make the scenario domain bit-exact — it eliminates the original `MI`/`Pop`
boundary cases, but introduces *different* ones (coordinate sums, e.g., started mismatching under
`-CfSSE2` where they hadn't before), because the C# `ScenarioLoader`/`GalaxySetup` and the Pascal
source are two independently-written implementations of the same formulas — even under identical
IEEE754 double precision, a different operand evaluation order can differ by one ULP, which is enough
to flip a `Trunc`/`Round` at an exact-integer boundary. There is no floating-point precision setting
that makes two independently-written implementations of an RNG-cascading algorithm agree forever; the
fragility is structural, not a compiler-flag bug.

This was confirmed concretely, not just reasoned about: `AWAKEN.SCN`'s starbase population (created by
two explicit `CreateStarbase` commands) mismatched even though it's computed from just the 10 explicit
`CreateWorld` population draws that precede it in the file — nowhere near the randomized
`CreateRandomWorlds` section. "Explicit command, not randomized generation" does not make a field safe
from this — anything downstream of *any* earlier `Rnd()`/`RndVar()` call in the same file is at risk.

**The actual target: fpc-compiled behavior, not the historical DOS binary.** Worth naming explicitly,
since it's easy to assume otherwise: the goal is for the C# port to match what *this project's own
patched Pascal source, compiled by the fpc we actually have*, produces — not necessarily what the
original 1990s Borland Turbo Pascal-compiled DOS binary produced. Turbo Pascal's native `Real` is a
6-byte software-emulated format, a third rounding regime distinct from both fpc's x87-extended and
C#'s double — so there was never a realistic path to historical bit-exactness here anyway, and it
isn't a project goal. The C# port also doesn't need to be RNG-stream-compatible with the Pascal
harness for gameplay purposes; bit-exact bug-for-bug behavior is a testing-fidelity nice-to-have, not
a correctness requirement, given the end goal is a playable game, not a historical reproduction.

**What's actually verified, and where:**
- **Formula-level correctness** for the randomized generation paths (`CreateRndPlanet`,
  `RandomTrillumReserves`, `NebulaeBand`/`NebulaePatches`) is covered by the dedicated Phase 2d domain
  tests (`randomplanet.golden`, `nebula.golden`, `trillumreserves.golden`), which use the
  `ForcedRandomValue` single-draw convention and don't chain into a real collision-retry loop — no
  RNG-cascade risk there.
- **Scenario-loader dispatch/structure correctness** (the actual thing `ScenarioLoaderGoldenTests`
  exists to check) is covered by exact-match assertions on fields that never involve a random draw:
  `year`, `planetcount`, `starbasecount`, `stargatecount`, `empirecount`, per-empire tech/revolution/
  modifier/empress summaries, and mined-cell count (`CreateSRMs` is a deterministic explicit
  rectangle, no RNG at all).
- **Everything else** (coordinates, population, trillum, ships/cargo/defenses, class/tech, nebula cell
  count, starbase population/efficiency) gets a cheap smoke test instead: bounds derived from
  type/domain invariants (a coordinate can't exceed the galaxy's size, an enum ordinal can't exceed its
  cardinality, a resource count can't be negative) rather than from expected game-balance values, so
  they can catch a genuinely broken formula without ever producing a false failure on legitimate
  scenario content or drifting with RNG-stream position.

The only remaining lever anyone's identified for tighter parity — not attempted, and not recommended
as a promising path — is reordering/rewriting the C# formulas to match the Pascal source's exact
operand evaluation order term-for-term, on the theory that identical order plus identical precision
might eliminate the ULP-level differences that flip boundary cases. This is a task of unbounded cost,
not a bounded fix: it would need doing per-formula, verifying term order against two different `Trunc`
implementations, in a system where a single remaining mismatch anywhere still cascades through the
entire shared RNG stream. Given the stated goals above, it isn't worth attempting.

---

## Outstanding Research (as of this draft)

All file groups originally listed here as pending (NPE AI, UI/presentation, the `PRIMINTR`/`INTRFACE` facade, the utility-library skim, and `VIEWMAP.PAS`) have since been read in full and folded into §4.5, §5, §1/§4, and §7 respectively. `DesignateWorld`'s actual industry-redistribution/efficiency-penalty logic is now covered directly (§1's facade note and §4.3). What remains genuinely open, gathered from every research pass's own "not reviewed in depth" list, is narrower:

1. **`GetProbe`/`LaunchProbe`** (referenced from `FLTCOMM.PAS`'s `LaunchProbeCommand`) — never opened by any pass; probe-slot allocation and the actual scan-on-arrival mechanic are only known from the manual side (§8), not from code.
2. **`ORDERS.PAS`'s `ParseLine`/`CompileOrders`/`DeCompileOrders` internals** — only the mnemonic table (`TRAN`/`DEST`/`REPE`/`WAIT`) and the `CommandRecord` shape were confirmed; the parser/decompiler bodies themselves were skimmed, not read line-by-line.
3. **Exact numeric cross-check of `DATACNST.PAS`'s `ConsCargoNeeded`/`YearsToBuild` tables against the manual's printed Construction Requirement Table** — structurally confirmed to match (§8), but no cell-by-cell value diff was performed.
4. **The external `MilitaryPower(ShipArray,DefnsArray):LongInt`** referenced from `ATTACK.PAS`'s holocaust code — distinct from the three other same-named/similar tables already catalogued (§4.2), its own definition (likely in `Fleet.PAS` or `Resource.PAS`) was never located and read.
5. **`Galaxy.PAS`'s full field-by-field layout of `SectorRecord`/`XYCoord`** beyond what's needed for the citations already made — sufficient for every claim in this document, but not exhaustively re-derived from the type declaration for byte-level save-format work.

None of these block the C# port's design decisions already documented above; they are precision/verification follow-ups, not open architectural questions.
