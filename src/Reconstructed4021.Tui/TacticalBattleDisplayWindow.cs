using System.Collections.ObjectModel;
using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// Engage (ATTCOMM.PAS:220-635) — the Tactical Battle Display: a fixed-size 80x28 window centered
/// over the map (same convention <see cref="CloseUpWindow"/> established — not full-terminal
/// <c>Dim.Fill()</c>, an earlier pass here got that wrong), round-by-round, over an
/// <see cref="InteractiveCombatState"/>.
///
/// Move/Retreat/Target/Group Status all happen *inside GroupWindow itself* (the persistent command
/// box), not as separate popups — confirmed by re-reading source directly: <c>GroupMove</c>
/// (ATTCOMM.PAS:352-461), <c>GroupRetreat</c> (:463-507), <c>GroupTarget</c> (:509-546), and
/// <c>GroupStatus</c> (:548-574) all open with <c>ActivateWindow(GroupWindow); ClrScr;</c> and then
/// <c>Writeln</c> their own prompts directly into that same window, accumulating lines without
/// clearing between steps within one flow (so earlier groups' answered prompts stay visible while
/// later ones are asked) until the interaction ends and <c>Menu</c>'s own <c>ClrScr</c> redraws the
/// standard command list. An earlier pass here used separate popup <c>Window</c>s for these — wrong,
/// fixed by giving <see cref="commandBoxContent"/> a swappable-content design instead: <see cref="activePrompt"/>
/// non-null means the next keypress routes to whatever sub-interaction is live instead of the
/// top-level E/M/T/R/G/D/A dispatch. <c>AttackDetails</c> (:576-607) is the one real exception — it
/// opens its own separate window over the *AttackWindow* area, not GroupWindow, so it's left as a
/// separate popup here too (not restyled to the same box-content mechanism as the others).
///
/// <c>DrawScreen</c>'s whole visual complex (<c>DrawGrid</c>/<c>DrawStars</c>/<c>DrawObject</c>/
/// <c>DrawEnemyShips</c>/<c>DrawGroupShips</c>) is reproduced in <see cref="BattleMapView"/>, using
/// real Pascal's own byte-offset constant tables (<c>OrbLoc</c>/<c>PlayerOffset</c>/<c>EnemyOffset</c>/
/// <c>Disp</c>/<c>Disp2</c>, ATTCOMM.PAS:46-65,1136-1142) decoded from DOS video-memory byte offsets
/// (<c>row*160+col*2</c>) to plain row/column deltas, plus <c>DrawObject</c>'s exact CP437 bytes
/// (extracted via a raw codepage-437 read of ATTCOMM.PAS — this project's own established caveat that
/// normal file reads corrupt those bytes). <c>WarpIn</c>'s own reveal animation (every group sliding
/// in from off-screen to DeepSpace when the screen first opens) is reproduced too, as a short
/// <c>Application.AddTimeout</c>-driven slide rather than the literal per-pixel Mem-poke choreography.
/// Not reproduced: the DOS video-memory poke mechanism itself (a rendering trick, not a game mechanic,
/// same precedent <see cref="ResourceDistributionEditor"/>'s own doc comment already establishes).
/// <c>DrawStars</c>' own 100 <c>Rnd(1,720)</c> draws and <c>DrawScreen</c>'s own opening
/// <c>Rnd(1,3)</c> flavor-line pick are still consumed from the same RNG stream even though the
/// result is purely decorative, for stream-order fidelity with real Pascal.
///
/// COLORS.INC's own <c>ColorScrColor</c> block, not <c>SYSDispWind</c> (which an earlier pass here
/// wrongly painted the whole window with): <c>AttackWind=15</c> (White/Black), <c>GroupWind=23</c>
/// (LightGray/Blue), <c>EnemyWind=4</c> (Red/Black), <c>SYSWBorder=7</c> (LightGray/Black) same as
/// every other window in this port.
///
/// One real, deliberate adaptation from source: <c>AttReport</c>'s non-empty calls block for a full
/// second (<c>Delay(1000)</c>) before the next screen update -- freezing the whole game loop is a
/// DOS-era artifact, not a mechanic worth reproducing, so this class shows the message immediately and
/// clears it on a one-shot <c>Application.AddTimeout</c> (the <see cref="TmaLogoWindow"/> idiom)
/// instead, without blocking input. Real Pascal's own <c>Menu</c> has no cancel key at all — the only
/// way out is <see cref="InteractiveCombatState.IsOver"/> becoming true, which is why this window is
/// added to <see cref="GameShell"/> via <c>AddModal(..., dismissOnOutsideClick: false)</c>. Port-only
/// addition: Esc at the top-level menu (<see cref="OnKeyDown"/>) routes into the same y/n Retreat
/// confirmation <c>&lt;R&gt;</c> already uses, rather than doing nothing — an unhandled Esc used to fall
/// through to Terminal.Gui's default quit behavior and unwind the whole <c>app.Run(gameShell)</c> call
/// instead of anything battle-related (issue #5).
/// </summary>
internal sealed class TacticalBattleDisplayWindow : Window
{
    private static readonly TgAttribute AttackWindAttribute = new(StandardColor.White, StandardColor.Black);
    private static readonly TgAttribute GroupWindAttribute = new(StandardColor.LightGray, StandardColor.Blue);
    private static readonly TgAttribute EnemyWindAttribute = new(DosColors.Red, StandardColor.Black);
    private static readonly TgAttribute BorderAttribute = new(StandardColor.LightGray, StandardColor.Black);
    // SYSDispSelect = 112 -- same real Pascal "selected row" constant GameShell's own pickers already use.
    private static readonly TgAttribute GroupSelectedAttribute = new(StandardColor.Black, StandardColor.LightGray);
    private static readonly ShellPosition[] _allShellPositions = Enum.GetValues<ShellPosition>();
    private static readonly AttackType[] _allAttackTypes = Enum.GetValues<AttackType>();

    // ATSymb (ATTCOMM.PAS:49-50): '-LDGIFHJTPSRMN' for AttackTypes NoRes..nnj -- GroupTarget's own
    // single-keypress selection, not a ListView (an earlier pass here built a picker; real Pascal
    // types one letter per group, no navigation).
    private static readonly (AttackType? Type, char Key)[] TargetChoices = [
        (null, '-'),
        (AttackType.Lam, 'L'), (AttackType.DefenseSatellite, 'D'), (AttackType.Gdm, 'G'), (AttackType.IonCannon, 'I'),
        (AttackType.Fighter, 'F'), (AttackType.HunterKiller, 'H'), (AttackType.Jumpship, 'J'),
        (AttackType.Jumptransport, 'T'), (AttackType.Penetrator, 'P'), (AttackType.Starship, 'S'),
        (AttackType.Transport, 'R'), (AttackType.Legion, 'M'), (AttackType.NinjaLegion, 'N'),
    ];

    private readonly InteractiveCombatState state;
    private readonly Random random;

    private readonly Label messageLabel;
    private readonly BattleMapView mapView;
    private readonly Label commandBoxContent;
    private readonly Label groupListHintLabel;
    private readonly ListView<GroupActionListItem> groupListView;
    private readonly Label enemyLabel;
    private readonly Label autoTargetLabel;
    private object? pendingMessageTimeout;

    // Non-null while a sub-interaction (Move/Retreat/Target/GroupStatus, all drawn into GroupWindow
    // itself) owns the next keypress instead of the top-level E/M/T/R/G/D/A dispatch.
    private Action<Key>? activePrompt;

