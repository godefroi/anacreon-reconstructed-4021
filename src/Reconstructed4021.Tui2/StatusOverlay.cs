using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;

namespace Reconstructed4021.Tui2;

// F3 (STAWIND.PAS: StatusWindow). Two nine-row panes sharing one scroll position -- a "world status"
// row up top, the matching "military status" row for the same world at the mirrored offset below a
// dividing line -- exactly WriteStatus's own y/y+NoOfLines+1 pairing, not two independently-scrollable
// lists. Uses ListBox<T> for the top pane's own navigation/scrolling, then reads its ScrollOffset back
// to draw the bottom pane in lockstep rather than duplicating the scroll-clamp logic a second time.
internal sealed class StatusOverlay : IOverlay
{
    private const int NoOfLines = 9; // STAWIND.PAS: NoOfLines:=(InitHeight DIV 2)-1, InitHeight=21.
    private const int Width = 86;

    private const string WorldHeader = "PlntName Sta C T Tl  Pop Eff A Impt Expt Rev  jtn  trn  amb  che  met  sup  tri ";
    private const string MilitaryHeader = "PlntName Sta   men ninj  fgt  hkr  jmp  jtn  pen  str  trn  LAM  def  GDM  ion  ";

    // DATACNST.PAS's TypeStr/ClassStr/TechStr, ordinal-aligned with WorldType/WorldClass/TechLevel --
    // same tables Tui's StatusWindow keeps its own copy of.
    private const string TypeCodes = "aAbBCcijJmNorRsStTUXz";
    private const string ClassCodes = "Aa0BjklmDEFGhIJO1P2UV";
    private static readonly string[] TechCodes = ["pt", " p", "pa", " a", "pw", " w", " j", " b", " s", "pg", " g"];

    private readonly Empire _viewer;
    private readonly ListBox<IEconomicWorld> _list;
    private readonly Action<ISectorObject> _onSelectWorld;

    public bool IsDismissed { get; private set; }

