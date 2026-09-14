using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Presentation;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;

namespace Reconstructed4021.Tui2;

// WorldInfoWindow, ported from Reconstructed4021.Tui: one of the player's own worlds, shown as a
// TabFrame with Close Up/Production/Designate always present and ISSP/Redirect added only for a
// Planet (a starbase has no settable ISSP dial or redirection field to edit -- see
// WorldCloseUpTabView/RedirectTabView's own doc comments). Unlike CloseUpOverlay, only Esc dismisses
// this -- every other key belongs to whichever tab is showing (arrows/Enter for its own editing).
//
// Deliberately deferred: Redirect's destination pick (a new cross-screen map-cursor input mode, not a
// port of anything -- its own slice), D:Deploy and F2:Rename (same scope cut CloseUpOverlay made).
internal sealed class WorldInfoOverlay : IOverlay
{
    private const int FrameWidth = 88;
    private const int FrameHeight = 24;

    private const ConsoleColor BorderFg = ConsoleColor.Gray; // SYSWBorder = 7.
    private const ConsoleColor BorderBg = ConsoleColor.Black;
    private const ConsoleColor ContentFg = ConsoleColor.Gray; // SYSDispWind = 23.
    private const ConsoleColor ContentBg = ConsoleColor.DarkBlue;
    private const ConsoleColor SelectedFg = ConsoleColor.Black; // SYSDispSelect = 112.
    private const ConsoleColor SelectedBg = ConsoleColor.Gray;

    private enum TabKind { CloseUp, Production, Issp, Designate, Redirect }

    private readonly IEconomicWorld _world;
    private readonly Empire _viewer;
    private readonly Game _game;
    private readonly Random _random;
    private readonly Action _refresh;
    private readonly Action<string, string> _showInfo;
    private readonly Action<IOverlay> _push;

    private readonly TabKind[] _tabKinds;
    private readonly TabFrame _frame;
    private readonly ListBox<WorldTypeChoice>? _designateList;

    private int _isspRow;
    private int _redirectRow;

    private WorldProductionPreview.Result? _productionCache;
    private bool _productionDirty = true;

    public bool IsDismissed { get; private set; }

    public WorldInfoOverlay(IEconomicWorld world, Empire viewer, Game game, Random random,
        Action refresh, Action<string, string> showInfo, Action<IOverlay> push, string initialTab = "Close Up")
    {
        _world = world;
        _viewer = viewer;
        _game = game;
        _random = random;
        _refresh = refresh;
        _showInfo = showInfo;
        _push = push;

        var isPlanet = world is Planet;
        List<TabKind> kinds = [TabKind.CloseUp, TabKind.Production];
        if (isPlanet)
        {
            kinds.Add(TabKind.Issp);
        }

        kinds.Add(TabKind.Designate);
        if (isPlanet)
        {
            kinds.Add(TabKind.Redirect);
        }

        _tabKinds = [.. kinds];
        _frame = new TabFrame(_tabKinds.Select(TabLabel).ToArray());
        _designateList = BuildDesignateList();

        var initialIndex = Array.IndexOf(_tabKinds, ParseInitialTab(initialTab));
        if (initialIndex >= 0)
        {
            _frame.SelectIndex(initialIndex);
        }
    }

    private static string TabLabel(TabKind kind) => kind switch
    {
        TabKind.CloseUp => "Close Up",
        TabKind.Production => "Production",
        TabKind.Issp => "ISSP",
        TabKind.Designate => "Designate",
        TabKind.Redirect => "Redirect",
        _ => kind.ToString(),
    };