    // Non-null only during Move/Target: groupListView's own KeyDown (wired once, below) just calls
    // through to whichever of these two flows currently owns it -- same reassign-on-entry idiom as
    // activePrompt itself, avoiding a fresh subscription (and the leak/reentrancy that would invite)
    // every time HandleMove/HandleTarget runs across a multi-round battle.
    private Action<Key>? groupListKeyHandler;

    /// <summary>Fired exactly once, when <see cref="InteractiveCombatState.IsOver"/> first becomes true. The caller applies the outcome (RestoreCombatant/ResolveAttack) and dismisses this window.</summary>
    public event EventHandler? BattleEnded;

    public TacticalBattleDisplayWindow(InteractiveCombatState state, Random random, string targetName)
    {
        this.state = state;
        this.random = random;

        // AttackWindow+GroupWindow+EnemyWindow together (ATTCOMM.PAS:1175-1177) fill the real DOS
        // screen edge to edge (80x25) -- reproduced here as a real fixed-size (80x28: 26 content rows
        // plus a real border, one row taller than the DOS original to leave the same small margin
        // CloseUpWindow's own 80x21 already leaves under its own 80x19 content) bordered `Window`
        // centered over the map.
        Title = "Tactical Battle Display";
        Width = 80;
        Height = 28;
        X = Pos.Center();
        Y = Pos.Center();
        BorderStyle = LineStyle.Single;
        CanFocus = true;
        SetScheme(new Scheme(AttackWindAttribute)); // the header/message lines sit in AttackWindow's own area
        Border.View?.SetScheme(new Scheme(BorderAttribute));

        AddAt(0, 0, $"Attacking {targetName}.");
        // Not real Pascal -- AutoTarget's own always-visible state readout (see InteractiveCombatState.
        // AutoTarget's doc comment); off by default, matching real Pascal's own fully-manual behavior.
        autoTargetLabel = AddAt(60, 0, string.Empty);
        messageLabel = AddAt(0, 1, string.Empty);

        // DrawGrid/DrawStars/DrawObject/DrawEnemyShips/DrawGroupShips, all together (see this class's
        // own doc comment) -- AttackWindow's real 80x13 footprint (ATTCOMM.PAS:1175).
        mapView = new BattleMapView(state, random) { X = 0, Y = 2, Width = 80, Height = 13 };
        Add(mapView);

        // GroupWindow (ATTCOMM.PAS:1176) -- the persistent, content-swapped command box, bordered same
        // as source (ThinBRD), positioned bottom-left same as source (col 1, row 14). One single
        // multi-line Label fills its whole interior (10 rows -- Height 12 minus 2 border) rather than
        // one Label per line, since its content is replaced wholesale by every sub-interaction.
        // CanFocus = true (an earlier pass here left this false, fine while everything inside was a
        // passive Label -- but Terminal.Gui never lets a descendant hold real focus while an ancestor
        // itself can't, so groupListView.SetFocus() below silently failed to do anything: every key,
        // including its own per-row letters, fell straight through to the top-level dispatch instead).
        var commandBox = new Window {
            X = 0, Y = 14, Width = 35, Height = 12,
            BorderStyle = LineStyle.Single, CanFocus = true,
        };
        commandBox.SetScheme(new Scheme(GroupWindAttribute));
        commandBox.Border.View?.SetScheme(new Scheme(BorderAttribute));
        commandBoxContent = new Label { X = 1, Y = 0, Width = Dim.Fill(1), Height = Dim.Fill() };
        commandBox.Add(commandBoxContent);

        // Port-only addition: Move/Target's own per-group prompts (HandleMove/HandleTarget) use these
        // two instead of accumulating text into commandBoxContent above -- a real ListView scrolls for
        // any number of groups with no fixed-row ceiling, and lets the player freely revisit/change an
        // earlier group's answer instead of only ever being asked once, strictly in order. Both hidden
        // outside those two flows; SetCommandBoxContent (used by every other flow) hides them again.
        // The hint stays its own Label (row 0) rather than sharing space with the group rows below it
        // (row 1 down) -- unlike every other flow's hint, which is just the growing Label's own first
        // line, Move/Target's hint needs to stay visible and pinned in place while the list beneath it
        // scrolls.
        groupListHintLabel = new Label { X = 1, Y = 0, Width = Dim.Fill(1), Visible = false };
        commandBox.Add(groupListHintLabel);
        groupListView = new ListView<GroupActionListItem> { X = 1, Y = 1, Width = Dim.Fill(1), Height = Dim.Fill(), Visible = false };
        groupListView.SetScheme(new Scheme { Normal = GroupWindAttribute, Focus = GroupSelectedAttribute });
        // Keeps the map's highlighted-group marker tracking whichever row is highlighted, including
        // free arrow-key navigation -- a real improvement over the old forced one-shot-per-step reveal.
        groupListView.ValueChanged += (_, _) => UpdateHighlightFromSelection();
        // Wired once here, dispatches to whichever flow (HandleMove/HandleTarget) currently owns
        // groupListKeyHandler -- same precedent as ShowObjectPicker's own fleet-action letters on
        // listView.KeyDown (GameShell.cs), which fires before ListView's own internal Up/Down/
        // type-ahead-search bindings, so arrow keys still reach the base ListView's own navigation.
        groupListView.KeyDown += (_, key) => groupListKeyHandler?.Invoke(key);
        commandBox.Add(groupListView);
        Add(commandBox);

        // EnemyWindow (ATTCOMM.PAS:1177) -- bottom-right, beside GroupWindow.
        enemyLabel = AddAt(36, 14, string.Empty);
        enemyLabel.SetScheme(new Scheme(EnemyWindAttribute));

        KeyDown += OnKeyDown;
        RefreshEnemyTable();
        RefreshAutoTargetLabel();
        ShowCommandMenu();

        // DrawScreen's own opening flavor line (ATTCOMM.PAS:1192-1196) and WarpIn's own reveal
        // animation (ATTCOMM.PAS:1198-1199) -- both deferred to Initialized, same reason as
        // TmaLogoWindow's own timer: App isn't assigned yet during construction -- this window is only
        // ever added as a child via GameShell's AddModal (called right after this constructor
        // returns), never run directly via Application.Run, so App!.AddTimeout would null-ref if
        // called here instead (App walks the SuperView chain, and there's no SuperView yet at
        // construction time). AddAt's own BeginInit/EndInit call for a view added to an
        // already-initialized parent fires this synchronously during that Add() call, so the relative
        // RNG-consumption order versus everything else (DrawStars' own draws inside BattleMapView's
        // constructor above) is unchanged.
        Initialized += (_, _) => {
            mapView.StartWarpIn(App!);
            var flavor = PascalMath.Rnd(random, 1, 3) switch {
                1 => "Fleet entering real space...",
                2 => "Fleet now coming out of hyperspace...",
                _ => "Fleet in combat status...",
            };
            FlashMessage(flavor);
        };
    }

    private Label AddAt(int x, Pos y, string text)
    {
        var label = new Label { X = x, Y = y, Text = text };
        Add(label);
        return label;
    }

