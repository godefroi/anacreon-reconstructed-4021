# v1.31 vs. v2 Pascal source diff

Reference document only — nothing here is acted on yet. v1.31 (`_ref/DOSAnacreonSource131`) remains the canonical baseline for the port. This records what actually differs in `_ref/DOSAnacreonSource20` ("v2"), so bugfixes can be pulled in deliberately and gameplay/balance changes can be gated behind explicit options later, rather than adopted silently. See [[anacreon-v2-source-caveat]] in project memory for the standing policy.

Research done by a sub-agent (full changelog read, full `diff -rq` of both trees, unified diffs of every differing file read with context); file-list and the `WorldClass` enum-insertion finding below were independently re-verified directly against source for this document.

## 1. Overview

- 77 `.PAS`/`.INC`/`.ASM` filenames, identical set in both trees. `diff -rq` confirms **22 source files differ** (plus the compiled binary `ANACREON.OVR`, not a source diff): `ANACREON.PAS`, `ATTACK.PAS`, `ATTCOMM.PAS`, `CLSCOMM.PAS`, `CONSTR.PAS`, `DATACNST.PAS`, `DATASTRC.PAS`, `DESIGN.PAS`, `FLEET.PAS`, `INTRFACE.PAS`, `LOADSAVE.PAS`, `NEWGAME.PAS`, `NEWS.PAS`, `NPEINTR.PAS`, `ORDERS.PAS`, `PLAYTURN.PAS`, `PRIMINTR.PAS`, `PROLOG.PAS`, `SCENA.PAS`, `TMA.PAS`, `TYPES.PAS`, `UPDATE.PAS`.
- v1.31-only files (build tooling, not part of the comparison): `ANACREON.CNF`, `D.BAT`, `LOG.TXT`, `License.txt`, `MK.BAT`, `TPC.CFG`. v2-only: `changelog.txt`.
- The v2 changelog frames this as a Jan 16 2004 "2.0" release: disruptor rework, an SRMSweep fleet order, Terraforming (previously dormant/dead code), `WorldBackgroundIndex` flavor-text fixes, a `RandomizePlayers` scenario command, a capital-change map-redraw fix, a materials-vanish-into-construction-site fix, an HK-fleet movement-speed fix, and Warp Link Frequencies. It also folds in an earlier Sep 24 2003 "1.31" changelog entry (unnamed-fleet visibility bug, advanced-ship-defending-planet bug) — already part of our v1.31 baseline.

## 2. Summary table

### Bugfixes — safe to port as v1.31-baseline behavior

| # | Fix | File(s) | What changed |
|---|---|---|---|
| 1 | HK-fleet movement-pass fix | `FLEET.PAS` | Hunter-Killer fleets now move with the jump-speed group instead of the warp-speed group. |
| 2 | Materials-vanish-into-construction-site fix | `FLEET.PAS` (`ExecuteTransCOM`) | Ground-drop restricted to `ObjTyp IN [Pln,Base]`, excluding construction sites. |
| 3 | Destroyed-fleet name-record leak fix | `ATTACK.PAS` (`ResolveAttack`, `AttDestroyedART` branch) | Missing `FleetNameDestruction` call before `DestroyFleet` added — same bug class as the known Enemy240 fix. |
| 4 | Construct-from-map UX fix | `CONSTR.PAS` / `PLAYTURN.PAS` | `ConstructCommand` now takes an `XY` chosen from the map instead of prompting internally. |
| 5 | Capital-change map redraw fix | `DESIGN.PAS` (`DesignateCommand`) | `InitScanWindowMap` now called immediately on capital change. |
| 6 | Multi-bracket flavor-text parsing fix | `SCENA.PAS` (`ParseLine`) | `IF` → `WHILE`, so every `[...]` token per line resolves, not just the first. |
| 7 | Void-coordinate suppression in flavor text | `SCENA.PAS` | Nonexistent-object coordinates no longer render as bogus off-map numbers. |
| 8 | LAM-attack coordinate-read-ordering fix (undocumented) | `DESIGN.PAS` (`LaunchLAM`) | `GetCoord` moved before `LAMAttack`, so target coordinates aren't read after the target fleet may already be destroyed. **Flag:** moderate confidence — not traced whether v1.31 actually misbehaves here. |
| 9 | Population-cap boundary fix (undocumented) | `UPDATE.PAS` (`UpdatePopulation`) | `Pop>MaxPop[Class]` → `Pop>=MaxPop[Class]`, closing an edge case where a world sitting exactly at its cap kept taking the exponential-growth branch. **Flag:** affects all world classes, not just the new Terraform class. |

