using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// Worlds menu > Production (CLSCOMM.PAS: ProductionCom). Read-only report -- dismissed by any key,
/// same convention as <see cref="CloseUpWindow"/> (see that class's own doc comment for why this
/// port uses a modal overlay here instead of Pascal's own non-modal shared display window).
///
/// Layout condenses DisplayIndusInfo's own 6-column shape (Bio/Che/Min/SY-/Sup/Tri -- the 4 shipyard
/// industries collapse to whichever one this world's type actually principally uses, matching real
/// ShipYardInd's own point: a world only ever grows one of the four) across 6 rows of context
/// (ISSP/ClsAdj/Target%/Opt/Current, all from real Pascal, plus this port's own "Next Tick" --
/// see <see cref="WorldProductionPreview"/>'s own doc comment for why that's computed by running the
/// real pipeline against a clone rather than a second reimplementation of the formula).
/// </summary>
internal sealed class ProductionWindow : Window
{
    private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue); // SYSDispWind = 23
    private static readonly TgAttribute BorderAttribute = new(StandardColor.LightGray, StandardColor.Black); // SYSWBorder = 7

    // DisplayIndusInfo's own column order (CLSCOMM.PAS:257): Bio, Che, Min, SY-, Sup, Tri.
    private static readonly (string Label, IndustryType Type)[] Columns = [
        ("Bio", IndustryType.Bioindustry),
        ("Che", IndustryType.Chemical),
        ("Min", IndustryType.Mining),
        ("SY-", IndustryType.ShipyardGeneral), // replaced per-world below by ActiveShipyardIndustry
        ("Sup", IndustryType.Supply),
        ("Tri", IndustryType.TrillumMining),
    ];

    public ProductionWindow(IEconomicWorld world, string worldName, Random random)
    {
        Title = $"Production: {worldName}";
        Width = 80;
        Height = 21;
        X = Pos.Center();
        Y = Pos.Center();
        BorderStyle = LineStyle.Single;
        CanFocus = true;
        SetScheme(new Scheme(DispWindAttribute));
        Border.View?.SetScheme(new Scheme(BorderAttribute));

        var columns = Columns;
        columns[3] = ("SY-", ActiveShipyardIndustry(world.Type));

        var preview = WorldProductionPreview.Compute(world, random);

        AddAt(1, 0, " Cls:"); AddAt(7, 0, world.EffectiveClass.ToString());
        AddAt(23, 0, "Tech:"); AddAt(29, 0, world.TechLevel.ToString());
        AddAt(45, 0, "Pop:"); AddAt(50, 0, world.Population.ToString());
        AddAt(62, 0, "Eff:"); AddAt(67, 0, $"{world.Efficiency}%");

        const int industryLabelWidth = 10;
        string Row(string label, IEnumerable<string> cells) => label.PadRight(industryLabelWidth) + string.Concat(cells.Select(cell => cell.PadLeft(5)));

        AddAt(0, 2, Row("", columns.Select(c => c.Label)));
        AddAt(0, 3, Row("ISSP:", columns.Select(c => IsspCell(world, c.Type))));
        AddAt(0, 4, Row("ClsAdj:", columns.Select(c => $"{AnnualTickHandlerClassAdj(world, c.Type)}%")));
        AddAt(0, 5, Row("Target%:", columns.Select(c => $"{preview.Distribution[c.Type]:0}%")));
        AddAt(0, 6, Row("Opt:", columns.Select(c => $"{preview.OptimalIndustry[c.Type]}")));
        AddAt(0, 7, Row("Current:", columns.Select(c => $"{world.Industry[c.Type]}")));
        AddAt(0, 8, Row("Next Tick:", columns.Select(c => $"{preview.ProjectedIndustry[c.Type]}")));

        var ships = Enum.GetValues<ShipType>();
        AddAt(0, 10, Row("", ships.Select(ShipAbbrev)));
        AddAt(0, 11, Row("Current:", ships.Select(s => $"{world.Ships[s]}")));
        AddAt(0, 12, Row("Next Tick:", ships.Select(s => $"{preview.ProjectedShips[s]}")));

        var cargoTypes = Enum.GetValues<CargoType>();
        AddAt(0, 14, Row("", cargoTypes.Select(CargoAbbrev)));
        AddAt(0, 15, Row("Current:", cargoTypes.Select(c => $"{world.Cargo[c]}")));
        AddAt(0, 16, Row("Next Tick:", cargoTypes.Select(c => $"{preview.ProjectedCargo[c]}")));

        AddAt(0, 17, $"Trillum reserves: {world.TrillumReserve}  ->  {preview.ProjectedTrillumReserve}");
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

    private static string ShipAbbrev(ShipType type) => type switch {
        ShipType.Fighter => "fgt", ShipType.HunterKiller => "hkr", ShipType.Jumpship => "jmp",
        ShipType.Jumptransport => "jtn", ShipType.Penetrator => "pen", ShipType.Starship => "str",
        ShipType.Transport => "trn", _ => type.ToString(),
    };

    private static string CargoAbbrev(CargoType type) => type switch {
        CargoType.Chemicals => "che", CargoType.Metals => "met", CargoType.Supplies => "sup",
        CargoType.Trillum => "tri", CargoType.Legion => "men", CargoType.NinjaLegion => "nnj",
        CargoType.Ambrosia => "amb", _ => type.ToString(),
    };
}