    // Menu (ATTCOMM.PAS:232-245): the standard command list, redrawn every time control returns to
    // top-level (matching real Pascal calling Menu() fresh at the top of every loop iteration).
    private void ShowCommandMenu()
    {
        activePrompt = null;
        // Every Move/Target sub-flow returns here to end (Esc, running out of groups, or finishing a
        // round) -- clearing the map highlight in this one shared spot covers all of those exits
        // without needing a matching clear at each one.
        mapView.HighlightedGroupIndex = null;
        mapView.SetNeedsDraw();
        SetCommandBoxContent([
            "", "<E>ngage", "<M>ove", "<G>roup status", "<T>arget", "<D>etails", "<R>etreat", "<A>uto-target", "", "Command",
        ]);
    }

    // GroupWindow's own real behavior once content exceeds its 10 interior rows: real Pascal's CRT
    // window auto-scrolls, keeping the newest lines visible -- approximated here by only ever showing
    // the last 10 lines of whatever's accumulated so far.
    private void SetCommandBoxContent(IReadOnlyList<string> lines)
    {
        groupListHintLabel.Visible = false;
        groupListView.Visible = false;
        commandBoxContent.Visible = true;

        const int interiorHeight = 10;
        var visible = lines.Count > interiorHeight ? lines.Skip(lines.Count - interiorHeight) : lines;
        commandBoxContent.Text = string.Join('\n', visible);
    }

    // EnemyStatus/UpdateEnemyWindow (ATTCOMM.PAS:149-186): a plain table instead of Pascal's own
    // fixed-cell layout -- only rows with anything present are worth a line here.
    private void RefreshEnemyTable()
    {
        var lines = new List<string> { "Enemy forces:", "                    Deep  High  Orbt  SubO  Grnd" };
        foreach (var type in _allAttackTypes) {
            var counts = _allShellPositions.Select(pos => state.Enemy[pos, type]).ToArray();
            if (counts.All(c => c == 0)) {
                continue;
            }
            lines.Add($"{TypeName(type),16}{string.Concat(counts.Select(c => $"{c,6}"))}");
        }
        enemyLabel.Text = string.Join('\n', lines);
    }

    private void RefreshAutoTargetLabel() => autoTargetLabel.Text = state.AutoTarget ? "Auto-target: ON" : "Auto-target: OFF";

    // Not real Pascal -- toggles InteractiveCombatState.AutoTarget (see its own doc comment). No round
    // consumed, same as Target itself; doesn't touch GroupWindow's own content at all.
    private void HandleAutoTargetToggle()
    {
        state.AutoTarget = !state.AutoTarget;
        RefreshAutoTargetLabel();
        FlashMessage(state.AutoTarget ? "Auto-targeting ON." : "Auto-targeting OFF.");
    }

    private void OnKeyDown(object? sender, Key key)
    {
        if (activePrompt is { } prompt) {
            prompt(key);
            return;
        }

        var ch = char.ToUpperInvariant((char)key.AsRune.Value);
        switch (ch) {
            case 'E':
                HandleEngage();
                key.Handled = true;
                break;
            case 'M':
                HandleMove();
                key.Handled = true;
                break;
            case 'T':
                HandleTarget();
                key.Handled = true;
                break;
            case 'R':
                HandleRetreat();
                key.Handled = true;
                break;
            case 'G':
                ShowGroupStatusInBox();
                key.Handled = true;
                break;
            case 'D':
                ShowDetails();
                key.Handled = true;
                break;
            case 'A':
                HandleAutoTargetToggle();
                key.Handled = true;
                break;
        }

        if (key == Key.Esc) {
            // Real Pascal's Menu has no cancel key (see this class's own doc comment) -- Esc falling
            // through unhandled used to hit Terminal.Gui's default quit behavior, unwinding all the way
            // out of app.Run(gameShell) and back to the turn-start greeting (issue #5) instead of doing
            // anything battle-related. Routed into the same y/n Retreat confirmation <R> already uses,
            // rather than a bare dismiss: this window only ever leaves via BattleEnded (GameShell.
            // StartEngagement), which runs RestoreCombatant/ResolveAttack -- skipping that would leave
            // the engaged fleets stuck mid-battle.
            HandleRetreat();
            key.Handled = true;
        }
    }

    private void HandleEngage()
    {
        if (state.IsOver) {
            return;
        }
        var wasDestroyed = state.Groups.Select(g => g.Sta == GroupStatus.Destroyed).ToArray();
        state.Engage(random);
        AfterRound(wasDestroyed);
        ShowCommandMenu();
    }

    // GroupRetreat (ATTCOMM.PAS:463-507): "Are you sure (y/N)" restricted to Y/N only, matching
    // GetCharacter(['Y','N'],...) exactly -- no Enter/Esc shortcut (real Pascal's GetCharacter with an
    // explicit char set simply keeps waiting for a legal key).
    private void HandleRetreat()
    {
        if (state.IsOver) {
            return;
        }

        SetCommandBoxContent(["Are you sure (y/N) → "]);
        activePrompt = key => {
            var ch = char.ToUpperInvariant((char)key.AsRune.Value);
            if (ch != 'Y' && ch != 'N') {
                return;
            }
            key.Handled = true;
            if (ch == 'Y') {
                var wasDestroyed = state.Groups.Select(g => g.Sta == GroupStatus.Destroyed).ToArray();
                state.Retreat(random);
                AfterRound(wasDestroyed, retreated: true);
            }
            ShowCommandMenu();
        };
    }

