#:project ../src/Reconstructed4021.Core/Reconstructed4021.Core.csproj
#:project ../src/Reconstructed4021.LegacyNpe/Reconstructed4021.LegacyNpe.csproj
// File-based apps default to PublishAot, which disables reflection-based System.Text.Json; GameJson needs it.
#:property PublishAot=false

// Runs one world from a JSON save through both the real patched Pascal UpdateWorld (runworld.pas's
// maturation domain) and the port's RunAnnualTick, year by year, and reports the first year and
// fields where they disagree.
//
//   dotnet run scripts/WorldDiff.cs -- <save.json> <x,y> [years=10] [--all]
//
// Both sides model the world the way the maturation domain does, not the way the saved game does:
// the world alone in the galaxy, its own empire's capital, every technology researched, ships and
// revolution index starting at 0. So a match means "the port's per-world tick agrees with Pascal
// for this world's economy", not "this game would have played out this way". Only fields the
// maturation domain prints are compared.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.SaveFormat;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;
using Reconstructed4021.LegacyNpe;

if (args.Length < 2) {
    Console.Error.WriteLine("usage: dotnet run scripts/WorldDiff.cs -- <save.json> <x,y> [years=10] [--all]");
    return 2;
}

var savePath = args[0];
var xy = args[1].Split(',');
var location = new Coordinate(int.Parse(xy[0]), int.Parse(xy[1]));
var years = args.Skip(2).Where(a => !a.StartsWith("--")).Select(int.Parse).DefaultIfEmpty(10).First();
var showAll = args.Contains("--all");

var saved = GameJson.Deserialize(File.ReadAllText(savePath), npeProvider: new LegacyNpeProvider());
var source = saved.Galaxy.Planets.SingleOrDefault(p => p.Location == location);
if (source is null) {
    Console.Error.WriteLine($"No planet at {location.X},{location.Y} in {savePath}.");
    return 1;
}

CargoType[] cargoOrder = [CargoType.Legion, CargoType.NinjaLegion, CargoType.Ambrosia, CargoType.Chemicals, CargoType.Metals, CargoType.Supplies, CargoType.Trillum];
DefenseType[] defenseOrder = [DefenseType.Lam, DefenseType.DefenseSatellite, DefenseType.Gdm, DefenseType.IonCannon];
var industries = Enum.GetValues<IndustryType>();

// Same field order runworld.pas's RunMaturationCase parses (and GoldenFileTests' maturation
// formatter sends), with the year count as the last field.
string Tuple(int yearCount) => string.Join(",", new[] {
        (int)source.Class, (int)source.Type, source.Population, source.Efficiency, (int)source.TechLevel, source.IsAddictedToAmbrosia ? 1 : 0 }
    .Concat(industries.Select(i => source.Industry[i]))
    .Concat(cargoOrder.Select(c => source.Cargo[c]))
    .Append(source.TrillumReserve)
    .Concat([source.SelfSufficiency.Chemical, source.SelfSufficiency.Metal, source.SelfSufficiency.Supply, source.SelfSufficiency.Trillum])
    .Concat(defenseOrder.Select(d => source.Defenses[d]))
    .Append(yearCount));

// Pascal: one process, one tuple per year count, so line k is the state after k+1 years.
var exe = EnsureHarnessBuilt();
var pascal = RunProcess(exe, ["case", "maturation", .. Enumerable.Range(1, years).Select(Tuple)], Path.GetDirectoryName(exe)!)
    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(ParseLine)
    .ToList();
if (pascal.Count != years) {
    Console.Error.WriteLine($"Expected {years} lines from runworld, got {pascal.Count}.");
    return 1;
}

// C#: the same single-world setup as AnnualTickHandlerMaturationTests.
var owner = new Empire { Name = "WorldDiff", TechnologyLevel = source.TechLevel };
owner.Technology.Ships.UnionWith(Enum.GetValues<ShipType>());
owner.Technology.Defenses.UnionWith(Enum.GetValues<DefenseType>());
var planet = new Planet {
    Location = new Coordinate(0, 0), Owner = owner, Class = source.Class, Type = source.Type, TechLevel = source.TechLevel,
    Efficiency = source.Efficiency, Population = source.Population, TrillumReserve = source.TrillumReserve,
    IsAddictedToAmbrosia = source.IsAddictedToAmbrosia,
};
foreach (var i in industries) {
    planet.Industry[i] = source.Industry[i];
}
foreach (var c in cargoOrder) {
    planet.Cargo[c] = source.Cargo[c];
}
foreach (var d in defenseOrder) {
    planet.Defenses[d] = source.Defenses[d];
}
planet.SelfSufficiency.Chemical = source.SelfSufficiency.Chemical;
planet.SelfSufficiency.Metal = source.SelfSufficiency.Metal;
planet.SelfSufficiency.Supply = source.SelfSufficiency.Supply;
planet.SelfSufficiency.Trillum = source.SelfSufficiency.Trillum;

