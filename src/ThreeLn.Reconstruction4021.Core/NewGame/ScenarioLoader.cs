using System.Text;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Turns;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.NewGame;

/// <summary>
/// NEWGAME.PAS:1650-1812 (LoadScenario) — the .SCN command tokenizer/dispatch loop, minus its DOS UI:
/// no OpenWindow/ClrScr/CloseWindow (only cosmetic), and no ScenarioIntroduction/InputEmpireName (the
/// player-count prompt and per-player name/password/sex input): <c>players</c> is that same
/// information as an explicit input parameter instead.
///
/// The <c>Seed</c> field in a real .SCN file is read and discarded here, which is not quite
/// equivalent to real Pascal: <c>IF Seed=0 THEN Randomize ELSE RandSeed:=Seed</c>
/// (NEWGAME.PAS:1735-1738) lets a scenario file opt into a fixed, reproducible RNG seed, and all 12
/// real shipped scenario files use <c>Seed=0</c> (confirmed: every one is commented "random
/// scenario" in its own header), so that branch is never exercised by real content — but the
/// mechanism is real. This loader always takes its <see cref="Random"/> from the caller instead
/// (already the case for <see cref="GalaxySetup"/>) rather than reading the field and calling
/// Pascal's Randomize/RandSeed itself; <paramref name="galaxySetup"/> and <paramref name="random"/>
/// must share the same underlying <see cref="Random"/> instance, mirroring Pascal's single implicit
/// global RNG. A caller that wants a specific .SCN file's own nonzero <c>Seed</c> to actually drive
/// reproducibility has no way to get it through this API — the value is consumed by the tokenizer
/// and never exposed (see docs/OPEN_GAPS.md).
///
/// Any parse/format error throws immediately (FormatException) rather than reproducing Pascal's
/// ScenaError flag (print a message, keep going until the *next* dispatch-loop check) — this port has
/// no non-fatal-error display path, so "loading is broken" should surface loudly, not silently, same
/// precedent as GalaxySetup.GetRandomXY's own real "no room left" failure.
/// </summary>
public sealed class ScenarioLoader(GalaxySetup galaxySetup, Random random)
{
    public sealed record PlayerInfo(string Name, string? Password, bool IsEmpress);