    private static TabKind ParseInitialTab(string tab) => tab switch
    {
        "CloseUp" or "Close Up" => TabKind.CloseUp,
        "Production" => TabKind.Production,
        "ISSP" => TabKind.Issp,
        "Designate" => TabKind.Designate,
        "Redirect" => TabKind.Redirect,
        _ => TabKind.CloseUp,
    };

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_frame.HandleKey(key))
        {
            return;
        }

        if (key.Key == ConsoleKey.Escape)
        {
            IsDismissed = true;
            return;
        }

        switch (_tabKinds[_frame.ActiveIndex])
        {
            case TabKind.Issp:
                HandleIsspKey(key);
                break;
            case TabKind.Designate:
                HandleDesignateKey(key);
                break;
            case TabKind.Redirect:
                HandleRedirectKey(key);
                break;
        }
    }

    public void Draw(FrameBuffer fb)
    {
        var width = Math.Min(FrameWidth, fb.Width);
        var height = Math.Min(FrameHeight, fb.Height);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        var name = _world.Names.GetValueOrDefault(_viewer) ?? CloseUpOverlay.DescribeLocation(_world, _viewer);
        _frame.Draw(fb, x, y, width, height, name, BorderFg, BorderBg, ConsoleColor.White, BorderBg);

        var cx = x + 1;
        var cy = y + 1;
        var cw = width - 2;
        var ch = height - 2;
        for (var row = 0; row < ch; row++)
        {
            fb.DrawText(cx, cy + row, new string(' ', cw), ContentFg, ContentBg);
        }

        switch (_tabKinds[_frame.ActiveIndex])
        {
            case TabKind.CloseUp:
                DrawCloseUp(fb, cx, cy, cw, ch);
                break;
            case TabKind.Production:
                DrawProduction(fb, cx, cy, cw, ch);
                break;
            case TabKind.Issp:
                DrawIssp(fb, cx, cy, cw, ch);
                break;
            case TabKind.Designate:
                DrawDesignate(fb, cx, cy, cw, ch);
                break;
            case TabKind.Redirect:
                DrawRedirect(fb, cx, cy, cw, ch);
                break;
        }
    }

    // Every field this feeds sits at a fixed row/column transcribed from real Pascal's own fixed
    // 80x24-DOS-screen layout -- on a terminal smaller than this frame's own natural size (88x24,
    // clamped in Draw), a row past the bottom border would otherwise draw straight over it instead of
    // just being omitted. Real Pascal never needed this (its screen could never be smaller than the
    // layout it drew); this port's own equivalent, not a restoration.
    private static void DrawClipped(FrameBuffer fb, int cx, int cy, int cw, int ch, int x, int y, string text)
    {
        if (x >= cw || y >= ch)
        {
            return;
        }

        fb.DrawText(cx + x, cy + y, text, ContentFg, ContentBg, maxWidth: cw - x);
    }

    // WorldCloseUpTabView: CLSCOMM.PAS's own DisplayBasicInfo/DisplayCargoInfo/DisplayMilitaryInfo/
    // DisplayBackground with every owned/scouted branch collapsed to its owned-and-scouted case --
    // ownership is a given here, unlike CloseUpOverlay's own general-purpose redaction.
    private void DrawCloseUp(FrameBuffer fb, int cx, int cy, int cw, int ch)
    {
        void At(int x, int y, string text) => DrawClipped(fb, cx, cy, cw, ch, x, y, text);
        var world = _world;

        At(1, 0, " Cls:"); At(1, 1, "Tech:"); At(1, 2, " Pop:");
        At(23, 0, "Eff:"); At(23, 1, "Amb:"); At(23, 2, "Rev:");
        At(7, 0, world.EffectiveClass.ToString());
        At(7, 1, world.TechLevel.ToString());
        At(7, 2, world.Population.ToString());
        At(28, 0, $"{world.Efficiency}%");
        At(28, 1, world.IsAddictedToAmbrosia ? "yes" : "no");
        At(28, 2, world.RevolutionIndex.ToString());

        At(51, 0, "amb  che  met  sup  tri");
        At(49, 1, $"{world.Cargo.Ambrosia,5}{world.Cargo.Chemicals,5}{world.Cargo.Metals,5}{world.Cargo.Supplies,5}{world.Cargo.Trillum,5}");

        At(2, 4, "fgt  hkr  jmp  jtn  pen  str  trn    men  nnj    LAM  def  GDM  ion");
        var s = world.Ships;
        var c = world.Cargo;
        var d = world.Defenses;
        At(0, 5,
            $"{s.Fighters,5}{s.HunterKillers,5}{s.Jumpships,5}{s.Jumptransports,5}{s.Penetrators,5}{s.Starships,5}{s.Transports,5}" +
            $"  {c.Legions,5}{c.NinjaLegions,5}" +
            $"  {d.Lams,5}{d.DefenseSatellites,5}{d.Gdms,5}{d.IonCannons,5}");

        if (world is Planet { Redirection.Destination: { } redirectDestination })
        {
            var origin = _viewer.Capital?.Location ?? new Coordinate(0, 0);
            At(1, 6, $"Redirecting new production -> ({RelativeCoordinate.Format(redirectDestination, origin)})");
        }

        if (Game.FindWorldBackgroundText(_game, world, _viewer, conquer: false) is { } background)
        {
            for (var i = 0; i < background.Count; i++)
            {
                At(1, 7 + i, background[i]);
            }
        }

        At(1, ch - 1, "Esc: close");
    }

    // ProductionWindow.Rebuild -- recomputed only when the Production tab becomes active or a value
    // it depends on (an ISSP dial) changes; WorldProductionPreview.Compute clones the world and runs
    // the real production pipeline, real work worth not repeating every frame.
    private static readonly (string Label, IndustryType Type)[] ProductionColumnsTemplate =
    [
        ("Bio", IndustryType.Bioindustry),
        ("Che", IndustryType.Chemical),
        ("Min", IndustryType.Mining),
        ("SY-", IndustryType.ShipyardGeneral),
        ("Sup", IndustryType.Supply),
        ("Tri", IndustryType.TrillumMining),
    ];

    private void DrawProduction(FrameBuffer fb, int cx, int cy, int cw, int ch)
    {
        if (_productionDirty || _productionCache is null)
        {
            _productionCache = WorldProductionPreview.Compute(_world, new Random(0));
            _productionDirty = false;
        }

        var preview = _productionCache;
        void At(int x, int y, string text) => DrawClipped(fb, cx, cy, cw, ch, x, y, text);

        var columns = (IReadOnlyList<(string Label, IndustryType Type)>)
            [.. ProductionColumnsTemplate[..3], ("SY-", ActiveShipyardIndustry(_world.Type)), .. ProductionColumnsTemplate[4..]];

        At(1, 0, " Cls:"); At(7, 0, _world.EffectiveClass.ToString());
        At(23, 0, "Tech:"); At(29, 0, _world.TechLevel.ToString());
        At(45, 0, "Pop:"); At(50, 0, _world.Population.ToString());
        At(62, 0, "Eff:"); At(67, 0, $"{_world.Efficiency}%");

        const int labelWidth = 10;
        string Row(string label, IEnumerable<string> cells) => label.PadRight(labelWidth) + string.Concat(cells.Select(cell => cell.PadLeft(5)));
        string RowWithGloss(string label, IEnumerable<string> cells, string gloss) => Row(label, cells) + "   " + gloss;

        At(0, 2, Row("", columns.Select(c => c.Label)));
        At(0, 3, RowWithGloss("ISSP:", columns.Select(c => IsspCell(_world, c.Type)), "target self-sufficiency %"));
        At(0, 4, RowWithGloss("ClsAdj:", columns.Select(c => $"{AnnualTickHandler.ClassIndustryAdjustment[(_world.EffectiveClass, c.Type)]}%"), "world-class industry modifier"));
        At(0, 5, RowWithGloss("Target%:", columns.Select(c => $"{preview.Distribution[c.Type]:0}%"), "this tick's output share"));
        At(0, 6, RowWithGloss("Opt:", columns.Select(c => $"{preview.OptimalIndustry[c.Type]}"), "optimal at full output"));
        At(0, 7, RowWithGloss("Current:", columns.Select(c => $"{_world.Industry[c.Type]}"), "industry level right now"));
        At(0, 8, RowWithGloss("Next Tick:", columns.Select(c => $"{preview.ProjectedIndustry[c.Type]}"), "level after next turn runs"));

        var ships = Enum.GetValues<ShipType>();
        At(0, 10, Row("", ships.Select(ResourceAbbreviation.Of)));
        At(0, 11, Row("Current:", ships.Select(sh => $"{_world.Ships[sh]}")));
        At(0, 12, Row("Next Tick:", ships.Select(sh => $"{preview.ProjectedShips[sh]}")));

        var cargoTypes = Enum.GetValues<CargoType>();
        At(0, 14, Row("", cargoTypes.Select(ResourceAbbreviation.Of)));
        At(0, 15, Row("Current:", cargoTypes.Select(ct => $"{_world.Cargo[ct]}")));
        At(0, 16, Row("Next Tick:", cargoTypes.Select(ct => $"{preview.ProjectedCargo[ct]}")));

        At(0, 17, $"Trillum reserves: {_world.TrillumReserve}  ->  {preview.ProjectedTrillumReserve}");
        At(0, 18, preview.ShortThisTick.Count == 0
            ? "Short this tick: none"
            : $"Short this tick: {string.Join(", ", preview.ShortThisTick.Select(ResourceAbbreviation.Of))}");

        string[] legend =
        [
            "fgt fighters      hkr hunter-killers",
            "jmp jumpships     jtn jumptransports",
            "pen penetrators   str starships",
            "trn transports",
            "",
            "che chemicals     met metals",
            "sup supplies      tri trillum",
            "men troops        nnj ninja legion",
            "amb ambrosia",
        ];
        for (var i = 0; i < legend.Length; i++)
        {
            At(50, 10 + i, legend[i]);
        }
    }

    private static IndustryType ActiveShipyardIndustry(WorldType type)
    {
        var principal = WorldDesignation.PrincipalIndustry[type];
        return principal is IndustryType.ShipyardGeneral or IndustryType.ShipyardJump
            or IndustryType.ShipyardStarship or IndustryType.ShipyardTransport
            ? principal
            : IndustryType.ShipyardGeneral;
    }

    private static string IsspCell(IEconomicWorld world, IndustryType type) => type switch
    {
        IndustryType.Chemical or IndustryType.Mining or IndustryType.Supply or IndustryType.TrillumMining
            => SelfSufficiencySettings.DisplayPercent(world.SelfSufficiencyIndex(type)),
        _ => "---",
    };

    // IsspEditor -- 4-row stepper over SelfSufficiencySettings, planet-only. Left/Right nudges the
    // selected row (clamped, no wrap); Up/Down/PageUp/PageDown moves the row cursor (wraps).
    private static readonly string[] IsspBandDescriptions =
    [
        "1%  (imports 99% of need)",
        "10%  (imports 90% of need)",
        "25%  (imports 75% of need)",
        "50%  (imports 50% of need)",
        "75%  (imports 25% of need)",
        "100%  (no import/export)",
        "150%  (exports 33% of production)",
        "200%  (exports 50% of production)",
        "300%  (exports 67% of production)",
        "400%  (exports 75% of production)",
        "500%  (exports 80% of production)",
    ];

    private static readonly (string Name, Func<SelfSufficiencySettings, int> Get, Action<SelfSufficiencySettings, int> Set)[] IsspRows =
    [
        ("Chemical industry:", s => s.Chemical, (s, v) => s.Chemical = v),
        ("Mining industry:", s => s.Metal, (s, v) => s.Metal = v),
        ("Supply industry:", s => s.Supply, (s, v) => s.Supply = v),
        ("Trillum industry:", s => s.Trillum, (s, v) => s.Trillum = v),
    ];

    private void DrawIssp(FrameBuffer fb, int cx, int cy, int cw, int ch)
    {
        void At(int x, int y, string text) => DrawClipped(fb, cx, cy, cw, ch, x, y, text);
        At(1, 0, "How much of a raw material a world produces relative to what it needs. Below 100%,");
        At(1, 1, "the shortfall must be shipped in by transport; above 100%, the surplus sits there");
        At(1, 2, "for you to ship out.");

        var settings = ((Planet)_world).SelfSufficiency;
        for (var i = 0; i < IsspRows.Length; i++)
        {
            var (name, get, _) = IsspRows[i];
            var text = $"{name,-19}{IsspBandDescriptions[get(settings)]}";
            var selected = i == _isspRow;
            var visible = text.Length > cw - 1 ? text[..(cw - 1)] : text.PadRight(cw - 1);
            fb.DrawText(cx + 1, cy + 5 + i, visible, selected ? SelectedFg : ContentFg, selected ? SelectedBg : ContentBg);
        }

        At(1, ch - 1, "Up/Down: select industry   Left/Right: change");
    }

    private void HandleIsspKey(ConsoleKeyInfo key)
    {
        var settings = ((Planet)_world).SelfSufficiency;
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
            {
                var (_, get, set) = IsspRows[_isspRow];
                var value = get(settings);
                if (value > 0)
                {
                    set(settings, value - 1);
                    _productionDirty = true;
                }

                return;
            }
            case ConsoleKey.RightArrow:
            {
                var (_, get, set) = IsspRows[_isspRow];
                var value = get(settings);
                if (value < SelfSufficiencySettings.Multipliers.Length - 1)
                {
                    set(settings, value + 1);
                    _productionDirty = true;
                }

                return;
            }
            case ConsoleKey.UpArrow:
            case ConsoleKey.PageUp:
                _isspRow = _isspRow == 0 ? IsspRows.Length - 1 : _isspRow - 1;
                return;
            case ConsoleKey.DownArrow:
            case ConsoleKey.PageDown:
                _isspRow = (_isspRow + 1) % IsspRows.Length;
                return;
        }
    }

    // DesignateTabView -- a persistent list (ListBox<T>) over every WorldType the world's own tech
    // level and class qualify for; Enter runs the same risk-confirm chain GameShell.ConfirmDesignate
    // does before actually mutating anything (WorldDesignation.Redesignate).
    private sealed record WorldTypeChoice(WorldType Type, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly HashSet<WorldType> NeverDesignable =
    [
        WorldType.Outpost, WorldType.BaseStarbase, WorldType.JumpshipBaseStarbase,
        WorldType.StarshipBaseStarbase, WorldType.TransportBaseStarbase, WorldType.RawMaterialMineStarbase,
        WorldType.Terraform,
    ];

    private ListBox<WorldTypeChoice> BuildDesignateList()
    {
        var cls = _world.EffectiveClass;
        var choices = Enum.GetValues<WorldType>()
            .Where(t => !NeverDesignable.Contains(t))
            .Where(t => WorldDesignation.MinTechForType[t] <= _world.TechLevel)
            .Where(t => t != WorldType.Ambrosia || cls is WorldClass.Ambrosia or WorldClass.Paradise)
            .Select(t => new WorldTypeChoice(t, MenuLine(t, cls)))
            .ToList();
        return new ListBox<WorldTypeChoice>(choices, c => c.Label);
    }

    private static string MenuLine(WorldType type, WorldClass cls)
    {
        var name = WorldDesignation.TypeName(type);
        var capitalized = char.ToUpperInvariant(name[0]) + name[1..];
        var industry = type switch
        {
            WorldType.University => "(research)",
            WorldType.RawMaterialMine or WorldType.RawMaterialMineStarbase => "raw material mining",
            WorldType.Capital => "administration",
            _ => IndustryConstants.Name(WorldDesignation.PrincipalIndustry[type]),
        };
        var suitability = AnnualTickHandler.ClassIndustryAdjustment[(cls, WorldDesignation.PrincipalIndustry[type])];
        return $"{capitalized,-30}{industry,-22}{suitability,4}%";
    }

    private void DrawDesignate(FrameBuffer fb, int cx, int cy, int cw, int ch)
    {
        var header = $"{"World Type",-30}{"Main Industry",-22}{"Suit.",4}";
        DrawClipped(fb, cx, cy, cw, ch, 0, 0, header);

        const int hintLines = 4;
        var listHeight = Math.Max(1, ch - 1 - hintLines);
        _designateList!.Draw(fb, cx, cy + 1, cw, listHeight, ContentFg, ContentBg, SelectedFg, SelectedBg);

        var hint = _designateList.SelectedItem is { } sel ? WorldDesignation.DesignationHint(_world, sel.Type) : string.Empty;
        var wrapped = WrapText(hint, cw);
        for (var i = 0; i < hintLines && i < wrapped.Count; i++)
        {
            DrawClipped(fb, cx, cy, cw, ch, 0, ch - hintLines + i, wrapped[i]);
        }
    }

    private static List<string> WrapText(string text, int width)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (candidate.Length > width && current.Length > 0)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        return lines;
    }

    private void HandleDesignateKey(ConsoleKeyInfo key)
    {
        if (_designateList!.HandleKey(key))
        {
            return;
        }

        if (key.Key == ConsoleKey.Enter && _designateList.SelectedItem is { } selected)
        {
            ConfirmDesignate(selected.Type);
        }
    }

    // GameShell.ConfirmDesignate's own risk-confirm chain (DESIGN.PAS:785-832) -- at most one warning
    // fires, in this exact order; a null warning designates immediately, matching real Pascal's own
    // "no objection" fall-through.
    private void ConfirmDesignate(WorldType newType)
    {
        var world = _world;
        var cls = world.EffectiveClass;
        var myLord = Honorifics.MyLord(_viewer.IsEmpress);
        var displayName = DisplayName(world);

        var warning = newType switch
        {
            WorldType.Capital =>
                $"{myLord}, changing the capital will result in short-term loss of efficiency\nand increased unrest among the people of the empire.",
            _ when world is Starbase { Kind: StarbaseKind.IndustrialComplex } && newType is not
                (WorldType.Base or WorldType.JumpshipBase or WorldType.StarshipBase or WorldType.TransportBase or WorldType.Capital or WorldType.NinjaWorld) =>
                $"But {myLord}, an industrial complex would be wasted on such a trivial designation.",
            WorldType.University when world.TechLevel < _viewer.Capital!.TechLevel =>
                $"But {myLord}, {displayName} is not yet as advanced as the capital.\nAs a university world it wouldn't be of much use.",
            WorldType.Mine or WorldType.RawMaterialMine or WorldType.TrillumMine when cls is WorldClass.GasGiant or WorldClass.Ice or WorldClass.Ocean or WorldClass.Poisonous =>
                $"{myLord}, the environment of {displayName} is not really suited to\nlarge scale mining operations.",
            WorldType.Agricultural when cls is WorldClass.Arid or WorldClass.Artificial or WorldClass.Barren or WorldClass.Desert or WorldClass.Ice or WorldClass.Poisonous or WorldClass.Underground or WorldClass.Volcanic =>
                $"I hope you will reconsider, {myLord}, {displayName} would not be\nan ideal agricultural world.",
            _ => null,
        };

        if (warning is null)
        {
            FinishDesignate(newType);
            return;
        }

        _push(new ConfirmOverlay("Designate", $"{warning}\nAre you sure about this command?", yes =>
        {
            if (yes)
            {
                FinishDesignate(newType);
            }
        }));
    }

    private void FinishDesignate(WorldType newType)
    {
        WorldDesignation.Redesignate(_world, newType, _random);
        _refresh();
        _productionDirty = true;
        _showInfo("Designate",
            $"{DisplayName(_world)} has been designated as {DesignationArticleName(newType)}.\n" +
            $"All industries are being re-distributed.  New efficiency: {_world.Efficiency}%");
    }

    private string DisplayName(ISectorObject obj) => obj.Names.GetValueOrDefault(_viewer) ?? CloseUpOverlay.DescribeLocation(obj, _viewer);

    private static string DesignationArticleName(WorldType type) => type switch
    {
        WorldType.Agricultural => "an agricultural world",
        WorldType.Ambrosia => "an ambrosia world",
        WorldType.Base => "a base planet",
        WorldType.BaseStarbase => "a specialized base planet",
        WorldType.Capital => "the capital of the empire",
        WorldType.Chemical => "a chemical factory world",
        WorldType.Independent => "an independent world",
        WorldType.JumpshipBase => "a jumpship complex",
        WorldType.JumpshipBaseStarbase => "a specialized jumpship complex",
        WorldType.Mine => "a metal-mining world",
        WorldType.NinjaWorld => "a ninja world",
        WorldType.Outpost => "an outpost",
        WorldType.RawMaterialMine => "a mining world",
        WorldType.RawMaterialMineStarbase => "a specialized mining world",
        WorldType.StarshipBase => "a starship complex",
        WorldType.StarshipBaseStarbase => "a specialized starship complex",
        WorldType.TransportBase => "a warpship complex",
        WorldType.TransportBaseStarbase => "a specialized warpship complex",
        WorldType.University => "a research university world",
        WorldType.Terraform => "a terraforming world",
        WorldType.TrillumMine => "a trillum-mining world",
        _ => type.ToString(),
    };

    // RedirectTabView -- planet-only. Destination picking isn't wired up yet (its own slice: a new
    // cross-screen map-cursor input mode, not a port of anything already specified); Enter says so
    // instead of silently doing nothing.
    private sealed record RedirectRow(string Name, Func<RedirectionSettings, RedirectionMode> Get, Action<RedirectionSettings, RedirectionMode> Set, RedirectionMode[] Cycle);

    private static RedirectRow ShipRow(string name, ShipType type, RedirectionMode[] cycle) => new(
        name,
        s => s.Ships.GetValueOrDefault(type, RedirectionMode.No),
        (s, v) => s.Ships[type] = v,
        cycle);

    private static readonly RedirectionMode[] YesNo = [RedirectionMode.No, RedirectionMode.Yes];
    private static readonly RedirectionMode[] YesNoAsNeeded = [RedirectionMode.No, RedirectionMode.Yes, RedirectionMode.AsNeeded];

    private static readonly RedirectRow[] RedirectRows =
    [
        ShipRow("Fighters:", ShipType.Fighter, YesNo),
        ShipRow("Hunter-killers:", ShipType.HunterKiller, YesNo),
        ShipRow("Jumpships:", ShipType.Jumpship, YesNo),
        ShipRow("Jumptransports:", ShipType.Jumptransport, YesNoAsNeeded),
        ShipRow("Penetrators:", ShipType.Penetrator, YesNo),
        ShipRow("Starships:", ShipType.Starship, YesNo),
        ShipRow("Transports:", ShipType.Transport, YesNoAsNeeded),
        new("Legions:", s => s.IncludeLegions ? RedirectionMode.Yes : RedirectionMode.No,
            (s, v) => s.IncludeLegions = v == RedirectionMode.Yes, YesNo),
        new("Ninja legions:", s => s.IncludeNinjaLegions ? RedirectionMode.Yes : RedirectionMode.No,
            (s, v) => s.IncludeNinjaLegions = v == RedirectionMode.Yes, YesNo),
        new("Join on arrival:", s => s.JoinOnArrival ? RedirectionMode.Yes : RedirectionMode.No,
            (s, v) => s.JoinOnArrival = v == RedirectionMode.Yes, YesNo),
        new("Preserve overflow:", s => s.PreserveOverflowOnJoin ? RedirectionMode.Yes : RedirectionMode.No,
            (s, v) => s.PreserveOverflowOnJoin = v == RedirectionMode.Yes, YesNo),
    ];

    private static string RedirectModeText(RedirectionMode mode) => mode switch
    {
        RedirectionMode.No => "No",
        RedirectionMode.Yes => "Yes",
        RedirectionMode.AsNeeded => "As needed",
        _ => mode.ToString(),
    };

    private void DrawRedirect(FrameBuffer fb, int cx, int cy, int cw, int ch)
    {
        void At(int x, int y, string text) => DrawClipped(fb, cx, cy, cw, ch, x, y, text);
        At(1, 0, "Newly produced ships/legions/ninja are auto-dispatched here every turn -- never");
        At(1, 1, "the planet's existing stockpile.");

        var settings = ((Planet)_world).Redirection;
        var origin = _viewer.Capital?.Location ?? new Coordinate(0, 0);
        At(1, 3, settings.Destination is { } d
            ? $"Destination: ({RelativeCoordinate.Format(d, origin)})"
            : "Destination: not set");

        for (var i = 0; i < RedirectRows.Length; i++)
        {
            var row = RedirectRows[i];
            var text = $"{row.Name,-19}{RedirectModeText(row.Get(settings))}";
            var selected = i == _redirectRow;
            var visible = text.Length > cw - 1 ? text[..(cw - 1)] : text.PadRight(cw - 1);
            fb.DrawText(cx + 1, cy + 5 + i, visible, selected ? SelectedFg : ContentFg, selected ? SelectedBg : ContentBg);
        }

        At(1, ch - 2, "Up/Down: select   Left/Right: change   X: clear dest.");
        At(1, ch - 1, "(destination picking isn't wired up yet in this port)");
    }

    private void HandleRedirectKey(ConsoleKeyInfo key)
    {
        var settings = ((Planet)_world).Redirection;
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                CycleRedirectRow(settings, -1);
                return;
            case ConsoleKey.RightArrow:
                CycleRedirectRow(settings, 1);
                return;
            case ConsoleKey.UpArrow:
            case ConsoleKey.PageUp:
                _redirectRow = _redirectRow == 0 ? RedirectRows.Length - 1 : _redirectRow - 1;
                return;
            case ConsoleKey.DownArrow:
            case ConsoleKey.PageDown:
                _redirectRow = (_redirectRow + 1) % RedirectRows.Length;
                return;
            case ConsoleKey.Enter:
                _showInfo("Redirect", "Destination picking isn't wired up yet in this port.");
                return;
        }

        if (key.KeyChar is 'x' or 'X')
        {
            settings.Destination = null;
        }
    }

    private void CycleRedirectRow(RedirectionSettings settings, int direction)
    {
        var row = RedirectRows[_redirectRow];
        var cycle = row.Cycle;
        var current = Array.IndexOf(cycle, row.Get(settings));
        if (current < 0)
        {
            current = 0;
        }

        var next = ((current + direction) % cycle.Length + cycle.Length) % cycle.Length;
        row.Set(settings, cycle[next]);
    }
}
