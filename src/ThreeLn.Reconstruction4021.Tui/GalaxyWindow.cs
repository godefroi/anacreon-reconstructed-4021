using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using ThreeLn.Reconstruction4021.Core;

namespace ThreeLn.Reconstruction4021.Tui;

internal sealed class GalaxyWindow : Window
{
    public GalaxyWindow(Game game)
    {
        var baseTitle = $"Anacreon -- {game.Galaxy.Size}x{game.Galaxy.Size} galaxy (arrows/PgUp/PgDn/Home/End move cursor, Esc quits)";
        Title = baseTitle;
        Width = Dim.Fill();
        Height = Dim.Fill();

        // GAUNTLET.SCN's file order puts CreatePlayerEmpire before every CreateNPEmpire, so the human
        // player is Empires[0] -- matches every dos_131 scenario's authoring convention (checked across
        // the committed set), not something the loader guarantees structurally.
        var galaxyView = new GalaxyView(game.Galaxy, game.Empires[0]) {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };

        // Temporary diagnostics while chasing render performance and the arrow-key report: last draw
        // duration, and the exact key GalaxyView's KeyDown saw (name + raw KeyCode) for every keypress,
        // whether or not it moved the cursor. Remove both once resolved.
        var lastFrameMs = "?";
        var lastKey = "(none yet)";

        void UpdateTitle() => Title = $"{baseTitle} | last draw: {lastFrameMs}ms | last key: {lastKey}";

        galaxyView.FrameRendered += (_, elapsed) => {
            lastFrameMs = elapsed.TotalMilliseconds.ToString("F1");
            UpdateTitle();
        };

        galaxyView.KeyReceived += (_, key) => {
            lastKey = $"{key} (code={(int)key.KeyCode})";
            UpdateTitle();
        };

        Add(galaxyView);

        // CanFocus=true alone doesn't make a view the initially focused one -- without this, arrow-key
        // presses never reach GalaxyView's own KeyDown at all (only Esc, bound here on the Window, ever
        // fired).
        galaxyView.SetFocus();

        KeyDown += (_, key) => {
            if (key.KeyCode != KeyCode.Esc) {
                return;
            }

            App?.RequestStop();
            key.Handled = true;
        };
    }
}
