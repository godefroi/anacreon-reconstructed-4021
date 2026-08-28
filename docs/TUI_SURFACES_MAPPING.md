# TUI Surfaces Mapping

Backlog for Phase 8 (`ROADMAP.md`). Every player-facing window, menu, dialog, prompt, and editor
in the original game, mapped to where it came from in the v1.31 Pascal source and which
Terminal.Gui v2 primitive it should be built from. Pascal ground truth is
`reference/DOSAnacreonSource131/` (not `DOSAnacreonSource20` — see `PASCAL_V1_VS_V2_DIFF.md`).
Primitive vocabulary matches `TUI_LIBRARY_RECOMMENDATION.md`.

Not a spec for each screen's exact fields — go read the cited Pascal procedure when it's time to
build that surface. This is the map of what exists and what to build it out of.

## Deliberate deviation from the original: the galaxy map is the shell

In the Pascal original, the galaxy map (`MAPWIND.PAS: ScanWindow`) is just one of several F-key
panels (`SWINDOWS.PAS`) that get swapped into a content area beneath the menu bar — structurally
no different from the Fleet, Empire, News, or Names windows. But in practice the player spends
nearly all their time either looking at the map or acting on something selected from it.

We're deliberately not reproducing that structure. In this port, **the galaxy map is the
permanent base view — the shell itself** — not a panel that gets swapped out. Every other surface
above (menus, pickers, dialogs, background windows) opens as an overlay on top of the map rather
than replacing it. Concretely:

- A game/turn opens directly onto the map, with initial focus on the map, not on the menu bar.
- F1/F3/F5/F7/F8/F9 open their windows as floating overlays above the map (closable back to it),
  rather than swapping the map out for their content.
- F10 (or Esc from an overlay) means "return focus to the map," not "switch to the map panel."

This mainly affects the "Function-key background panels" and "Suggested build order" entries
below — noted inline where relevant. No other surface's own content or behavior changes because of
this; it's purely about what sits underneath everything else.

## Already built

| Surface | Where used | Pascal source | Terminal.Gui primitives |
|---|---|---|---|
| Galaxy map viewport | F10 / main map view | `MAPWIND.PAS: ScanWindow` | Custom `View` subclass, own `Draw()` writing cells via `Move()`/`SetAttribute()`/`AddRune()` — done, this is `GalaxyView.cs` |
| Top-level navigation shell | Always visible | `PLAYTURN.PAS`/`PULLDOWN.PAS` (menu), `SWINDOWS.PAS` (status/help line) | `MenuBar` + `StatusBar`, `GalaxyView` as the permanent base — done, this is `GameShell.cs`. Every menu/status-bar leaf item is still stubbed to a `MessageBox` placeholder; each gets a real implementation as its own surface is built |

Not yet covered by the existing view, but part of the same screen:

| Surface | Where used | Pascal source | Terminal.Gui primitives |
|---|---|---|---|
| Cursor coordinate/name readout | Help line, updates as cursor moves | `MAPWIND.PAS: DrawMapCursor` | `StatusBar`/`Label` bound to cursor position |
| Sector Selected Popup | Enter on a sector with 2+ objects | `MAPWIND.PAS: GetMapObject`/`SelectPoint` | `Dialog` + `ListView` (Enter on a single-object sector skips straight to Close Up) |

## Turn Start / Player Login (hotseat)

Chained sequence run once per player, per turn, before that player gets to `PlayerTakesTurn` —
driven by `ANACREON.PAS`'s main loop calling `PROLOG.PAS: SetUpPlayer` for each empire in turn.

| Surface | Where used | Pascal source | Terminal.Gui primitives |
|---|---|---|---|
| Turn Start Greeting | First thing shown in `SetUpPlayer`, every turn | `PROLOG.PAS: DisplayIntroScreen` | Small `Dialog`/`Label` — header (empire name + year) plus one of 3 greeting lines, picked via `CASE Rnd(1,3)` in the original (`Random.Shared.Next(1, 4)` equivalent) |
| Password Prompt | Immediately after the greeting | `PROLOG.PAS: GetPassword` | `TextField` (secret) in a `Dialog`; Esc cancels back out to the prologue/main menu without taking the turn |
| Capital Fallen Report | After password, only if this empire's capital was conquered since its last turn | `PROLOG.PAS: EmpireNews` (the `CapID.ObjTyp=Void` branch) | Read-only `TextView`/`Label`, dismiss-on-any-key — narrative defeat text, then triggers empire elimination. (Despite the name, `EmpireNews` is this conquest check, not a news feed — it's a no-op UI otherwise.) |
| Empire Status Report | After the above, skipped if this was the player's last turn | `PROLOG.PAS: EmpireStatus` | Read-only, scrollable `TextView` — plain narrative summary (world/population counts, average industry/efficiency, mastered technologies, military totals); original renders it with raw `Writeln` rather than the windowing system |

