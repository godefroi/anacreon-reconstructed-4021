using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// Worlds/Ministry menu bar's F3 shortcut (STAWIND.PAS: StatusWindow). Its own independent
/// 80x21 overlay window (not the shared "display window" <see cref="CloseUpWindow"/>/Production
/// reuse -- STAWIND.PAS opens its own <c>Handle</c>, own Title), scrolling through
/// <see cref="WorldStatusReport.BuildRows"/>'s ordered world list. Splits the interior into two
/// nine-row panes sharing one scroll position -- a "world status" line up top, the matching
/// "military status" line for the same world at the mirrored row below a dividing bar -- exactly
/// <c>WriteStatus</c>'s own <c>y</c>/<c>y+NoOfLines+1</c> pairing, not two independently-scrollable
/// lists.
///
/// Redaction matches <c>GetWorldStatus</c>/<c>GetMilitaryStatus</c> field-by-field, not a blanket
/// owned/foreign switch: a foreign scouted world's jtn/trn ship counts (world-status pane) and all
/// 13 military-status fields (men/ninja/7 ships/4 defenses) fall back to <see cref="CloseUpWindow.YesNo"/>'s
/// coarse magnitude bucket, but the world-status pane's 5 cargo columns are always literal dashes for
/// a foreign world -- real Pascal never even approximates cargo there, confirmed from
/// <c>GetWorldStatus</c>'s own literal <c>'  --   --   --   --   --  '</c>.
/// </summary>
internal sealed class StatusWindow : Window
{
    private const int NoOfLines = 9; // STAWIND.PAS: NoOfLines:=(InitHeight DIV 2)-1, InitHeight=21.
    private const int DividingBarRow = NoOfLines;
    private const int MilitaryStartRow = NoOfLines + 1;