    static ScenarioLoader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// .SCN files are plain DOS text -- ASCII for most scenarios, but some (AFTERMAT.SCN's box-drawing
    /// banner, confirmed from its raw bytes) use CP437's high byte range for box-drawing/accented
    /// characters, the same encoding the .PAS source itself uses. Detected per file rather than assumed
    /// universally: a strict UTF-8 decode is tried first (a plain-ASCII file is valid UTF-8 by
    /// construction, so this never misclassifies the common case, and correctly reads any scenario that
    /// happens to already be genuine UTF-8), falling back to CP437 only when that decode fails.
    /// </summary>
    public static string ReadScenarioFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        } catch (DecoderFallbackException) {
            return Encoding.GetEncoding(437).GetString(bytes);
        }
    }

    /// <summary>NEWGAME.PAS:60-85 (RndEmpireName) — GetRandomEmpireName's candidate pool.</summary>
    private static readonly string[] _rndEmpireNames = [
        "Aaraavon", "Antramis", "Azores", "Bok", "Brekandi", "Byzantium", "Cal'Dulmas", "Cerberon", "Chulron",
        "Dol Parem", "Doramis", "Drii", "Earon", "Entares", "Esperance", "Fahron", "First Sun", "Freberon",
        "Geldtried", "Gen-Tarem", "Ghaza", "Haar", "Hasarem", "Highguard", "Horace", "Iileron", "Illissia",
        "Jamin", "Jasper", "Jool Den", "Kandii", "Kendrezani", "Lazarus", "Lililth", "Moorline", "Mu", "Mutara",
        "Ny", "N'zares", "Occem", "Ovaris", "Palanhoth", "Pell", "Pharo", "Quezelquan", "Rho Kandii", "Rosseri",
        "Sarlok", "Sol-Terra", "Terminus", "Terra", "Trantor", "Ultarion", "Vex", "Vlandis", "Whorl", "Xi",
        "Yew", "Yolandis",
    ];

    /// <summary>
    /// Pascal's single TechnologyTypes enum (NoRes,LAM..dis), the raw ordinal space a scenario file's
    /// "extra tech" integers are written in — index 0 (NoRes) is a sentinel with no real grant and
    /// never appears in a real file. See TechCatalog.Grant's own doc comment for why this decode table
    /// lives at the parsing boundary rather than as a TechCatalog-owned method.
    /// </summary>
    private static readonly Action<UnlockedTechnology>[] _technologyTypeGrants = [
        _ => throw new FormatException("ERROR: TechnologyTypes ordinal 0 (NoRes) is a sentinel, not a grantable tech."),
        TechCatalog.Grant(DefenseType.Lam), TechCatalog.Grant(DefenseType.DefenseSatellite),
        TechCatalog.Grant(DefenseType.Gdm), TechCatalog.Grant(DefenseType.IonCannon),
        TechCatalog.Grant(ShipType.Fighter), TechCatalog.Grant(ShipType.HunterKiller), TechCatalog.Grant(ShipType.Jumpship),
        TechCatalog.Grant(ShipType.Jumptransport), TechCatalog.Grant(ShipType.Penetrator), TechCatalog.Grant(ShipType.Starship),
        TechCatalog.Grant(ShipType.Transport),
        TechCatalog.Grant(CargoType.Legion), TechCatalog.Grant(CargoType.NinjaLegion), TechCatalog.Grant(CargoType.Ambrosia),
        TechCatalog.Grant(CargoType.Chemicals), TechCatalog.Grant(CargoType.Metals), TechCatalog.Grant(CargoType.Supplies),
        TechCatalog.Grant(CargoType.Trillum),
        TechCatalog.Grant(ConstructionType.Minefield), TechCatalog.Grant(ConstructionType.CommandBase),
        TechCatalog.Grant(ConstructionType.Fortress), TechCatalog.Grant(ConstructionType.IndustrialComplex),
        TechCatalog.Grant(ConstructionType.Outpost), TechCatalog.Grant(ConstructionType.Gate),
        TechCatalog.Grant(ConstructionType.WarpLink), TechCatalog.Grant(ConstructionType.Disrupter),
    ];

    /// <summary>DATACNST.PAS's StarbaseTypes subrange (cmm..out) sits at ordinals 20-23 of the full TechnologyTypes enum — see CreateBase's own field comment.</summary>
    private static readonly StarbaseKind[] _starbaseKindByFullOrdinal = [StarbaseKind.CommandBase, StarbaseKind.Fortress, StarbaseKind.IndustrialComplex, StarbaseKind.Outpost];

    /// <summary>StargateTypes (gte..dis) sits at ordinals 24-26 of the full TechnologyTypes enum.</summary>
    private static readonly StargateKind[] _stargateKindByFullOrdinal = [StargateKind.Gate, StargateKind.WarpLink, StargateKind.Disrupter];

    private readonly Dictionary<int, (Coordinate UpperLeft, Coordinate LowerRight)> _zones = new();
    private readonly Dictionary<string, Coordinate> _xyPoints = new();
    private readonly Dictionary<int, Empire> _empireBySlot = new();
    private int _scenaVersion;
    private int _trillumReserveBase = 100;
    private IReadOnlyList<WorldClass>? _classTable;
    private IReadOnlyList<TechLevel>? _techTable;

    /// <summary>
    /// NEWGAME.PAS:1650-1812 (LoadScenario). <paramref name="players"/> stands in for
    /// ScenarioIntroduction+InputEmpireName's prompted player count and per-player name/password/sex
    /// — its Count is Pascal's NoOfPlayers. An empty list matches Pascal's own "player declined"
    /// Abort:=True branch: InitializeUniverse still runs (a real, empty galaxy/game is returned), but
    /// no scenario command is ever processed.
    /// </summary>
    public Game Load(string scenarioText, IReadOnlyList<PlayerInfo> players)
    {
        var tokenizer = new ScenarioTokenizer(scenarioText);

        // ReadLn(ScenaFile,Vers); Val(Copy(Vers,10,2),ScenaVersion,Dummy) -- 1-based Copy(Vers,10,2) is
        // characters at 0-based indices 9,10.
        var versionLine = tokenizer.ReadLine();
        _scenaVersion = int.Parse(versionLine.Substring(9, 2));

        var (_, _, _, sizeOfGalaxy, firstYear) = ReadHeaderTokens(tokenizer);

        var galaxy = new Galaxy.Galaxy(sizeOfGalaxy);
        var game = new Game(galaxy) { Year = firstYear };

        if (players.Count == 0)
            return game;

        SkipIntroText(tokenizer);

        _zones[1] = (new Coordinate(0, 0), new Coordinate(sizeOfGalaxy - 1, sizeOfGalaxy - 1));

        while (true) {
            var command = NextToken(tokenizer).ToUpperInvariant();
            switch (command) {
                case "DEBUGSCENARIO": break; // DebugScena only gates diagnostic WriteLns in real Pascal -- no state effect to model
                case "BEGINDESCRIPTION": SkipDescriptions(tokenizer); break;
                case "CLASSTABLE": _classTable = LoadClassArray(tokenizer); break;
                case "CREATENEBULA": RunCreateNebula(tokenizer, galaxy); break;
                case "CREATERANDOMNEBULA": RunCreateRandomNebula(tokenizer, galaxy); break;
                case "CREATESRMS": RunCreateSRMs(tokenizer, galaxy); break;
                case "CREATEPLAYEREMPIRE": RunCreatePlayerEmpire(tokenizer, players, game); break;
                case "CREATENPEMPIRE": RunCreateNPEmpire(tokenizer, game); break;
                case "CREATERANDOMWORLDS": RunCreateRandomWorlds(tokenizer, galaxy); break;
                case "CREATEWORLD": RunCreateWorld(tokenizer, galaxy); break;
                case "CREATESTARBASE": RunCreateBase(tokenizer, galaxy); break;
                case "CREATESTARGATE": RunCreateGate(tokenizer, galaxy); break;
                case "DEFINEZONE": RunDefineZone(tokenizer, galaxy); break;
                case "DEFINEXY": RunDefineXYPoint(tokenizer, galaxy); break;
                case "REPORT": NextToken(tokenizer); break; // WriteLn(token) in real Pascal -- no console to write to
                case "TECHTABLE": _techTable = LoadTechArray(tokenizer); break;
                case "SETTRILLUMRESERVES": _trillumReserveBase = RunSetTrillumReserves(tokenizer); break;
                case "ENDSCENARIO": return game;
                default: throw new FormatException($"ERROR: Unknown command \"{command}\"");
            }

            if (tokenizer.AtEnd)
                return game;
        }
    }

    public sealed record ScenarioHeader(string Title, int MinPlayers, int MaxPlayers, int GalaxySize);

    /// <summary>
    /// NEWGAME.PAS:1650-1701's header-token sequence, shared by <see cref="Load"/>, <see
    /// cref="ReadHeader"/>, and <see cref="ReadIntroText"/> so all three can never drift out of sync
    /// with what a real load actually consumes first. Assumes the caller has already consumed the
    /// version line (Load parses it into <see cref="_scenaVersion"/>; the other two don't need it).
    /// </summary>
    private static (string Title, int MinPlayers, int MaxPlayers, int GalaxySize, int FirstYear) ReadHeaderTokens(ScenarioTokenizer tokenizer)
    {
        var title = NextToken(tokenizer); // Title -- consumed, not modeled (no UI to display it in)
        NextToken(tokenizer); // Seed -- consumed, discarded (see class doc comment)
        var minPlayers = NextInteger(tokenizer); // MinPlay -- consumed, not enforced (caller already decided players.Count)
        var maxPlayers = NextInteger(tokenizer); // MaxPlay -- consumed, same reason
        var galaxySize = NextInteger(tokenizer);
        NextToken(tokenizer); // NoOfPlanets cap -- consumed; Galaxy.Planets is an unbounded List
        NextToken(tokenizer); // Difficulty -- consumed, no C# equivalent (never read elsewhere in NEWGAME.PAS either)
        NextToken(tokenizer); // MinLen -- consumed, same reason
        NextToken(tokenizer); // MaxLen -- consumed, same reason
        var firstYear = NextInteger(tokenizer);
        return (title, minPlayers, maxPlayers, galaxySize, firstYear);
    }

    /// <summary>NEWGAME.PAS:1650-1812 (LoadScenario)'s header only, for a scenario picker to list without running a full Load.</summary>
    public static ScenarioHeader ReadHeader(string scenarioText)
    {
        var tokenizer = new ScenarioTokenizer(scenarioText);
        tokenizer.ReadLine(); // version line -- not needed for a picker
        var (title, minPlayers, maxPlayers, galaxySize, _) = ReadHeaderTokens(tokenizer);
        return new ScenarioHeader(title, minPlayers, maxPlayers, galaxySize);
    }

    /// <summary>
    /// NEWGAME.PAS:1491-1505 (ScenarioIntroduction's own ReadPage loop) -- the intro narrative text
    /// between BEGINTEXT and ENDTEXT, already split into ready-to-display pages so a caller (e.g.
    /// IntroTextWindow) does no text processing of its own, only rendering. NEWPAGE markers are the
    /// *only* page breaks, matching real Pascal exactly: ReadPage's own read loop (<c>Line: ARRAY
    /// [1..25] OF LineStr; REPEAT Inc(LineNo); ReadLn(SF,Line[LineNo]) UNTIL Pos('ENDTEXT',...)&lt;&gt;0
    /// OR Pos('NEWPAGE',...)&lt;&gt;0</c>) has no line-count check at all -- it just keeps appending
    /// lines until it hits a literal marker token. The 25-element array is a hard crash boundary for a
    /// scenario author who writes too many lines before their next marker, not a graceful auto-page-
    /// break; a real page can be any length up to that. An earlier version of this method invented a
    /// fixed-line-count fallback chunker for "a real page still too long for one screen" -- confirmed
    /// wrong against real Pascal (EASTWEST.SCN's real single 22-line page shows as one screen, not two)
    /// and removed; there is no such case in real Pascal because there's no such enforcement to begin
    /// with. Each real NEWPAGE-delimited page has its own trailing blank lines trimmed and is dropped
    /// entirely if that leaves it empty (FENCES.SCN's trailing NEWPAGE immediately before ENDTEXT does
    /// this -- see CollectIntroTextPages' own doc comment on why that's correct, not a bug). Returns an
    /// empty list for a scenario with no real content between BEGINTEXT/ENDTEXT (a caller decides
    /// whether to skip showing an intro screen at all in that case).
    /// </summary>
    public static IReadOnlyList<string> ReadIntroPages(string scenarioText)
    {
        var tokenizer = new ScenarioTokenizer(scenarioText);
        tokenizer.ReadLine(); // version line
        ReadHeaderTokens(tokenizer);

        var pages = new List<string>();
        foreach (var pageLines in CollectIntroTextPages(tokenizer)) {
            var lines = pageLines;
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
                lines.RemoveAt(lines.Count - 1);

            if (lines.Count > 0)
                pages.Add(string.Join('\n', lines));
        }

        return pages;
    }

    /// <summary>DFA.PAS's DFA1NextToken, wrapped to throw the same way a malformed token would report through ScenaError.</summary>
    private static string NextToken(ScenarioTokenizer tokenizer)
    {
        var (token, error) = tokenizer.NextToken();
        if (error)
            throw new FormatException($"ERROR: Bad token \"{token}\"");
        return token;
    }

    private static int NextInteger(ScenarioTokenizer tokenizer)
    {
        var token = NextToken(tokenizer);
        if (!int.TryParse(token, out var value))
            throw new FormatException($"ERROR: Illegal number format \"{token}\"");
        return value;
    }

    /// <summary>
    /// NEWGAME.PAS:1388-1515 (ScenarioIntroduction), file-consumption only — the display/PressAnyKey
    /// pagination and GetNoOfPlayers/NoChoice prompt are dead UI (player count is this call's own
    /// input instead).
    /// </summary>
    private static void SkipIntroText(ScenarioTokenizer tokenizer) => CollectIntroTextPages(tokenizer);

    /// <summary>
    /// Shared by <see cref="SkipIntroText"/> and <see cref="ReadIntroPages"/> — scans for BEGINTEXT, then
    /// reads lines until one that's ENDTEXT (trimmed) or EoF, splitting into a new page on each line
    /// that's NEWPAGE. Matched as a whole trimmed line, not real Pascal's own Pos('NEWPAGE',Line)&lt;&gt;0
    /// substring-anywhere check (ScenarioIntroduction's ReadPage) -- that quirk has no RNG/parse-stream
    /// stakes riding on it the way e.g. ScenarioTokenizer's quote-swallowing one does, so there's no
    /// compatibility reason to keep the footgun of a scenario author's own prose accidentally containing
    /// "NEWPAGE" or "ENDTEXT" as a substring and silently truncating their text; every real committed
    /// scenario's marker lines are just the bare word plus trailing whitespace, so this is unaffected by
    /// any of them. A trailing NEWPAGE immediately followed by ENDTEXT (FENCES.SCN does this) produces a
    /// genuinely empty final page here, matching real Pascal exactly: its own ReadPage would hit ENDTEXT
    /// on the very next call with LineNo still at 1, so its "FOR i:=1 TO LineNo-1" print loop runs zero
    /// times and PressAnyKey never fires -- ReadIntroPages drops empty pages for the same reason, not by
    /// coincidence.
    /// </summary>
    private static List<List<string>> CollectIntroTextPages(ScenarioTokenizer tokenizer)
    {
        string token;
        do {
            token = NextToken(tokenizer);
        } while (token.ToUpperInvariant() != "BEGINTEXT" && !tokenizer.AtEnd);
        tokenizer.ReadLine();

        var pages = new List<List<string>>();
        var currentPage = new List<string>();
        while (!tokenizer.AtEnd) {
            var line = tokenizer.ReadLine();
            var trimmedUpper = line.Trim().ToUpperInvariant();
            if (trimmedUpper == "ENDTEXT")
                break;
            if (trimmedUpper == "NEWPAGE") {
                pages.Add(currentPage);
                currentPage = [];
                continue;
            }

            currentPage.Add(line);
        }

        pages.Add(currentPage);
        return pages;
    }

    /// <summary>NEWGAME.PAS:1372-1386 (SkipDescriptions) — whole-line reads, not tokens, until a line containing ENDDESCRIPTION or EoF.</summary>
    private static void SkipDescriptions(ScenarioTokenizer tokenizer)
    {
        string line;
        do {
            if (tokenizer.AtEnd)
                throw new FormatException("ERROR: EndDescription not found.");
            line = tokenizer.ReadLine();
        } while (!line.ToUpperInvariant().Contains("ENDDESCRIPTION"));
    }

    /// <summary>NEWGAME.PAS:258-290 (GetRandomRange) — "N" (a fixed value) or "N..M" (a range), both ends inclusive.</summary>
    private static (int Low, int High) GetRandomRange(string token)
    {
        var dashPos = token.IndexOf("..", StringComparison.Ordinal);
        if (dashPos < 0) {
            if (!int.TryParse(token, out var value))
                throw new FormatException($"ERROR: Illegal random range \"{token}\"");
            return (value, value);
        }

        if (!int.TryParse(token[..dashPos], out var low) || !int.TryParse(token[(dashPos + 2)..], out var high))
            throw new FormatException($"ERROR: Illegal random range \"{token}\"");
        return (low, high);
    }

    private bool IsInGalaxy(int size, int x, int y) => x >= 0 && x < size && y >= 0 && y < size;

    /// <summary>
    /// NEWGAME.PAS:292-421 (GetNextXY) — four token shapes: "Z:&lt;zone&gt;" (random point in a
    /// predefined zone), "R:&lt;x1..x2&gt;,&lt;y1..y2&gt;" (random point in an explicit range),
    /// "&lt;name&gt;:&lt;dx..dx2&gt;,&lt;dy..dy2&gt;" (random point relative to a named XY point), or
    /// plain "x,y" (an absolute coordinate, no randomization). Every raw integer in this file is
    /// 1-based; this method normalizes to this port's 0-based Coordinate immediately at the absolute
    /// coordinate sites (Z:/R:/plain-xy's own x1,y1,x2,y2 and x,y) — a relative offset added onto an
    /// already-0-based stored XYPoint needs no shift of its own. Normalizing bounds down by one before
    /// calling GalaxySetup.GetRandomXY (rather than shifting its 1-based result afterward) is
    /// numerically identical under both the real and ForcedRandomValue RNG conventions, since Rnd is
    /// affine in its own Min argument — see this phase's own investigation notes.
    /// </summary>
    private Coordinate GetNextXY(ScenarioTokenizer tokenizer, Galaxy.Galaxy galaxy, bool checkWorlds)
    {
        var token = NextToken(tokenizer);
        var commaPos = token.IndexOf(',');
        var colonPos = token.IndexOf(':');
        var header = token.Length >= 2 ? char.ToUpperInvariant(token[0]).ToString() + token[1] : "";

        if (header == "Z:") {
            var zoneNumber = int.Parse(token[2..]);
            if (!_zones.TryGetValue(zoneNumber, out var zone))
                throw new FormatException($"ERROR: Illegal zone coordinate \"{token}\"");
            return galaxySetup.GetRandomXY(galaxy, zone.UpperLeft, zone.LowerRight, checkWorlds);
        }

        if (header == "R:") {
            var (x1, x2) = GetRandomRange(token[2..commaPos]);
            var (y1, y2) = GetRandomRange(token[(commaPos + 1)..]);
            x1--; x2--; y1--; y2--;
            if (!IsInGalaxy(galaxy.Size, x1, y1) || !IsInGalaxy(galaxy.Size, x2, y2))
                throw new FormatException($"ERROR: Illegal random coordinates \"{token}\"");
            return galaxySetup.GetRandomXY(galaxy, new Coordinate(x1, y1), new Coordinate(x2, y2), checkWorlds);
        }

        if (colonPos >= 0) {
            var (dx1, dx2) = GetRandomRange(token[(colonPos + 1)..commaPos]);
            var (dy1, dy2) = GetRandomRange(token[(commaPos + 1)..]);
            var name = token[..colonPos].ToUpperInvariant();
            if (!_xyPoints.TryGetValue(name, out var point))
                throw new FormatException($"ERROR: XYPoint not found \"{name}\"");
            var x1 = point.X + dx1; var x2 = point.X + dx2;
            var y1 = point.Y + dy1; var y2 = point.Y + dy2;
            if (!IsInGalaxy(galaxy.Size, x1, y1) || !IsInGalaxy(galaxy.Size, x2, y2))
                throw new FormatException("ERROR: Relative coordinates outside of galaxy.");
            return galaxySetup.GetRandomXY(galaxy, new Coordinate(x1, y1), new Coordinate(x2, y2), checkWorlds);
        }

        if (commaPos >= 0) {
            var x = int.Parse(token[..commaPos]) - 1;
            var y = int.Parse(token[(commaPos + 1)..]) - 1;
            if (!IsInGalaxy(galaxy.Size, x, y))
                throw new FormatException("ERROR: Absolute coordinates outside of galaxy.");
            return new Coordinate(x, y);
        }

        throw new FormatException($"ERROR: Illegal coordinate \"{token}\"");
    }

    /// <summary>NEWGAME.PAS:704-728 (LoadClassArray) — 21 percentile counts, one per WorldClass in enum order, must sum to 100.</summary>
    private static IReadOnlyList<WorldClass> LoadClassArray(ScenarioTokenizer tokenizer)
    {
        var table = new WorldClass[100];
        var index = 0;
        foreach (var cls in Enum.GetValues<WorldClass>()) {
            var count = NextInteger(tokenizer);
            for (var i = 0; i < count; i++)
                table[index++] = cls;
        }
        if (index != 100)
            throw new FormatException("ERROR: Class table probabilities do not add up to 100.");
        return table;
    }

    /// <summary>NEWGAME.PAS:730-754 (LoadTechArray) — same shape as LoadClassArray, one count per TechLevel.</summary>
    private static IReadOnlyList<TechLevel> LoadTechArray(ScenarioTokenizer tokenizer)
    {
        var table = new TechLevel[100];
        var index = 0;
        foreach (var tech in Enum.GetValues<TechLevel>()) {
            var count = NextInteger(tokenizer);
            for (var i = 0; i < count; i++)
                table[index++] = tech;
        }
        if (index != 100)
            throw new FormatException("ERROR: Tech table probabilities do not add up to 100.");
        return table;
    }

    /// <summary>NEWGAME.PAS:756-790 (DefineXYPoint).</summary>
    private void RunDefineXYPoint(ScenarioTokenizer tokenizer, Galaxy.Galaxy galaxy)
    {
        var name = NextToken(tokenizer).ToUpperInvariant();
        var xy = GetNextXY(tokenizer, galaxy, checkWorlds: false);
        _xyPoints[name] = xy;
    }

    /// <summary>NEWGAME.PAS:792-811 (DefineZone).</summary>
    private void RunDefineZone(ScenarioTokenizer tokenizer, Galaxy.Galaxy galaxy)
    {
        var zoneNumber = NextInteger(tokenizer);
        var xy1 = GetNextXY(tokenizer, galaxy, checkWorlds: false);
        var xy2 = GetNextXY(tokenizer, galaxy, checkWorlds: false);
        _zones[zoneNumber] = (xy1, xy2);
    }

    /// <summary>NEWGAME.PAS:697-702 (SetTrillumReserves).</summary>
    private static int RunSetTrillumReserves(ScenarioTokenizer tokenizer)
    {
        var triRes = NextInteger(tokenizer);
        if (triRes is < 0 or > 100)
            throw new FormatException("ERROR: Illegal trillum reserve setting.");
        return triRes;
    }

    /// <summary>NEWGAME.PAS:1315-1331 (CreateNebula).</summary>
    private void RunCreateNebula(ScenarioTokenizer tokenizer, Galaxy.Galaxy galaxy)
    {
        var type = (NebulaType)NextInteger(tokenizer);
        var upperLeft = GetNextXY(tokenizer, galaxy, checkWorlds: false);
        var lowerRight = GetNextXY(tokenizer, galaxy, checkWorlds: false);
        GalaxySetup.CreateNebula(galaxy, upperLeft, lowerRight, type);
    }

    /// <summary>NEWGAME.PAS:1333-1349 (CreateRandomNebula) — mode 1 is NebulaeBand (Min/Max unused), mode 2 is NebulaePatches(Rnd(Min,Max)).</summary>
    private void RunCreateRandomNebula(ScenarioTokenizer tokenizer, Galaxy.Galaxy galaxy)
    {
        var mode = NextInteger(tokenizer);
        var min = NextInteger(tokenizer);
        var max = NextInteger(tokenizer);
        switch (mode) {
            case 1: galaxySetup.NebulaeBand(galaxy); break;
            case 2: galaxySetup.NebulaePatches(galaxy, PascalMath.Rnd(random, min, max)); break;
            default: throw new FormatException($"ERROR: Unknown nebula mode {mode}.");
        }
    }

    /// <summary>NEWGAME.PAS:1351-1370 (CreateSRMs).</summary>
    private void RunCreateSRMs(ScenarioTokenizer tokenizer, Galaxy.Galaxy galaxy)
    {
        var e = NextInteger(tokenizer);
        var upperLeft = GetNextXY(tokenizer, galaxy, checkWorlds: false);
        var lowerRight = GetNextXY(tokenizer, galaxy, checkWorlds: false);
        GalaxySetup.CreateSRMs(galaxy, upperLeft, lowerRight, ResolveEmpire(e));
    }

    /// <summary>
    /// NEWGAME.PAS:953-1025 (CreateWorld) — SF-token-parsing wrapper, minus the FirstWorld slot counter
    /// (Galaxy.Planets is an unbounded List). E's sentinel for Independent is always 8, regardless of
    /// how many players this scenario actually declares — NEWGAME.PAS's Empire enum has a fixed 8
    /// player slots (Empire1..Empire8) plus Indep last, at ordinal 8; a scenario's own declared player
    /// count only ever gates whether a slot has been created (EmpireActive), never which raw integer
    /// means Indep. Real Pascal's own NoOfPlayers parameter is passed to CreateWorld but never actually
    /// read in its body — confirmed directly from source, not an oversight here.
    /// </summary>
    private void RunCreateWorld(ScenarioTokenizer tokenizer, Galaxy.Galaxy galaxy)
    {
        NextInteger(tokenizer); // n -- debug-output only in real Pascal
        var xy = GetNextXY(tokenizer, galaxy, checkWorlds: true);
        var cls = (WorldClass)NextInteger(tokenizer);
        var tech = (TechLevel)NextInteger(tokenizer);
        var type = (WorldType)NextInteger(tokenizer);
        var e = NextInteger(tokenizer);
        var population = NextInteger(tokenizer);
        var efficiency = NextInteger(tokenizer);
        var triRes = _scenaVersion >= 12 ? NextInteger(tokenizer) : 100;
        var (shipBase, cargoBase, defenseBase) = ReadShipCargoDefenseBase(tokenizer);

        var owner = ResolveEmpire(e);
        if (e != 8 && !_empireBySlot.ContainsKey(e)) {
            owner = Empire.Independent;
            type = WorldType.Independent;
        }

        galaxySetup.CreateWorld(galaxy, xy, cls, tech, type, owner, population, efficiency, triRes, shipBase, cargoBase, defenseBase);
    }

    /// <summary>NEWGAME.PAS:1027-1098 (CreateBase) — SF-token-parsing wrapper. See RunCreateWorld's own doc comment on E's fixed Indep sentinel.</summary>
    private void RunCreateBase(ScenarioTokenizer tokenizer, Galaxy.Galaxy galaxy)
    {
        NextInteger(tokenizer); // n -- debug-output only
        var xy = GetNextXY(tokenizer, galaxy, checkWorlds: true);
        var starbaseTypeOrdinal = NextInteger(tokenizer);
        var tech = (TechLevel)NextInteger(tokenizer);
        var explicitTypeOrdinal = NextInteger(tokenizer);
        var e = NextInteger(tokenizer);
        var population = NextInteger(tokenizer);
        var efficiency = NextInteger(tokenizer);
        var (shipBase, cargoBase, defenseBase) = ReadShipCargoDefenseBase(tokenizer);

        if (e != 8 && !_empireBySlot.ContainsKey(e))
            return;

        var kind = _starbaseKindByFullOrdinal[starbaseTypeOrdinal - 20];
        var explicitType = kind is StarbaseKind.CommandBase or StarbaseKind.Fortress or StarbaseKind.Outpost
            ? (WorldType?)null : (WorldType)explicitTypeOrdinal;

        galaxySetup.CreateBase(galaxy, xy, kind, explicitType, tech, ResolveEmpire(e), population, efficiency, shipBase, cargoBase, defenseBase);
    }

    /// <summary>NEWGAME.PAS:1100-1120 (CreateGate) — no active-empire fallback in real Pascal; a never-created slot falls back to Independent here as a documented simplification (see class doc comment on unmodeled edge cases).</summary>
    private void RunCreateGate(ScenarioTokenizer tokenizer, Galaxy.Galaxy galaxy)
    {
        var xy = GetNextXY(tokenizer, galaxy, checkWorlds: true);
        var gateTypeOrdinal = NextInteger(tokenizer);
        var e = NextInteger(tokenizer);
        var kind = _stargateKindByFullOrdinal[gateTypeOrdinal - 24];
        GalaxySetup.CreateGate(galaxy, xy, kind, ResolveEmpire(e));
    }

    /// <summary>NEWGAME.PAS:1122-1163 (CreateRandomWorlds) — delegates the whole per-world loop to the already-ported GalaxySetup.CreateRandomWorlds.</summary>
    private void RunCreateRandomWorlds(ScenarioTokenizer tokenizer, Galaxy.Galaxy galaxy)
    {
        var noOfWorlds = NextInteger(tokenizer);
        var zoneNumber = NextInteger(tokenizer);
        if (!_zones.TryGetValue(zoneNumber, out var zone))
            throw new FormatException($"ERROR: Undefined zone {zoneNumber}.");
        if (_classTable is null || _techTable is null)
            throw new InvalidOperationException("CREATERANDOMWORLDS needs CLASSTABLE and TECHTABLE to be loaded first.");

        galaxySetup.CreateRandomWorlds(galaxy, noOfWorlds, zone.UpperLeft, zone.LowerRight, _classTable, _techTable, _trillumReserveBase);
    }

    /// <summary>NEWGAME.PAS:1186-1217 (CreatePlayerEmpire).</summary>
    private void RunCreatePlayerEmpire(ScenarioTokenizer tokenizer, IReadOnlyList<PlayerInfo> players, Game game)
    {
        var pl = NextInteger(tokenizer);
        var revFactor = NextInteger(tokenizer);
        var tech = (TechLevel)NextInteger(tokenizer);
        var extraTechs = ReadExtraTechs(tokenizer);
        var centralModifier = ReadModifierList(tokenizer);

        if (pl >= players.Count)
            return;

        var player = players[pl];
        var empire = EmpireFactory.CreateEmpire(player.Name, player.Password, player.IsEmpress, tech, revFactor, centralModifier, game.Year, extraTechs);
        _empireBySlot[pl] = empire;
        game.Empires.Add(empire);
    }

    /// <summary>
    /// NEWGAME.PAS:1219-1259 (CreateNPEmpire). Kingdom1/Kingdom2 empires get a KingdomTurnHandler
    /// registered in Game.TurnHandlers — its constructor is real Pascal's own InitializeNPE call
    /// (NEWGAME.PAS:1250, right after CreateEmpire), seeding persona/diplomacy state from this same
    /// <c>random</c> instance so those draws land in the exact position they do in the real
    /// scenario-load RNG stream. Other NPE types (Pirate/Berserker/Guardian/Trader) get NpeType
    /// recorded but no handler: this port doesn't model those personalities, matching the existing
    /// "ai has no entry in TurnHandlers" behavior (see <c>TurnEngineTests</c>).
    /// </summary>
    private void RunCreateNPEmpire(ScenarioTokenizer tokenizer, Game game)
    {
        var e = NextInteger(tokenizer);
        var npeType = (NpeEmpireType)NextInteger(tokenizer);
        var nameToken = NextToken(tokenizer);
        var revFactor = NextInteger(tokenizer);
        var tech = (TechLevel)NextInteger(tokenizer);
        var extraTechs = ReadExtraTechs(tokenizer);
        var centralModifier = ReadModifierList(tokenizer);

        if (_empireBySlot.ContainsKey(e))
            return;

        var name = nameToken == "RndName" ? GetRandomEmpireName(game) : nameToken;
        var isEmpress = PascalMath.Rnd(random, 0, 1) != 0;
        var empire = EmpireFactory.CreateEmpire(name, null, isEmpress, tech, revFactor, centralModifier, game.Year, extraTechs);
        empire.NpeType = npeType;
        _empireBySlot[e] = empire;
        game.Empires.Add(empire);

        if (npeType is NpeEmpireType.Kingdom1 or NpeEmpireType.Kingdom2) {
            game.TurnHandlers[empire] = new KingdomTurnHandler(empire, npeType, random);
        }
    }

    /// <summary>Shared "NoOfTechs then that many TechnologyTypes ordinals" tail of CreatePlayerEmpire/CreateNPEmpire.</summary>
    private static Action<UnlockedTechnology>[] ReadExtraTechs(ScenarioTokenizer tokenizer)
    {
        var count = NextInteger(tokenizer);
        var grants = new Action<UnlockedTechnology>[count];
        for (var i = 0; i < count; i++)
            grants[i] = _technologyTypeGrants[NextInteger(tokenizer)];
        return grants;
    }

    /// <summary>NEWGAME.PAS:1165-1184 (ReadModifierList) — version-gated; only CENTRAL has any real effect (see LosesIfCapitalConquered's own doc comment).</summary>
    private bool ReadModifierList(ScenarioTokenizer tokenizer)
    {
        if (_scenaVersion < 12)
            return false;

        var count = NextInteger(tokenizer);
        var central = false;
        for (var i = 0; i < count; i++) {
            if (NextToken(tokenizer).ToUpperInvariant() == "CENTRAL")
                central = true;
        }
        return central;
    }

    /// <summary>
    /// NEWGAME.PAS:1576 (InputEmpireName's own GetRandomEmpireName call) — the same random-name pool
    /// and until-unused retry loop as <see cref="GetRandomEmpireName"/> below, exposed for the New Game
    /// player-name prompt (no Game exists yet at that point, hence an explicit already-chosen list
    /// instead of scanning Game.Empires).
    /// </summary>
    public static string SuggestEmpireName(Random random, IReadOnlyCollection<string> alreadyChosen)
    {
        string name;
        do {
            name = _rndEmpireNames[PascalMath.Rnd(random, 1, _rndEmpireNames.Length) - 1];
        } while (alreadyChosen.Contains(name));
        return name;
    }

    /// <summary>NEWGAME.PAS:175-195 (GetRandomEmpireName) — retries until a name not already used by any empire created so far in this game.</summary>
    private string GetRandomEmpireName(Game game) => SuggestEmpireName(random, game.Empires.Select(emp => emp.Name).ToList());

    /// <summary>The 18-integer NLAM,Ndef,NGDM,Nion,Nfgt,...,Ntri tail shared by CreateWorld/CreateBase.</summary>
    private static (ShipCounts Ships, CargoHold Cargo, DefenseCounts Defenses) ReadShipCargoDefenseBase(ScenarioTokenizer tokenizer)
    {
        var defenses = new DefenseCounts { Lams = NextInteger(tokenizer), DefenseSatellites = NextInteger(tokenizer), Gdms = NextInteger(tokenizer), IonCannons = NextInteger(tokenizer) };
        var ships = new ShipCounts {
            Fighters = NextInteger(tokenizer), HunterKillers = NextInteger(tokenizer), Jumpships = NextInteger(tokenizer),
            Jumptransports = NextInteger(tokenizer), Penetrators = NextInteger(tokenizer), Starships = NextInteger(tokenizer), Transports = NextInteger(tokenizer),
        };
        var cargo = new CargoHold {
            Legions = NextInteger(tokenizer), NinjaLegions = NextInteger(tokenizer), Ambrosia = NextInteger(tokenizer),
            Chemicals = NextInteger(tokenizer), Metals = NextInteger(tokenizer), Supplies = NextInteger(tokenizer), Trillum = NextInteger(tokenizer),
        };
        return (ships, cargo, defenses);
    }

    /// <summary>Empire(E) where E=8 is always Indep (NEWGAME.PAS's Empire enum has Indep last, ordinal 8) — see CreateGate's own doc comment on the never-created-slot fallback.</summary>
    private Empire ResolveEmpire(int e) => e == 8 ? Empire.Independent : _empireBySlot.GetValueOrDefault(e, Empire.Independent);
}