var game = new Game(new Galaxy(size: 20));
game.Galaxy.Planets.Add(planet);
game.Empires.Add(owner);
owner.Capital = planet;
var handler = new AnnualTickHandler(new FixedRandom(0));

Console.WriteLine($"{source.Class} {source.Type} at {location.X},{location.Y}, {years} years:");
var firstDiffYear = 0;
for (var year = 1; year <= years; year++) {
    handler.RunAnnualTick(game);
    var port = Snapshot(planet);
    var expected = pascal[year - 1];
    var diffs = expected.Where(kv => !port.TryGetValue(kv.Key, out var v) || v != kv.Value)
        .Select(kv => $"{kv.Key}: pascal={kv.Value} port={(port.TryGetValue(kv.Key, out var v) ? v.ToString() : "(missing)")}")
        .ToList();
    if (diffs.Count == 0) {
        continue;
    }

    firstDiffYear = firstDiffYear == 0 ? year : firstDiffYear;
    Console.WriteLine($"  year {year}: {string.Join("; ", diffs)}");
    if (!showAll) {
        Console.WriteLine("  (stopping at the first differing year; pass --all to see every year)");
        break;
    }
}
if (firstDiffYear == 0) {
    Console.WriteLine("  identical every year");
}
return firstDiffYear == 0 ? 0 : 1;

static Dictionary<string, int> Snapshot(Planet p) => new() {
    ["pop"] = p.Population, ["eff"] = p.Efficiency, ["tech"] = (int)p.TechLevel, ["revindex"] = p.RevolutionIndex,
    ["bio"] = p.Industry.Bioindustry, ["che"] = p.Industry.Chemical, ["min"] = p.Industry.Mining,
    ["syg"] = p.Industry.ShipyardGeneral, ["syj"] = p.Industry.ShipyardJump, ["sys"] = p.Industry.ShipyardStarship,
    ["syt"] = p.Industry.ShipyardTransport, ["sup"] = p.Industry.Supply, ["tri"] = p.Industry.TrillumMining,
    ["fgt"] = p.Ships.Fighters, ["hkr"] = p.Ships.HunterKillers, ["jmp"] = p.Ships.Jumpships,
    ["jtn"] = p.Ships.Jumptransports, ["pen"] = p.Ships.Penetrators, ["ssp"] = p.Ships.Starships, ["trn"] = p.Ships.Transports,
    ["cargomen"] = p.Cargo.Legions, ["cargoche"] = p.Cargo.Chemicals, ["cargomet"] = p.Cargo.Metals,
    ["cargosup"] = p.Cargo.Supplies, ["cargotri"] = p.Cargo.Trillum, ["trillumreserve"] = p.TrillumReserve,
    ["lam"] = p.Defenses.Lams, ["def"] = p.Defenses.DefenseSatellites, ["gdm"] = p.Defenses.Gdms, ["ion"] = p.Defenses.IonCannons,
};

static Dictionary<string, int> ParseLine(string line) =>
    line.Split(';', StringSplitOptions.RemoveEmptyEntries)
        .Select(f => f.Split('=', 2))
        .ToDictionary(kv => kv[0], kv => int.Parse(kv[1]));

// Rebuilds via build.ps1 when runworld.exe is missing or older than anything it's built from.
static string EnsureHarnessBuilt()
{
    var verifyDir = Path.Combine(RepoRoot(), "reference", "verify");
    var exe = Path.Combine(verifyDir, "patched", "runworld.exe");
    var inputs = new[] { Path.Combine(verifyDir, "runworld.pas") }
        .Concat(Directory.EnumerateFiles(Path.Combine(verifyDir, "patches")))
        .Concat(Directory.EnumerateFiles(Path.Combine(verifyDir, "shims")));
    var newestInput = inputs.Max(File.GetLastWriteTimeUtc);
    if (!File.Exists(exe) || File.GetLastWriteTimeUtc(exe) < newestInput) {
        Console.Error.WriteLine("Building the Pascal harness (reference/verify/build.ps1)...");
        RunProcess("pwsh", ["-NoProfile", "-File", Path.Combine(verifyDir, "build.ps1")], verifyDir);
    }
    return exe;
}

static string RunProcess(string fileName, IEnumerable<string> arguments, string workingDirectory)
{
    var psi = new ProcessStartInfo(fileName) {
        WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true,
    };
    foreach (var a in arguments) {
        psi.ArgumentList.Add(a);
    }
    using var process = Process.Start(psi)!;
    var stderr = process.StandardError.ReadToEndAsync();
    var stdout = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0) {
        throw new InvalidOperationException($"{fileName} exited {process.ExitCode}:\n{stderr.Result}\n{stdout}");
    }
    return stdout;
}

static string RepoRoot([CallerFilePath] string thisFile = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, ".."));

// The harness forces every Pascal Rnd() to its minimum (ForcedRandomValue:=0); this is the port-side
// match, the same as the test project's FixedRandom(0), which a script can't reference.
sealed class FixedRandom(int value) : Random
{
    public override int Next(int maxValue) => value;
}