    // BuildRoundMessage/AttReport's own priority (ATTCOMM.PAS:326-350,483): ALL GROUPS DESTROYED and
    // THE ENEMY HAS SURRENDERED are the last things AttReport would show before Menu redraws in real
    // Pascal's own sequential Delay(1000) flashes -- reproduced here as a single priority pick instead
    // of a literal sequence of blocking flashes (see this class's own doc comment).
    private void AfterRound(bool[] wasDestroyed, bool retreated = false)
    {
        mapView.SetNeedsDraw();
        RefreshEnemyTable();

        string message;
        if (state.Result == AttackResultType.AttackerDestroyed) {
            message = "ALL GROUPS DESTROYED";
        } else if (state.Result == AttackResultType.DefenderConquered) {
            message = "THE ENEMY HAS SURRENDERED";
        } else {
            var destroyedNow = Enumerable.Range(0, state.Groups.Count)
                .Where(i => !wasDestroyed[i] && state.Groups[i].Sta == GroupStatus.Destroyed)
                .Select(i => i + 1)
                .ToList();
            message = destroyedNow.Count switch {
                0 when retreated => "ALL GROUPS RETREATING",
                0 => "",
                1 => $"Group {destroyedNow[0]} destroyed.",
                _ => $"Groups {string.Join(", ", destroyedNow)} destroyed.",
            };
        }

        FlashMessage(message);
        if (state.IsOver) {
            BattleEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void FlashMessage(string text)
    {
        if (pendingMessageTimeout is { } token) {
            App?.RemoveTimeout(token);
            pendingMessageTimeout = null;
        }

        messageLabel.Text = text;
        if (text.Length > 0) {
            pendingMessageTimeout = App!.AddTimeout(TimeSpan.FromSeconds(1), () => {
                messageLabel.Text = "";
                pendingMessageTimeout = null;
                return false;
            });
        }
    }

    /// <summary>
    /// No Pascal equivalent -- the row types behind <see cref="groupListView"/>, shared by
    /// <see cref="HandleMove"/>'s per-group list, <see cref="HandleTarget"/>, and (Move only)
    /// <see cref="ShellHeaderItem"/>'s section headings. A plain marker base rather than a shared
    /// "has a Group" property: a header row has none, and forcing one nullable onto every row just to
    /// accommodate headers pushed null-checks onto <see cref="TargetGroupItem"/>/
    /// <see cref="MoveGroupItem"/> call sites that can never actually see one -- <see cref="IGroupRow"/>
    /// below is the real shared shape those two have.
    /// </summary>
    private abstract class GroupActionListItem;

    /// <summary>A <see cref="GroupActionListItem"/> row that corresponds to a real, still-live group, as opposed to a section heading.</summary>
    private interface IGroupRow
    {
        GroupRecord Group { get; }
    }

    // GroupTarget (ATTCOMM.PAS:509-546)'s own per-row shape: <see cref="Decision"/> genuinely changes
    // in place as the player answers, rather than the item being replaced, and always starts at a
    // concrete value (the group's current Trg) rather than null/blank, so a row the player never
    // touches is exactly as valid an answer as one they explicitly confirmed.
    private sealed class TargetGroupItem(GroupRecord group, string baseLine) : GroupActionListItem, IGroupRow
    {
        public GroupRecord Group { get; } = group;
        public string Decision { get; set; } = "";
        public override string ToString() => $"{baseLine} -> {Decision}";
    }

    /// <summary>
    /// A shell-position section heading in <see cref="HandleMove"/>'s own group list -- not itself
    /// selectable (<see cref="ShowGroupActionList"/>/the list's own Up/Down handling both skip past
    /// these), just a once-per-shell label so individual rows don't need to repeat "O:high orbit" on
    /// every line (see <see cref="MoveGroupItem"/>'s own doc comment for why that repetition was the
    /// actual bug).
    /// </summary>
    private sealed class ShellHeaderItem(ShellPosition shell) : GroupActionListItem
    {
        public override string ToString() => $"-- {PosName(shell)} --";
    }

    // groupListView's own real usable width (measured live via TuiDriver, not the 33-column interior
    // commandBox's border-to-border width suggests -- ListView reserves a 1-column left indent and
    // Dim.Fill(1)'s own 1-column right margin, neither available to row text) is 31. The widest a Move
    // row's own leading label ever gets: a player's own attacking groups only ever come from
    // Fleet.Ships/Cargo (Fighter..Transport, Legion, NinjaLegion -- GDM/def. satellite/ion cannon/LAM
    // are world-side static defenses, never a mobile fleet's own group), and "hunter-killers"/
    // "jumptransports" (14 chars) are the longest of those; "99:9999 " (2 number + ":" + 4 count + " ")
    // is 8 more, for 22 total ("** MOVE ALL **", MoveAllItem's own label, fits well under that same
    // budget too) -- exactly 31 minus the toggle's own fixed 9 columns (3 cells x 3 chars, no separator
    // needed: every unselected cell already opens with its own space), so the widest real row fits with
    // nothing to spare.
    private const int MoveRowLabelWidth = 22;

    /// <summary>
    /// <see cref="HandleMove"/>'s own per-group row: ship type/count plus a fixed-width Retreat/Stay/
    /// Advance toggle (<c>"[R] S  A"</c> etc.) instead of the old free-text <c>"-> Advance"</c>
    /// suffix. That old suffix, appended to a line that already carried the group's own O:{shell} and
    /// (Trg) text, routinely overflowed <see cref="commandBoxContent"/>'s real 35-column fixed width
    /// (a Pascal-era constant, GroupWindow's own real footprint) -- a real reported bug ("choosing
    /// Advance for every group, then Confirm, does nothing") that turned out to be the confirm working
    /// correctly with zero visible confirmation that it had, since the one piece of text that would've
    /// shown it never fit on screen. This format can't overflow: the toggle's three cells are always
    /// three characters each regardless of which one's selected (no extra separator between them --
    /// each cell's own space padding around an unselected letter already reads as a gap), and O:
    /// {shell} is hoisted out to a <see cref="ShellHeaderItem"/> shown once per shell instead of once
    /// per group. A letter not currently legal for this group (<see cref="CanAdvance"/>/
    /// <see cref="CanRetreat"/>, gating the same as <see cref="InteractiveCombat.CanAdvance"/>/
    /// <see cref="InteractiveCombat.CanRetreat"/>) renders as "&#183;" instead of its letter, so
    /// illegality is visible up front rather than a keypress silently doing nothing.
    /// </summary>
    private sealed class MoveGroupItem(GroupRecord group, string shipLine, bool canAdvance, bool canRetreat) : GroupActionListItem, IGroupRow
    {
        public GroupRecord Group { get; } = group;
        public bool CanAdvance { get; } = canAdvance;
        public bool CanRetreat { get; } = canRetreat;

        public override string ToString()
        {
            var r = CanRetreat ? (Group.Sta == GroupStatus.Retreating ? "[R]" : " R ") : " · ";
            var s = Group.Sta == GroupStatus.Ready ? "[S]" : " S ";
            var a = CanAdvance ? (Group.Sta == GroupStatus.Advancing ? "[A]" : " A ") : " · ";
            return $"{shipLine.PadRight(MoveRowLabelWidth)}{r}{s}{a}";
        }
    }

    /// <summary>
    /// <see cref="HandleMove"/>'s own bulk-action row, always <see cref="ShowMoveList"/>'s first item:
    /// same fixed-width Retreat/Stay/Advance toggle as <see cref="MoveGroupItem"/>, labeled "** MOVE
    /// ALL **" instead of a ship type, and legal (per cell) whenever any eligible group is. Choosing
    /// Retreat/Advance here applies it to every group that can legally do it (skipping any that can't,
    /// same as the rest can't-do-nothing-silently convention elsewhere in this class), immediately --
    /// same "commit as you go" behavior <see cref="MoveGroupItem"/>'s own rows already have, not
    /// deferred to Enter. Port-only addition, no Pascal equivalent: real GroupMove has no bulk action at
    /// all. Navigating (Up/Down) across the boundary between this row and the per-group rows below it
    /// resets every choice made on whichever side is being left, per the user's own explicit request --
    /// this row and the per-group list are two alternate ways to answer the same prompt, not layers that
    /// combine, so leaving one behind clears it rather than leaving a stale, possibly-contradictory
    /// choice in place.
    /// </summary>
    private sealed class MoveAllItem(bool canAdvance, bool canRetreat) : GroupActionListItem
    {
        public bool CanAdvance { get; } = canAdvance;
        public bool CanRetreat { get; } = canRetreat;
        public GroupStatus Choice { get; set; } = GroupStatus.Ready;

        public override string ToString()
        {
            var r = CanRetreat ? (Choice == GroupStatus.Retreating ? "[R]" : " R ") : " · ";
            var s = Choice == GroupStatus.Ready ? "[S]" : " S ";
            var a = CanAdvance ? (Choice == GroupStatus.Advancing ? "[A]" : " A ") : " · ";
            return $"{"** MOVE ALL **".PadRight(MoveRowLabelWidth)}{r}{s}{a}";
        }
    }

    /// <summary>
    /// Shared setup for <see cref="HandleMove"/>/<see cref="HandleTarget"/>: swaps
    /// <see cref="commandBoxContent"/> out for <see cref="groupListView"/> (a real scrollable,
    /// freely-navigable list -- see <see cref="GroupActionListItem"/>'s own doc comment for why that
    /// replaces GroupMove/GroupTarget's literal accumulate-into-a-Label behavior) and focuses it. Each
    /// flow still wires its own <see cref="groupListKeyHandler"/>/<see cref="activePrompt"/> before
    /// calling this, since Move and Target genuinely differ on legal keys, per-row actions, and what
    /// finishing means. Selects the first selectable (non-header) row, in case <paramref name="items"/>
    /// opens with a <see cref="ShellHeaderItem"/> -- not the case today (<see cref="MoveAllItem"/>
    /// always leads <see cref="ShowMoveList"/>'s own list, and <see cref="HandleTarget"/> has no
    /// headers at all) but cheap insurance against a future list that does.
    /// </summary>
    private void ShowGroupActionList(IReadOnlyList<GroupActionListItem> items, string hint)
    {
        commandBoxContent.Visible = false;
        groupListHintLabel.Visible = true;
        groupListHintLabel.Text = hint;
        groupListView.Visible = true;
        groupListView.SetSource(new ObservableCollection<GroupActionListItem>(items));

        var firstRow = 0;
        while (firstRow < items.Count && items[firstRow] is ShellHeaderItem) {
            firstRow++;
        }
        groupListView.Index = firstRow;
        groupListView.KeystrokeNavigator = null; // otherwise swallows S/A/R and every TargetChoices letter before groupListKeyHandler ever sees them
        groupListView.SetFocus();
        UpdateHighlightFromSelection();
    }

    private void UpdateHighlightFromSelection()
    {
        mapView.HighlightedGroupIndex = groupListView.Value is IGroupRow row ? state.Groups.IndexOf(row.Group) : null;
        mapView.SetNeedsDraw();
    }

    // GroupMove (ATTCOMM.PAS:352-461). Real Pascal accumulates each group's own status+prompt line
    // into GroupWindow, strictly sequential with no way back -- replaced here with ShowMoveList's own
    // scrollable list. A group not currently eligible for either Advance or Retreat is skipped
    // entirely, matching source.
    private void HandleMove()
    {
        if (state.IsOver) {
            return;
        }

        var candidates = new List<GroupRecord>();
        for (var i = 0; i < state.Groups.Count; i++) {
            var g = state.Groups[i];
            if (g.Sta != GroupStatus.Destroyed && (state.CanAdvance(g) || state.CanRetreat(g))) {
                candidates.Add(g);
            }
        }

        if (candidates.Count == 0) {
            ShowCommandMenu();
            return;
        }

        ShowMoveList(candidates);
    }

    // One row per eligible group, grouped under a ShellHeaderItem per shell (see MoveGroupItem's own
    // doc comment for why the O:{shell} text moved there), led by a MoveAllItem bulk-action row (see
    // its own doc comment for the crossing-resets-the-other-side behavior). Left/Right cycles the
    // selected row's own Retreat/Stay/Advance choice through whichever of those are actually legal for
    // it; S/A/R letters still work too, matching source's own single-keypress-per-group input. Up/Down
    // moves between rows, stepping over header rows. Enter confirms whatever's been chosen so far; Esc
    // cancels everything queued this pass and returns to the menu, same simplification of GroupMove's
    // real Esc-mid-loop edge case this method always used (ambiguous even against source;
    // net-equivalent in practice since real Pascal's own CancelAdvance fires on every path that isn't
    // an explicit Y to Maneuver anyway).
    private void ShowMoveList(List<GroupRecord> candidates)
    {
        var allItem = new MoveAllItem(candidates.Any(state.CanAdvance), candidates.Any(state.CanRetreat));
        var items = new List<GroupActionListItem> { allItem };
        foreach (var shell in _allShellPositions) {
            var atShell = candidates.Where(g => g.Pos == shell).ToList();
            if (atShell.Count == 0) {
                continue;
            }
            items.Add(new ShellHeaderItem(shell));
            foreach (var g in atShell) {
                var number = state.Groups.IndexOf(g) + 1;
                items.Add(new MoveGroupItem(g, $"{number,2}:{g.Num,4} {TypeName(g.Typ)}", state.CanAdvance(g), state.CanRetreat(g)));
            }
        }

        void SetChoice(GroupRecord g, GroupStatus want)
        {
            if (want == GroupStatus.Advancing) {
                state.QueueAdvance(g);
            } else if (want == GroupStatus.Retreating) {
                state.QueueRetreat(g);
            } else {
                g.Sta = GroupStatus.Ready;
            }
            groupListView.SetNeedsDraw();
        }

        // Applies want to every candidate that can legally do it (Ready is always legal), skipping the
        // rest -- same "best effort, matching source's own already-established idiom rather than block
        // the whole action" precedent as everywhere else illegality is silently allowed to leave a row
        // unchanged. allItem.Choice always reflects the player's own request, even for a group that
        // couldn't actually receive it.
        void ApplyAllChoice(GroupStatus want)
        {
            allItem.Choice = want;
            foreach (var g in candidates) {
                var legal = want switch {
                    GroupStatus.Advancing => state.CanAdvance(g),
                    GroupStatus.Retreating => state.CanRetreat(g),
                    _ => true,
                };
                if (legal) {
                    SetChoice(g, want);
                }
            }
        }

        // See MoveAllItem's own doc comment: crossing the boundary between it and the per-group rows,
        // in either direction, clears whatever was chosen on the side being left.
        void ResetAllAndGroups()
        {
            allItem.Choice = GroupStatus.Ready;
            foreach (var g in candidates) {
                g.Sta = GroupStatus.Ready;
            }
            groupListView.SetNeedsDraw();
        }

        // Shared Retreat/Stay/Advance cycling rule for both MoveAllItem and MoveGroupItem rows (only
        // their own CanAdvance/CanRetreat/current-status/apply differ) -- returns whether a legal
        // neighbor existed in the requested direction.
        bool Cycle(bool canAdvance, bool canRetreat, GroupStatus current, int direction, Action<GroupStatus> apply)
        {
            var states = new List<GroupStatus>();
            if (canRetreat) {
                states.Add(GroupStatus.Retreating);
            }
            states.Add(GroupStatus.Ready);
            if (canAdvance) {
                states.Add(GroupStatus.Advancing);
            }
            var idx = states.IndexOf(current) + direction;
            if (idx < 0 || idx >= states.Count) {
                return false;
            }
            apply(states[idx]);
            return true;
        }

        groupListKeyHandler = key => {
            var code = key.NoAlt.NoCtrl.NoShift.KeyCode;
            // Enter/Esc handled right here, not via activePrompt: groupListView holds focus while
            // this flow is open, and the base View class's own default key binding
            // (View.Keyboard.cs: KeyBindings.Add(Key.Enter, Command.Accept)) would otherwise consume
            // Enter internally -- via Command.Accept's own SuperView-bubbling, not the plain C#
            // KeyDown event activePrompt relies on -- before it ever reached the outer window. Found
            // live: Enter silently returned straight to the command menu, skipping FinishMove/the
            // Maneuver prompt entirely.
            if (code == KeyCode.Esc) {
                key.Handled = true;
                state.CancelAllQueuedMoves();
                ShowCommandMenu();
                return;
            }
            if (code == KeyCode.Enter) {
                key.Handled = true;
                FinishMove(candidates.Any(g => g.Sta != GroupStatus.Ready));
                return;
            }
            if (code is KeyCode.CursorUp or KeyCode.CursorDown) {
                key.Handled = true;
                var step = code == KeyCode.CursorDown ? 1 : -1;
                var current = groupListView.Index ?? 0;
                var next = current + step;
                while (next >= 0 && next < items.Count && items[next] is ShellHeaderItem) {
                    next += step;
                }
                if (next < 0 || next >= items.Count) {
                    return;
                }
                if (items[current] is MoveAllItem != items[next] is MoveAllItem) {
                    ResetAllAndGroups();
                }
                groupListView.Index = next;
                return;
            }

            if (code is KeyCode.CursorLeft or KeyCode.CursorRight) {
                var direction = code == KeyCode.CursorRight ? 1 : -1;
                key.Handled = groupListView.Value switch {
                    MoveAllItem all => Cycle(all.CanAdvance, all.CanRetreat, all.Choice, direction, ApplyAllChoice),
                    MoveGroupItem item => Cycle(item.CanAdvance, item.CanRetreat, item.Group.Sta, direction, want => SetChoice(item.Group, want)),
                    _ => false,
                };
                return;
            }

            var ch = char.ToUpperInvariant((char)key.AsRune.Value);
            switch (groupListView.Value) {
                case MoveAllItem all:
                    if (ch == 'S') {
                        key.Handled = true;
                        ApplyAllChoice(GroupStatus.Ready);
                    } else if (ch == 'A' && all.CanAdvance) {
                        key.Handled = true;
                        ApplyAllChoice(GroupStatus.Advancing);
                    } else if (ch == 'R' && all.CanRetreat) {
                        key.Handled = true;
                        ApplyAllChoice(GroupStatus.Retreating);
                    }
                    break;
                case MoveGroupItem item:
                    if (ch == 'S') {
                        key.Handled = true;
                        SetChoice(item.Group, GroupStatus.Ready);
                    } else if (ch == 'A' && item.CanAdvance) {
                        key.Handled = true;
                        SetChoice(item.Group, GroupStatus.Advancing);
                    } else if (ch == 'R' && item.CanRetreat) {
                        key.Handled = true;
                        SetChoice(item.Group, GroupStatus.Retreating);
                    }
                    break;
            }
        };

        // Blocks the top-level E/M/T/R/G/D/A dispatch for whatever groupListView itself doesn't
        // handle (or hasn't yet consumed via its own internal command bindings, e.g. arrow-key
        // navigation) while this flow is open -- ShowCommandMenu resets this back to null on exit.
        activePrompt = _ => { };

        ShowGroupActionList(items, "<-/-> choose  up/down group  Enter:confirm  Esc:cancel");
    }

    private void FinishMove(bool anyQueued)
    {
        if (!anyQueued) {
            ShowCommandMenu();
            return;
        }

        SetCommandBoxContent(["Designated groups ready", "for maneuver, Your Highness.", "", "Maneuver (y/N) → "]);
        activePrompt = key => {
            var ch = char.ToUpperInvariant((char)key.AsRune.Value);
            if (ch != 'Y' && ch != 'N') {
                return;
            }
            key.Handled = true;
            if (ch == 'Y') {
                var wasDestroyed = state.Groups.Select(g => g.Sta == GroupStatus.Destroyed).ToArray();
                state.Engage(random);
                AfterRound(wasDestroyed);
            } else {
                state.CancelAllQueuedMoves();
            }
            ShowCommandMenu();
        };
    }

    // GroupTarget (ATTCOMM.PAS:509-546). Same ListView-based replacement as HandleMove, see its own
    // doc comment. A single ATSymb keypress selects the target (TargetChoices, matching source's own
    // restricted GetCharacter set exactly), applied immediately per row -- real Pascal has no
    // queue/confirm step for targeting, so both Enter and Esc here just mean "done looking at this
    // list," identical to each other (unlike Move, where Esc also cancels queued moves): whatever's
    // already been set on any row stays set either way. No round consumed.
    private void HandleTarget()
    {
        var items = new List<GroupActionListItem>();
        for (var i = 0; i < state.Groups.Count; i++) {
            var g = state.Groups[i];
            if (g.Sta == GroupStatus.Destroyed) {
                continue;
            }
            items.Add(new TargetGroupItem(g, GroupLine(g, i + 1)) { Decision = g.Trg is { } t ? TypeName(t) : "-" });
        }

        if (items.Count == 0) {
            ShowCommandMenu();
            return;
        }

        groupListKeyHandler = key => {
            var code = key.NoAlt.NoCtrl.NoShift.KeyCode;
            // Enter/Esc handled right here, not via activePrompt -- see HandleMove's own doc comment
            // on groupListView's base-View Enter binding (Command.Accept) otherwise swallowing it
            // before activePrompt (on the outer window) ever sees it.
            if (code == KeyCode.Enter || code == KeyCode.Esc) {
                key.Handled = true;
                ShowCommandMenu();
                return;
            }

            if (groupListView.Value is not TargetGroupItem item) {
                return;
            }

            var ch = char.ToUpperInvariant((char)key.AsRune.Value);
            var match = Array.Find(TargetChoices, c => c.Key == ch);
            if (match.Key == '\0') {
                return; // not a legal key -- ignored, matches GetCharacter's own restricted set
            }
            key.Handled = true;

            state.SetTarget(item.Group, match.Type);
            item.Decision = match.Type is { } matchedType ? TypeName(matchedType) : "-";
            groupListView.SetNeedsDraw();
            if (groupListView.Index < items.Count - 1) {
                groupListView.Index++;
            }
        };

        activePrompt = _ => { }; // see HandleMove's own doc comment on why this stays a no-op blocker
        ShowGroupActionList(items, "Pick a new target per group, -:clear  Enter/Esc:done");
    }

    // GroupStatus (ATTCOMM.PAS:548-574): ActivateWindow(GroupWindow); ClrScr; the full listing, then
    // GetCharacter(AnyKey,...) -- any keypress at all dismisses back to the menu.
    private void ShowGroupStatusInBox()
    {
        SetCommandBoxContent(state.Groups.Select(GroupStatusLine).ToList());
        activePrompt = key => {
            key.Handled = true;
            ShowCommandMenu();
        };
    }

    // GroupMove/GroupTarget's own per-group status line (ATTCOMM.PAS:414,526): "i: Num Typ O:Pos (Trg)",
    // no Dst/GAT/Clk tail -- that tail is specific to GroupStatus's own separate Write call.
    private static string GroupLine(GroupRecord g, int number) =>
        $"{number,2}: {g.Num,4} {TypeName(g.Typ)} O:{PosName(g.Pos)} ({(g.Trg is { } t ? TypeName(t) : "-")})";

    // GroupStatus's own per-group line (ATTCOMM.PAS:560-571): the same status line plus Dst (destroyed)
    // / GAT count (transport groups) / Clk (uncloaked hunter-killer check) tail.
    private string GroupStatusLine(GroupRecord g)
    {
        var i = state.Groups.IndexOf(g) + 1;
        var line = GroupLine(g, i);
        if (g.Sta == GroupStatus.Destroyed) {
            line += "  Dst";
        } else if (g.Typ is AttackType.Transport or AttackType.Jumptransport) {
            line += $"  {g.Gat}";
        } else if (g.Typ == AttackType.HunterKiller && !g.Flg) {
            line += "  Clk";
        }
        return line;
    }

    // AttackDetails (ATTCOMM.PAS:576-607): the one real exception to "everything happens in
    // GroupWindow" -- real Pascal opens this as its own separate window over the AttackWindow/map
    // area (OpenWindow(1,1,80,13,...)), not GroupWindow, so it stays a separate popup here too. Last
    // round's per-(AttackType,group) damage -- Details is FillChar'd at the top of every GroupEngage
    // round in real Pascal too, so this is never cumulative.
    private void ShowDetails()
    {
        var lines = new List<string>();
        foreach (var type in _allAttackTypes) {
            if (!state.LastRoundDetails.AnyDamage(type)) {
                continue;
            }
            var perGroup = state.Groups.Select(g => state.LastRoundDetails.Get(type, g));
            lines.Add($"{TypeName(type),20}: {string.Concat(perGroup.Select(v => $"{v,5}"))}");
        }
        lines.Add("");
        lines.AddRange(state.Groups.Select(GroupStatusLine));

        DosDialogWindow.ShowInfo(App!, "Engage Results", string.Join('\n', lines));
    }

    private static string TypeName(AttackType type) => type switch {
        AttackType.Lam => "LAMs", AttackType.DefenseSatellite => "def. satellites", AttackType.Gdm => "GDMs",
        AttackType.IonCannon => "ion cannons", AttackType.Fighter => "fighters", AttackType.HunterKiller => "hunter-killers",
        AttackType.Jumpship => "jumpships", AttackType.Jumptransport => "jumptransports", AttackType.Penetrator => "penetrators",
        AttackType.Starship => "starships", AttackType.Transport => "transports", AttackType.Legion => "legions",
        AttackType.NinjaLegion => "ninja legions", _ => type.ToString(),
    };

    private static string PosName(ShellPosition pos) => pos switch {
        ShellPosition.DeepSpace => "deep space", ShellPosition.HighOrbit => "high orbit", ShellPosition.Orbit => "orbit",
        ShellPosition.SubOrbit => "sub-orbit", ShellPosition.Ground => "ground", _ => pos.ToString(),
    };

    /// <summary>
    /// AttackWindow's whole drawn contents (ATTCOMM.PAS:1093-1212, minus the DOS-poke mechanism itself
    /// -- see the outer class's own doc comment): the range grid or starfield background, the target's
    /// own silhouette, the abstracted enemy-ship-cluster glyphs, every live group's own marker at its
    /// current shell, and the initial WarpIn reveal. All positions are decoded from real Pascal's own
    /// byte-offset constants (<c>OrbLoc</c>/<c>PlayerOffset</c>/<c>EnemyOffset</c>/<c>Disp</c>/
    /// <c>Disp2</c>, ATTCOMM.PAS:46-65,1136-1142) to plain (row,col) deltas: a DOS text-mode byte offset
    /// is <c>row*160+col*2</c> (80 columns * 2 bytes/cell), so each constant was decoded once by solving
    /// for the (row,col) pair nearest that value and hasn't been re-derived here.
    /// </summary>
    private sealed class BattleMapView : View
    {
        private static readonly Rune DotRune = new('·'); // CP437 #250, real Pascal's own range-grid/starfield dot
        private static readonly Rune PlayerMarkerRune = new('►'); // CP437 #16
        private static readonly Rune EnemyMarkerRune = new('▼'); // CP437 #17

        // No Pascal equivalent -- HighlightedGroupIndex's own marker color (green: distinct from a
        // plain friendly marker's White and the enemy's own Red, and reads as "this one's actionable"
        // rather than either of those).
        private static readonly TgAttribute NormalMarkerAttribute = new(StandardColor.White, StandardColor.Black);
        private static readonly TgAttribute HighlightedMarkerAttribute = new(StandardColor.BrightGreen, StandardColor.Black);

        // OrbLoc (ATTCOMM.PAS:59-60): (990,1024,1054,1078,1100) decoded -- all land on the same row
        // (6), at columns (15,32,47,59,70) for DpSpc,HiOrb,Orbit,SbOrb,Grnd in that order (ShellPosition's
        // own declared order matches, so indexing by (int)ShellPosition works directly).
        private const int CenterRow = 6;
        private static readonly int[] ShellColumn = [15, 32, 47, 59, 70];

        // PlayerOffset (ATTCOMM.PAS:64-65): 10 (rowDelta,colDelta) pairs, one per group's fixed position
        // in the configured list (1-based in source; indexed 0-based here) -- always slightly left of
        // whichever shell's column the group currently occupies.
        private static readonly (int Row, int Col)[] PlayerOffset = [
            (0, -1), (0, -2), (-1, -2), (1, -2), (0, -3), (1, -3), (-1, -3), (-2, -3), (2, -3), (0, -4),
        ];

        // EnemyOffset (ATTCOMM.PAS:62-63): same idea, always slightly right of the shell's column --
        // visually separating "your ships" (left) from "their ships" (right) at the same shell.
        private static readonly (int Row, int Col)[] EnemyOffset = [
            (0, 2), (0, 3), (1, 3), (-1, 3), (0, 4), (1, 4), (-1, 4), (-2, 4), (2, 4), (0, 5), (1, 5), (-1, 5),
        ];

        // Disp (ATTCOMM.PAS:1137-1139): HiOrb/Orbit's own one-sided curve, j=-5..5 -> raw byte deltas;
        // Disp2 (ATTCOMM.PAS:1141-1142): Grnd's own two-sided (mirrored) curve, j=-4..4.
        private static readonly int[] DispHiOrb = [4, 2, 2, 0, 0, 0, 0, 0, 2, 2, 4];
        private static readonly int[] DispOrbit = [10, 4, 2, 2, 0, 0, 0, 2, 2, 4, 10];
        private static readonly int[] Disp2 = [0, 10, 14, 16, 16, 16, 14, 10, 0];

        private static readonly ShipType[] _shipAndTransportTypes = [
            ShipType.Fighter, ShipType.HunterKiller, ShipType.Jumpship, ShipType.Jumptransport,
            ShipType.Penetrator, ShipType.Starship, ShipType.Transport,
        ];

        private readonly InteractiveCombatState state;
        private readonly bool isFleetTarget;
        private readonly List<(int Row, int Col)> stars = [];

        /// <summary>
        /// No Pascal equivalent -- the group Move/Target prompts (<see cref="TacticalBattleDisplayWindow.MoveNextGroup"/>/
        /// <see cref="TacticalBattleDisplayWindow.TargetNextGroup"/>) set this to whichever group index
        /// they're currently asking about, so its marker draws in a different color instead of blending
        /// in with every other friendly group. Cleared (null) once that prompt sequence ends.
        /// </summary>
        public int? HighlightedGroupIndex { get; set; }

        // WarpIn (ATTCOMM.PAS:91-124): every group starts at DeepSpace and slides in from off-screen
        // when DrawScreen first runs. Reproduced as a short slide rather than the literal per-pixel
        // Mem-poke choreography -- ticksRemaining counts down from WarpInTicks to 0, and
        // DrawGroupMarkers interpolates each DeepSpace group's column from off-screen toward its real
        // position while it's still counting down.
        private const int WarpInTicks = 12;
        private int warpInTicksRemaining;

        public BattleMapView(InteractiveCombatState state, Random random)
        {
            this.state = state;
            isFleetTarget = state.CombatData.Target is Fleet;

            // DrawStars (ATTCOMM.PAS:1164-1171): computed once here, not per-frame -- a Fleet target's
            // starfield is static for the whole engagement in real Pascal too (nothing ever re-pokes
            // those cells). Consumes the same 100 Rnd(1,720) draws from the shared RNG stream real
            // Pascal's own DrawStars would, purely for stream-order fidelity (never read back).
            if (isFleetTarget) {
                for (var i = 0; i < 100; i++) {
                    var raw = 160 + 2 * PascalMath.Rnd(random, 1, 720);
                    stars.Add((raw / 160, (raw % 160) / 2));
                }
            }

            DrawingContent += OnDrawingContent;
        }

        public void StartWarpIn(Terminal.Gui.App.IApplication app)
        {
            warpInTicksRemaining = WarpInTicks;
            app.AddTimeout(TimeSpan.FromMilliseconds(40), () => {
                warpInTicksRemaining--;
                SetNeedsDraw();
                return warpInTicksRemaining > 0;
            });
        }

        private void OnDrawingContent(object? sender, DrawEventArgs e)
        {
            e.Cancel = true;
            SetAttribute(new TgAttribute(StandardColor.White, StandardColor.Black)); // AttackWind = 15

            if (isFleetTarget) {
                DrawStars();
            } else {
                DrawRangeGrid();
                DrawTargetGlyph();
            }
            DrawEnemyShipClusters();
            DrawGroupMarkers();
        }

        private void DrawDot(int row, int col)
        {
            if (row >= 0 && row < Viewport.Height && col >= 0 && col < Viewport.Width) {
                Move(col, row);
                AddRune(DotRune);
            }
        }

        private void DrawStars()
        {
            foreach (var (row, col) in stars) {
                DrawDot(row, col);
            }
        }

        // DrawGrid (ATTCOMM.PAS:1144-1158): DpSpc is a plain vertical line (no per-row offset); HiOrb/
        // Orbit each curve one-sided per Disp; Grnd curves both sides (mirrored) per Disp2, forming a
        // closed lens -- the target's own range-ring silhouette.
        private void DrawRangeGrid()
        {
            for (var j = -5; j <= 5; j++) {
                DrawDot(CenterRow + j, ShellColumn[(int)ShellPosition.DeepSpace]);
            }
            for (var j = -5; j <= 5; j++) {
                DrawDot(CenterRow + j, ShellColumn[(int)ShellPosition.HighOrbit] + DispHiOrb[j + 5] / 2);
            }
            for (var j = -5; j <= 5; j++) {
                DrawDot(CenterRow + j, ShellColumn[(int)ShellPosition.Orbit] + DispOrbit[j + 5] / 2);
            }
            for (var j = -4; j <= 4; j++) {
                var delta = Disp2[j + 4] / 2;
                var col = ShellColumn[(int)ShellPosition.Ground];
                DrawDot(CenterRow + j, col + delta);
                DrawDot(CenterRow + j, col - delta);
            }
        }

        // DrawObject (ATTCOMM.PAS:1093-1129): the target's own ASCII silhouette, verbatim (extracted via
        // a raw CP437 byte read of ATTCOMM.PAS -- see the outer class's own doc comment). Only ever
        // drawn for a Planet or Starbase target (a Fleet target draws stars instead, matching source's
        // own unconditional-but-type-gated call).
        private void DrawTargetGlyph()
        {
            foreach (var (row, col, text) in TargetGlyphRows()) {
                if (row < 0 || row >= Viewport.Height) {
                    continue;
                }
                Move(Math.Max(col, 0), row);
                foreach (var ch in text) {
                    AddRune(new Rune(ch));
                }
            }
        }

        private IEnumerable<(int Row, int Col, string Text)> TargetGlyphRows()
        {
            if (state.CombatData.Target is Planet) {
                yield return (3, 69, "▄▄▄");
                yield return (4, 66, "▄███████▄");
                yield return (5, 65, "▐█████████▌");
                yield return (6, 66, "▀███████▀");
                yield return (7, 69, "▀▀▀");
                yield break;
            }

            if (state.CombatData.Target is not Starbase starbase) {
                yield break;
            }

            switch (starbase.Kind) {
                case StarbaseKind.Outpost:
                    yield return (4, 70, "■┬▀");
                    yield return (5, 66, "■█■▓█▓■█■");
                    yield return (6, 69, " ▌▀");
                    yield return (7, 70, "│");
                    break;
                case StarbaseKind.IndustrialComplex:
                    yield return (3, 69, "▄▄▄");
                    yield return (4, 66, "▄█▓▀ ▀▒▓▄");
                    yield return (5, 65, "▐█▓▌   ▐▓█▌");
                    yield return (6, 66, "▀▒▓▄ ▄▓█▀");
                    yield return (7, 69, "▀▀▀");
                    break;
                case StarbaseKind.Fortress:
                case StarbaseKind.CommandBase:
                    yield return (3, 65, "    ▐▄     ");
                    yield return (4, 65, "  ▐▄██▄■▌ │");
                    yield return (5, 65, "▐▄█▓█▀███▄▌");
                    yield return (6, 65, " ▀▓▒┼■┼▒█▀ ");
                    yield return (7, 65, "    ▌▀▐    ");
                    break;
            }
        }

        // DrawEnemyShips (ATTCOMM.PAS:188-212): one abstracted "how many" glyph cluster per shell from
        // DpSpc to SubOrbit (Ground is excluded in source too), sized Round(Total/500)+1 capped at 12 --
        // alongside, not instead of, RefreshEnemyTable's own exact counts.
        private void DrawEnemyShipClusters()
        {
            foreach (var pos in new[] { ShellPosition.DeepSpace, ShellPosition.HighOrbit, ShellPosition.Orbit, ShellPosition.SubOrbit }) {
                var total = _shipAndTransportTypes.Sum(t => state.Enemy[pos, t.ToAttackType()]);
                if (total <= 0) {
                    continue;
                }
                var shapes = Math.Min(12, PascalMath.PascalRound(total / 500.0) + 1);
                for (var i = 0; i < shapes; i++) {
                    var (rowDelta, colDelta) = EnemyOffset[i];
                    if (CenterRow + rowDelta is var row && row >= 0 && row < Viewport.Height) {
                        var col = ShellColumn[(int)pos] + colDelta;
                        if (col >= 0 && col < Viewport.Width) {
                            Move(col, row);
                            AddRune(EnemyMarkerRune);
                        }
                    }
                }
            }
        }

        // DrawGroupShips (ATTCOMM.PAS:259-278): a Destroyed group simply stops being drawn anywhere,
        // matching source exactly (real Pascal never shows a "destroyed" marker here -- that's Group
        // Status/Details' own job). Indexed by each group's fixed position in the configured list, same
        // as PlayerOffset's own real Pascal indexing. A group still at DeepSpace during the WarpIn
        // countdown draws at an interpolated column sliding in from off-screen instead of its real one.
        private void DrawGroupMarkers()
        {
            var warpFraction = warpInTicksRemaining > 0 ? warpInTicksRemaining / (double)WarpInTicks : 0.0;

            for (var i = 0; i < state.Groups.Count; i++) {
                var g = state.Groups[i];
                if (g.Sta == GroupStatus.Destroyed) {
                    continue;
                }
                var (rowDelta, colDelta) = PlayerOffset[i % PlayerOffset.Length];
                var row = CenterRow + rowDelta;
                var col = ShellColumn[(int)g.Pos] + colDelta;
                if (warpFraction > 0 && g.Pos == ShellPosition.DeepSpace) {
                    col -= (int)(warpFraction * 20);
                }
                if (row >= 0 && row < Viewport.Height && col >= 0 && col < Viewport.Width) {
                    SetAttribute(i == HighlightedGroupIndex ? HighlightedMarkerAttribute : NormalMarkerAttribute);
                    Move(col, row);
                    AddRune(PlayerMarkerRune);
                }
            }
        }
    }
}