## Galaxy/Sector views

| Surface | Where used | Pascal source | Terminal.Gui primitives |
|---|---|---|---|
| Close Up | Selecting a planet/base/fleet from the map or a picker | `CLSCOMM.PAS: CloseUpCom` | `FrameView`/`Dialog` with `Label` rows (read-only, fixed layout; fogs unscouted fields) |
| Production | Close Up on an owned world, drill into industry detail | `CLSCOMM.PAS: ProductionCom` | Same as Close Up — `FrameView` + `Label` rows, read-only |

## Fleet management

| Surface | Where used | Pascal source | Terminal.Gui primitives |
|---|---|---|---|
| Deploy Fleet | Fleet menu → Deploy | `FLTCOMM.PAS: LaunchFleetCommand` | `Dialog` + `TextField` (name), map cursor reuse for launch/destination, then Resource Distribution Editor |
| Resource Distribution Editor (shared) | Deploy, Abort/Join, Transfer | `FLTCOMM.PAS: InputNewDistribution` | Custom grid `View` (own `Draw()`, arrow-key column cursor, Up/Down bulk fill/empty) with a small `Dialog`+`TextField` popup for per-cell numeric entry — no stock widget supports live drill-down-to-edit on a grid |
| Abort/Join Fleet | Fleet menu → Abort/Join | `FLTCOMM.PAS: AbortFleetCommand` | `Dialog` + Ground/Fleet Target Picker, `MessageBox` confirm on overflow/non-empire territory |
| Ground/Fleet Target Picker (shared) | Abort, Transfer, Refuel | `FLTCOMM.PAS: GetGround` | `ListView` inside a `Dialog` |
| Transfer Fleet | Fleet menu → Transfer | `FLTCOMM.PAS: TransferFleetCommand` | Target Picker + Resource Distribution Editor |
| Refuel Fleet | Fleet menu → Refuel | `FLTCOMM.PAS: RefuelFleetCommand` | Target Picker (`ListView`/`Dialog`) + `TextField` numeric prompt |
| Change Destination | Fleet menu → Change Destination | `FLTCOMM.PAS: ChangeDestinationCommand` | `Dialog` + `TextField` (coordinate/name), reuse map cursor as an alternative entry path |
| Launch Probe | Fleet menu → Launch Probe | `FLTCOMM.PAS: LaunchProbeCommand` | `Dialog` + `TextField` (coordinate) |
| Mine Sweeper | Fleet menu → SRM Sweep | `FLTCOMM.PAS: MineSweeperCommand` | None — one-line `MessageBox`/status result, no dialog needed |
| Fleet Orders Editor | Fleet menu → Orders | `FLTCOMM.PAS: FleetOrdersCommand` + `EDIT.PAS` | `TextView` (multi-line) in a `Dialog`/`Window`; `MessageBox` for line-numbered compile errors on Esc |
| Cancel Orders | Fleet menu → Cancel Orders | `FLTCOMM.PAS: FleetCancelOrdersCommand` | `MessageBox` confirm |
| Fleet Window | F5 | `FLTWIND.PAS` | `TableView` (full-screen panel) |

## Attack / combat

| Surface | Where used | Pascal source | Terminal.Gui primitives |
|---|---|---|---|
| Attack Target Picker | Ministry of War → Attack | `ATTCOMM.PAS: GetTarget` | `ListView` in a `Dialog` |
| Fleet Group Configuration | Pre-battle setup | `ATTCOMM.PAS: GetGroups` | Custom grid `View` (cursor-driven, role assignment + load/unload sub-widgets) |
| Tactical Battle Display | During an engagement | `ATTCOMM.PAS: DrawScreen`/`Engage`/`GroupMove`/etc. | Custom animated `View`, own `Draw()` + `Application.AddTimeout` for tick-driven redraws — same shape as `GalaxyView` but animated |
| Post-Battle Reports | After an engagement | `ATTCOMM.PAS: CleanUp` and related | Chained `MessageBox`/`Dialog` screens |
| Auto Attack | Ministry of War → Auto Attack | `ATTCOMM.PAS: AutoAttackCommand` | `MessageBox` casualty report, no tactical screen |
| Launch LAMs | Ministry of War → Launch LAMs | `DESIGN.PAS: LaunchLAM` | Target Picker (`ListView`/`Dialog`) + `TextField` count + `MessageBox` report |
| Defenses | Ministry of War → Defenses | `MSCCOMM.PAS: DefenseCommand` | Custom grid `View` (ship type × orbital shell percentages), same pattern as the Resource Distribution Editor |
| Self-Destruct | Worlds menu → Self-Destruct | `MSCCOMM.PAS: SelfDestructCommand` | `TextField` name prompt + `MessageBox` confirm |

