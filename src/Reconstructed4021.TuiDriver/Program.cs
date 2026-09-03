using System.Drawing;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Testing;
using Reconstructed4021.Core.SaveFormat;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Tui;

// Headless functional-UI driver for Reconstructed4021.Tui, replacing the psmux (tmux-alike)-driven
// playtests this session used before: Terminal.Gui's own Terminal.Gui.Testing namespace runs the real
// app with no pty/terminal attached at all, and Driver.Contents gives back exact rendered text
// instead of an ANSI-art screenshot that has to be eyeballed. A separate project (not a --headless
// flag on Reconstructed4021.Tui itself) so a future Tui test project can reference this same driver
// without dragging in Program.cs's own interactive splash/title/scenario-picker bootstrap.
//
// Usage: dotnet run --project src/Reconstructed4021.TuiDriver --
//          --load "assets/saves/Garrisoned Outpost.json" --script path/to/script.txt [--cols 100] [--rows 40]
//
// Two real bugs, found and fixed by decompiling Terminal.Gui itself (dotnet-inspect) rather than
// guessing after the first two designs deadlocked:
//
// 1. IApplication.InjectKey (the InputInjectionExtensions convenience method) defaults to
//    InputInjectionMode.Direct -- ResolveMode(Auto) resolves to Direct -- which calls
//    IInputProcessor.RaiseKeyDownEvent synchronously, i.e. it runs the key's entire handler chain
//    on the calling thread before returning. GameShell opens MessageBox.Query in several places
//    (e.g. "Standard battle configuration?"), and MessageBox.Query is itself a *nested* Run() call
//    that only returns once its own answer has been injected -- so a script that (correctly) tries
//    to answer that dialog on a later line can never get the chance: the call that opened it hasn't
//    returned yet. This is true even from a second thread via IApplication.Invoke: the callback
//    Invoke runs still calls InjectKey's Direct-mode synchronous dispatch on the UI thread, so the
//    UI thread is what ends up stuck.
//
//    The fix is InputInjectionMode.Pipeline via IApplication.GetInputInjector().InjectKey(key, ...)
//    instead of the IApplication.InjectKey extension method: Pipeline mode calls
//    IInputProcessor.InjectKeyDownEvent, which just enqueues onto the processor's own InputQueue
//    (confirmed by decompiling InputProcessorImpl<T>.InjectKeyDownEvent) -- a real, thread-safe queue,
//    not a direct call. ProcessQueue() (called once per iteration by whichever Run() loop is
//    currently live, nested or not -- same mechanism the framework's own real keyboard-reading thread
//    uses) drains it in order, so a key queued while a MessageBox is open gets processed by *that*
//    modal's own next iteration, exactly like a real keypress would.
//
// 2. Given (1), driving the whole script from the same thread that calls app.Run(gameShell, null) is
//    impossible -- that call doesn't return until the whole session ends. So app.Run stays on the
//    main thread (it needs to be a real, continuously-iterating loop for Pipeline-mode queuing to
//    ever get drained at all) while a second thread walks the script, queuing input with
//    AutoProcess: false and a short real sleep between actions so the live loop's own iterations
//    have time to actually drain and redraw before the next line runs or a DUMP reads the screen.
//
// Script format, one instruction per line:
//   # comment                    -- ignored, as is a blank line
//   DUMP                         -- prints the current screen as plain text
//   SLEEP <ms>                   -- extra real Thread.Sleep on top of the per-action pacing below --
//                                    for AddTimeout-driven UI (WarpIn's slide, FlashMessage's
//                                    auto-clear) that needs more wall-clock time to elapse
//   <key> [<key> ...]            -- one or more space-separated keys, each queued in order with a
//                                    pacing sleep after every one. Parsed via Key.TryParse
//                                    (Terminal.Gui's own KeyCode names: Enter, Esc, Tab, Space,
//                                    F1..F24, CursorUp/CursorDown/CursorLeft/CursorRight, a single
//                                    character like 'y' or 'G' -- case matters, it's a real Shift),
//                                    plus Up/Down/Left/Right as friendlier aliases for the Cursor*
//                                    names.
// The final screen state is always printed at the end, labeled, whether or not the script itself
// ends with an explicit DUMP.

const int ActionPacingMs = 60;

var loadPath = RequireArg("--load");
var scriptPath = RequireArg("--script");
var cols = OptionalIntArg("--cols") ?? 100;
var rows = OptionalIntArg("--rows") ?? 40;

