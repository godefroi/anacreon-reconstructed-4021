# Reconstructed4021.TuiDriver

A headless driver for `Reconstructed4021.Tui`: runs the real `GameShell` (or a standalone window)
with no pty/terminal attached, feeds it a scripted key sequence, and returns the exact rendered
screen as plain text. Use this instead of asking the user to drive the app by hand, and instead of
guessing whether a Tui change works from reading the code alone.

The full technical background — why a separate project, the two real deadlocks it works around
(`InjectKey`'s Direct-mode dispatch vs. Pipeline-mode queuing, `RequestStop` marshaling), exact CLI
args — lives in the comment block at the top of `Program.cs`. This file is the "how do I actually
use it well" guide; read `Program.cs`'s comment if you need the mechanism, not just the workflow.

## When to reach for it

Any time you touch `Reconstructed4021.Tui` and want to know whether it actually works, not just
whether it compiles:

- Verifying a bugfix in an interactive window (exactly what this fixed issue #4: staged a type,
  navigated across empty groups, confirmed the staged type survived by checking what actually got
  loaded).
- Checking a new screen or command wires up end-to-end (menu → window → Core call → result dialog).
- Reproducing a bug report before touching code, so you know what "fixed" looks like.
- Regression-proofing a fix so it doesn't silently regress later — promote the script into
  `assets/tui-driver-scripts/` (see below) once it demonstrates something worth protecting.

`dotnet test` proves the Core logic is correct in isolation. It proves nothing about whether the
Tui actually calls that logic correctly, displays the right thing, or leaves the right window
state on screen. TuiDriver is how you close that gap without a real terminal.

## Quick start

```
dotnet run --project src/Reconstructed4021.TuiDriver -- \
  --load "assets/saves/Garrisoned Outpost.json" --script path/to/script.txt
```

Two other entry points, for screens that don't need a full `Game`:

```
dotnet run --project src/Reconstructed4021.TuiDriver -- --player-setup --script path/to/script.txt
dotnet run --project src/Reconstructed4021.TuiDriver -- --save-picker  --script path/to/script.txt
```

Optional: `--cols`/`--rows` (default 100×40), `--output <path>` (default: an auto-named, gitignored
file under `logs/`, printed to stdout before the script runs — no shell redirect needed). The final
screen is always printed at the end, labeled `=== final state ===`, whether or not the script ends
in `DUMP`.

A nonzero exit code, or a `Script thread failed: ...` line on stderr, means the script itself broke
(bad key token, unhandled exception) — treat that as a real failure, not just log noise.

## Script format

```
# comment                 -- ignored, as is a blank line
DUMP                      -- print the current screen as plain text
SLEEP <ms>                -- extra real sleep, for AddTimeout-driven UI (animations,
                             FlashMessage auto-clear) that needs wall-clock time to elapse
<key> [<key> ...]         -- one or more space-separated keys, each queued with a pacing
                             sleep after it
```

Keys parse via `Key.TryParse` (Terminal.Gui's `KeyCode` names — `Enter`, `Esc`, `Tab`, `Space`,
`F1`..`F24`, `CursorUp`/`CursorDown`/`CursorLeft`/`CursorRight`, or a single character like `y` or
`G`, case-sensitive since it's a real Shift), plus the friendlier aliases `Up`/`Down`/`Left`/
`Right`/`Escape`.

## Working effectively

**Find the real key sequence before writing a script — don't guess it.** Menu mnemonics, dialog
hotkeys, and window key bindings live in `GameShell.cs` and the individual `*Window.cs` files.
Grep for the menu title or the `KeyDown`/`AddCommand` handler rather than assuming a letter. The
existing scripts in `assets/tui-driver-scripts/` are also a working reference for real navigation
idioms (e.g. `m` then a mnemonic letter opens a Ministry of War submenu item directly; a
`DosDialogWindow` confirm answers with `y`/`n`/`Esc` plus whatever extra hint key it advertises).

**Iterate in your scratchpad, not in the repo.** Write the script to your scratch directory first,
run it, read the log, adjust, repeat. Only write it into `assets/tui-driver-scripts/` once it
reliably demonstrates the thing you actually care about — that's a commitment to keep it passing,
matching the other scripts already there.

**DUMP liberally while developing, then trim.** Add a `DUMP` after every action while you're
figuring out what a screen actually does; once you understand the flow, cut it down to the `DUMP`s
that matter so the script reads as a specification, not a screenshot slideshow.

**Colors and highlighting don't survive to text.** `RenderScreen` reads `Driver.Contents`'
`Grapheme`s only — a selected row's background color, a highlighted pool cell, anything conveyed
by `Scheme`/`Attribute` alone is invisible in the dump. Verify state through a textual side effect
instead: read a label's actual text, or drive an action that only succeeds/produces the text you
expect if the state you care about is what you think it is. (The issue #4 regression script does
exactly this: rather than trying to detect which type was highlighted, it presses Space and checks
which type actually got loaded into the group.)

**`SLEEP` is for animations, not for pacing between keys.** Every injected key already gets a
60ms pacing sleep before the next one. Only add `SLEEP <ms>` on top of that when something is
driven by `Application.AddTimeout` (a slide-in, an auto-clearing message) that needs real
wall-clock time to fire.

**Don't rebuild the driver's own plumbing.** The Direct-vs-Pipeline injection mode and the
`RequestStop` thread-marshaling in `Program.cs` are both there because the naive versions
deadlock — see that file's comment for exactly how. If a script hangs, that's almost never the fix
needed; check your key sequence and dialog-answering order first.

**Fixture files:** prefer an existing committed save under `assets/saves/`. If you need a fixture
that doesn't exist yet and isn't worth committing as a real save, an uncommitted scratch fixture is
fine — describe exactly how it was derived in the script's own header comment (see
`resupply-smoke-test.txt` for the pattern), so a future reader can reconstruct it.

## Adding a durable regression script

Once a script proves a fix or a screen works, add it to `assets/tui-driver-scripts/` with a header
comment matching the existing files: the exact invocation command, then a paragraph of *why* this
sequence matters and what it's actually checking — not just a restatement of the keys. These
scripts aren't wired into `dotnet test`; they're run manually, so note in your PR/commit description
which script(s) validate the change, and re-run any existing script that covers a screen you touched.