## Empire / planet management

| Surface | Where used | Pascal source | Terminal.Gui primitives |
|---|---|---|---|
| Designate | Worlds menu → Designate | `DESIGN.PAS: DesignateCommand` | `ListView`/`Dialog` type picker + `MessageBox` confirms for risky choices |
| ISSP editor | Worlds menu → ISSP | `DESIGN.PAS: ChangeISSPCom` | Custom small `View` (4-row stepper, Left/Right cycles 11 named bands) — no stock widget covers a labeled band-stepper |
| Trade Technology | Empire menu → Trade Technology | `DESIGN.PAS: SellTechnology` | Two chained `ListView`/`Dialog` pickers (empire, then tech) |
| Liberate / Grant Independence | Worlds menu → Liberate | `DESIGN.PAS: GrantIndependenceCommand` | `ListView`/`Dialog` empire picker + `MessageBox` confirm |
| Send Message | Empire menu → Send Message | `DESIGN.PAS: SendMessageCommand` | `ListView` with `MarkMultiple` (multi-select recipients) → `TextView` editor → `MessageBox` confirm |
| Read Messages | Empire menu → Read Messages | `DESIGN.PAS: ReadMessageCommand` | `TextView` (read-only) + `Label` page indicator, PageUp/PageDown |
| Add/Delete Name | Worlds menu → Add/Delete Name | `NAMES.PAS` | `TextField`/`Dialog`, one-line result |
| Names Window | F9 | `NMSWIND.PAS` | `ListView` (full-screen panel) |
| Status Hardcopy | Game menu → Status Hardcopy | `NAMES.PAS: StatusHardcopy` | No TUI equivalent for a physical printer — reinterpret as export-to-file, triggered from a `MessageBox`/`Dialog` prompt for a path |

## Construction

| Surface | Where used | Pascal source | Terminal.Gui primitives |
|---|---|---|---|
| New Construction Site | Build menu → New | `CONSTR.PAS: ConstructCommand` | `ListView`/`Dialog` type picker (tech-filtered) + map cursor for coordinate + `MessageBox` cost confirm |
| Abort Construction | Build menu → Abort | `CONSTR.PAS: AbortConstructionCommand` | `MessageBox` confirm |
| Construction Site Status | Build menu → Site Status | `CONSTR.PAS: ConstrStatusCommand` | `TableView` (read-only) |

## Menus & navigation

| Surface | Where used | Pascal source | Terminal.Gui primitives |
|---|---|---|---|
| Main Menu Bar | Always visible, top line | `PLAYTURN.PAS: InitializeMainMenu`, `PULLDOWN.PAS` | `MenuBar` + `MenuBarItem`/`MenuItem`, built-in mnemonics |
| Generic scrollable menu widget | Underlies nearly every picker above | `MENU.PAS` | `ListView` — this is the direct built-in replacement, no custom widget needed |
| Function-key background panels | F1/F3/F5/F7/F8/F9 (F10 returns focus to the map) | `SWINDOWS.PAS` | Floating overlay `Window`s opened above the permanent `GalaxyView` shell, not a swapped-in panel — see "Deliberate deviation" above. No stock "panel manager" widget needed, just `Window`s added/removed from the `Toplevel`. |
| Help-line hint bar | Bottom line, changes per active screen | `DISPLAY.PAS: WriteMainHelpLine`/`WriteHelpLine` | `StatusBar` composed of `Shortcut` items |
| Turn countdown clock | Top-right, always visible during play | `SWINDOWS.PAS: UpdateClock` | `Label` updated via `Application.AddTimeout` |

## Background windows

| Surface | Where used | Pascal source | Terminal.Gui primitives |
|---|---|---|---|
| Status Window | F3 | `STAWIND.PAS` | `TableView` (split-pane: world status / military status) |
| Empire Window | F8 | `EMPWIND.PAS` | `TableView` (fixed comparison table, ≤8 rows, no scrolling needed) |
| News Window | F7 | `NWSWIND.PAS` | `ListView` (scrollable feed, local + general sections) |