    public StatusOverlay(Game game, Empire viewer, Action<ISectorObject> onSelectWorld)
    {
        _viewer = viewer;
        _onSelectWorld = onSelectWorld;
        _list = new ListBox<IEconomicWorld>(WorldStatusReport.BuildRows(game.Galaxy, viewer), FormatWorldStatus);
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_list.HandleKey(key))
        {
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.Enter when _list.SelectedItem is { } world:
                // Stacks Close Up on top instead of dismissing -- Esc from Close Up hands focus
                // straight back to this overlay, scroll position and highlighted row untouched
                // (STAWIND.PAS's own GameShell precedent, see CloseUpOverlay's own doc comment).
                _onSelectWorld(world);
                break;
            case ConsoleKey.Escape or ConsoleKey.F3:
                IsDismissed = true;
                break;
        }
    }

    private string FormatWorldStatus(IEconomicWorld world)
    {
        var owned = ReferenceEquals(world.Owner, _viewer);
        var name = CloseUpOverlay.DisplayName(world, _viewer).PadRight(8)[..8];
        var ownerName = world.Owner.Name.PadRight(3)[..3];
        var pop = world.Population > 9 ? $"{world.Population / 100.0,4:0.0}" : "<0.1";
        var s = world.Ships;
        var c = world.Cargo;

        var tail = owned
            ? $"{s.Jumptransports,5}{s.Transports,5}{c.Ambrosia,5}{c.Chemicals,5}{c.Metals,5}{c.Supplies,5}{c.Trillum,5}"
            : $"{CloseUpWindowText.YesNo(s.Jumptransports),5}{CloseUpWindowText.YesNo(s.Transports),5}  --   --   --   --   --  ";

        return $"{name} {ownerName} {ClassCodes[(int)world.EffectiveClass]} {TypeCodes[(int)world.Type]} {TechCodes[(int)world.TechLevel]} " +
               $"{pop} {world.Efficiency,3} {(world.IsAddictedToAmbrosia ? "y" : "-")} {ImportExportCodes(world)} {HiLo(world.RevolutionIndex)}" +
               $"{tail}";
    }

    private string FormatMilitaryStatus(IEconomicWorld world)
    {
        var owned = ReferenceEquals(world.Owner, _viewer);
        var name = CloseUpOverlay.DisplayName(world, _viewer).PadRight(8)[..8];
        var ownerName = world.Owner.Name.PadRight(3)[..3];
        var s = world.Ships;
        var c = world.Cargo;
        var d = world.Defenses;

        string Level(int value) => owned ? $"{value,5}" : $"{CloseUpWindowText.YesNo(value),5}";

        return $"{name} {ownerName} " +
               $"{Level(c.Legions)}{Level(c.NinjaLegions)}" +
               $"{Level(s.Fighters)}{Level(s.HunterKillers)}{Level(s.Jumpships)}{Level(s.Jumptransports)}{Level(s.Penetrators)}{Level(s.Starships)}{Level(s.Transports)}" +
               $"{Level(d.Lams)}{Level(d.DefenseSatellites)}{Level(d.Gdms)}{Level(d.IonCannons)}";
    }

    // GetImportExportStr (INTRFACE.PAS:557-591): each of Che/Min/Sup/Tri's own ISSP dial contributes a
    // letter to exactly one of the Import/Export 4-char strings based on whether it's below/above
    // SelfSufficiencySettings' own index 5 ("100%, no import/export").
    private static string ImportExportCodes(IEconomicWorld world)
    {
        (char Letter, IndustryType Type)[] industries = [('C', IndustryType.Chemical), ('M', IndustryType.Mining), ('S', IndustryType.Supply), ('T', IndustryType.TrillumMining)];
        var import = "";
        var export = "";
        foreach (var (letter, type) in industries)
        {
            var index = world.SelfSufficiencyIndex(type);
            import += index < 5 ? letter : '-';
            export += index > 5 ? letter : '-';
        }

        return $"{import} {export}";
    }

    // MISC.PAS's HiLo (:65-76) -- a coarse bucket for RevolutionIndex, not the raw number.
    private static string HiLo(int revolutionIndex) => revolutionIndex switch
    {
        >= 0 and <= 10 => "no ",
        >= 11 and <= 25 => "Lo-",
        >= 26 and <= 50 => "Lo+",
        >= 51 and <= 75 => "Hi-",
        >= 76 and <= 100 => "Hi+",
        _ => "---",
    };

    public void Draw(FrameBuffer fb)
    {
        var width = Math.Min(Width, fb.Width);
        var paneRows = Math.Min(NoOfLines, Math.Max(1, (fb.Height - 6) / 2));
        var height = Math.Min(paneRows * 2 + 5, fb.Height - 2);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        fb.DrawText(x + Math.Max(1, (width - 8) / 2), y, " Status ", ConsoleColor.White, ConsoleColor.Black);

        fb.DrawText(x + 1, y + 1, WorldHeader, ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
        _list.Draw(fb, x + 1, y + 2, width - 2, paneRows, ConsoleColor.Gray, ConsoleColor.Black, ConsoleColor.Black, ConsoleColor.Gray);

        var militaryHeaderRow = y + 2 + paneRows;
        fb.DrawText(x + 1, militaryHeaderRow, MilitaryHeader, ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);

        var offset = _list.ScrollOffset;
        for (var row = 0; row < paneRows; row++)
        {
            var index = offset + row;
            var selected = index == _list.SelectedIndex;
            var text = index < _list.Items.Count ? FormatMilitaryStatus(_list.Items[index]) : string.Empty;
            var visible = text.Length > width - 2 ? text[..(width - 2)] : text.PadRight(width - 2);
            fb.DrawText(x + 1, militaryHeaderRow + 1 + row, visible, selected ? ConsoleColor.Black : ConsoleColor.Gray, selected ? ConsoleColor.Gray : ConsoleColor.Black);
        }
    }
}
