# TUI Library Recommendation for the Anacreon C# Port

Decision record. Not a tutorial.

## 1. Recommendation: Terminal.Gui v2

Use **Terminal.Gui v2** (NuGet `Terminal.Gui`, repo now hosted at `github.com/tui-cs/Terminal.Gui`, formerly `gui-cs/Terminal.Gui`).

Top reasons:

- **It already is a windowing toolkit, not just a text renderer.** It has a retained-mode `View` tree with `Window`, `Toplevel`, floating/overlapping views, focus management, and input routing — the same job WND.PAS/WNDTYPES.PAS do by hand. Spectre.Console does not have this (see below); Terminal.Gui is the only actively-maintained C# library that does.
- **Maintenance is real and current, not just "not archived."** v2 shipped stable in 2026 (v2.4.15 promoted the v2.4.14 beta to stable on 2026-06-28; v2.4.17 released 2026-07-07; prerelease dev builds as recent as 2026-08-10). 1.9M+ NuGet downloads, 11.2k GitHub stars, MIT license, open issues/PRs actively triaged (44 open issues / 16 open PRs at time of check, not a pile of untouched rot).
- **Cell-level drawing escape hatch exists for the map.** `View.Draw()` gives direct `Move()/SetAttribute()/AddRune()` access to a `Cell`-based buffer with TrueColor (and automatic 16-color fallback), which is exactly the primitive FASTSCR.ASM/EIO provided by hand. The map viewport still has to be custom code either way, and Terminal.Gui doesn't fight you for the ability to write it.

## 2. Candidates considered

| | **Terminal.Gui v2** | Spectre.Console | Consolonia |
|---|---|---|---|
| What it is | Retained-mode TUI widget toolkit (views, focus, layout) | Rendering/formatting library + one-shot prompts | Avalonia's XAML/control stack retargeted to a console renderer |
| Last release (checked 2026-08) | v2.4.17, 2026-07-07 (stable); dev builds through 2026-08-10 | v0.57.2, 2026-07-02 | Beta; no dated release, "beta" tag on README, 402 commits |
| License | MIT | MIT | MIT |
| Overlapping/floating windows | Yes — native `Window`/`Toplevel` z-order, drag/resize | **No** — output is linear/composed-once or `Live` region re-render, no independent window objects | Yes, via Avalonia's window/control model |
| Menu bar / pulldown menus | Yes — `MenuBar`, `PopoverMenu`, mnemonics, keyboard+mouse | No (no persistent menu bar concept) | Yes (Avalonia `Menu` control) |
| Scrollable lists/panes | Yes — `ListView`, `TableView`, `TreeView`, scroll bars | Table/Tree renderables, not scrollable/interactive panes | Yes (Avalonia `ListBox`/`ScrollViewer`) |
| Text input fields | Yes — `TextField`, `TextView` | `TextPrompt` (one-shot, not a persistent field in a live layout) | Yes (Avalonia `TextBox`) |
| Dialogs | Yes — `Dialog`, `MessageBox`, modal stack | No modal window concept | Yes |
| Mouse input | Yes, full (`MouseState`, hover/click, drag) | Partial/limited, no widget hit-testing tree | Yes (via Avalonia input) |
| Color | TrueColor with automatic 16-color fallback | TrueColor/256/16 | TrueColor via ANSI |
| Cross-platform terminal | Windows/macOS/Linux (Console + native drivers) | Windows/macOS/Linux | Windows/macOS/Linux |
| Learning curve | Moderate — new concepts (View, Viewport-relative coords, Cell, LineCanvas) but extensive docs/deep-dives and an active Discussions board | Low for output, but you'd be assembling ad hoc UI plumbing yourself to get windows/menus | Moderate-to-high if unfamiliar with Avalonia/XAML; steeper if not |
| Biggest risk for this project | v2 is young (post-beta since ~March 2026); API can still shift in minor versions | Not fit for purpose as the primary app shell — would still need something else for windows/menus | Beta-stage, ~800 stars, one visible maintainer pattern; smaller ecosystem to lean on if something breaks |

