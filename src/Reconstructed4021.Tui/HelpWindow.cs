using System.Collections.ObjectModel;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core.Entities;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// F1 Help (HLPWIND.PAS). Content is <see cref="HelpPages"/> (loaded from the embedded
/// Assets/help.kdl resource -- that file's own header comment has full provenance: the correction
/// made to real Pascal's own stale page 2, and the IndexPageNo-is-a-0-based-Seek-offset correction
/// behind <see cref="MinPageNumber"/> below).
///
/// PageUp/Down move a page at a time (HLPWIND.PAS's own <c>PageUp</c>/<c>PageDown</c> procedures are
/// named for which way <c>PageNo</c> counts, not which physical key triggers them -- the dispatch
/// table wires the physical PageUp key to the procedure that *decrements* PageNo and physical
/// PageDown to the one that *increments* it, so from a player's seat Up genuinely means "earlier page"
/// and Down "later page," this port's own natural mapping). End opens the topic index
/// (<c>CenterWCM</c>/<c>EndWCM</c> -&gt; <c>HelpIndex</c>), matching page 2's own documented hint.
///
/// Port-only addition, no Pascal equivalent: <c>/</c> opens a search prompt over
/// <see cref="HelpPages.Search"/> -- a linear substring scan is the whole feature, the content is
/// small enough that nothing fancier earns its keep.
/// </summary>
internal sealed class HelpWindow : Window
{
    private const int NoOfLines = 19; // HLPWIND.PAS's own HelpPage array bound.

    // PageDown's own guard (HLPWIND.PAS:204-214) floors PageNo at 1, so it can never reach 0 --
    // HelpPages.Pages[0] ("Index", a cursor-instructions blurb) is real decoded content but
    // unreachable through any actual navigation path in the original game; this window matches that.
    private const int MinPageNumber = 2;

