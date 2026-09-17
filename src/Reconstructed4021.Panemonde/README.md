# Reconstructed4021.Panemonde

A from-scratch, immediate-mode console rendering engine. `Reconstructed4021.Tui` is built on top of
it; this project has no game-specific content of its own. See `docs/PORT_DESIGN.md`'s "Presentation
layer" section and `docs/TUI_LIBRARY_RECOMMENDATION.md` (sections 5-6) for why this exists instead of
Terminal.Gui, the library the project started with.

## Core pieces

- **`FrameBuffer`** — a double-buffered `Cell[]` grid. `Present()` diffs the back buffer against the
  front one, batches every changed cell's cursor-position/SGR/glyph escape sequences into a single
  string, and issues exactly one write syscall. This diff-and-single-flush behavior is the whole
  reason the project exists — see the class's own comment for what it replaces.
- **`IScreen`** — one full-screen piece of the game (splash, title, galaxy map, ...). Owns input and
  drawing for as long as it's current; animation state lives in fields on the screen itself, driven by
  `Update`'s elapsed-time parameter rather than a timer/callback system. Sets `NextScreen` when it's
  done, to hand control to another screen (or `QuitScreen.Instance` to end the run).
- **`ScreenRunner`** — the screen-switching logic (call `HandleKey`/`Update`/`Draw`, advance to
  `NextScreen` when one is set) shared between a real console run and a headless driver, so a driver
  can exercise a screen without touching `Console` or a background thread at all.
- **`ScreenHost.Run`** — the real-console entry point: a background thread does the blocking
  `Console.ReadKey` read and queues keys; the main thread paces frames off a `Stopwatch`, drains
  queued input, updates with real elapsed time, and draws. No artificial delay on either side.
- **`Widgets/`** — reusable pieces built on the above: `ListBox<T>`, `MenuBar`, `TabFrame`,
  `TextEditor`/`TextInputField`, `IOverlay` (a modal panel stacked on a screen — Close Up over the
  galaxy map, a picker over that — plus its concrete overlays like `SingleSelectOverlay<T>` and
  `TextPromptOverlay`).

## Running it directly

`Program.cs` is a standalone harness for the renderer itself, independent of the game:

```
dotnet run --project src/Reconstructed4021.Panemonde -- --bench
dotnet run --project src/Reconstructed4021.Panemonde
```

`--bench` runs headless (writes to `Stream.Null`) and reports bytes written per frame for a
full-redraw and an incremental-redraw scenario — safe under a tool-captured shell, but reports no
wall-clock timing since a redirected pipe says nothing about real terminal latency. With no
arguments it opens an interactive session in the real terminal (arrow keys pan a synthetic
starfield, `r` forces a full-viewport redraw, `l` prints input-to-flush latency stats, `q` quits and
prints them once more) — run this one yourself in a real Windows Terminal window, not through a
tool-captured shell.

To exercise the actual game screens built on this engine, headlessly, see
`src/Reconstructed4021.TuiDriver`.
