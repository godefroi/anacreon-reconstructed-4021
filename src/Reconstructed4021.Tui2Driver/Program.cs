using Reconstructed4021.Core.SaveFormat;
using Reconstructed4021.LegacyNpe;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Tui2;

// Headless functional driver for Reconstructed4021.Tui2 screens, same purpose as
// Reconstructed4021.TuiDriver serves for the Terminal.Gui-based Tui project: a script of key presses
// and DUMPs, run without a real terminal attached, so a screen's behavior can be checked by re-running
// a script instead of a human eyeballing a live session every time.
//
// Much simpler than TuiDriver, and deliberately: TuiDriver needs a whole synchronous-vs-pipeline
// input-injection workaround and a second thread because Terminal.Gui's Application.Run blocks the
// calling thread for the whole session and its AddTimeout callbacks only fire from inside that same
// loop. Nothing here has that shape -- IScreen.Update takes real elapsed time as a plain parameter, so
// this driver just calls ScreenRunner.HandleKey/Update/Draw directly, on one thread, with no timers,
// no queues, and no real terminal at all (FrameBuffer writes to Stream.Null). SLEEP below advances the
// screens' own simulated clock rather than actually blocking, so a script covering minutes of
// animation still runs instantly.
//
// Usage: dotnet run --project src/Reconstructed4021.Tui2Driver -- --script path/to/script.txt
//          [--cols 100] [--rows 40] [--output path/to/output.log]
//          [--load path/to/save.json]  -- skip the whole pre-game flow (splash/title/picker/player
//          setup) and drop straight into the galaxy map from a save fixture, same purpose as
//          Reconstructed4021.Tui's own Program.cs --load flag.
//
// Script format, one instruction per line:
//   # comment                    -- ignored, as is a blank line
//   DUMP                         -- prints the current screen as plain text
//   SLEEP <ms>                   -- advances the screens' simulated clock by <ms> (no real wait)
//   <key> [<key> ...]            -- one or more space-separated keys, each fed to HandleKey in order.
//                                    ConsoleKey names (Enter, Spacebar, LeftArrow, N, Q, ...), plus
//                                    Up/Down/Left/Right/Space/Escape as friendlier aliases, plus a
//                                    single letter/digit as itself (case matters for KeyChar, but every
//                                    screen so far matches hotkeys case-insensitively).
// The final screen state is always printed at the end, labeled, whether or not the script itself ends
// with an explicit DUMP.

var scriptPath = RequireArg("--script");
var cols = OptionalIntArg("--cols") ?? 100;
var rows = OptionalIntArg("--rows") ?? 40;

var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
var resolvedScriptPath = Path.IsPathRooted(scriptPath) ? scriptPath : Path.Combine(repoRoot, scriptPath);
if (!File.Exists(resolvedScriptPath))
{
    Console.Error.WriteLine($"Script file not found: {resolvedScriptPath}");
    return 1;
}

var outputPathArg = OptionalArg("--output");
var outputPath = outputPathArg is not null
    ? (Path.IsPathRooted(outputPathArg) ? outputPathArg : Path.Combine(repoRoot, outputPathArg))
    : Path.Combine(repoRoot, "logs", $"tui2driver-{DateTime.Now:yyyyMMdd-HHmmss}.log");
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
Console.WriteLine($"Output: {outputPath}");
using var output = new StreamWriter(outputPath) { AutoFlush = true };

var loadPathArg = OptionalArg("--load");
IScreen initialScreen;
if (loadPathArg is not null)
{
    var resolvedLoadPath = Path.IsPathRooted(loadPathArg) ? loadPathArg : Path.Combine(repoRoot, loadPathArg);
    var game = GameJson.Deserialize(File.ReadAllText(resolvedLoadPath), new Random(4021), new LegacyNpeProvider());
    initialScreen = Bootstrap.CreateGalaxyMapScreen(repoRoot, game);
}
else
{
    initialScreen = Bootstrap.CreateInitialScreen(repoRoot);
}

var runner = new ScreenRunner(initialScreen, new FrameBuffer(cols, rows, Stream.Null));

foreach (var (lineNumber, rawLine) in File.ReadLines(resolvedScriptPath).Select((line, i) => (i + 1, line)))
{
    var line = rawLine.Trim();
    if (line.Length == 0 || line.StartsWith('#'))
    {
        continue;
    }

    if (line == "DUMP")
    {
        output.WriteLine(RenderCurrentFrame());
        continue;
    }

    if (line.StartsWith("SLEEP ", StringComparison.Ordinal))
    {
        runner.Update(TimeSpan.FromMilliseconds(double.Parse(line["SLEEP ".Length..].Trim())));
        continue;
    }

    foreach (var token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
    {
        if (!TryParseKey(token, out var key))
        {
            throw new FormatException($"Script line {lineNumber}: unrecognized key token '{token}'.");
        }

        runner.HandleKey(key);
    }
}

output.WriteLine("=== final state ===");
output.WriteLine(RenderCurrentFrame());
return 0;

string RenderCurrentFrame()
{
    runner.Draw();
    runner.FrameBuffer.Present();
    return string.Join('\n', runner.FrameBuffer.RenderText());
}

// A few friendlier spellings on top of ConsoleKey's own names, matching TuiDriver's script vocabulary.
static bool TryParseKey(string token, out ConsoleKeyInfo key)
{
    if (token.StartsWith("Alt+", StringComparison.OrdinalIgnoreCase) && token.Length == 5)
    {
        var ch = token[4];
        if (char.IsLetterOrDigit(ch) && Enum.TryParse<ConsoleKey>(char.ToUpperInvariant(ch).ToString(), out var altKey))
        {
            key = new ConsoleKeyInfo(ch, altKey, shift: false, alt: true, control: false);
            return true;
        }

        key = default;
        return false;
    }

    var alias = token switch
    {
        "Up" => "UpArrow",
        "Down" => "DownArrow",
        "Left" => "LeftArrow",
        "Right" => "RightArrow",
        "Space" => "Spacebar",
        "Escape" => "Escape",
        _ => token,
    };

    if (alias.Length == 1)
    {
        var ch = alias[0];
        var parsed = char.IsLetterOrDigit(ch) && Enum.TryParse<ConsoleKey>(char.ToUpperInvariant(ch).ToString(), out var letterKey)
            ? letterKey
            : ConsoleKey.NoName;
        key = new ConsoleKeyInfo(ch, parsed, shift: false, alt: false, control: false);
        return true;
    }

    if (!Enum.TryParse<ConsoleKey>(alias, out var namedKey))
    {
        key = default;
        return false;
    }

    var keyChar = namedKey switch { ConsoleKey.Enter => '\r', ConsoleKey.Spacebar => ' ', _ => '\0' };
    key = new ConsoleKeyInfo(keyChar, namedKey, shift: false, alt: false, control: false);
    return true;
}

static string FindRepoRoot(string startDirectory)
{
    var dir = new DirectoryInfo(startDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Reconstructed4021.slnx")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root (Reconstructed4021.slnx not found above " + startDirectory + ").");
}

string RequireArg(string name)
{
    var index = Array.IndexOf(args, name);
    if (index < 0 || index + 1 >= args.Length)
    {
        throw new ArgumentException($"Missing required argument: {name} <value>");
    }

    return args[index + 1];
}

int? OptionalIntArg(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? int.Parse(args[index + 1]) : null;
}

string? OptionalArg(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