    private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue);
    private static readonly TgAttribute BorderAttribute = new(StandardColor.LightGray, StandardColor.Black);
    private static readonly TgAttribute PickerNormalAttribute = new(StandardColor.LightGray, StandardColor.Black);
    private static readonly TgAttribute PickerSelectedAttribute = new(StandardColor.Black, StandardColor.LightGray);

    private readonly Label[] _lineLabels = new Label[NoOfLines];
    private readonly Label _hintLabel;
    private int _pageNumber = MinPageNumber; // InitializeHelpWindow's own PageNo:=1 -- Seek is 0-based, so this is the *2nd* physical record ("Help!"), the real first screen shown.

    public HelpWindow()
    {
        Title = "ANACREON: Help";
        Width = 84;
        Height = NoOfLines + 3; // +2 border, +1 hint line.
        X = Pos.Center();
        Y = Pos.Center();
        BorderStyle = LineStyle.Single;
        CanFocus = true;
        SetScheme(new Scheme(DispWindAttribute));
        Border.View?.SetScheme(new Scheme(BorderAttribute));

        for (var i = 0; i < NoOfLines; i++) {
            _lineLabels[i] = new Label { X = 0, Y = i, Text = string.Empty };
            Add(_lineLabels[i]);
        }

        _hintLabel = new Label { X = 0, Y = NoOfLines + 1, Text = "PgUp/PgDn: page   End: index   /: search   Esc: close" };
        _hintLabel.SetScheme(new Scheme(BorderAttribute));
        Add(_hintLabel);

        DisplayPage();

        KeyDown += (_, key) => {
            switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
                case KeyCode.PageUp: TurnPage(-1); key.Handled = true; break;
                case KeyCode.PageDown: TurnPage(1); key.Handled = true; break;
                case KeyCode.End: OpenIndex(); key.Handled = true; break;
                case (KeyCode)'/': OpenSearch(); key.Handled = true; break;
            }
        };
    }

    // PageUp (HLPWIND.PAS:192-202) -- guards against FileSize(HelpFile)-1, a Pascal off-by-one this
    // port sidesteps by just clamping against the real page count directly.
    private void TurnPage(int delta)
    {
        var target = Math.Clamp(_pageNumber + delta, MinPageNumber, HelpPages.Pages.Count);
        if (target == _pageNumber) {
            return;
        }

        _pageNumber = target;
        DisplayPage();
    }

    private void GoToPage(int pageNumber)
    {
        _pageNumber = Math.Clamp(pageNumber, MinPageNumber, HelpPages.Pages.Count);
        DisplayPage();
    }

    private void DisplayPage()
    {
        var lines = HelpPages.Pages[_pageNumber - 1];
        for (var i = 0; i < NoOfLines; i++) {
            _lineLabels[i].Text = i < lines.Count ? lines[i] : string.Empty;
        }
    }

    // GetIndexEntry (HLPWIND.PAS:126-163) -- real Pascal's own 15-line menu, transcribed verbatim
    // (HelpPages.Index), including its own dotted-leader page numbers and the two topic pairs that
    // share a page.
    private void OpenIndex()
    {
        var picker = new Window {
            Title = "Index",
            X = Pos.Center(), Y = Pos.Center(),
            Width = 45, Height = 19,
            BorderStyle = LineStyle.Single,
            CanFocus = true,
        };
        picker.SetScheme(new Scheme(PickerNormalAttribute));
        picker.Border.View?.SetScheme(new Scheme(BorderAttribute));

        var listView = new ListView<string> { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        listView.SetScheme(new Scheme { Normal = PickerNormalAttribute, Focus = PickerSelectedAttribute });
        listView.SetSource(new ObservableCollection<string>(HelpPages.Index.Select(e => e.Label)));
        listView.Index = 0;
        picker.Add(listView);

        var dismiss = AddModalChild(picker);
        picker.KeyDown += (_, key) => {
            switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
                case KeyCode.Enter when listView.Index is { } index:
                    var chosen = HelpPages.Index[index];
                    dismiss();
                    GoToPage(chosen.PageNumber);
                    key.Handled = true;
                    break;
                case KeyCode.Esc:
                    dismiss();
                    key.Handled = true;
                    break;
            }
        };
    }

    // Port-only addition -- see this class's own doc comment.
    private void OpenSearch()
    {
        var dialog = new Window {
            Title = "Search Help",
            X = Pos.Center(), Y = Pos.Center(),
            Width = 60, Height = 5,
            BorderStyle = LineStyle.Single,
            CanFocus = true,
        };
        dialog.SetScheme(new Scheme(PickerNormalAttribute));
        dialog.Border.View?.SetScheme(new Scheme(BorderAttribute));

        var queryField = new TextField { X = 1, Y = 1, Width = Dim.Fill(1) };
        dialog.Add(new Label { X = 1, Y = 0, Text = "Search for:" });
        dialog.Add(queryField);
        dialog.Add(new Label { X = 1, Y = Pos.AnchorEnd(1), Text = "Enter: search   Esc: cancel" });

        var dismissDialog = AddModalChild(dialog);
        queryField.SetFocus();

        queryField.KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode != KeyCode.Enter) {
                return;
            }

            var query = queryField.Text?.Trim() ?? "";
            dismissDialog();
            ShowSearchResults(query);
            key.Handled = true;
        };
        dialog.KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode != KeyCode.Esc) {
                return;
            }

            dismissDialog();
            key.Handled = true;
        };
    }

    private void ShowSearchResults(string query)
    {
        var results = HelpPages.Search(query).ToList();

        var picker = new Window {
            Title = results.Count > 0 ? $"Search: \"{query}\" ({results.Count})" : $"Search: \"{query}\" -- no matches",
            X = Pos.Center(), Y = Pos.Center(),
            Width = 70, Height = 15,
            BorderStyle = LineStyle.Single,
            CanFocus = true,
        };
        picker.SetScheme(new Scheme(PickerNormalAttribute));
        picker.Border.View?.SetScheme(new Scheme(BorderAttribute));

        var listView = new ListView<string> { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        listView.SetScheme(new Scheme { Normal = PickerNormalAttribute, Focus = PickerSelectedAttribute });
        listView.SetSource(new ObservableCollection<string>(results.Select(r => $"p{r.PageNumber,-3} {r.Line}")));
        if (results.Count > 0) {
            listView.Index = 0;
        }
        picker.Add(listView);

        var dismiss = AddModalChild(picker);
        picker.KeyDown += (_, key) => {
            switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
                case KeyCode.Enter when listView.Index is { } index && index < results.Count:
                    var chosen = results[index];
                    dismiss();
                    GoToPage(chosen.PageNumber);
                    key.Handled = true;
                    break;
                case KeyCode.Esc:
                    dismiss();
                    key.Handled = true;
                    break;
            }
        };
    }

    // A small popup layered on top of this already-open Window, not GameShell's own shell-level
    // AddModal (which manages children of the permanent Toplevel) -- Index/Search are transient
    // sub-screens of Help itself, not separate F-key panels.
    private Action AddModalChild(Window child)
    {
        Add(child);
        child.SetFocus();
        return () => {
            Remove(child);
            SetFocus();
        };
    }
}
