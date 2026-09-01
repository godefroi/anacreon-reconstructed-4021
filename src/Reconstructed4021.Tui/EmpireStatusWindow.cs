using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// PROLOG.PAS's EmpireStatus (:490-615), shown right after the turn-start greeting -- part of the
/// same chained "Turn Start / Player Login" sequence as TurnStartGreetingWindow, sharing that
/// window's own styling (full-screen SYSDispWind fill; real Pascal draws both into the one
/// SetUpWindow it opens once for the whole sequence). Real Pascal's own "skipped if this was the
/// player's last turn" gate has no equivalent here -- this port's own turn loop (Program.cs) never
/// shows a human session at all once PendingElimination/Eliminated, so there's no "last turn" case
/// left to special-case by the time this window could run.
///
/// All figures come from <see cref="EmpireStatusReport"/> -- this class only formats them.
/// </summary>
internal sealed class EmpireStatusWindow : Window
{
    public EmpireStatusWindow(Empire empire, Game game)
    {
        Width = Dim.Fill();
        Height = Dim.Fill();
        SetScheme(new Scheme(new TgAttribute(StandardColor.LightGray, StandardColor.Blue))); // SYSDispWind

        // A plain Label, not a TextView: Terminal.Gui has deprecated TextView in favor of a
        // separate tui-cs/Editor package this project doesn't reference, and this report is
        // read-only text anyway -- same primitive TurnStartGreetingWindow's own short text already
        // uses. No scrolling if a report somehow runs past the screen's own height; every real
        // report this branch's fixture can produce fits well within a normal terminal's height.
        var report = EmpireStatusReport.For(empire, game);
        var body = new Label { X = 1, Y = 1, Text = BuildReport(empire, game, report) };
        Add(body);
        Add(new Label { X = 1, Y = Pos.AnchorEnd(1), Text = "Press any key to continue..." });

        KeyDown += (_, _) => App?.RequestStop();
    }

    private static string BuildReport(Empire empire, Game game, EmpireStatusReport report)
    {
        var age = game.Year - empire.FoundingYear + 1;
        var lines = new List<string> {
            $"{empire.Name} Empire Status Report      {game.Year}",
            "",
            $"In the year of our Lord {game.Year}, the {age}{DisplayText.OrdinalSuffix(age)} year of your reign, the empire of",
            $"{empire.Name} consists of {report.TotalWorlds} world{(report.TotalWorlds == 1 ? "" : "s")} " +
                $"with a total population of {report.TotalPopulation / 100.0:F1} billion.",
            $"The average industrial production is {report.AverageIndustry} and the average efficiency is {report.AverageEfficiency}%.",
            "",
        };

        if (report.ExtraTechnologies.Count == 0) {
            lines.Add($"None of the potential technologies of the {report.TechLevel} level have been");
            lines.Add("developed.");
        } else {
            lines.Add("The empire has mastered the following technologies:");
            lines.Add(WrapTechList(report.ExtraTechnologies));
        }

        lines.Add("");
        lines.Add("The military force of the empire consists of the following:");
        lines.Add("");
        foreach (var ship in Enum.GetValues<ShipType>()) {
            lines.Add($"{ShipThingName(ship),24}: {report.TotalShips[ship],6}");
        }

        return string.Join('\n', lines);
    }

    // DisplayMenu's own line-wrap (PROLOG.PAS:590-602): each "     Name" entry appended until the
    // running column would reach 77, then a fresh line -- not a generic word-wrap, this exact rule.
    private static string WrapTechList(IReadOnlyList<TechCatalog.TechGrantIdentity> techs)
    {
        var sb = new StringBuilder();
        var col = 1;
        foreach (var t in techs) {
            var entry = "     " + TechCatalog.DisplayName(t);
            if (col + entry.Length >= 77) {
                sb.Append('\n');
                col = 1;
            }

            sb.Append(entry);
            col += entry.Length;
        }

        return sb.ToString();
    }

    // ThingNames (DATACNST.PAS:100-112), fgt..trn only -- the plural/descriptive names this one
    // report uses, distinct from CloseUpWindow's own abbreviated column headers and from
    // TechCatalog.DisplayName's singular tech-catalog names.
    private static string ShipThingName(ShipType ship) => ship switch {
        ShipType.Fighter => "fighter squadrons",
        ShipType.HunterKiller => "hunter-killers",
        ShipType.Jumpship => "jumpships",
        ShipType.Jumptransport => "jumptransports",
        ShipType.Penetrator => "penetrators",
        ShipType.Starship => "starships",
        ShipType.Transport => "transports",
        _ => throw new ArgumentOutOfRangeException(nameof(ship)),
    };
}
