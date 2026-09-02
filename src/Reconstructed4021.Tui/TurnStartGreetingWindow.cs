using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core.Entities;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// PROLOG.PAS's SetUpPlayer/DisplayIntroScreen: the greeting shown at the start of each player's turn --
/// one of 3 lines (<c>CASE Rnd(1,3)</c>, passed in as <paramref name="greetingVariant"/> so a caller can
/// either randomize it per turn or cycle through all 3 for review), each addressing the player by a
/// title randomized independently via PRIMINTR.PAS's MyLord. Doesn't yet chain into the password prompt,
/// empire news, or status report that follow it in the original -- those are separate, unbuilt backlog
/// items (docs/TUI_SURFACES_MAPPING.md).
/// </summary>
internal sealed class TurnStartGreetingWindow : Window
{
    public TurnStartGreetingWindow(Empire player, int year, int greetingVariant)
    {
        Width = Dim.Fill();
        Height = Dim.Fill();

        // COLORS.INC's SYSDispWind -- the same window color PROLOG.PAS uses for this exact screen.
        // A single Scheme set on the Window is enough; the Labels below have none of their own, so they
        // inherit it.
        SetScheme(new Scheme(new TgAttribute(StandardColor.LightGray, StandardColor.Blue)));

        var lord = Honorifics.MyLord(player.IsEmpress);

        var greeting = greetingVariant switch {
            1 => $"Welcome, {lord}, I trust your sleep was peaceful and untroubled by\nthe events of the year.",
            2 => $"Welcome, {lord}, Your humble servant awaits your instructions.",
            _ => $"Greetings, {lord}, I hope your sleep was pleasant and peaceful.",
        };

        Add(new Label { X = 1, Y = 1, Text = $"{player.Name}: {year}" });
        Add(new Label { X = 1, Y = 3, Text = greeting });
        Add(new Label { X = 1, Y = Pos.AnchorEnd(2), Text = "Press any key to continue..." });

        KeyDown += (_, _) => App?.RequestStop();
    }
}