**Spectre.Console** was seriously evaluated because it's the other big name, but it answers a different question. It's built for composing rich output and one-shot interactive prompts (`SelectionPrompt`, `TextPrompt`, `Live`), not for running a persistent multi-pane windowed app with independent overlapping panels and a menu bar. Telling evidence: the community itself ships a `Terminal.Gui.Interop.Spectre` bridge package so Spectre's renderables can be drawn *inside* a Terminal.Gui `View` — i.e., even Spectre's own ecosystem treats Terminal.Gui as the app shell and Spectre as a rendering add-on, not a replacement.

**Consolonia** is the interesting fallback if the team would rather work in Avalonia/XAML idioms (declarative views, data binding, existing Avalonia skills) than Terminal.Gui's imperative `View` API. Ruled out as the primary pick for this project because it's explicitly beta, has a much smaller install base (816 stars vs 11.2k), and the port would be betting a large codebase on a young project's stability. Worth a revisit later if Terminal.Gui's imperative model proves painful for the team.

**Desktop UI (Avalonia) "retro terminal look" fallback**: not pursued as a real option. Nothing found in Terminal.Gui's feature set (windows, menus, mouse, color, custom cell drawing) is missing that would force a move to a full desktop UI stack, and a desktop app trades away the actual terminal-mode aesthetic and portability the original DOS UI trades on. Not worth the detour.

**ktsu-dev/TUI** (NuGet `ktsu.TUI.Core`, checked 2026-09): a Spectre.Console-based component library, not a windowing toolkit — no overlapping windows, no menu bar, no dialog/modal concept, same structural gap as Spectre itself. 3 GitHub stars, one visible maintainer, no dated stable release found. Ruled out: doesn't clear the windowing bar and has negligible adoption to lean on.

**Ratatui.cs** (`holo-q/Ratatui.cs`, checked 2026-09): .NET bindings over the Rust Ratatui engine via FFI — retained-mode stateful widgets (List, Table, Gauge, Tabs, Scrollbar), full mouse events, headless snapshot testing for CI. Genuinely more capable than Spectre for widget-level work, but still no windowing/menu-bar abstraction — Ratatui itself is an immediate-mode-per-frame widget-rendering crate, not a window manager, and the C# binding doesn't add one. 16 stars, single maintainer, 0.3.x/alpha. Ruled out for the same structural reason as Spectre: would need a windowing layer built on top, at which point you're not saving the work Terminal.Gui already did. Worth a revisit only if the project ever needs Ratatui-specific widgets (e.g. `BarChart`/`Sparkline`) badly enough to justify an FFI dependency and hand-rolled window management.

Other libraries surfacing in search (`SharpConsoleUI`, various small console-GUI frameworks like `TomaszRewak/C-sharp-console-gui-framework`, `DoubleNegation/ConsoleUI`) were not carried forward: none combine floating windows + menu bar + dialogs + mouse + active maintenance in one package the way Terminal.Gui does; they're single-purpose or dormant hobby projects.

## 3. What Terminal.Gui buys us vs. hand-rolling (mapped to the Pascal units)