### Gameplay/balance changes — opt-in only, default OFF

| # | Change | File(s) | What changed |
|---|---|---|---|
| 10 | Disruptor rework | `FLEET.PAS` (`InRangeOfDisrupter`, new `InRangeOfMyDisrupter`) | Enemy-slowdown now keyed to Warp-Link-Frequency mismatch rather than plain ownership; new effect lets your own warp-speed fleets move at jump speed near your own disrupter. **Flag:** enemy-slowdown range is ≤3 sectors, self-boost range is ≤2, but the changelog says "3" for both — asymmetry unexplained. |
| 11 | `PassingThroughGate` frequency-check sense mismatch | `INTRFACE.PAS` | Direct-gate check uses `=`, linked-gate check uses `<>`, and the old same-owner/`Known()` gate is dropped entirely. **Flag:** could be an intentional relaxation or a real logic bug — not resolvable by static reading alone. |
| 12 | Terraforming skips annual efficiency update | `UPDATE.PAS` | Worlds with `Cls=TerCls` no longer call `UpdateEfficiency` that year — part of Terraforming's balance envelope, not standalone. |
| 13 | NPE-first-turn-order guard | `ANACREON.PAS` | Defensive code so an NPE-occupied first empire slot doesn't get prompted for a password/turn; only reachable via `RandomizePlayers`, so this is feature-support, not a standalone v1.31 bugfix. |

### New features — opt-in / future, not baseline

