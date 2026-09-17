using System.Text;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Presentation;
using Reconstructed4021.Core.Types;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Tui2.Screens;


namespace Reconstructed4021.Tui2.NewGame;


// ANACREON.PAS's own main loop, ported from Reconstructed4021.Tui's Program.cs (RunGame): play the
// current empire's turn, human or NPE, one after another, wrapping back to the first when it runs
// out. A human empire gets its own Turn Start Greeting + Empire Status Report (PROLOG.PAS's
// SetUpPlayer, "once per player, per turn") plus its own interactive GalaxyMapScreen; anything else
// (an NPE, or a human quietly finishing off via PendingElimination/Eliminated) advances with no
// screen shown at all -- tui1's own doc comment on this loop already makes the same call.
//
// Entered from two places: NewGameFlow.CompleteSetup (a fresh game's very first turn -- a scenario's
// starting empire could in principle be an NPE that has to be silently advanced through first, exactly
// like any other iteration) and GalaxyMapScreen's own End Turn handler. Loading a save skips this
// entirely and goes straight to a GalaxyMapScreen (Bootstrap.CreateGalaxyMapScreen,
// SaveGamePickerScreen): a save only ever captures a human mid-turn (the one time Save Game is
// reachable), so BeginTurn and that turn's own greeting/status report already ran before the save was
// made.
//
// Deliberately still out of scope, same as tui1's own doc comment on this loop: the password prompt.
internal static class TurnLoop
{
    public static IScreen Start(Game game, NewGameContext context)
    {
        while (true)
        {
            var current = game.CurrentEmpire ?? throw new InvalidOperationException("Game.CurrentEmpire must be set before the first turn.");
            var handler = game.TurnHandlers[current];

            if (!handler.IsHuman || current.Status != EmpireStatus.Active)
            {
                // A human parked at PendingElimination reaches this branch too (Status<>Active) --
                // AdvanceOneTurn is where TurnEngine.BeginTurn actually finishes that empire's own
                // teardown (CombatOutcome.DestroyEmpire), so the transition to Eliminated has to be
                // detected around this exact call, not before it.
                var wasPendingElimination = handler.IsHuman && current.Status == EmpireStatus.PendingElimination;
                context.TurnEngine.AdvanceOneTurn(game);

                if (wasPendingElimination)
                {
                    return new TurnMessageScreen("Capital Fallen", CapitalFallenText(current), () => Start(game, context));
                }
            }
            else
            {
                // BeginTurn (fog-of-war refresh, matching ANACREON.PAS's SetUpTurn) runs here, before
                // the human sees anything -- not inside GalaxyMapScreen's own End Turn, which would show
                // them a map still reflecting the end of their *previous* turn.
                context.TurnEngine.BeginTurn(game);

                var greeting = GreetingText(current, context.Random.Next(1, 4));
                var status = StatusText(current, game);
                return new TurnMessageScreen($"{current.Name}: {game.Year}", greeting, () =>
                    new TurnMessageScreen($"{current.Name} Empire Status Report", status, () =>
                        new GalaxyMapScreen(game, current, context)));
            }

            if (!game.AnyHumanPlayersRemain)
            {
                return new TurnMessageScreen("Defeat", "Your empire has fallen.", context.MakeTitleScreen);
            }

            var npeEmpires = game.Empires.Where(e => e.NpeType is not null).ToList();
            if (npeEmpires.Count > 0 && npeEmpires.All(e => e.Status == EmpireStatus.Eliminated))
            {
                return new TurnMessageScreen("Victory", "Every enemy empire has been destroyed.", context.MakeTitleScreen);
            }
        }
    }

    // PROLOG.PAS's SetUpPlayer greeting (CASE Rnd(1,3)) -- MyLord (PRIMINTR.PAS) is randomized
    // independently of which greeting variant gets picked.
    private static string GreetingText(Empire player, int variant)
    {
        var lord = Honorifics.MyLord(player.IsEmpress);
        return variant switch
        {
            1 => $"Welcome, {lord}, I trust your sleep was peaceful and untroubled by\nthe events of the year.",
            2 => $"Welcome, {lord}, Your humble servant awaits your instructions.",
            _ => $"Greetings, {lord}, I hope your sleep was pleasant and peaceful.",
        };
    }

    // PROLOG.PAS's EmpireStatus (:490-615), ported verbatim from Reconstructed4021.Tui's own
    // EmpireStatusWindow.BuildReport -- EmpireStatusReport (Core) does the actual aggregation, this
    // only formats it.
    private static string StatusText(Empire empire, Game game)
    {
        var report = EmpireStatusReport.For(empire, game);
        var age = game.Year - empire.FoundingYear + 1;
        var lines = new List<string>
        {
            $"In the year of our Lord {game.Year}, the {age}{DisplayText.OrdinalSuffix(age)} year of your reign, the empire of",
            $"{empire.Name} consists of {report.TotalWorlds} world{(report.TotalWorlds == 1 ? "" : "s")} " +
                $"with a total population of {report.TotalPopulation / 100.0:F1} billion.",
            $"The average industrial production is {report.AverageIndustry} and the average efficiency is {report.AverageEfficiency}%.",
            "",
        };

        if (report.ExtraTechnologies.Count == 0)
        {
            lines.Add($"None of the potential technologies of the {report.TechLevel} level have been");
            lines.Add("developed.");
        }
        else
        {
            lines.Add("The empire has mastered the following technologies:");
            lines.Add(WrapTechList(report.ExtraTechnologies));
        }

        lines.Add("");
        lines.Add("The military force of the empire consists of the following:");
        lines.Add("");
        foreach (var ship in Enum.GetValues<ShipType>())
        {
            lines.Add($"{new ResourceKind.Ship(ship).DisplayName,24}: {report.TotalShips[ship],6}");
        }

        return string.Join('\n', lines);
    }

    // DisplayMenu's own line-wrap (PROLOG.PAS:590-602): each "     Name" entry appended until the
    // running column would reach 77, then a fresh line -- not a generic word-wrap, this exact rule.
    private static string WrapTechList(IReadOnlyList<TechCatalog.TechGrantIdentity> techs)
    {
        var sb = new StringBuilder();
        var col = 1;
        foreach (var t in techs)
        {
            var entry = "     " + TechCatalog.DisplayName(t);
            if (col + entry.Length >= 77)
            {
                sb.Append('\n');
                col = 1;
            }

            sb.Append(entry);
            col += entry.Length;
        }

        return sb.ToString();
    }

    // EmpireNews (PROLOG.PAS:456-488), the "Capital Fallen Report" -- transcribed verbatim from
    // Reconstructed4021.Tui's own Program.cs.
    private static string CapitalFallenText(Empire defeated)
    {
        var lord = Honorifics.MyLord(defeated.IsEmpress);
        return
            $"{lord},\n\n" +
            "I regret that I must communicate the dreadful news in this impersonal way,\n" +
            "but by the time you read this I will most likely be either dead or\n" +
            "imprisoned.  While you slept peacefully, our capital was attacked by the\n" +
            $"{defeated.DefeatedBy!.Name} Empire.  Though our men and women fought bravely,\n" +
            "the strength of our adversary overwhelmed us and we were forced to surrender.\n\n" +
            "I have arranged an honorable course of action for Your Majesty; you will find\n" +
            $"necessary materials by your bedside.  Good luck, {lord}.\n\n" +
            "- Your Loyal Servant";
    }
}