| Pascal unit | What it hand-rolled | Terminal.Gui v2 equivalent |
|---|---|---|
| `WND.PAS` / `WNDTYPES.PAS` | Window handle table, `OpenWindow`/`CloseWindow`/`ActivateWindow`, z-order stack, borders (`ThinBRD`/`DoubleBRD`/etc.), title bars, `AttentionWindow` modal prompt | `Toplevel`/`Window` view tree with built-in z-order, `Border` styling, `Dialog`/`MessageBox` for the `AttentionWindow` case |
| `PULLDOWN.PAS` | Menu bar struct (`MenuBar`, `AddBarItem`, `AddBarMenuItem`), mnemonic-key activation, keyboard nav between menus/items | `MenuBar` + `MenuItem`/`PopoverMenu`, built-in mnemonics, arrow-key + hotkey navigation, mouse click-to-open |
| `EMPWIND.PAS`, `FLTWIND.PAS`, `NMSWIND.PAS`/`NWSWIND.PAS`, `STAWIND.PAS`, `HLPWIND.PAS` | Purpose-built scrolling list/detail panes and a status bar, each with its own scroll/redraw logic on top of WND | `ListView`/`TableView`/`ScrollView` for the scrolling panes, `StatusBar` view for the status line — game-specific content still has to be written, but scrolling, selection highlight, and redraw-on-resize are provided |
| `EDIT.PAS` | Hand-written text editing (cursor movement, insert/overwrite, line wrap) | `TextField`/`TextView` (single-line and multi-line editors with selection, clipboard, undo) |
| `COLORS.INC` | Manual 16-DOS-color attribute byte constants | `Attribute`/`Color` API with named colors, TrueColor, and automatic 16-color downgrade for compatibility |
| `FASTSCR.ASM` | Hand-written direct video-memory writer for speed | `Cell`-based draw buffer + `ConsoleDriver` abstraction (per-OS backends); diffed redraw is handled by the framework instead of by ASM |
| `SWINDOWS.PAS` | Shared window utility glue across the above units | Mostly unnecessary — the `View` base class already provides the shared behavior these utilities patched in by hand |

Net effect: the entire generic windowing/menu/input layer (WND, WNDTYPES, PULLDOWN, EDIT, COLORS, FASTSCR, SWINDOWS) is replaced by framework code the port doesn't have to write or debug. Effort moves to game-specific views: the map viewport, fleet/empire/news panes' actual content and game-state bindings, and any Anacreon-specific keyboard commands.

## 4. Known gaps / risks with Terminal.Gui v2, and how to handle them

- **The map viewport (`MAPWIND.PAS`) is not a built-in widget and won't be.** MAPWIND draws a per-cell grid (`CellRecord` with separate char+color for fleet/world/enemy layers per galaxy cell) — that's game-specific and no TUI toolkit ships a "starmap" control. Handle it as a custom `View` subclass overriding `Draw()`, writing cells directly via `Move()/SetAttribute()/AddRune()`. This is genuinely equivalent in scope to what MAPWIND.PAS did, just without needing to also hand-roll window management, scrolling, or input routing around it — Terminal.Gui gives you the viewport scrolling/clipping and mouse/key routing for free, you still own the cell-drawing logic.
- **v2 is a young stable.** It only promoted out of beta in 2026 (per the project's own release notes) after a long v1→v2 rewrite. Pin an exact NuGet version, don't float `*`, and expect to read changelogs before bumping minor versions until the API fully settles.
- **Performance at scale is unverified for this project.** No published benchmarks or first-party examples of a fast-refresh action-game-style redraw loop (as opposed to typical form-driven business-TUI use) were found. Before committing the map renderer's architecture, prototype a worst-case redraw (full-galaxy view, every cell dirty) and measure actual frame time — don't assume it's fine.
- **Two GitHub org names in the wild** (`gui-cs/Terminal.Gui` and `tui-cs/Terminal.Gui`) point to the same project; use whichever resolves at package-restore time and don't be surprised if search results/docs mix both — confirm the NuGet package id (`Terminal.Gui`) is what's actually referenced, since that's stable regardless of the source org.
- **Mouse support depends on the terminal/OS driver in use.** Confirm mouse behavior on the actual target terminals (Windows Terminal, a Linux TTY vs. a terminal emulator, etc.) early, since DOS-era Anacreon leaned on mouse+keyboard interchangeably and any gaps need a keyboard-only fallback path regardless.
- **No feature was found in the original UI that Terminal.Gui structurally cannot do** (overlapping windows, pulldown menus with mnemonics, scrollable panes, text fields, modal dialogs, status bar, 16+ colors, mouse). The custom-drawing risk above (map viewport) is the one place real net-new UI code is still required.
