using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Reconstructed4021.Tui;

/// <summary>
/// Shared "swap one content view in and out of a plain bordered Window" tab mechanism -- originally
/// built inline for <see cref="WorldInfoWindow"/> (Close Up/Production/ISSP/Designate/Redirect), now
/// extracted so <see cref="CloseUpWindow"/> (Close Up/Orders on one of the player's own fleets) reuses
/// the identical behavior instead of its own separate, simpler toggle. No Pascal-fidelity reason left
/// to keep them different -- both are already TUI-only conveniences with no real Pascal screen to
/// match, see each window's own doc comment.
///
/// Not built on Terminal.Gui's own Tabs control: its tab-strip chrome adds five lines around the
/// content (border, tab line, a line above and below the tabs), its own arrow-key navigation
/// intercepts Left/Right before a tab's own content (ISSP's stepper) reliably gets real keyboard
/// focus -- confirmed live, not assumed -- and its header rendering has no public customization
/// surface (every drawing-related member on Tabs is private). Swapping a single content view in and
/// out sidesteps all three: ordinary Window chrome is two lines total, only one content view is ever a
/// focusable child so SetFocus is unambiguous, and the tab names live in the Title text instead of a
/// framework-drawn strip -- Window.Title has no per-run styling (TextFormatter's only styling hook is
/// the single-character HotKeySpecifier), so the active tab is marked with brackets rather than a
/// color -- and switch on Ctrl+PageUp/Ctrl+PageDown, a chord none of this port's tab content ever reads
/// (every child view that also uses Up/Down/PageUp/PageDown for its own row navigation -- IsspEditor,
/// RedirectTabView -- explicitly ignores any Ctrl-chord first, letting this bubble up here).
///
/// Every tab view must have <c>CanFocus = true</c> on itself, even one that wraps a nested focusable
/// child (an embedded <c>Editor</c>, e.g. <see cref="FleetOrdersTabView"/>) -- confirmed by decompiling
/// <c>View.SetHasFocusTrue</c>: it bails out immediately whenever a focusable view's own
/// <c>SuperView.CanFocus</c> is false, so a non-focusable *container* doesn't transparently delegate
/// focus to a focusable child, it *blocks* that child from ever receiving focus at all.
/// <see cref="FocusCurrentTab"/>/<see cref="Step"/> both just call the plain <see cref="View.SetFocus"/>
/// on whichever tab is current -- Terminal.Gui's own <c>SetHasFocusTrue</c>/<c>AdvanceFocus</c> already
/// descend into a focusable container's own focusable child automatically from there, no special-cased
/// per-tab focus routing needed.
/// </summary>
internal sealed class TabbedWindow
{
    /// <param name="OnActivated">Runs every time this tab becomes the visible one, including the
    /// first time -- e.g. ProductionWindow's own Refresh(), stale otherwise since a sibling tab's own
    /// edit (ISSP's dial) never re-runs its preview on its own.</param>
    private readonly record struct Tab(string Name, View View, Action? OnActivated);

    private readonly Window window;
    private string namePrefix;
    private readonly List<Tab> tabs = [];
    private int currentIndex;

    public TabbedWindow(Window window, string namePrefix)
    {
        this.window = window;
        this.namePrefix = namePrefix;

        window.KeyDown += (_, key) => {
            if (!key.IsCtrl) {
                return;
            }

            switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
                case KeyCode.PageUp:
                    Step(-1);
                    key.Handled = true;
                    break;
                case KeyCode.PageDown:
                    Step(1);
                    key.Handled = true;
                    break;
            }
        };
    }

    public void AddTab(string name, View view, Action? onActivated = null) => tabs.Add(new Tab(name, view, onActivated));

    /// <summary>Shows a tab by name (falling back to the first one added if <paramref name="initialTabName"/> is omitted or unknown), adds it to the window, and updates the title. Call once, after every <see cref="AddTab"/>.</summary>
    public void Show(string? initialTabName = null)
    {
        currentIndex = Math.Max(0, tabs.FindIndex(t => t.Name == initialTabName));
        window.Add(tabs[currentIndex].View);
        tabs[currentIndex].OnActivated?.Invoke();
        UpdateTitle();
    }

    /// <summary>
    /// Focuses the current tab's own content -- call from the window's own <c>Initialized</c> event,
    /// matching every other content-view-needs-real-focus precedent in this codebase (a view added
    /// mid-construction can't reliably take focus before the window is actually running).
    /// </summary>
    public void FocusCurrentTab() => tabs[currentIndex].View.SetFocus();

    public bool IsShowing(string name) => tabs[currentIndex].Name == name;

    /// <summary>Updates the name prefix in the title (e.g. after a rename) without rebuilding the window -- <see cref="namePrefix"/> is otherwise fixed at construction.</summary>
    public void Rename(string newPrefix)
    {
        namePrefix = newPrefix;
        UpdateTitle();
    }

    private void Step(int direction)
    {
        window.Remove(tabs[currentIndex].View);
        currentIndex = (currentIndex + direction + tabs.Count) % tabs.Count;
        window.Add(tabs[currentIndex].View);
        tabs[currentIndex].View.SetFocus();
        tabs[currentIndex].OnActivated?.Invoke();
        UpdateTitle();
    }

    private void UpdateTitle() =>
        window.Title = $"{namePrefix}  " + string.Join("  ", tabs.Select((t, i) => i == currentIndex ? $"[{t.Name}]" : t.Name));
}
