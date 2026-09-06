using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Editor;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;

namespace Reconstructed4021.Tui;

/// <summary>
/// Fleet menu > Orders (`FLTCOMM.PAS: FleetOrdersCommand`) -- the real `ORDERS.PAS` mini scripting
/// language, compiled/decompiled by <see cref="FleetOrderCompiler"/> (Core). Hosts one
/// <see cref="Editor"/> child instead of hand-rolling a multi-line text widget -- Terminal.Gui's own
/// `TextView` is deprecated in this version with no in-house replacement (see the `Terminal.Gui.Editor`
/// `PackageReference` in the `.csproj` for why this specific package). Plain text only: no
/// `GutterOptions`/`HighlightingDefinition`, this isn't source code.
///
/// Esc attempts to commit (`CompileOrders`' own `REPEAT...UNTIL Ok OR Abort`, `FLTCOMM.PAS:877-888`):
/// a successful compile applies the result to the fleet and closes; a failed one shows the real
/// compile error via a blocking Yes/No confirm (`FLTCOMM.PAS:829`'s own "Press &lt;Esc&gt; to abort
/// command", translated to this port's own Yes/No/Esc convention) -- Yes discards every edit and
/// closes without touching the fleet's existing orders at all, matching Pascal's own abort branch
/// (only the local scratch `Code` gets disposed, `SetFleetCode` is never called, so there's no
/// completion message either); No or Esc returns focus to the editor to keep working on it.
/// </summary>
internal sealed class FleetOrdersWindow : Window
{
    private readonly Game game;
    private readonly Fleet fleet;
    private readonly OrderEditor editor;

    /// <summary>
    /// Fires exactly once, when this window is ready to close. A non-null message is the real
    /// completion/cancellation report (`WriteCommLine`'s own text) for the caller to show via
    /// `ShowInfo`; null means the compile was aborted and nothing changed.
    /// </summary>
    public event EventHandler<string?>? Closed;

    public FleetOrdersWindow(Fleet fleet, Game game)
    {
        this.fleet = fleet;
        this.game = game;

        Width = 80;
        Height = 21;
        X = Pos.Center();
        Y = Pos.Center();
        Title = "Anacreon: Orders";
        BorderStyle = LineStyle.Single;
        CanFocus = true;

        editor = new OrderEditor {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            GutterOptions = GutterOptions.None,
        };
        editor.Text = string.Join('\n', FleetOrderCompiler.Decompile(fleet.Owner, fleet.Orders));
        editor.EscPressed = TryCommit;
        Add(editor);

        Add(new Label {
            X = 0, Y = Pos.AnchorEnd(1),
            Text = "DESTination <name/x,y>  TRANsfer <amt> <code>  REPEat  WAIT   |   Esc: compile and close",
        });

        Initialized += (_, _) => editor.SetFocus();
    }

    private void TryCommit()
    {
        var lines = editor.Text.Split('\n');
        var result = FleetOrderCompiler.Compile(game, fleet.Owner, lines);

        if (result.ErrorMessage is not null) {
            var choice = DosDialogWindow.Confirm(App!, "Fleet Orders",
                $"{result.ErrorMessage} {result.ErrorLine}.\n\nDiscard changes and close?");
            if (choice == 0) {
                Closed?.Invoke(this, null);
            } else {
                editor.SetFocus();
            }
            return;
        }

        var fleetName = CloseUpWindow.DescribeLocation(fleet, fleet.Owner);
        var myLord = Honorifics.MyLord(fleet.Owner.IsEmpress);
        string message;

        if (result.Orders.Count == 0) {
            fleet.Orders.Clear();
            fleet.NextOrder = 0;
            message = $"All orders to {fleetName} cancelled, {myLord}.";
        } else {
            fleet.Orders.Clear();
            fleet.Orders.AddRange(result.Orders);
            fleet.NextOrder = 1;
            message = $"Orders to {fleetName} completed, {myLord}.";
        }

        Closed?.Invoke(this, message);
    }

    /// <summary>
    /// Overrides whatever Esc is bound to by default via Terminal.Gui's own Command-dispatch path
    /// (the same mechanism Editor's own <c>CreateCommandsAndBindings</c> uses) rather than a plain C#
    /// <c>KeyDown</c> subscription -- <see cref="View.AddCommand"/> is <c>protected</c>, so doing this
    /// needs a thin subclass rather than reaching in from outside. Confirmed working end-to-end live
    /// via TuiDriver (compile success, compile-error keep-editing, and discard-and-close all verified).
    /// <see cref="Command.Accept"/> (ordinal 1) isn't one of the ~50 commands Editor's own constructor
    /// already binds.
    /// </summary>
    private sealed class OrderEditor : Editor
    {
        public Action? EscPressed { get; set; }

        public OrderEditor()
        {
            KeyBindings.Remove(Key.Esc);
            AddCommand(Command.Accept, () => { EscPressed?.Invoke(); return true; });
            KeyBindings.Add(Key.Esc, Command.Accept);
        }
    }
}
