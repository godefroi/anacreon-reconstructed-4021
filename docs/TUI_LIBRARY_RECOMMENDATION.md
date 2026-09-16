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

**RazorConsole** (`RazorConsole/RazorConsole`, checked 2026-09): Razor components compiled to a VDOM and translated into Spectre.Console renderables — built directly on Spectre, so it inherits the same structural gap: no independent overlapping window stack, no menu bar. Its `ModalWindow` component is a single centered overlay positioned by z-index, not a `Window`/`Toplevel` object model. No mouse support documented anywhere in the README or component docs. Genuinely active (1,733 stars, 50 forks, created 2025-10-02, latest stable v0.5.0 on 2026-03-17 with a running nightly channel, 33 open issues), but positioned around "agentic TUI" work — its flagship example is an LLM chat interface — and it requires the Razor SDK plus an ASP.NET Core-style `IHostBuilder` host, a heavier and differently-shaped dependency than a plain console app. Ruled out for the same structural reason as Spectre.Console itself.

**klooie** (`adamabdelhamed/klooie`, checked 2026-09): a single-root `ConsoleApp`/`LayoutRoot` GUI framework. `Dialogs` are focus-restricting overlay panels, not an independent window stack, and there's no menu bar concept. No mouse support documented anywhere. Older than it looks (created 2022-08-22) but has no tagged releases at all, 12 stars, 0 forks, 1 open issue — effectively a single maintainer with no versioning discipline to lean on. Its distinguishing features (physics/collision, animation easing, Windows-only sound) exist to support the maintainer's own terminal game (`cliborg`), not to replace a windowed business-app shell. Ruled out: no windowing/menu-bar abstraction, and negligible adoption.

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
- **Performance at scale: resolved, and against Terminal.Gui.** See section 5 — this recommendation is superseded on performance grounds. Terminal.Gui's own redraw and input-dispatch overhead, not Windows Terminal, is the confirmed bottleneck.
- **Two GitHub org names in the wild** (`gui-cs/Terminal.Gui` and `tui-cs/Terminal.Gui`) point to the same project; use whichever resolves at package-restore time and don't be surprised if search results/docs mix both — confirm the NuGet package id (`Terminal.Gui`) is what's actually referenced, since that's stable regardless of the source org.
- **Mouse support depends on the terminal/OS driver in use.** Confirm mouse behavior on the actual target terminals (Windows Terminal, a Linux TTY vs. a terminal emulator, etc.) early, since DOS-era Anacreon leaned on mouse+keyboard interchangeably and any gaps need a keyboard-only fallback path regardless.
- **No feature was found in the original UI that Terminal.Gui structurally cannot do** (overlapping windows, pulldown menus with mnemonics, scrollable panes, text fields, modal dialogs, status bar, 16+ colors, mouse). The custom-drawing risk above (map viewport) is the one place real net-new UI code is still required.

## 5. 2026-09-13 update: Terminal.Gui is the bottleneck, not Windows Terminal — recommendation superseded

After many sessions of Terminal.Gui feeling slow in Windows Terminal on this project (worst visible as redraw tearing and sluggish arrow-key response on `GalaxyView`), the working theory in earlier sessions was that Windows Terminal itself might be the limiting factor. That theory is now closed out: it's Terminal.Gui.

The project already had a concrete lead pointing this direction before this session — `Reconstructed4021.Tui.csproj`'s own package-reference comment records that Terminal.Gui's `OutputBase.Write` issues a separate write per dirty row instead of batching a frame into one write, and names a planned (never-built) `SingleFlushAnsiOutput` workaround for it.

Branch `new-tui-exploration` (`src/Reconstructed4021.RenderProto`, since renamed to `src/Reconstructed4021.Panemonde` — see section 6) tests the fix directly: a from-scratch renderer with zero Terminal.Gui dependency, built on two rules —

1. Diff a double-buffered `Cell[]` grid and batch the entire frame's cursor-position/SGR/glyph escape sequences into one write to the raw stdout stream (`Console.OpenStandardOutput()`), never `Console.Write`.
2. Read input with a plain blocking `Console.ReadKey`, no polling interval — addressing a second, separate axis: Terminal.Gui's own input loop batches keystrokes with up to ~40ms of added lag on top of the redraw cost.

