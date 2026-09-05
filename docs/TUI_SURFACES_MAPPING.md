# TUI Surfaces Mapping

Backlog for the human interactive turn handler / Terminal.Gui UI (`ROADMAP.md`). Every player-facing window, menu, dialog, prompt, and editor
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
| Galaxy map viewport | F10 / main map view | `MAPWIND.PAS: ScanWindow` | Custom `View` subclass, own `Draw()` writing cells via `Move()`/`SetAttribute()`/`AddRune()` — done, this is `GalaxyView.cs`. Fog-of-war is now real (`UMSector`/`UMFleets`/`EnemyFleetInSector`, gated on `Game.Visible`) — an unknown world/enemy fleet just doesn't draw, same as empty space; not reproduced: the `UnkPlanetChar` nebula-specific case (docs/OPEN_GAPS.md) |
| Top-level navigation shell | Always visible | `PLAYTURN.PAS`/`PULLDOWN.PAS` (menu), `SWINDOWS.PAS` (status/help line) | `MenuBar` + `StatusBar`, `GalaxyView` as the permanent base — done, this is `GameShell.cs`. Every menu/status-bar leaf item is still stubbed to a `MessageBox` placeholder; each gets a real implementation as its own surface is built |
| TMA Logo Splash | Once, at program launch | `ANACREON.PAS: Introduction` / `TMA.PAS: TMALogo` | Done — see Meta/one-off below for detail |
| Anacreon Title + Orbit | Immediately after the TMA logo, once | `PROLOG.PAS: MainTitle`/`ZoomOutSFX`, `InitStarArray`/`UpdateStarArray` | Done — see Meta/one-off below for detail |
| Turn Start Greeting | First thing shown in `SetUpPlayer`, every turn | `PROLOG.PAS: DisplayIntroScreen` | Done — see Turn Start / Player Login below for detail |

Not yet covered by the existing view, but part of the same screen:

| Surface | Where used | Pascal source | Terminal.Gui primitives |
|---|---|---|---|
| Cursor coordinate/name readout | Help line, updates as cursor moves | `MAPWIND.PAS: DrawMapCursor` | `StatusBar`/`Label` bound to cursor position |
| Sector Selected Popup | Enter on a sector with 2+ objects | `MAPWIND.PAS: GetMapObject`/`SelectPoint` | **Done** (`GameShell.ShowSectorPicker`, now a thin wrapper over the shared `GameShell.ShowObjectPicker` — see Ground/Fleet Target Picker below, its other real caller) — small `ListView` overlay added/removed directly on `GameShell` (not a `Dialog`, matching this project's own overlay convention); Enter on a single-object sector skips straight to Close Up, matching the original. Port-only addition, no Pascal equivalent: when the highlighted item is one of the player's own fleets, C/T/J/A run Change Destination/Transfer/Abort-Join/Attack directly (`GameShell.ResolveFleetContextAction`, shared with Close Up below) — required disabling the `ListView`'s own type-ahead-search `KeystrokeNavigator`, which otherwise swallows every plain letter key before it can reach this handler |

## Turn Start / Player Login (hotseat)

Chained sequence run once per player, per turn, before that player gets to `PlayerTakesTurn` —
driven by `ANACREON.PAS`'s main loop calling `PROLOG.PAS: SetUpPlayer` for each empire in turn.

| Surface | Where used | Pascal source | Terminal.Gui primitives |
|---|---|---|---|
| Turn Start Greeting | First thing shown in `SetUpPlayer`, every turn | `PROLOG.PAS: DisplayIntroScreen` | **Done** (`TurnStartGreetingWindow.cs`) — small `Window`/`Label`, header (empire name + year) plus one of 3 greeting lines, picked via `CASE Rnd(1,3)` in the original (`Random.Shared.Next(1, 4)` equivalent); `--greetings` cycles all 3 once each then exits, for review |
| Password Prompt | Immediately after the greeting | `PROLOG.PAS: GetPassword` | `TextField` (secret) in a `Dialog`; Esc cancels back out to the prologue/main menu without taking the turn |
| Capital Fallen Report | After password, only if this empire's capital was conquered since its last turn | `PROLOG.PAS: EmpireNews` (the `CapID.ObjTyp=Void` branch) | **Done** (`Program.cs`'s `ShowCapitalFallenReport`) — `MessageBox` with the letter transcribed verbatim from `EmpireNews`' own `WriteString` calls, shown once at the exact point `TurnEngine`'s own `PendingElimination`→`Eliminated` transition fires, in place of that turn's `GameShell` session. (Despite the name, `EmpireNews` is this conquest check, not a news feed.) |
| Empire Status Report | After the above, skipped if this was the player's last turn | `PROLOG.PAS: EmpireStatus` | **Done** (`EmpireStatusWindow.cs`, totals from `Core.EmpireStatusReport`) — shown right after the Turn Start Greeting in `Program.cs`'s per-empire loop; a plain `Label` rather than `TextView` (this Terminal.Gui version has deprecated `TextView` in favor of a separate package this project doesn't reference), word-wrapped tech list at column 77 matching `DisplayMenu`'s own wrap rule |

## Galaxy/Sector views

| Surface | Where used | Pascal source | Terminal.Gui primitives |
|---|---|---|---|
| Close Up | Selecting a planet/base/fleet from the map or a picker | `CLSCOMM.PAS: CloseUpCom` | **Done** (`CloseUpWindow.cs`) — real 80x21 fixed-size window (`DISPLAY.PAS`'s own shared `OpenWindow(1,4,80,21,ThinBRD,...)`), centered over the map instead of pinned under the menu bar; field layout and per-field `Known`/`Scouted` redaction (`Game.ScoutedOrOwned`, `MISC.PAS`'s `YesNo` magnitude bucket) transcribed row/column-exact from `DisplayBasicInfo`/`DisplayCargoInfo`/`DisplayMilitaryInfo` (world) and `DisplayFleetInfo`/`DisplayFleetComplement` (fleet), plus the scenario's own flavor text (`Game.FindWorldBackgroundText`, `SCENA.PAS: DisplayBackground`'s `conquer:false` case). `GameShell.ObjectsAt` gates on `Game.Visible` too, so this can only be opened on something the player can already see. `DisplayBackground`'s other caller (`ATTCOMM.PAS: EnemyConquered`'s post-attack report, `conquer:true`) is wired now too, from `GameShell.ApplyAttackOutcome` — see Attack / combat below. Port-only addition, no Pascal equivalent: on your own fleet, C/T/J/A run Change Destination/Transfer/Abort-Join/Attack directly instead of dismissing (`GameShell.ShowCloseUp`'s own handling, shared with the Sector Selected Popup above) — any other key still dismisses |
| Production | Close Up on an owned world, drill into industry detail | `CLSCOMM.PAS: ProductionCom` | Same as Close Up — `FrameView` + `Label` rows, read-only |

## Fleet management

| Surface | Where used | Pascal source | Terminal.Gui primitives |
|---|---|---|---|
| Deploy Fleet | Fleet menu → Deploy | `FLTCOMM.PAS: LaunchFleetCommand` | **Done** (`GameShell.DeployFleet`) — map cursor reuse for source and destination, a `TextField` name prompt (`FleetName[1]:=UpCase` transcribed), and the real Resource Distribution Editor for the ship/cargo split, not an all-or-nothing dump |
| Resource Distribution Editor (shared) | Deploy, Abort/Join, Transfer | `FLTCOMM.PAS: InputNewDistribution` | **Done** (`ResourceDistributionEditor.cs`, transfer math in `Core.Entities.ResourceDistribution`) — custom grid `View`, arrow-key column cursor, digit/+/-/x numeric entry matching `GetChange`, Up/Down bulk fill/empty (`FillFleet`/`EmptyFleet`). Plus port-only additions with no Pascal equivalent: click/mouse-wheel column selection, Shift/Ctrl±100/1000 quick-step keys — see the class's own doc comment |
| Abort/Join Fleet | Fleet menu → Abort/Join | `FLTCOMM.PAS: AbortFleetCommand` | **Done** (`GameShell.AbortJoinFleet`) — Ground/Fleet Target Picker, `MessageBox` confirm on overflow/non-empire territory (single confirm each, not Pascal's own double-prompt-even-after-declining quirk — see `ConfirmAbortJoin`'s own doc comment) |
| Ground/Fleet Target Picker (shared) | Abort, Transfer, Refuel | `FLTCOMM.PAS: GetGround` | **Done** (`GameShell.PickGround`, sharing `ShowObjectPicker` with the Sector Selected Popup above) — `ListView` inside a `Window` overlay |
| Transfer Fleet | Fleet menu → Transfer | `FLTCOMM.PAS: TransferFleetCommand` | **Done** (`GameShell.TransferFleet`) — Target Picker + Resource Distribution Editor, `ChangeCompositionOfFleet` applying the result |
| Refuel Fleet | Fleet menu → Refuel | `FLTCOMM.PAS: RefuelFleetCommand` | **Done** (`GameShell.RefuelFleet`) — Target Picker + `TextField` numeric prompt (`GetTrillumToUse` transcribed: 0/blank defaults to max, out-of-range re-prompts) |
| Change Destination | Fleet menu → Change Destination | `FLTCOMM.PAS: ChangeDestinationCommand` | **Done** (`GameShell.ChangeDestination`) — map cursor reuse for the new destination, same `BeginPick` idiom as Deploy's own destination step; `FleetLifecycle.SetFleetDestination` is unconditional so there's no legality check to reproduce |
| Launch Probe | Fleet menu → Probe | `FLTCOMM.PAS: LaunchProbeCommand` | **Done** (`GameShell.LaunchProbe`) — map cursor reuse for the target coordinate, `Empire.TryLaunchProbe`; unlike every other Fleet-menu command this one isn't tied to a specific fleet in real Pascal either, so no fleet-pick step first |
| Mine Sweeper | Fleet menu → SRM Sweep | `FLTCOMM.PAS: MineSweeperCommand` | **Done** (`GameShell.SrmSweep`) — one-line `MessageBox` result, `Galaxy.GetMineOwner`/`ClearMine`/`ClearMineScouted` (already built for scenario loading's `CreateSRMs`) |
| Fleet Orders Editor | Fleet menu → Orders | `FLTCOMM.PAS: FleetOrdersCommand` + `EDIT.PAS` | `TextView` (multi-line) in a `Dialog`/`Window`; `MessageBox` for line-numbered compile errors on Esc |
| Cancel Orders | Fleet menu → Cancel Orders | `FLTCOMM.PAS: FleetCancelOrdersCommand` | `MessageBox` confirm |
| Fleet Window | F5 | `FLTWIND.PAS` | `TableView` (full-screen panel) |

## Attack / combat

| Surface | Where used | Pascal source | Terminal.Gui primitives |
|---|---|---|---|
| Attack Target Picker | Ministry of War → Attack | `ATTCOMM.PAS: GetTarget` | **Done** (`GameShell.Attack`) — attacking fleet resolved via `GameShell.PickOwnFleetAtCursor` (auto-picks if exactly one of the player's own fleets is under the cursor, `ShowObjectPicker` if 2+); target selection transcribed from `GetTarget`'s own nested `CreateMenu` (enemy fleets first, the world itself only offered when none are present), with `ShowObjectPicker` now covering 2+ enemy fleets in one sector too. MVP gate still in place: attacker and target must already share a sector, real Pascal's exact range rule wasn't re-derived |
| Fleet Group Configuration | Pre-battle setup | `ATTCOMM.PAS: GetGroups` | **Done** (`FleetGroupConfigurationWindow.cs`, bookkeeping in `Core.Combat.FleetGroupConfiguration`) — custom grid `View`, cursor-driven, over a cloned ship/cargo pool; reached via `GameShell.BeginAttack`'s "Standard battle configuration (Y/n)?" fork (N opens it, Y skips straight to `DefaultDistribution`) |
| Tactical Battle Display | During an engagement | `ATTCOMM.PAS: DrawScreen`/`Engage`/`GroupMove`/etc. | **Done** (`TacticalBattleDisplayWindow.cs`, round driver in `Core.Combat.InteractiveCombat`) — genuinely full-screen `View`, `Application.AddTimeout` for the message-line auto-clear; `DrawScreen`'s whole visual complex (`DrawGrid`/`DrawStars`/`DrawObject`/`DrawEnemyShips`/`DrawGroupShips`) reproduced too, via a custom-drawn `BattleMapView` using real Pascal's own byte-offset constant tables decoded to row/column deltas — not just the exact-count tables (`EnemyStatus`) and command menu, which came first. Port-only addition, no Pascal equivalent: an `<A>uto-target` toggle (`InteractiveCombatState.AutoTarget`) that re-aims any group the player hasn't manually pinned, reusing the automatic NPE engine's own priority rule |
| Post-Battle Reports | After an engagement | `ATTCOMM.PAS: CleanUp` and related | **Done, minus the world-list screen** (`GameShell.ApplyAttackOutcome`) — `RestoreCombatant` for both sides, `OldShipsFound`, `AskToCapture` (`MessageBox` Yes/No, inverted polarity transcribed), `DisplayBackground(conquer:true)` falling back to `EnemyConquered`'s own three verbatim messages, then `ResolveAttack`. `EmpireConquestReport` (the post-conquest world-list screen) not built (docs/OPEN_GAPS.md) |
| Auto Attack | Ministry of War → Auto Attack | `ATTCOMM.PAS: AutoAttackCommand` | **Done** (`GameShell.AutoAttack`/`BeginAutoAttack`) — same target pick as Attack (shared `FindAttackTarget`), one confirm, then resolved in a single call to `Core.Combat.CombatResolution.NPEAttack` (the same headless engine the Kingdom AI uses) — no Fleet Group Configuration or Tactical Battle Display at all, matching `AutoAttackCommand`'s own plain `ResultMessage`/`CasualtyReport` pair rather than `CleanUp`'s fuller flow |
| Launch LAMs | Ministry of War → Launch LAMs | `DESIGN.PAS: LaunchLAM` | **Done** (`GameShell.LaunchLams`/`PromptForLamCount`/`FinishLaunchLams`) — launching world resolved from the cursor (`FindWorldAt`, gated on ownership + having any LAMs, matching `LAMCom`'s own `NotPartOfEmp`/`NotAWorld`/`NoLAMs` `ErrorCond`s), `GetTarget`'s own Known/Distance≤5 target list transcribed directly (not shared with Attack's `FindAttackTarget`, a genuinely different rule), a `TextField` count prompt, then `Core.Combat.CombatStandalone.LAMAttack` (the same primitive NpeToolkit's own LAM strikes already use) with a casualty-line report |
| Defenses | Ministry of War → Defenses | `MSCCOMM.PAS: DefenseCommand` | **Done** (`DefensesEditor`) — 7×5 grid (ship type × orbital shell percentage), same pattern as the Resource Distribution Editor: arrow-key cursor, digit/Enter inline edit, `CheckForIllegalAmounts`/`Normalize` transcribed exactly (a row not summing to 100, or an illegal ship type parked on the ground, keeps the screen open through one more Esc). Edits `DefenseSettings.Fleets` live; `.Starbases` (Pascal's dead `StarbaseDefDist`) untouched |
| Self-Destruct | Worlds menu → Self-Destruct | `MSCCOMM.PAS: SelfDestructCommand` | `TextField` name prompt + `MessageBox` confirm |

## Empire / planet management

| Surface | Where used | Pascal source | Terminal.Gui primitives |
|---|---|---|---|
| Designate | Worlds menu → Designate | `DESIGN.PAS: DesignateCommand` | **Done** (`GameShell.Designate`/`ConfirmDesignate`, `Core.Entities.WorldDesignation`) — `ListView` type picker (tech gate + Ambrosia's own class gate + the 7 never-manually-designable types), `MessageBox` confirms for the 5 risky choices (declining re-opens the picker rather than cancelling), `Redesignate` applying the efficiency penalty and, for a new Capital, the tech-reset/old-capital-demotion/unrest sequence. Deliberate addition beyond Pascal: a class-suitability % column per row plus a bottom info panel (`WorldDesignation.DesignationHint`) — the real production split for `RawMaterialSplit` types, or this world's own current ISSP dials otherwise — since optimally choosing a designation in real Pascal meant cross-referencing manual tables by hand |
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
| TMA Logo Splash | Once, at program launch, before the main menu | `ANACREON.PAS: Introduction` calls `TMA.PAS: TMALogo` | **Done** (`TmaLogoWindow.cs`) — custom `Window`, reveal animation via `Application.AddTimeout` (a growing-suffix "characters march in from a fixed column" effect, matching `TMALogo`'s actual prepend loop, not a simple left-to-right wipe); auto-continues after a few seconds or on keypress. One-shot, not the 3-random-variant thing — that's Turn Start Greeting above. |
| Anacreon Title + Orbit + main menu | Immediately after the TMA logo | `PROLOG.PAS: MainTitle`/`ZoomOutSFX`, `InitStarArray`/`UpdateStarArray`, plus (for the menu) `Prologue`'s `InitializeMenuBar`/`AddBarItem`/`AddBarMenuItem` | **Done** (`AnacreonTitleWindow.cs`) — custom `Window`, now doubling as this project's stand-in for the real DOS main menu (see "Not applicable" below for why that one isn't ported as-is). Title/orbit: deliberately not a literal port — the original flies the word in via `BITPIC.INC`'s 4 hand-drawn bitmap frames (only 4 discrete images, always a jump-cut regardless of hold time) and only starts the star orbit afterward, in the outer menu loop; replaced with one continuous formula-driven system instead — the same 12 stars (still `PROLOG.PAS`'s `∙ o ☼ o` cycle, CP437 15 confirmed as the sunburst) orbit the title from frame one, radius easing out from 0 as the "fly-in," reprojected as an obliquely-viewed vertical ring (rather than the original's flat `Cycle`/`InitCycle` screen-offset table) so it passes convincingly behind/in front of the text; the white highlight band sweep is unchanged, and the ring keeps spinning as ambient decoration for as long as the menu is up, same as the original. Menu: four plain `View`s (not stock `Button`s) with a red double-line border and centered text, tiled in one row via `Pos`/`Dim` percentages — New Game/Quit dismiss the window (`Choice` reports which), Load Game/Options are still `MessageBox` stubs. Left/Right arrows move focus, N/L/O/Q are hotkeys regardless of focus, mouse click works. Version/copyright lines below the ring come from `MainTitle`'s own `WriteString` calls. `--intro-only` plays the logo + this then exits regardless of choice (for reviewing the animation); `--no-intro` skips straight to this screen, past the TMA logo. |
| New Game setup (scenario, intro text, player count, name/gender) | Main menu → New Game | `NEWGAME.PAS: ScenarioIntroduction`/`GetNoOfPlayers`/`InputEmpireName` | **Done** (`ScenarioPickerWindow.cs`, `IntroTextWindow.cs`, `PlayerCountWindow.cs`, `PlayerSetupWindow.cs`) — real Pascal prompts for one hardcoded scenario filename; this instead scans `reference/scenarios/dos_131/*.SCN` and lists every title (`ScenarioLoader.ReadHeader` reads just the header tokens `Load()` itself would, so the list can't drift out of sync) in a `ListView` (no Pascal equivalent, so left in this project's own default colors). `.SCN` files are read via `ScenarioLoader.ReadScenarioFile`, not a plain `File.ReadAllText`: most are ASCII, but some (AFTERMAT.SCN's box-drawing banner, confirmed from its raw bytes) use CP437 like the `.PAS` source itself does, so a strict UTF-8 decode is tried first and only falls back to CP437 on failure, per file. Once a scenario's picked, every following screen in this flow uses `COLORS.INC`'s `SYSDispWind` (LightGray-on-Blue) as its background, titled with the scenario's own name — matching the single full-screen `OpenWindow(1,1,80,24,ThinBRD,Title,C.SYSDispWind,...)` real Pascal opens around this whole flow (NEWGAME.PAS:1702). Intro text (`BEGINTEXT`/`ENDTEXT`) is shown first, paginated on the scenario's own real `NEWPAGE` markers (`ScenarioLoader.ReadIntroText`/`CollectIntroTextPages`) rather than a blind fixed line count, so a scenario's own decorative boxes (AFTERMAT.SCN has three) each stay on one screen; a real page still too long for one screen falls back to fixed-size chunking. Player count is skipped when a scenario's Min/MaxPlayers are equal (matching `ScenarioIntroduction`'s own `NoChoice` branch); otherwise a plain `TextField` prompt straight on that blue background, same as real Pascal's own `InputString` call (no popup). Per player: name, same plain-on-blue treatment (`TextField`, prefilled with a random suggestion via `ScenarioLoader.SuggestEmpireName`, same pool/retry-until-unused logic as `GetRandomEmpireName`), then gender (`OptionSelector<Gender>`, decides `IsAnEmpress`) revealed in a small bordered box using `CommWind` (White-on-Black) -- matching real Pascal's own separate `OpenWindow(...,C.CommWind,...)` popup for this one prompt, reproduced as a box within the same window rather than a second nested `Application.Run` (this codebase never nests one modal run inside another). Real Pascal's next step, a twice-entered password, is dropped: it exists only to protect each human's turn in hot-seat multiplayer, and nothing in this port cycles through more than one human's turn yet (`GameShell` always plays `Empires[0]`), so collecting one now would have no consumer. `Esc` at any step backs out to the main menu, matching every prompt's own `<Esc>:Exit`. |
| About Anacreon | ⌂ menu → About | `TMA.PAS: AboutAnacreon` | Static `Dialog`/`Window` with `Label`/`TextView` content, dismiss on any key |
| Pause | Game menu → Pause | `SWINDOWS.PAS: PauseCommand` | `MessageBox` |
| Quit / Exit to OS confirmations | Game menu → Quit / Exit to OS | `PLAYTURN.PAS: XXXCom` | **Done** (`GameShell.cs`) — real Pascal's own Quit (`XXXCom`) just sets `ExitGame:=True`, unwinding the per-turn loop back to `Prologue` (the main menu), not a full process exit; `ConfirmQuit`'s `MessageBox` matches that, reported via `GameShell.Choice` so `Program.cs`'s main-menu loop knows to `continue` rather than end the app. "Exit to OS" has no Pascal equivalent here (the real full exit is a separate command on `Prologue`'s own menu bar, one screen further back) -- added as a TUI-only convenience once Quit stopped exiting the app outright, so leaving the game still has a one-step way out. |
| End Turn confirmation | Game menu → End Turn, or timer expiry | `PLAYTURN.PAS` | `MessageBox` |
| DOS Shell | Game menu → Shell | `PLAYTURN.PAS: ShellCommand` | N/A — drop, no TUI equivalent for shelling to the host OS |

## Not applicable

- **Artifact / Transaction / Holocaust commands** (`MSCCOMM.PAS`, `(* commented out *)`) — dead code in v1.31, not live features in this version. Revisit only if a later phase pulls features from v2 (`PASCAL_V1_VS_V2_DIFF.md`).
- **Pre-game setup** (`PROLOG.PAS: Prologue`'s real menu bar — ~20 commands across ⌂/Game/Options/Configure: save-game slots, multi-empire setup, config toggles, time limits) — a structurally separate program phase from the in-turn player interface this doc scopes, and most of those commands have no backing feature yet. `AnacreonTitleWindow.cs` (Meta/one-off above) now covers just the two commands this project can actually do something with (New Game, Quit) as a small 4-button menu, not that real menu bar. Worth its own mapping pass, replacing that stand-in, now that save/load (`ROADMAP.md`) is real.

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
3. The custom-drawn grids are the genuinely novel work — same category of effort as `GalaxyView`,
   no stock widget covers them. The Resource Distribution Editor is **done** (`ResourceDistributionEditor.cs`,
   shared by Deploy/Transfer/Abort-Join per above) and turned out to be the right one to build
   first among them, not last: real Pascal callers only ever call `InputNewDistribution` from
   Fleet-menu commands, so building it unlocked Deploy/Transfer in one pass and Abort-Join for free
   (that one doesn't even use the grid — see its own row above). Fleet Group Configuration and
   Tactical Battle Display are **done** too now (`FleetGroupConfigurationWindow.cs`/
   `TacticalBattleDisplayWindow.cs`). Defenses remains unbuilt.