    private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue);
    private static readonly TgAttribute BorderAttribute = new(StandardColor.LightGray, StandardColor.Black);

    // DATACNST.PAS's TypeStr/ClassStr/TechStr -- one-or-two-char abbreviation tables, ordinal-aligned
    // with WorldType/WorldClass/TechLevel (each already verified against the Pascal enum's own
    // declared order, not assumed).
    private const string TypeCodes = "aAbBCcijJmNorRsStTUXz";
    private const string ClassCodes = "Aa0BjklmDEFGhIJO1P2UV";
    private static readonly string[] TechCodes = ["pt", " p", "pa", " a", "pw", " w", " j", " b", " s", "pg", " g"];

    private readonly IReadOnlyList<IEconomicWorld> _rows;
    private readonly Empire _viewer;
    private readonly Label[] _worldStatusLabels = new Label[NoOfLines];
    private readonly Label[] _militaryStatusLabels = new Label[NoOfLines];
    private int _beginIndex;

    public StatusWindow(Game game, Empire viewer)
    {
        _viewer = viewer;
        _rows = WorldStatusReport.BuildRows(game.Galaxy, viewer);

        Title = "PlntName Sta C T Tl  Pop Eff A Impt Expt Rev  jtn  trn  amb  che  met  sup  tri ";
        // Real Pascal's own header/data-row text is ~79-80 columns, sized for its own bare 80-column
        // screen -- this port's Title rendering reserves a couple of columns either side of the text
        // for its "|<title>|" bracket glyphs, on top of the ordinary 2-column border cost, so a plain
        // Width=80 (matching CloseUpWindow/ProductionWindow's own precedent) clips both the header and
        // the data rows' own last column. Widened rather than shortening either -- the data is real
        // Pascal's own column set, not something to trim for a Terminal.Gui rendering quirk.
        Width = 86;
        Height = 21;
        X = Pos.Center();
        Y = Pos.Center();
        BorderStyle = LineStyle.Single;
        CanFocus = true;
        SetScheme(new Scheme(DispWindAttribute));
        Border.View?.SetScheme(new Scheme(BorderAttribute));

        for (var i = 0; i < NoOfLines; i++) {
            _worldStatusLabels[i] = new Label { X = 0, Y = i, Text = string.Empty };
            Add(_worldStatusLabels[i]);
        }

        Add(new Label {
            X = 0, Y = DividingBarRow,
            Text = "PlntName Sta   men ninj  fgt  hkr  jmp  jtn  pen  str  trn  LAM  def  GDM  ion  ",
        });

        for (var i = 0; i < NoOfLines; i++) {
            _militaryStatusLabels[i] = new Label { X = 0, Y = MilitaryStartRow + i, Text = string.Empty };
            Add(_militaryStatusLabels[i]);
        }

        Redraw();

        KeyDown += (_, key) => {
            switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
                case KeyCode.CursorUp: ScrollBy(-1); key.Handled = true; break;
                case KeyCode.CursorDown: ScrollBy(1); key.Handled = true; break;
                case KeyCode.PageUp: ScrollBy(-NoOfLines); key.Handled = true; break;
                case KeyCode.PageDown: ScrollBy(NoOfLines); key.Handled = true; break;
                case KeyCode.Home: _beginIndex = 0; Redraw(); key.Handled = true; break;
                case KeyCode.End: _beginIndex = MaxBeginIndex(); Redraw(); key.Handled = true; break;
            }
        };
    }

    private int MaxBeginIndex() => Math.Max(0, _rows.Count - NoOfLines);

    private void ScrollBy(int delta)
    {
        var clamped = Math.Clamp(_beginIndex + delta, 0, MaxBeginIndex());
        if (clamped != _beginIndex) {
            _beginIndex = clamped;
            Redraw();
        }
    }

    private void Redraw()
    {
        for (var i = 0; i < NoOfLines; i++) {
            var index = _beginIndex + i;
            if (index >= _rows.Count) {
                _worldStatusLabels[i].Text = string.Empty;
                _militaryStatusLabels[i].Text = string.Empty;
                continue;
            }

            var world = _rows[index];
            _worldStatusLabels[i].Text = FormatWorldStatus(world);
            _militaryStatusLabels[i].Text = FormatMilitaryStatus(world);
        }
    }

    private string FormatWorldStatus(IEconomicWorld world)
    {
        var owned = ReferenceEquals(world.Owner, _viewer);
        var name = (world.Names.GetValueOrDefault(_viewer) ?? CloseUpWindow.DescribeKind(world)).PadRight(8)[..8];
        var ownerName = world.Owner.Name.PadRight(3)[..3];
        var pop = world.Population > 9 ? $"{world.Population / 100.0,4:0.0}" : "<0.1";
        var s = world.Ships;
        var c = world.Cargo;

        var tail = owned
            ? $"{s.Jumptransports,5}{s.Transports,5}{c.Ambrosia,5}{c.Chemicals,5}{c.Metals,5}{c.Supplies,5}{c.Trillum,5}"
            : $"{CloseUpWindow.YesNo(s.Jumptransports),5}{CloseUpWindow.YesNo(s.Transports),5}  --   --   --   --   --  ";

        return $"{name} {ownerName} {ClassCodes[(int)world.EffectiveClass]} {TypeCodes[(int)world.Type]} {TechCodes[(int)world.TechLevel]} " +
               $"{pop} {world.Efficiency,3} {(world.IsAddictedToAmbrosia ? "y" : "-")} {ImportExportCodes(world)} {HiLo(world.RevolutionIndex)}" +
               $"{tail}";
    }

    private string FormatMilitaryStatus(IEconomicWorld world)
    {
        var owned = ReferenceEquals(world.Owner, _viewer);
        var name = (world.Names.GetValueOrDefault(_viewer) ?? CloseUpWindow.DescribeKind(world)).PadRight(8)[..8];
        var ownerName = world.Owner.Name.PadRight(3)[..3];
        var s = world.Ships;
        var c = world.Cargo;
        var d = world.Defenses;

        string Level(int value) => owned ? $"{value,5}" : $"{CloseUpWindow.YesNo(value),5}";

        return $"{name} {ownerName} " +
               $"{Level(c.Legions)}{Level(c.NinjaLegions)}" +
               $"{Level(s.Fighters)}{Level(s.HunterKillers)}{Level(s.Jumpships)}{Level(s.Jumptransports)}{Level(s.Penetrators)}{Level(s.Starships)}{Level(s.Transports)}" +
               $"{Level(d.Lams)}{Level(d.DefenseSatellites)}{Level(d.Gdms)}{Level(d.IonCannons)}";
    }

    // GetImportExportStr (INTRFACE.PAS:557-591): each of Che/Min/Sup/Tri's own ISSP dial contributes
    // a letter to exactly one of the Import/Export 4-char strings (dash in the other) based on
    // whether it's below/above SelfSufficiencySettings' own index 5 ("100%, no import/export").
    private static string ImportExportCodes(IEconomicWorld world)
    {
        (char Letter, IndustryType Type)[] industries = [('C', IndustryType.Chemical), ('M', IndustryType.Mining), ('S', IndustryType.Supply), ('T', IndustryType.TrillumMining)];
        var import = "";
        var export = "";
        foreach (var (letter, type) in industries) {
            var index = world.SelfSufficiencyIndex(type);
            import += index < 5 ? letter : '-';
            export += index > 5 ? letter : '-';
        }

        return $"{import} {export}";
    }

    // MISC.PAS's HiLo (:65-76) -- a coarse bucket for RevolutionIndex, not the raw number.
    private static string HiLo(int revolutionIndex) => revolutionIndex switch {
        >= 0 and <= 10 => "no ",
        >= 11 and <= 25 => "Lo-",
        >= 26 and <= 50 => "Lo+",
        >= 51 and <= 75 => "Hi-",
        >= 76 and <= 100 => "Hi+",
        _ => "---",
    };
}