var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
var resolvedLoadPath = Path.IsPathRooted(loadPath) ? loadPath : Path.Combine(repoRoot, loadPath);
var resolvedScriptPath = Path.IsPathRooted(scriptPath) ? scriptPath : Path.Combine(repoRoot, scriptPath);

if (!File.Exists(resolvedLoadPath)) {
    Console.Error.WriteLine($"Save file not found: {resolvedLoadPath}");
    return 1;
}

if (!File.Exists(resolvedScriptPath)) {
    Console.Error.WriteLine($"Script file not found: {resolvedScriptPath}");
    return 1;
}

var random = new Random(4021);
var turnEngine = new TurnEngine(new VisibilityHandler(random), new FleetMovementHandler(random), new AnnualTickHandler(random));
var game = GameJson.Deserialize(File.ReadAllText(resolvedLoadPath), random);

var human = game.CurrentEmpire ?? throw new InvalidOperationException("Save has no CurrentEmpire set -- nothing to drive.");
turnEngine.BeginTurn(game);

IApplication app = Application.Create().Init();
Application.MaximumIterationsPerSecond = 240; // Program.cs's own setting -- shortens the queue-drain latency below.
app.Driver!.SetScreenSize(cols, rows);
app.Screen = new Rectangle(0, 0, cols, rows);

var injector = app.GetInputInjector();
var injectOptions = new InputInjectionOptions { Mode = InputInjectionMode.Pipeline, AutoProcess = false };

var gameShell = new GameShell(game, turnEngine, human, random);
var exitCode = 0;

var scriptThread = new Thread(() => {
    try {
        RunScript();
    } catch (Exception ex) {
        Console.Error.WriteLine($"Script thread failed: {ex}");
        exitCode = 1;
    } finally {
        Thread.Sleep(ActionPacingMs);
        app.Invoke(() => app.RequestStop()); // Calling RequestStop directly from this thread didn't reliably unblock app.Run -- Invoke matches the same thread-marshaling this file already needed for GetInputInjector's own AutoProcess-less queuing to actually get drained.
    }
});
scriptThread.IsBackground = true;
scriptThread.Start();

try {
    app.Run(gameShell, null);
} finally {
    app.Dispose();
}

return exitCode;

void RunScript()
{
    foreach (var (lineNumber, rawLine) in File.ReadLines(resolvedScriptPath).Select((line, i) => (i + 1, line))) {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#')) {
            continue;
        }

        if (line == "DUMP") {
            Console.WriteLine(RenderScreen());
            continue;
        }

        if (line.StartsWith("SLEEP ", StringComparison.Ordinal)) {
            Thread.Sleep(int.Parse(line["SLEEP ".Length..].Trim()));
            continue;
        }

        foreach (var token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
            if (!TryParseKey(token, out var key)) {
                throw new FormatException($"Script line {lineNumber}: unrecognized key token '{token}'.");
            }

            injector.InjectKey(key, injectOptions);
            Thread.Sleep(ActionPacingMs);
        }
    }

    Console.WriteLine("=== final state ===");
    Console.WriteLine(RenderScreen());
}

string RenderScreen()
{
    var contents = app.Driver!.Contents!;
    var lines = new List<string>();
    for (var row = 0; row < contents.GetLength(0); row++) {
        var text = "";
        for (var col = 0; col < contents.GetLength(1); col++) {
            text += contents[row, col].Grapheme;
        }
        lines.Add(text.TrimEnd());
    }
    return string.Join('\n', lines);
}

// A few friendlier spellings on top of Key.TryParse's own KeyCode-name grammar, matching the arrow-key
// vocabulary this session's own psmux transcripts already used.
static bool TryParseKey(string token, out Key key)
{
    var alias = token switch {
        "Up" => "CursorUp",
        "Down" => "CursorDown",
        "Left" => "CursorLeft",
        "Right" => "CursorRight",
        "Escape" => "Esc",
        _ => token,
    };

    return Key.TryParse(alias, out key!);
}

static string FindRepoRoot(string startDirectory)
{
    var dir = new DirectoryInfo(startDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Reconstructed4021.slnx"))) {
        dir = dir.Parent;
    }

    return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root (Reconstructed4021.slnx not found above " + startDirectory + ").");
}

string RequireArg(string name)
{
    var index = Array.IndexOf(args, name);
    if (index < 0 || index + 1 >= args.Length) {
        throw new ArgumentException($"Missing required argument: {name} <value>");
    }

    return args[index + 1];
}

int? OptionalIntArg(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? int.Parse(args[index + 1]) : null;
}
