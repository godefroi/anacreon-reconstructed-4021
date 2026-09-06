using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// Worlds menu > Production (CLSCOMM.PAS: ProductionCom). Read-only report; one tab page swapped
/// into <see cref="WorldInfoWindow"/>'s content area (a plain <see cref="View"/>, not a
/// <see cref="Window"/> -- the outer window already draws the border).
///
/// Layout condenses DisplayIndusInfo's own 6-column shape (Bio/Che/Min/SY-/Sup/Tri -- the 4 shipyard
/// industries collapse to whichever one this world's type actually principally uses, matching real
/// ShipYardInd's own point: a world only ever grows one of the four) across 6 rows of context
/// (ISSP/ClsAdj/Target%/Opt/Current, all from real Pascal, plus this port's own "Next Tick" --
/// see <see cref="WorldProductionPreview"/>'s own doc comment for why that's computed by running the
/// real pipeline against a clone rather than a second reimplementation of the formula). The terse
/// row labels (ISSP/ClsAdj/Target%/Opt) are spelled out in-line as an extra column past the industry
/// cells; the ship/cargo type codes get a separate legend in the space to the right of those tables,
/// reusing the real in-game glosses (F1 Help) rather than inventing new ones.
///
/// Must be recomputed on every visit, not just at construction -- <see cref="Refresh"/>, called by
/// <see cref="WorldInfoWindow"/> on tab switch -- since the sibling ISSP tab can change the dials
/// this preview depends on at any time. Uses its own fixed-seed Random rather than the game's shared
/// one so repeated visits never perturb the real turn-resolution RNG.
/// </summary>
internal sealed class ProductionWindow : View
{
    private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue); // SYSDispWind = 23

    // DisplayIndusInfo's own column order (CLSCOMM.PAS:257): Bio, Che, Min, SY-, Sup, Tri.
    private static readonly (string Label, IndustryType Type)[] Columns = [
        ("Bio", IndustryType.Bioindustry),
        ("Che", IndustryType.Chemical),
        ("Min", IndustryType.Mining),
        ("SY-", IndustryType.ShipyardGeneral), // replaced per-world below by ActiveShipyardIndustry
        ("Sup", IndustryType.Supply),
        ("Tri", IndustryType.TrillumMining),
    ];

    private readonly IEconomicWorld world;

    public ProductionWindow(IEconomicWorld world)
    {
        this.world = world;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;
        SetScheme(new Scheme(DispWindAttribute));

        Rebuild();
    }

    /// <summary>
    /// Recomputes the preview against the world's current state -- must be called whenever this tab
    /// is revisited, since the constructor's own snapshot otherwise goes stale the moment the sibling
    /// ISSP tab changes a dial (the bug this method exists to fix). Uses its own fixed-seed Random
    /// rather than the game's shared one, so re-opening this tab never perturbs the real turn-
    /// resolution RNG and always renders the same projection for the same world state.
    /// </summary>
    public void Refresh()
    {
        RemoveAll();
        Rebuild();
    }

    private void Rebuild()
    {
        var columns = Columns;
        columns[3] = ("SY-", ActiveShipyardIndustry(world.Type));

        var preview = WorldProductionPreview.Compute(world, new Random(0));

        AddAt(1, 0, " Cls:"); AddAt(7, 0, world.EffectiveClass.ToString());
        AddAt(23, 0, "Tech:"); AddAt(29, 0, world.TechLevel.ToString());
        AddAt(45, 0, "Pop:"); AddAt(50, 0, world.Population.ToString());
        AddAt(62, 0, "Eff:"); AddAt(67, 0, $"{world.Efficiency}%");

        const int industryLabelWidth = 10;
        string Row(string label, IEnumerable<string> cells) => label.PadRight(industryLabelWidth) + string.Concat(cells.Select(cell => cell.PadLeft(5)));
        string RowWithGloss(string label, IEnumerable<string> cells, string gloss) => Row(label, cells) + "   " + gloss;

        AddAt(0, 2, Row("", columns.Select(c => c.Label)));
        AddAt(0, 3, RowWithGloss("ISSP:", columns.Select(c => IsspCell(world, c.Type)), "target self-sufficiency %"));
        AddAt(0, 4, RowWithGloss("ClsAdj:", columns.Select(c => $"{AnnualTickHandlerClassAdj(world, c.Type)}%"), "world-class industry modifier"));
        AddAt(0, 5, RowWithGloss("Target%:", columns.Select(c => $"{preview.Distribution[c.Type]:0}%"), "this tick's output share"));
        AddAt(0, 6, RowWithGloss("Opt:", columns.Select(c => $"{preview.OptimalIndustry[c.Type]}"), "optimal at full output"));
        AddAt(0, 7, RowWithGloss("Current:", columns.Select(c => $"{world.Industry[c.Type]}"), "industry level right now"));
        AddAt(0, 8, RowWithGloss("Next Tick:", columns.Select(c => $"{preview.ProjectedIndustry[c.Type]}"), "level after next turn runs"));

        var ships = Enum.GetValues<ShipType>();
        AddAt(0, 10, Row("", ships.Select(ShipAbbrev)));
        AddAt(0, 11, Row("Current:", ships.Select(s => $"{world.Ships[s]}")));
        AddAt(0, 12, Row("Next Tick:", ships.Select(s => $"{preview.ProjectedShips[s]}")));

        var cargoTypes = Enum.GetValues<CargoType>();
        AddAt(0, 14, Row("", cargoTypes.Select(CargoAbbrev)));
        AddAt(0, 15, Row("Current:", cargoTypes.Select(c => $"{world.Cargo[c]}")));
        AddAt(0, 16, Row("Next Tick:", cargoTypes.Select(c => $"{preview.ProjectedCargo[c]}")));

        AddAt(0, 17, $"Trillum reserves: {world.TrillumReserve}  ->  {preview.ProjectedTrillumReserve}");

        AddAt(0, 18, preview.ShortThisTick.Count == 0
            ? "Short this tick: none"
            : $"Short this tick: {string.Join(", ", preview.ShortThisTick.Select(CargoAbbrev))}");

        AddLegend();
    }

    // Unused screen real estate to the right of the ship/cargo tables (which only reach x=44) --
    // ship and cargo type codes are the least likely of the abbreviations here to be misunderstood,
    // so they're what's shown in this narrower legend; ISSP/ClsAdj/Target%/Opt get spelled out
    // in-line as their own table column instead (RowWithGloss above), where there's no ambiguity
    // about which row a description belongs to. Wording matches the real in-game glosses (F1 Help,
    // "Ships and Defenses"/"Materials" pages) rather than inventing new ones.
    private void AddLegend()
    {
        const int x = 50;
        const int y = 10;
        string[] lines = [
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
        for (var i = 0; i < lines.Length; i++) {
            AddAt(x, y + i, lines[i]);
        }
    }

    private void AddAt(int x, int y, string text) => Add(new Label { X = x, Y = y, Text = text });

    // ShipYardInd (CLSCOMM.PAS): the one shipyard industry a world's own type actually principally
    // grows -- the other three always sit at 0, so showing all four separately is noise, not signal.
    private static IndustryType ActiveShipyardIndustry(WorldType type)
    {
        var principal = WorldDesignation.PrincipalIndustry[type];
        return principal is IndustryType.ShipyardGeneral or IndustryType.ShipyardJump
            or IndustryType.ShipyardStarship or IndustryType.ShipyardTransport
            ? principal
            : IndustryType.ShipyardGeneral;
    }

    // DisplayIndusInfo's own dash guard (CLSCOMM.PAS:270-271): Bio/SY- have no ISSP dial at all.
    private static string IsspCell(IEconomicWorld world, IndustryType type) => type switch {
        IndustryType.Chemical or IndustryType.Mining or IndustryType.Supply or IndustryType.TrillumMining
            => SelfSufficiencySettings.DisplayPercent(world.SelfSufficiencyIndex(type)),
        _ => "---",
    };

    private static int AnnualTickHandlerClassAdj(IEconomicWorld world, IndustryType type) =>
        Core.Turns.AnnualTickHandler.ClassIndustryAdjustment[(world.EffectiveClass, type)];

    private static string ShipAbbrev(ShipType type) => ResourceAbbreviation.Of(type);

    private static string CargoAbbrev(CargoType type) => ResourceAbbreviation.Of(type);
}