## Prompts & dialogs — shared primitives, build once

| Primitive | Pascal source | Terminal.Gui equivalent |
|---|---|---|
| Attention/Confirm popup | `WND.PAS: AttentionWindow` | `MessageBox` |
| Command Dialog area (bordered scratch panel every command draws into) | `DISPLAY.PAS: ClrDisplayScreen`/`WriteCommLine`/`WriteErrorMessage` | `FrameView`/`Dialog` — the de facto container most items above render inside |
| Inline string/integer prompt | `DISPLAY.PAS: InputStrgDisplayScreen`/`InputIntegerDisplayScreen`, `SWINDOWS.PAS: GetInputString` | Small `Dialog` + `TextField` |
| Command-line parameter prompt (typed coordinates/object names) | `DISPLAY.PAS: InputParameter`/`InterpretXY`/`InterpretObj` | `TextField`, or reuse the map cursor as an alternative input path |
| ID/menu choice picker | `DISPLAY.PAS: GetIDMenuChoice` | `ListView` in a `Dialog` — the base pattern behind every target/ground/empire picker above |
| Full-screen text editor | `EDIT.PAS` | `TextView` |

## Help

| Surface | Where used | Pascal source | Terminal.Gui primitives |
|---|---|---|---|
| Help Window | F1 | `HLPWIND.PAS` | `ListView` topic index + `TextView` page body |

## Meta / one-off

| Surface | Where used | Pascal source | Terminal.Gui primitives |
|---|---|---|---|
| TMA Logo Splash | Once, at program launch, before the main menu | `ANACREON.PAS: Introduction` calls `TMA.PAS: TMALogo` | Custom `View`/`Dialog` doing a timed reveal animation (original wipes the logo in column-by-column) via `Application.AddTimeout`; auto-continues after a few seconds or on keypress. One-shot animated splash, not the 3-random-variant thing — that's Turn Start Greeting above. |
| About Anacreon | ⌂ menu → About | `TMA.PAS: AboutAnacreon` | Static `Dialog`/`Window` with `Label`/`TextView` content, dismiss on any key |
| Pause | Game menu → Pause | `SWINDOWS.PAS: PauseCommand` | `MessageBox` |
| End Turn / Quit confirmations | Game menu → End Turn / Quit, or timer expiry | `PLAYTURN.PAS` | `MessageBox` |
| DOS Shell | Game menu → Shell | `PLAYTURN.PAS: ShellCommand` | N/A — drop, no TUI equivalent for shelling to the host OS |

## Not applicable

- **Artifact / Transaction / Holocaust commands** (`MSCCOMM.PAS`, `(* commented out *)`) — dead code in v1.31, not live features in this version. Revisit only if a later phase pulls features from v2 (`PASCAL_V1_VS_V2_DIFF.md`).
- **Pre-game setup** (`PROLOG.PAS`: New/Load/Save Game, player/AI setup, time limits) — a structurally separate program phase from the in-turn player interface this doc scopes. Worth its own mapping pass when save/load (`ROADMAP.md` Phase 7) becomes real.

## Suggested build order

1. ~~Top-level navigation shell first~~ — **done** (`GameShell.cs`): `MenuBar` + `StatusBar`, with
   `GalaxyView` as the permanent base view underneath (see "Deliberate deviation" above — not a
   swappable panel) and every menu/status-bar leaf item stubbed to a `MessageBox` placeholder.
   Initial focus goes to the map, not the menu bar. This was built first specifically so there'd
   be a testable, navigable app early rather than static infrastructure with no usage to shape it.
2. From there, build individual command screens directly against their own Pascal source, starting
   with the simplest read-only ones (Close Up, Status/Empire/News/Names background windows).
   `MessageBox` needs no dedicated build step — it's a stock static helper, use it ad hoc for
   confirms and stubs. Don't pre-build a shared `FrameView`/`ListView`-in-`Dialog` picker
   convention speculatively; per the project's "abstract on the second or third real use" rule,
   write the first couple of pickers concretely and extract the shared pattern from what actually
   repeats.
3. The custom-drawn grids (Resource Distribution Editor, Defenses, Fleet Group Configuration,
   Tactical Battle Display) are the remaining genuinely novel work — same category of effort as
   `GalaxyView`, no stock widget covers them. Save these for last; everything else is wiring.
