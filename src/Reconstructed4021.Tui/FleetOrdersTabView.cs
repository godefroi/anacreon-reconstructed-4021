using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Editor;
using Terminal.Gui.Editor.Rendering;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Turns;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// The "Orders" tab inside <see cref="CloseUpWindow"/>, only present for one of the player's own
/// fleets -- FLTCOMM.PAS's own <c>FleetOrdersCommand</c> (:843-915) mini scripting language
/// (<see cref="FleetOrderCompiler"/>), editable in place, superseding the old standalone Fleet menu >
/// Orders window (<c>FleetOrdersWindow</c>, retired).
///
/// Esc attempts to commit (that same procedure's own <c>REPEAT...UNTIL Ok OR Abort</c>, :877-888): a
/// successful compile applies the result to the fleet and raises <see cref="Committed"/> with a
/// completion message so <see cref="CloseUpWindow"/> can close; a failed one shows the real compile
/// error via a blocking Yes/No confirm -- Yes discards every edit and raises <see cref="Committed"/>
/// with <c>null</c> (close without touching the fleet's existing orders at all), No/Esc returns focus
/// to the editor.
///
/// The highlighted line is this port's own "next order" marker (no Pascal equivalent -- see
/// <see cref="FleetOrderCompiler.Compile"/>'s own <c>markedLine</c> doc comment for why real Pascal's
/// blind index-preservation isn't imitated here). Ctrl+N moves it to the cursor's current line; it
/// starts on whatever line matches <see cref="Fleet.NextOrder"/> when this tab is built
/// (<see cref="FleetOrderCompiler.Decompile"/> emits exactly one line per order, so that mapping is
/// exact before any edits).
/// </summary>
internal sealed class FleetOrdersTabView : View
{
    // COLORS.INC's ColorScrColor: SYSDispWind = 23 (LightGray on Blue) -- same content-area scheme
    // CloseUpContentView uses, for visual consistency between the two tabs (this one previously had
    // none at all, same gap the old standalone FleetOrdersWindow already had).
    private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue);

    private readonly Game game;
    private readonly Fleet fleet;
    private readonly Empire viewer;
    private readonly OrderEditor editor;
    private readonly NextOrderLineHighlighter highlighter;

    /// <summary>Fires exactly once, when this tab is ready for the whole <see cref="CloseUpWindow"/> to close. A non-null message is the real completion/cancellation report; null means the compile was aborted and nothing changed.</summary>
    public event EventHandler<string?>? Committed;

    public FleetOrdersTabView(Fleet fleet, Game game, Empire viewer)
    {
        this.fleet = fleet;
        this.game = game;
        this.viewer = viewer;

        Width = Dim.Fill();
        Height = Dim.Fill();
        // Must be true, not false: View.SetHasFocusTrue bails out immediately whenever a focusable
        // child's own SuperView.CanFocus is false (confirmed by decompiling it) -- a non-focusable
        // container doesn't transparently delegate to a focusable child, it *blocks* it from ever
        // receiving focus at all. True here lets Terminal.Gui's own SetHasFocusTrue/AdvanceFocus
        // descend into the embedded editor automatically, same as every other tab view in this
        // codebase (WorldCloseUpTabView, IsspEditor, ...) -- no special-case focus routing needed.
        CanFocus = true;
        SetScheme(new Scheme(DispWindAttribute));

        var name = fleet.Names.GetValueOrDefault(viewer) ?? CloseUpWindow.DescribeLocation(fleet, viewer);
        Add(new Label { X = 0, Y = 0, Text = $"Orders: {name}" });

        var lines = FleetOrderCompiler.Decompile(viewer, fleet.Orders);
        highlighter = new NextOrderLineHighlighter {
            MarkedLineNumber = fleet.NextOrder > 0 ? fleet.NextOrder : lines.Count > 0 ? 1 : 0,
        };

        editor = new OrderEditor {
            X = 0, Y = 1, Width = Dim.Fill(), Height = Dim.Fill(3),
            GutterOptions = GutterOptions.None,
            Text = string.Join('\n', lines),
        };
        editor.LineTransformers.Add(highlighter);
        editor.MarkAsNext = () => {
            if (editor.Document is null) {
                return;
            }
            highlighter.MarkedLineNumber = editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber;
            editor.SetNeedsDraw();
        };
        editor.EscPressed = TryCommit;
        Add(editor);

        Add(new Label { X = 0, Y = Pos.AnchorEnd(2), Text = "DESTination <name/x,y>  TRANsfer <amt> <code>  REPEat  WAIT  REFUel  JOIN [OVER]" });
        Add(new Label { X = 0, Y = Pos.AnchorEnd(1), Text = "Ctrl+N: mark as next order   Esc: compile and close" });
    }

    private void TryCommit()
    {
        var lines = editor.Text.Split('\n');
        var result = FleetOrderCompiler.Compile(game, fleet.Owner, lines, highlighter.MarkedLineNumber);

        if (result.ErrorMessage is not null) {
            var choice = DosDialogWindow.Confirm(App!, "Orders",
                $"{result.ErrorMessage} {result.ErrorLine}.\n\nDiscard changes and close?");
            if (choice == 0) {
                Committed?.Invoke(this, null);
            } else {
                editor.SetFocus();
            }
            return;
        }

        var fleetName = CloseUpWindow.DescribeLocation(fleet, viewer);
        var myLord = Honorifics.MyLord(fleet.Owner.IsEmpress);

        FleetMovementHandler.CommitOrders(fleet, result.Orders, result.MarkedOrderIndex, game);
        var message = result.Orders.Count == 0
            ? $"All orders to {fleetName} cancelled, {myLord}."
            : $"Orders to {fleetName} completed, {myLord}.";

        Committed?.Invoke(this, message);
    }

    /// <summary>
    /// Overrides Esc via Terminal.Gui's own Command-dispatch path rather than a plain C#
    /// <c>KeyDown</c> subscription -- <see cref="View.AddCommand"/> is <c>protected</c>, so doing this
    /// needs a thin subclass rather than reaching in from outside (same technique the old
    /// <c>FleetOrdersWindow.OrderEditor</c> proved live via TuiDriver). <see cref="Command.Accept"/>
    /// and <see cref="Command.Toggle"/> are both confirmed (by decompiling
    /// <c>Editor.CreateCommandsAndBindings</c>) to be outside the ~54 commands Editor's own
    /// constructor already binds -- free slots, not overriding a real editing command. The
    /// <c>KeyBindings.Remove</c> calls are defensive regardless (idempotent even if nothing was
    /// bound), same precaution already taken for Esc.
    /// </summary>
    private sealed class OrderEditor : Editor
    {
        public Action? EscPressed { get; set; }
        public Action? MarkAsNext { get; set; }

        public OrderEditor()
        {
            KeyBindings.Remove(Key.Esc);
            AddCommand(Command.Accept, () => { EscPressed?.Invoke(); return true; });
            KeyBindings.Add(Key.Esc, Command.Accept);

            KeyBindings.Remove(Key.N.WithCtrl);
            AddCommand(Command.Toggle, () => { MarkAsNext?.Invoke(); return true; });
            KeyBindings.Add(Key.N.WithCtrl, Command.Toggle);

            // Defensively unbound (whether or not anything claimed them by default) so they always
            // bubble up to CloseUpWindow's own Ctrl+PageUp/PageDown tab-switch handler instead of
            // being silently consumed here.
            KeyBindings.Remove(Key.PageUp.WithCtrl);
            KeyBindings.Remove(Key.PageDown.WithCtrl);
        }
    }

    /// <summary>Highlights one line (the "next order" marker) via <see cref="CellVisualLineElement.Attribute"/> -- presentation only, never literal text in the buffer (see <see cref="FleetOrderCompiler.Compile"/>'s own <c>markedLine</c> doc comment for why).</summary>
    private sealed class NextOrderLineHighlighter : IVisualLineTransformer
    {
        private static readonly TgAttribute HighlightAttribute = new(StandardColor.Black, StandardColor.BrightYellow);

        public int MarkedLineNumber { get; set; }

        public void Transform(CellVisualLine line)
        {
            if (line.DocumentLine.LineNumber != MarkedLineNumber) {
                return;
            }

            foreach (var element in line.Elements) {
                element.Attribute = HighlightAttribute;
            }
        }
    }
}