Tested interactively in real Windows Terminal, panning a synthetic starfield sized to `GalaxyView`'s own worst-case density (~290 objects, ~1800-cell viewport, per that file's comments): smooth scrolling, no tearing, no discernible input or output lag, no degradation after the window lost and regained focus (tab-switch or alt-tab). Confirmed on **both** of Windows Terminal's rendering engines (the default and the Direct3D 11 engine), ruling out a renderer-specific quirk. Measured input-to-flush latency (time from key read to the redraw's write-syscall completing, not full glass-to-glass latency) on the Direct3D 11 engine, n=79 keypresses: min 0.12ms, median 0.31ms, p95 0.74ms, max 0.80ms.

**Conclusion:** Windows Terminal is capable of excellent TUI performance in both of its rendering engines. Terminal.Gui was the bottleneck all along, not the terminal or the platform. Its per-dirty-row output writes and batched input dispatch are the suspected mechanism — real, observed behaviors that plausibly explain the slowness — but this prototype didn't isolate them by patching only those paths inside Terminal.Gui itself, so treat that as a strong working theory, not a proven root cause. Section 1's recommendation to use Terminal.Gui v2 is superseded on performance grounds regardless.

## 6. 2026-09-13: decision — full replacement, not a Terminal.Gui hybrid

Before committing to a full replacement, a hybrid was investigated: keep Terminal.Gui's `View`/`Toplevel`/`MenuBar`/`Dialog` tree (this project barely uses real overlapping windows — see below — so that tree wasn't worth much anyway, but it was worth checking) and replace only the output/input plumbing underneath it via Terminal.Gui v2's own `IComponentFactory`/`DriverRegistry.Register` extension points.

That hybrid is a dead end in Terminal.Gui 2.4.17. `ApplicationImpl.CreateDriver` (decompiled during this investigation) does look up a custom driver registered via `DriverRegistry.Register`, but the code path that actually *builds* a driver from a name is a hardcoded `switch` over the three built-in names (`"windows"`, `"dotnet"`, `"ansi"`) that never calls the registered descriptor's `CreateFactory()`. Every `ApplicationImpl` constructor that could take a custom `IComponentFactory` directly is `internal`. `DriverRegistry.Register` is real API surface, but in this version it's dead for actually selecting a custom driver from application code — an incomplete feature or a bug, not something app-side code can route around without reflection or vendoring/patching Terminal.Gui's own source (attempted as `src/Reconstructed4021.TuiOutputProto`, since deleted).

Separately, this project's own "windowing" usage turned out to be thin: 21 `Window`/`Dialog` subclasses, but per `GameShell.cs`'s own comment they're added/removed as children of a single running `Toplevel` rather than run as independent overlapping windows, one `MenuBar` used in one place, and no real tab-order (`TabbedWindow` swaps one content `View` for another on a keychord, it doesn't manage parallel focus). None of that gives Terminal.Gui much credit for value actually being used.

**Decision:** replace Terminal.Gui entirely with a purpose-built engine, `src/Reconstructed4021.Panemonde` (promoted from the `RenderProto` prototype in section 5) — named after the in-universe book quoted in `INTRO.SCN`'s opening flavor text ("The Seeds of Panemonde"), not a generic name. Plan is three projects: `Reconstructed4021.Panemonde` (the terminal engine itself: cell buffer/diff/single-flush renderer, keyboard input with modifiers, mouse input, a small bounded widget set — text field, selectable list, menu bar, dialog frame), a shared presentation-logic project (display strings/formatting/honorifics — reusable by a future mobile or web client, explicitly *not* glyphs/colors, which stay terminal-specific), and a new game shell built on both that eventually replaces `Reconstructed4021.Tui`. The orders-editor DSL becomes a dropdown/selectable-list UI instead of free text, so `Terminal.Gui.Editor`'s multi-line editor — the single biggest reimplementation risk — never needs rebuilding. Mouse input is the one genuinely new, unbuilt piece (nothing prototyped touches it yet).