| # | Feature | File(s) | Notes |
|---|---|---|---|
| 14 | Terraforming | `TYPES.PAS`, `DATACNST.PAS`, `DATASTRC.PAS`, `PRIMINTR.PAS`, `INTRFACE.PAS`, `UPDATE.PAS`, `NEWGAME.PAS`, `DESIGN.PAS`, `PLAYTURN.PAS`, `PROLOG.PAS`, `CLSCOMM.PAS`, `ATTACK.PAS`, `LOADSAVE.PAS` | New `ter` tech; new `TerCls` **inserted mid-`WorldClass`-enum**, verified directly at `TYPES.PAS:97` (v2) between `UndCls` and `VlcCls` — v1.31's equivalent line (`TYPES.PAS:95`) has no `TerCls`. This shifts `VlcCls`'s ordinal by one, which is an ordinal-shift save/scenario-compat break, not just an appended field. `TerraformWorld` (`INTRFACE.PAS`) halves industry and cuts efficiency to 30–60%; `UpdateTerraforming` (`UPDATE.PAS`) runs an annual chaos/success roll. **Flag:** two different tech-gate constants (`MinTechForType[TerTyp]=WrpTchLvl` vs. `TechDev[GteTchLvl]`) gate the tech and the world-designation separately — reconciling mechanism not located. Also unverified whether `SetType(ID,IndTyp)` firing on both the chaos *and* success outcomes is intentional. |
| 15 | Warp Link Frequencies | `TYPES.PAS` (`Freq=0..9999`), `DATASTRC.PAS` (`StargateRecord.WLF`), `PRIMINTR.PAS`, `INTRFACE.PAS` (see #11), `CONSTR.PAS`, `PLAYTURN.PAS`, `LOADSAVE.PAS` | Per-gate frequency; save-compat gives pre-2.0 saves a randomized default frequency. |
| 16 | SRMSweep fleet order | `FLEET.PAS` (`SweepCom`/`ExecuteSweepCOM`, requires ≥100 starships present), `NEWS.PAS`, `ORDERS.PAS` (`'SRMS'` token) | **Flag:** distinct from the pre-existing, unchanged `SRMSweepCom`/`MineSweeperCommand` immediate map command — naming collision risk if the port reuses the name. |
| 17 | `RandomizePlayers` scenario command | `NEWGAME.PAS` | Randomly permutes not-yet-created player-empire slots; threaded via new `EmpsCreated`/`NextEmp` out-params on `CreatePlayerEmpire`/`CreateNPEmpire`. |
| 18 | `WorldBackgroundIndex` `'O'` (owner-match) condition | `SCENA.PAS` (`SatisfiesConditions` gains a `Player` param), `ATTCOMM.PAS` | Scenario-authoring feature. |
| 19 | `E:ALL` keyword in scenario empire-set syntax (undocumented) | `SCENA.PAS` (`BuildEmpireSet`) | Shorthand for "every empire"; no gameplay effect. |
| 20 | `PAUSE` scenario debug command (undocumented, dev-only) | `NEWGAME.PAS` | Only active when `DebugScena` is set; negligible. |

## 3. Cosmetic / no-op changes (recorded, not further investigated)

- `PLAYTURN.PAS`: command-dispatch reordering into commented section headers, tab reindentation, `NotKnown`-block reindent.
- `PLAYTURN.PAS`: `{$IFNDEF Demo}...{$ENDIF}` wrapper removed around most gameplay commands — a **build-configuration** change (Demo builds only), not gameplay; Demo mode isn't in scope for the port.
- `TMA.PAS`: version banner/about-box text rewrite (`'1.31'`→`'2.0'`, feature-list text).
- `INTRFACE.PAS`: `InRangeOfStarbase` early-return removed (result unchanged, just less efficient); `DetermineIfScouted` reindent; `EstimatedDateOfArrival` dead-variable removal.
- `DATACNST.PAS`: comment-only rename; `TypeName[TerTyp]` string wording tweak (bundled under Terraforming, not separately counted).
- `TYPES.PAS`: `ResourceArray` reindent.
- `CONSTR.PAS`: `USES` clause reorder (needed for new menu code); `LOADSAVE.PAS` stray blank-line removal and one dead `TestString` local.
- `ATTACK.PAS` / `ATTCOMM.PAS` / `NEWS.PAS`: whitespace/comment reformatting in dead/commented blocks and around `DummyEmp`/`OutProbe`.
- `PROLOG.PAS`: `TerraCom` label comment `{TERraform}`→`{Terraform}` casing tweak (separate from the real Terraforming dispatch wiring, counted under #14).
- Also present but inert: a commented-out `'ABOR'`/`AbortCom` fleet-order stub (`ORDERS.PAS`) — a half-completed v1.31-changelog to-do item, shipped disabled in both versions.

## 4. Files identical between the two trees

All 77 filenames minus the 22 differing ones above (≈54 files) — confirmed via direct `diff -rq` for this document. Notably includes `MISC.PAS` and `FLTCOMM.PAS`, both specifically relevant to the ship-cap question below.

## 5. Open questions / not fully verified

1. ~~Prior project-memory claim that v2 removes the 9999 per-type ship-stacking cap~~ — **checked directly, does not hold up.** `NPEINTR.PAS`'s stacking-cap logic (`ImplementStackMSN`/`ImplementGuardMSN`, v1.31 ~lines 1362-1365/1443-1449) is byte-identical in v2, shifted by one line from an unrelated earlier insertion. `MISC.PAS` (defines `ThgLmt`, the function that clamps values to `MaxResources`) doesn't even appear in the `diff -rq` output — confirmed independently for this document (`MISC.PAS:98-109`, identical in both trees). `TYPES.PAS`'s `MaxResources=9999` constant is unchanged (`TYPES.PAS:40`, confirmed independently, identical in both trees). The only new `9999` literals in v2 (`CONSTR.PAS:231/233`, `INTRFACE.PAS:449`) are the new Warp-Link-Frequency range (`Freq=0..9999`), unrelated to ship stacking. The original memory note has been corrected accordingly.
2. `PassingThroughGate` `=`/`<>` sense mismatch (#11) — needs live-play or author clarification to resolve.
3. Disruptor effect-radius asymmetry, 3 vs. 2 sectors (#10) — needs clarification against actual played v2 behavior.
4. Terraforming's two different tech-gate constants (#14) — reconciling code path not located within the reviewed diff hunks.
5. `SetType(ID,IndTyp)` firing on both Terraforming chaos and success outcomes (#14) — not verified whether intentional.
6. Full enumeration of every ordinal-indexed array/serialized value affected by the `WorldClass` enum insertion — only the `DATACNST.PAS` tables touched by the reviewed diff hunks are cited here; an exhaustive sweep of every `ARRAY[WorldClass]` across the whole codebase would need a dedicated pass if the port ever needs exact v2 ordinal compatibility (irrelevant to the v1.31-baseline port, relevant only if Terraforming is ever pulled in as an opt-in feature).
7. `NEWGAME.PAS`'s added `USES SWindows;` — unclear whether it's used anywhere in the live (non-commented) portion of that file.

## Bottom line for the port

- Port items 1-9 (bugfixes) as the default behavior — they fix real bugs without changing balance.
- Gate items 10-13 (balance changes) and 14-20 (new features) behind explicit opt-in settings if ever implemented; none are part of the v1.31 baseline.
- Terraforming (#14) is the highest-risk item to ever adopt as-is: it changes `WorldClass` ordinals, which matters for save-format and any ordinal-indexed table — treat as a bigger undertaking than a simple feature flag if it's ever pulled in.
