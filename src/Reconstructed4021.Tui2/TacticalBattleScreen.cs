using System.Text;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Presentation;
using Reconstructed4021.Core.Types;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;

namespace Reconstructed4021.Tui2;

// Engage (ATTCOMM.PAS:220-635) -- the Tactical Battle Display: a full IScreen (not an overlay --
// IOverlay has no Update hook, and the warp-free message flash below needs Update's own real-elapsed-
// time parameter), fixed at 80x28 centered over whatever terminal size is available, same convention
// CloseUpOverlay/ResourceDistributionOverlay already established.
//
// Ported from Reconstructed4021.Tui's own TacticalBattleDisplayWindow. Move ('M') and Target ('T')
// adopt that file's own freely-navigable-list enhancement over GroupMove/GroupTarget's real Pascal
// behavior (ATTCOMM.PAS:352-461/509-546, strictly sequential, no way back) -- tui1's own doc comment
// flags that enhancement as its own, not source, but it's a real usability improvement (fix a misclick
// without redoing the whole pass, bulk "move all") worth keeping. Built on Panemonde.Widgets.ListBox
// (the same widget ObjectPickerOverlay already uses), with a MoveTo method added to it so this
// screen's own custom Up/Down rule (skip header rows, reset state when crossing the MoveAllItem
// boundary) can drive selection directly instead of ListBox's own plain wrap-around Up/Down.
//
// The WarpIn slide-in reveal and DrawStars' decorative starfield (100 Rnd(1,720) draws, consumed only
// for RNG-stream fidelity with a display nothing reads back) are both dropped: no golden file or replay
// test compares this screen's own visual output against real Pascal, so there is no fidelity contract
// to keep here, and the drop costs nothing but a bit of visual polish. The Planet/Starbase target's own
// range-grid + ASCII silhouette (deterministic, no RNG) is kept -- it's the one real visual difference
// between the "planet" and "deep space" (Fleet target) scenarios, which is worth keeping.
internal sealed class TacticalBattleScreen : IScreen
{
    private const int FrameWidth = 80;
    private const int FrameHeight = 28;

    private const ConsoleColor Bg = ConsoleColor.Black;
    private const ConsoleColor AttackFg = ConsoleColor.White; // AttackWind = 15 (White/Black).
    private const ConsoleColor GroupFg = ConsoleColor.Gray; // GroupWind = 23 (LightGray/Blue).
    private const ConsoleColor GroupBg = ConsoleColor.DarkBlue;
    private const ConsoleColor EnemyFg = ConsoleColor.DarkRed; // EnemyWind = 4 (Red/Black) -- DarkRed is this port's own truecolor-calibrated Red.
    private const ConsoleColor BorderFg = ConsoleColor.Gray; // SYSWBorder = 7.
    private const ConsoleColor HighlightFg = ConsoleColor.Green; // no Pascal equivalent -- tui1's own "this group is what Move/Target is asking about" marker color.
    private const ConsoleColor ListSelectedFg = ConsoleColor.Black; // SYSDispSelect = 112, same convention as every other picker in this port.
    private const ConsoleColor ListSelectedBg = ConsoleColor.Gray;

    private const string ContinueHint = "Press any key to continue...";

    // MoveGroupItem's own row-label width (tui1's own measured value): a player's own attacking groups
    // only ever come from Fleet.Ships/Cargo (Fighter..Transport), and "hunter-killers"/"jumptransports"
    // (14 chars) are the longest of those; "99:9999 " (2 number + ":" + 4 count + " ") is 8 more, for
    // 22 total -- the widest real row fits with nothing to spare against the command box's own 31
    // usable columns (35-wide box minus a 1-column border/margin on each side, minus the toggle's own
    // fixed 9 columns).
    private const int MoveRowLabelWidth = 22;

    private static readonly ShellPosition[] AllShellPositions = Enum.GetValues<ShellPosition>();
    private static readonly AttackType[] AllAttackTypes = Enum.GetValues<AttackType>();
    private static readonly ShipType[] ShipAndTransportTypes = [
        ShipType.Fighter, ShipType.HunterKiller, ShipType.Jumpship, ShipType.Jumptransport,
        ShipType.Penetrator, ShipType.Starship, ShipType.Transport,
    ];

    // ATSymb (ATTCOMM.PAS:49-50): '-LDGIFHJTPSRMN' for AttackTypes NoRes..nnj -- GroupTarget's own
    // single-keypress selection.
    private static readonly (AttackType? Type, char Key)[] TargetChoices = [
        (null, '-'),
        (AttackType.Lam, 'L'), (AttackType.DefenseSatellite, 'D'), (AttackType.Gdm, 'G'), (AttackType.IonCannon, 'I'),
        (AttackType.Fighter, 'F'), (AttackType.HunterKiller, 'H'), (AttackType.Jumpship, 'J'),
        (AttackType.Jumptransport, 'T'), (AttackType.Penetrator, 'P'), (AttackType.Starship, 'S'),
        (AttackType.Transport, 'R'), (AttackType.Legion, 'M'), (AttackType.NinjaLegion, 'N'),
    ];

    // OrbLoc (ATTCOMM.PAS:59-60): all five shells land on the same row (6), at columns
    // (15,32,47,59,70) for DpSpc,HiOrb,Orbit,SbOrb,Grnd in that order -- ShellPosition's own declared
    // order matches, so indexing by (int)ShellPosition works directly.
    private const int CenterRow = 6;
    private static readonly int[] ShellColumn = [15, 32, 47, 59, 70];

    // PlayerOffset/EnemyOffset (ATTCOMM.PAS:62-65): fixed (row,col) deltas per group's own position in
    // the configured list, always slightly left (player) or right (enemy) of whichever shell column the
    // group currently occupies.
    private static readonly (int Row, int Col)[] PlayerOffset = [
        (0, -1), (0, -2), (-1, -2), (1, -2), (0, -3), (1, -3), (-1, -3), (-2, -3), (2, -3), (0, -4),
    ];
    private static readonly (int Row, int Col)[] EnemyOffset = [
        (0, 2), (0, 3), (1, 3), (-1, 3), (0, 4), (1, 4), (-1, 4), (-2, 4), (2, 4), (0, 5), (1, 5), (-1, 5),
    ];

    // Disp/Disp2 (ATTCOMM.PAS:1137-1142): HiOrb/Orbit's own one-sided curve, Ground's own two-sided
    // (mirrored) curve -- the target's own range-ring silhouette.
    private static readonly int[] DispHiOrb = [4, 2, 2, 0, 0, 0, 0, 0, 2, 2, 4];
    private static readonly int[] DispOrbit = [10, 4, 2, 2, 0, 0, 0, 2, 2, 4, 10];
    private static readonly int[] Disp2 = [0, 10, 14, 16, 16, 16, 14, 10, 0];

    private readonly Game _game;
    private readonly Empire _player;
    private readonly NewGameContext _context;
    private readonly Fleet _attackerFleet;
    private readonly ISectorObject _target;
    private readonly InteractiveCombatState _state;

    private IReadOnlyList<string> _commandLines = [];
    private Action<ConsoleKeyInfo>? _activePrompt;
    private string _message = "";
    private double _messageSeconds;
    private int? _highlightedGroupIndex;

    // Move/Target's own list mode (see this class's own doc comment): non-null in place of
    // _commandLines while either flow is open. _listHint is a pinned first line above the scrolling
    // list itself (unlike every other flow's hint, which is just _commandLines' own first line) --
    // Move/Target's hint needs to stay visible while the list beneath it scrolls.
    private ListBox<GroupActionItem>? _activeList;
    private string _listHint = "";

    // A full-screen modal text box (Group Status/Details/the post-battle outcome chain) -- same visual
    // convention as GalaxyMapScreen's own ShowInfo/DrawInfoPopup, duplicated rather than shared: that
    // one is coupled to GalaxyMapScreen's own overlay-stack input routing, and this screen's own
    // activePrompt-based routing is different enough that sharing isn't a clean fit yet.
    private string? _infoTitle;
    private string? _infoMessage;
    private Action<ConsoleKeyInfo>? _infoKeyHandler;

    public IScreen? NextScreen { get; private set; }

    public TacticalBattleScreen(Game game, Empire player, NewGameContext context, Fleet attackerFleet, ISectorObject target, InteractiveCombatState state)
    {
        _game = game;
        _player = player;
        _context = context;
        _attackerFleet = attackerFleet;
        _target = target;
        _state = state;

        ShowCommandMenu();
        // DrawScreen's own opening flavor line (ATTCOMM.PAS:1192-1196) -- WarpIn's own reveal animation
        // is dropped, see this class's own doc comment.
        FlashMessage(PascalMath.Rnd(_context.Random, 1, 3) switch
        {
            1 => "Fleet entering real space...",
            2 => "Fleet now coming out of hyperspace...",
            _ => "Fleet in combat status...",
        });
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_infoKeyHandler is { } infoHandler)
        {
            infoHandler(key);
            return;
        }

        if (_activePrompt is { } prompt)
        {
            prompt(key);
            return;
        }

        var ch = char.ToUpperInvariant(key.KeyChar);
        switch (ch)
        {
            case 'E':
                HandleEngage();
                return;
            case 'M':
                HandleMove();
                return;
            case 'T':
                HandleTarget();
                return;
            case 'R':
                HandleRetreat();
                return;
            case 'G':
                ShowGroupStatus();
                return;
            case 'D':
                ShowDetails();
                return;
            case 'A':
                HandleAutoTargetToggle();
                return;
        }

        // Port-only addition, same as tui1's own: real Pascal's Menu has no cancel key at all, so an
        // unhandled Esc used to fall through to whatever the host does with it. Routed into the same
        // y/n Retreat confirmation <R> already uses, rather than doing nothing.
        if (key.Key == ConsoleKey.Escape)
        {
            HandleRetreat();
        }
    }

    public void Update(TimeSpan elapsed)
    {
        if (_messageSeconds <= 0)
        {
            return;
        }

        _messageSeconds -= elapsed.TotalSeconds;
        if (_messageSeconds <= 0)
        {
            _messageSeconds = 0;
            _message = "";
        }
    }

    // Menu (ATTCOMM.PAS:232-245): the standard command list, redrawn every time control returns to top level.
    private void ShowCommandMenu()
    {
        _activePrompt = null;
        _activeList = null;
        _listHint = "";
        _highlightedGroupIndex = null;
        _commandLines = ["", "<E>ngage", "<M>ove", "<G>roup status", "<T>arget", "<D>etails", "<R>etreat", "<A>uto-target", "", "Command"];
    }

    private void FlashMessage(string text)
    {
        _message = text;
        _messageSeconds = text.Length > 0 ? 1.0 : 0;
    }

    private void HandleEngage()
    {
        if (_state.IsOver)
        {
            return;
        }

        var wasDestroyed = _state.Groups.Select(g => g.Sta == GroupStatus.Destroyed).ToArray();
        _state.Engage(_context.Random);
        AfterRound(wasDestroyed);
        ShowCommandMenu();
    }

    // GroupRetreat (ATTCOMM.PAS:463-507): "Are you sure (y/N)" restricted to Y/N only -- no Enter/Esc shortcut.
    private void HandleRetreat()
    {
        if (_state.IsOver)
        {
            return;
        }

        _commandLines = ["Are you sure (y/N) -> "];
        _activePrompt = key =>
        {
            var ch = char.ToUpperInvariant(key.KeyChar);
            if (ch != 'Y' && ch != 'N')
            {
                return;
            }

            if (ch == 'Y')
            {
                var wasDestroyed = _state.Groups.Select(g => g.Sta == GroupStatus.Destroyed).ToArray();
                _state.Retreat(_context.Random);
                AfterRound(wasDestroyed, retreated: true);
            }

            ShowCommandMenu();
        };
    }

    private void HandleAutoTargetToggle()
    {
        _state.AutoTarget = !_state.AutoTarget;
        FlashMessage(_state.AutoTarget ? "Auto-targeting ON." : "Auto-targeting OFF.");
    }

    // BuildRoundMessage/AttReport's own priority (ATTCOMM.PAS:326-350,483).
    private void AfterRound(bool[] wasDestroyed, bool retreated = false)
    {
        string message;
        if (_state.Result == AttackResultType.AttackerDestroyed)
        {
            message = "ALL GROUPS DESTROYED";
        }
        else if (_state.Result == AttackResultType.DefenderConquered)
        {
            message = "THE ENEMY HAS SURRENDERED";
        }
        else
        {
            var destroyedNow = Enumerable.Range(0, _state.Groups.Count)
                .Where(i => !wasDestroyed[i] && _state.Groups[i].Sta == GroupStatus.Destroyed)
                .Select(i => i + 1)
                .ToList();
            message = destroyedNow.Count switch
            {
                0 when retreated => "ALL GROUPS RETREATING",
                0 => "",
                1 => $"Group {destroyedNow[0]} destroyed.",
                _ => $"Groups {string.Join(", ", destroyedNow)} destroyed.",
            };
        }

        FlashMessage(message);
        if (_state.IsOver)
        {
            BeginOutcome();
        }
    }

    // No Pascal equivalent -- the row types behind _activeList, shared by HandleMove's per-group list,
    // HandleTarget, and (Move only) ShellHeaderItem's section headings. A plain marker base rather than
    // a shared "has a Group" property: a header row has none, and forcing one nullable onto every row
    // just to accommodate headers would push null-checks onto call sites that can never actually see
    // one -- IGroupRow below is the real shared shape those two have.
    private abstract class GroupActionItem;

    private interface IGroupRow
    {
        GroupRecord Group { get; }
    }

    // A shell-position section heading in HandleMove's own group list -- not itself selectable (the
    // Up/Down handling below skips past these), just a once-per-shell label so individual rows don't
    // need to repeat "O:high orbit" on every line.
    private sealed class ShellHeaderItem(ShellPosition shell) : GroupActionItem
    {
        public override string ToString() => $"-- {PosName(shell)} --";
    }

    // HandleMove's own per-row shape: a fixed-width Retreat/Stay/Advance toggle ("[R] S  A" etc.) --
    // three cells, always three characters each regardless of which one's selected, no separator needed
    // since each cell's own space padding around an unselected letter already reads as a gap. A letter
    // not currently legal for this group (CanAdvance/CanRetreat) renders as "·" instead of its letter,
    // so illegality is visible up front rather than a keypress silently doing nothing.
    private sealed class MoveGroupItem(GroupRecord group, string shipLine, bool canAdvance, bool canRetreat) : GroupActionItem, IGroupRow
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

    // HandleMove's own bulk-action row, always ShowMoveList's first item: same fixed-width toggle as
    // MoveGroupItem, labeled "** MOVE ALL **" instead of a ship type, legal (per cell) whenever any
    // eligible group is. Choosing Retreat/Advance here applies it to every group that can legally do
    // it, immediately -- same "commit as you go" behavior MoveGroupItem's own rows already have.
    // Navigating across the boundary between this row and the per-group rows below it resets every
    // choice made on whichever side is being left -- this row and the per-group list are two alternate
    // ways to answer the same prompt, not layers that combine.
    private sealed class MoveAllItem(bool canAdvance, bool canRetreat) : GroupActionItem
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

    // GroupTarget (ATTCOMM.PAS:509-546)'s own per-row shape: Decision genuinely changes in place as the
    // player answers, rather than the item being replaced, and always starts at a concrete value (the
    // group's current Trg) rather than null/blank, so a row the player never touches is exactly as
    // valid an answer as one they explicitly confirmed.
    private sealed class TargetGroupItem(GroupRecord group, string baseLine) : GroupActionItem, IGroupRow
    {
        public GroupRecord Group { get; } = group;
        public string Decision { get; set; } = "";
        public override string ToString() => $"{baseLine} -> {Decision}";
    }

    // Shared setup for HandleMove/HandleTarget: swaps _commandLines out for _activeList and focuses the
    // first selectable (non-header) row, in case items opens with a ShellHeaderItem.
    private void ShowGroupActionList(List<GroupActionItem> items, string hint)
    {
        _activeList = new ListBox<GroupActionItem>(items, item => item.ToString()!);
        _listHint = hint;

        var firstRow = 0;
        while (firstRow < items.Count && items[firstRow] is ShellHeaderItem)
        {
            firstRow++;
        }
        _activeList.MoveTo(firstRow);
        UpdateHighlightFromSelection();
    }

    private void UpdateHighlightFromSelection() =>
        _highlightedGroupIndex = _activeList?.SelectedItem is IGroupRow row ? GroupIndex(row.Group) : null;

    private void ExitGroupActionList()
    {
        _activeList = null;
        _listHint = "";
        _highlightedGroupIndex = null;
    }

    // GroupMove (ATTCOMM.PAS:352-461): a group not currently eligible for either Advance or Retreat is
    // skipped entirely, matching source.
    private void HandleMove()
    {
        if (_state.IsOver)
        {
            return;
        }

        var candidates = _state.Groups.Where(g => g.Sta != GroupStatus.Destroyed && (_state.CanAdvance(g) || _state.CanRetreat(g))).ToList();
        if (candidates.Count == 0)
        {
            ShowCommandMenu();
            return;
        }

        ShowMoveList(candidates);
    }

    // One row per eligible group, grouped under a ShellHeaderItem per shell, led by a MoveAllItem bulk-
    // action row. Left/Right cycles the selected row's own Retreat/Stay/Advance choice through whichever
    // of those are actually legal for it; S/A/R letters still work too, matching source's own single-
    // keypress-per-group input. Up/Down moves between rows, stepping over header rows. Enter confirms
    // whatever's been chosen so far; Esc cancels everything queued this pass and returns to the menu.
    private void ShowMoveList(List<GroupRecord> candidates)
    {
        var allItem = new MoveAllItem(candidates.Any(_state.CanAdvance), candidates.Any(_state.CanRetreat));
        var items = new List<GroupActionItem> { allItem };
        foreach (var shell in AllShellPositions)
        {
            var atShell = candidates.Where(g => g.Pos == shell).ToList();
            if (atShell.Count == 0)
            {
                continue;
            }

            items.Add(new ShellHeaderItem(shell));
            foreach (var g in atShell)
            {
                items.Add(new MoveGroupItem(g, $"{GroupIndex(g) + 1,2}:{g.Num,4} {TypeName(g.Typ)}", _state.CanAdvance(g), _state.CanRetreat(g)));
            }
        }

        void SetChoice(GroupRecord g, GroupStatus want)
        {
            if (want == GroupStatus.Advancing)
            {
                _state.QueueAdvance(g);
            }
            else if (want == GroupStatus.Retreating)
            {
                _state.QueueRetreat(g);
            }
            else
            {
                g.Sta = GroupStatus.Ready;
            }
        }

        // Applies want to every candidate that can legally do it (Ready is always legal), skipping the
        // rest. allItem.Choice always reflects the player's own request, even for a group that couldn't
        // actually receive it.
        void ApplyAllChoice(GroupStatus want)
        {
            allItem.Choice = want;
            foreach (var g in candidates)
            {
                var legal = want switch
                {
                    GroupStatus.Advancing => _state.CanAdvance(g),
                    GroupStatus.Retreating => _state.CanRetreat(g),
                    _ => true,
                };
                if (legal)
                {
                    SetChoice(g, want);
                }
            }
        }

        // Crossing the boundary between MoveAllItem and the per-group rows, in either direction, clears
        // whatever was chosen on the side being left.
        void ResetAllAndGroups()
        {
            allItem.Choice = GroupStatus.Ready;
            foreach (var g in candidates)
            {
                g.Sta = GroupStatus.Ready;
            }
        }

        // Shared Retreat/Stay/Advance cycling rule for both MoveAllItem and MoveGroupItem rows (only
        // their own CanAdvance/CanRetreat/current-status/apply differ) -- returns whether a legal
        // neighbor existed in the requested direction.
        bool Cycle(bool canAdvance, bool canRetreat, GroupStatus current, int direction, Action<GroupStatus> apply)
        {
            var states = new List<GroupStatus>();
            if (canRetreat)
            {
                states.Add(GroupStatus.Retreating);
            }
            states.Add(GroupStatus.Ready);
            if (canAdvance)
            {
                states.Add(GroupStatus.Advancing);
            }

            var idx = states.IndexOf(current) + direction;
            if (idx < 0 || idx >= states.Count)
            {
                return false;
            }

            apply(states[idx]);
            return true;
        }

        _activePrompt = key =>
        {
            if (key.Key == ConsoleKey.Escape)
            {
                _state.CancelAllQueuedMoves();
                ExitGroupActionList();
                ShowCommandMenu();
                return;
            }

            if (key.Key == ConsoleKey.Enter)
            {
                var anyQueued = candidates.Any(g => g.Sta != GroupStatus.Ready);
                ExitGroupActionList();
                FinishMove(anyQueued);
                return;
            }

            if (key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
            {
                var step = key.Key == ConsoleKey.DownArrow ? 1 : -1;
                var current = _activeList!.SelectedIndex;
                var next = current + step;
                while (next >= 0 && next < items.Count && items[next] is ShellHeaderItem)
                {
                    next += step;
                }
                if (next < 0 || next >= items.Count)
                {
                    return;
                }
                if (items[current] is MoveAllItem != items[next] is MoveAllItem)
                {
                    ResetAllAndGroups();
                }
                _activeList.MoveTo(next);
                UpdateHighlightFromSelection();
                return;
            }

            if (key.Key is ConsoleKey.LeftArrow or ConsoleKey.RightArrow)
            {
                var direction = key.Key == ConsoleKey.RightArrow ? 1 : -1;
                switch (_activeList!.SelectedItem)
                {
                    case MoveAllItem all:
                        Cycle(all.CanAdvance, all.CanRetreat, all.Choice, direction, ApplyAllChoice);
                        break;
                    case MoveGroupItem item:
                        Cycle(item.CanAdvance, item.CanRetreat, item.Group.Sta, direction, want => SetChoice(item.Group, want));
                        break;
                }
                return;
            }

            var ch = char.ToUpperInvariant(key.KeyChar);
            switch (_activeList!.SelectedItem)
            {
                case MoveAllItem all:
                    if (ch == 'S')
                    {
                        ApplyAllChoice(GroupStatus.Ready);
                    }
                    else if (ch == 'A' && all.CanAdvance)
                    {
                        ApplyAllChoice(GroupStatus.Advancing);
                    }
                    else if (ch == 'R' && all.CanRetreat)
                    {
                        ApplyAllChoice(GroupStatus.Retreating);
                    }
                    break;
                case MoveGroupItem item:
                    if (ch == 'S')
                    {
                        SetChoice(item.Group, GroupStatus.Ready);
                    }
                    else if (ch == 'A' && item.CanAdvance)
                    {
                        SetChoice(item.Group, GroupStatus.Advancing);
                    }
                    else if (ch == 'R' && item.CanRetreat)
                    {
                        SetChoice(item.Group, GroupStatus.Retreating);
                    }
                    break;
            }
        };

        ShowGroupActionList(items, "<-/-> choose  up/down group  Enter:confirm  Esc:cancel");
    }

    private void FinishMove(bool anyQueued)
    {
        if (!anyQueued)
        {
            ShowCommandMenu();
            return;
        }

        _commandLines = ["Designated groups ready", "for maneuver, Your Highness.", "", "Maneuver (y/N) -> "];
        _activePrompt = key =>
        {
            var ch = char.ToUpperInvariant(key.KeyChar);
            if (ch != 'Y' && ch != 'N')
            {
                return;
            }

            if (ch == 'Y')
            {
                var wasDestroyed = _state.Groups.Select(g => g.Sta == GroupStatus.Destroyed).ToArray();
                _state.Engage(_context.Random);
                AfterRound(wasDestroyed);
            }
            else
            {
                _state.CancelAllQueuedMoves();
            }

            ShowCommandMenu();
        };
    }

    // GroupTarget (ATTCOMM.PAS:509-546): no queue/confirm step -- applied immediately per row, no round
    // consumed. Both Enter and Esc here just mean "done looking at this list": whatever's already been
    // set on any row stays set either way.
    private void HandleTarget()
    {
        var items = new List<GroupActionItem>();
        for (var i = 0; i < _state.Groups.Count; i++)
        {
            var g = _state.Groups[i];
            if (g.Sta == GroupStatus.Destroyed)
            {
                continue;
            }
            items.Add(new TargetGroupItem(g, GroupLine(g, i + 1)) { Decision = g.Trg is { } t ? TypeName(t) : "-" });
        }

        if (items.Count == 0)
        {
            ShowCommandMenu();
            return;
        }

        _activePrompt = key =>
        {
            if (key.Key is ConsoleKey.Enter or ConsoleKey.Escape)
            {
                ExitGroupActionList();
                ShowCommandMenu();
                return;
            }

            if (key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
            {
                var step = key.Key == ConsoleKey.DownArrow ? 1 : -1;
                var next = _activeList!.SelectedIndex + step;
                if (next < 0 || next >= items.Count)
                {
                    return;
                }
                _activeList.MoveTo(next);
                UpdateHighlightFromSelection();
                return;
            }

            if (_activeList!.SelectedItem is not TargetGroupItem item)
            {
                return;
            }

            var ch = char.ToUpperInvariant(key.KeyChar);
            var match = Array.Find(TargetChoices, c => c.Key == ch);
            if (match.Key == '\0')
            {
                return; // not a legal key -- ignored, matches GetCharacter's own restricted set.
            }

            _state.SetTarget(item.Group, match.Type);
            item.Decision = match.Type is { } matchedType ? TypeName(matchedType) : "-";
            if (_activeList.SelectedIndex < items.Count - 1)
            {
                _activeList.MoveTo(_activeList.SelectedIndex + 1);
            }
        };

        ShowGroupActionList(items, "Pick a new target per group, -:clear  Enter/Esc:done");
    }

    // GroupStatus (ATTCOMM.PAS:548-574): any keypress at all dismisses back to the menu.
    private void ShowGroupStatus() => ShowInfoBox("Group Status", string.Join('\n', _state.Groups.Select(GroupStatusLine)), ShowCommandMenu);

    // AttackDetails (ATTCOMM.PAS:576-607): last round's per-(AttackType,group) damage -- LastRoundDetails
    // is cleared at the top of every round in Core too, so this is never cumulative.
    private void ShowDetails()
    {
        var lines = new List<string>();
        foreach (var type in AllAttackTypes)
        {
            if (!_state.LastRoundDetails.AnyDamage(type))
            {
                continue;
            }

            var perGroup = _state.Groups.Select(g => _state.LastRoundDetails.Get(type, g));
            lines.Add($"{TypeName(type),20}: {string.Concat(perGroup.Select(v => $"{v,5}"))}");
        }

        lines.Add("");
        lines.AddRange(_state.Groups.Select(GroupStatusLine));
        ShowInfoBox("Engage Results", string.Join('\n', lines), ShowCommandMenu);
    }

    // InteractiveCombatState.Groups is IReadOnlyList<GroupRecord> (no IndexOf) -- reference-identity
    // scan, matching GroupRecord's own doc comment on why it's a mutable class keyed by identity, not
    // structural equality.
    private int GroupIndex(GroupRecord g)
    {
        for (var i = 0; i < _state.Groups.Count; i++)
        {
            if (ReferenceEquals(_state.Groups[i], g))
            {
                return i;
            }
        }

        return -1;
    }

    // GroupMove/GroupTarget's own per-group status line (ATTCOMM.PAS:414,526).
    private static string GroupLine(GroupRecord g, int number) =>
        $"{number,2}: {g.Num,4} {TypeName(g.Typ)} O:{PosName(g.Pos)} ({(g.Trg is { } t ? TypeName(t) : "-")})";

    // GroupStatus's own per-group line (ATTCOMM.PAS:560-571): the same status line plus Dst (destroyed)
    // / GAT count (transport groups) / Clk (uncloaked hunter-killer check) tail.
    private string GroupStatusLine(GroupRecord g)
    {
        var i = GroupIndex(g) + 1;
        var line = GroupLine(g, i);
        if (g.Sta == GroupStatus.Destroyed)
        {
            line += "  Dst";
        }
        else if (g.Typ is AttackType.Transport or AttackType.Jumptransport)
        {
            line += $"  {g.Gat}";
        }
        else if (g.Typ == AttackType.HunterKiller && !g.Flg)
        {
            line += "  Clk";
        }

        return line;
    }

    // ResourceKind.DisplayName (Core) already covers every AttackType via ToResourceKind() -- matches
    // exactly except Fighter ("fighters" here, ResourceKind's fuller "fighter squadrons") and
    // DefenseSatellite ("def. satellites" here, ResourceKind's "defense satellites"), both real Pascal
    // TypeName exceptions carried over from tui1's own identical table, kept for this screen's own
    // narrower columns.
    private static string TypeName(AttackType type) => type switch
    {
        AttackType.Fighter => "fighters",
        AttackType.DefenseSatellite => "def. satellites",
        _ => type.ToResourceKind().DisplayName,
    };

    private static string PosName(ShellPosition pos) => pos switch
    {
        ShellPosition.DeepSpace => "deep space",
        ShellPosition.HighOrbit => "high orbit",
        ShellPosition.Orbit => "orbit",
        ShellPosition.SubOrbit => "sub-orbit",
        ShellPosition.Ground => "ground",
        _ => pos.ToString(),
    };

    // EnemyStatus/UpdateEnemyWindow (ATTCOMM.PAS:149-186): only rows with anything present are worth a line.
    private IEnumerable<string> EnemyTableLines()
    {
        yield return "Enemy forces:";
        yield return "                    Deep  High  Orbt  SubO  Grnd";
        foreach (var type in AllAttackTypes)
        {
            var counts = AllShellPositions.Select(pos => _state.Enemy[pos, type]).ToArray();
            if (counts.All(c => c == 0))
            {
                continue;
            }

            yield return $"{TypeName(type),16}{string.Concat(counts.Select(c => $"{c,6}"))}";
        }
    }

    // ---- post-battle outcome (CleanUp, ATTCOMM.PAS:1564-1588) ----

    private void ShowInfoBox(string title, string message, Action onDismissed)
    {
        _infoTitle = title;
        _infoMessage = message;
        _infoKeyHandler = _ =>
        {
            CloseInfoBox();
            onDismissed();
        };
    }

    // DosMessageWindow's own confirmDestroy mode: Y/N/Enter only, matching GetCharacter(['Y','N'],...) plus
    // an accepted Enter default. onAnswered receives capture (true unless the player explicitly chose Y/destroy).
    private void ShowConfirmBox(string title, string message, string prompt, Action<bool> onAnswered)
    {
        _infoTitle = title;
        _infoMessage = $"{message}\n\n{prompt}";
        _infoKeyHandler = key =>
        {
            var ch = char.ToUpperInvariant(key.KeyChar);
            if (ch != 'Y' && ch != 'N' && key.Key != ConsoleKey.Enter)
            {
                return;
            }

            var destroy = ch == 'Y';
            CloseInfoBox();
            onAnswered(!destroy);
        };
    }

    private void CloseInfoBox()
    {
        _infoTitle = null;
        _infoMessage = null;
        _infoKeyHandler = null;
    }

    // CleanUp (ATTCOMM.PAS:1564-1588): RestoreCombatant for both sides first, then (only on a successful
    // conquest) OldShipsFound/AskToCapture/EnemyConquered's own background call -- all three run *before*
    // ResolveAttack, which is what actually reassigns ownership. FindWorldBackgroundText(conquer:true)
    // has to run before ResolveAttack: its own 'A:' condition reads the target's pre-conquest owner.
    private void BeginOutcome()
    {
        var hkSurprise = CombatEngine.ForcesUnknown(_attackerFleet, _target.Owner);
        CombatOutcome.RestoreCombatant(_attackerFleet, _state.Casualties);
        CombatOutcome.RestoreCombatant(_target, _state.Killed);

        if (_state.Result != AttackResultType.DefenderConquered)
        {
            var report = _state.Result == AttackResultType.AttackerRetreats ? Retreated() : BattleLostMessage(_target);
            FinishAttackOutcome(hkSurprise, capture: true, report);
            return;
        }

        ShowOldShipsFound(() =>
        {
            if (_target is Fleet targetFleet && HasAnyShips(targetFleet.Ships))
            {
                // AskToCapture (ATTCOMM.PAS:1349-1380): "Y" (destroy) declines capture; anything else --
                // the default, including a bare Enter -- captures. The random "commander begs for his
                // life" flavor line is skipped, same simplification tui1's own port already made.
                var captured = Enum.GetValues<ShipType>().Where(t => targetFleet.Ships[t] > 0).Select(t => $"{targetFleet.Ships[t]} {t}");
                var body = $"You have captured:\n{string.Join('\n', captured)}";
                ShowConfirmBox("Attack", body, "Do you wish to destroy the enemy fleet (y/N) ? ",
                    capture => FinishConquest(hkSurprise, capture));
            }
            else
            {
                FinishConquest(hkSurprise, capture: true);
            }
        });
    }

    // OldShipsFound (ATTCOMM.PAS:1485-1526): an Independent planet may hold ships too obsolete for its
    // own tech level -- read-only info, no state effect.
    private void ShowOldShipsFound(Action continuation)
    {
        if (_target is not Planet planet || !planet.Owner.IsIndependent)
        {
            continuation();
            return;
        }

        var obsolete = Enum.GetValues<ShipType>()
            .Where(t => planet.Ships[t] > 0 && TechCatalog.MinTechForShip[t] > planet.TechLevel)
            .Select(t => $"{planet.Ships[t]} {t}")
            .ToList();

        if (obsolete.Count == 0)
        {
            continuation();
            return;
        }

        ShowInfoBox("Attack", $"We have found the following ships in orbit:\n{string.Join('\n', obsolete)}", continuation);
    }

    private void FinishConquest(bool hkSurprise, bool capture)
    {
        var report = Game.FindWorldBackgroundText(_game, _target, _player, conquer: true) is { } lines
            ? string.Join('\n', lines)
            : ConquestMessage(_target);
        FinishAttackOutcome(hkSurprise, capture, report);
    }

    private void FinishAttackOutcome(bool hkSurprise, bool capture, string report)
    {
        CombatOutcome.ResolveAttack(_state.Result, _attackerFleet, _target, hkSurprise, capture, _state.Casualties, _state.Killed, _game, _context.Random);
        ShowInfoBox("Attack", report, () => NextScreen = new GalaxyMapScreen(_game, _player, _context));
    }

    private string Retreated() => $"The attacking force has retreated, {MyLord()}.";

    // BattleLost (ATTCOMM.PAS:1224-1322): a flavor message picked at random from 4 variants, with
    // different odds against an Independent target (Message3 never fires against one -- it needs
    // another real empire to name) versus a real empire.
    private string BattleLostMessage(ISectorObject subject)
    {
        var messageNumber = subject.Owner.IsIndependent
            ? PascalMath.Rnd(_context.Random, 1, 12) switch { 1 => 1, 2 => 2, _ => 4 }
            : PascalMath.Rnd(_context.Random, 1, 4);

        return messageNumber switch
        {
            1 => BattleLostMessage1(),
            2 => BattleLostMessage2(),
            3 => BattleLostMessage3(),
            _ => BattleLostMessage4(),
        };
    }

    private string BattleLostMessage1()
    {
        var ownWorlds = _game.Galaxy.Planets.Where(p => p.Owner == _player).ToList();
        var mostRestless = ownWorlds.Count > 0 ? ownWorlds.MaxBy(p => p.RevolutionIndex) : null;
        var callout = mostRestless is not null && mostRestless.RevolutionIndex > 20
            ? $"\nDo not forget that {DisplayName(mostRestless)} is quickly growing doubtful of the Empire's\nability to defend itself.  "
            : "";

        return $"I'm sorry, {MyLord()}, the entire attack force has been destroyed.\n" +
            "I hope I do not have to remind you about the repercussion that this\n" +
            "loss will have.  Cetain factions within the Empire are already counting\n" +
            $"on fear to incite rebellion.{callout}";
    }

    private string BattleLostMessage2() =>
        $"{MyLord()}, I'm sorry to report that the entire attack force was lost\n" +
        "in the battle.  At the risk of offending Your Highness, I would like to\n" +
        "point out that an option to retreat was open at all times.  Although\n" +
        "sacrifice is something that all your troops know, it is often best to\n" +
        "allow them the luxury of living to fight another day.";

    private string BattleLostMessage3()
    {
        var others = _game.Empires.Where(e => !ReferenceEquals(e, _player) && e.Status == EmpireStatus.Active).ToList();
        var other = others.Count > 0 ? others[PascalMath.Rnd(_context.Random, 1, others.Count) - 1] : _player;

        return $"{MyLord()}, the entire attack force was destroyed in battle.\n" +
            "Although I certainly do not question the orders and decision of Your\n" +
            "Highness, I should like to mention that this defeat will not go\n" +
            $"unnoticed in the Galaxy.  Already {other.Name} is starting to\n" +
            "believe that this Empire would not be an overly costly target.";
    }

    private string BattleLostMessage4()
    {
        var tail = PascalMath.Rnd(_context.Random, 1, 3) switch
        {
            1 => "You must be careful, Your Highness, or greater battles will be lost.",
            2 => "Do not think that this defeat will go unnoticed in the Galaxy.",
            _ => "You must be careful, other star systems grow suspicious of your defenses.",
        };

        return $"{MyLord()}, the attack force has been totally destroyed by the enemy.\n{tail}";
    }

    // EnemyConquered's own three fallback congratulatory messages (ATTCOMM.PAS:1403-1431): Message1
    // always wins for a conquered capital; otherwise one of the three is picked uniformly at random.
    private string ConquestMessage(ISectorObject subject)
    {
        var isCapital = subject is IEconomicWorld { Type: WorldType.Capital };
        var messageNumber = isCapital ? 1 : PascalMath.Rnd(_context.Random, 1, 3);

        return messageNumber switch
        {
            1 => Honorifics.SovereigntyDeclaration(_player, subject),
            2 => $"Congratulations {MyLord()}, {DisplayName(_attackerFleet)} has succeeded in its attack against\n{DisplayName(subject)}.  No doubt some of your enemies will in the future\nbe more careful when challenging this empire.",
            _ => $"Congratulations on your victory, {MyLord()}, but remember that not\nall battles will be this easy.",
        };
    }

    private static bool HasAnyShips(ShipCounts s) =>
        s.Fighters + s.HunterKillers + s.Jumpships + s.Jumptransports + s.Penetrators + s.Starships + s.Transports > 0;

    private string DisplayName(ISectorObject obj) => CloseUpOverlay.DisplayName(obj, _player);

    private string MyLord() => Honorifics.MyLord(_player.IsEmpress);

    public void Draw(FrameBuffer fb)
    {
        fb.Clear(new Cell(new Rune(' '), AttackFg, Bg));

        var w = Math.Min(FrameWidth, fb.Width);
        var h = Math.Min(FrameHeight, fb.Height);
        var x = Math.Max(0, (fb.Width - w) / 2);
        var y = Math.Max(0, (fb.Height - h) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, w, h, BorderFg, Bg);
        const string titleText = " Tactical Battle Display ";
        fb.DrawText(x + Math.Max(1, (w - titleText.Length) / 2), y, titleText, ConsoleColor.White, Bg);

        var cx = x + 1;
        var cy = y + 1;
        var cw = w - 2;
        var ch = h - 2;

        fb.DrawText(cx, cy, $"Attacking {DisplayName(_target)}.", AttackFg, Bg, maxWidth: cw);
        if (cx + 60 < cx + cw)
        {
            fb.DrawText(cx + 60, cy, _state.AutoTarget ? "Auto-target: ON" : "Auto-target: OFF", AttackFg, Bg, maxWidth: cw - 60);
        }
        fb.DrawText(cx, cy + 1, _message, AttackFg, Bg, maxWidth: cw);

        DrawMap(fb, cx, cy + 2, cw, Math.Min(13, Math.Max(0, ch - 2)));

        var boxY = cy + 14;
        var boxHeight = Math.Max(0, ch - 14);
        if (boxHeight > 0)
        {
            DrawCommandBox(fb, cx, boxY, Math.Min(35, cw), boxHeight);
        }

        var enemyX = cx + 36;
        if (enemyX < cx + cw)
        {
            var enemyLine = 0;
            foreach (var line in EnemyTableLines())
            {
                if (boxY + enemyLine >= cy + ch)
                {
                    break;
                }
                fb.DrawText(enemyX, boxY + enemyLine, line, EnemyFg, Bg, maxWidth: cx + cw - enemyX);
                enemyLine++;
            }
        }

        if (_infoMessage is not null)
        {
            DrawInfoBox(fb);
        }
    }

    private void DrawCommandBox(FrameBuffer fb, int x, int y, int w, int h)
    {
        BoxDrawing.DrawSingleLine(fb, x, y, w, h, BorderFg, GroupBg);
        var cx = x + 1;
        var cy = y + 1;
        var cw = w - 2;
        var ch = h - 2;
        for (var row = 0; row < ch; row++)
        {
            fb.DrawText(cx, cy + row, new string(' ', cw), GroupFg, GroupBg);
        }

        if (_activeList is not null)
        {
            fb.DrawText(cx, cy, _listHint, GroupFg, GroupBg, maxWidth: cw);
            if (ch > 1)
            {
                _activeList.Draw(fb, cx, cy + 1, cw, ch - 1, GroupFg, GroupBg, ListSelectedFg, ListSelectedBg);
            }
            return;
        }

        // GroupWindow's own real behavior once content exceeds its interior rows: real Pascal's CRT
        // window auto-scrolls, keeping the newest lines visible -- approximated by only showing the
        // last ch lines of whatever's accumulated so far.
        var visible = _commandLines.Count > ch ? _commandLines.Skip(_commandLines.Count - ch).ToList() : _commandLines;
        for (var i = 0; i < visible.Count; i++)
        {
            fb.DrawText(cx, cy + i, visible[i], GroupFg, GroupBg, maxWidth: cw);
        }
    }

    private void DrawInfoBox(FrameBuffer fb)
    {
        var lines = _infoMessage!.Split('\n');
        var width = Math.Min(Math.Max(Math.Max(lines.Max(l => l.Length), _infoTitle!.Length + 2), ContinueHint.Length) + 4, fb.Width);
        var height = Math.Min(lines.Length + 5, fb.Height);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        var titleText = $" {_infoTitle} ";
        fb.DrawText(x + Math.Max(1, (width - titleText.Length) / 2), y, titleText, ConsoleColor.White, ConsoleColor.Black);
        for (var i = 0; i < lines.Length && i + 2 < height; i++)
        {
            fb.DrawText(x + 2, y + 2 + i, lines[i], ConsoleColor.White, ConsoleColor.Black, maxWidth: width - 3);
        }

        fb.DrawText(x + 2, y + height - 2, ContinueHint, ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 3);
    }

    // DrawGrid/DrawStars/DrawObject/DrawEnemyShips/DrawGroupShips (ATTCOMM.PAS:1093-1212), all combined:
    // the range grid or, for a Fleet target, nothing (see this class's own doc comment on dropping
    // DrawStars), the target's own ASCII silhouette, abstracted enemy-ship-cluster glyphs, and every
    // live group's own marker at its current shell.
    private void DrawMap(FrameBuffer fb, int x, int y, int w, int h)
    {
        if (h <= 0)
        {
            return;
        }

        void DrawDot(int row, int col)
        {
            if (row >= 0 && row < h && col >= 0 && col < w)
            {
                fb.Set(x + col, y + row, new Cell(new Rune('·'), AttackFg, Bg));
            }
        }

        if (_target is not Fleet)
        {
            for (var j = -5; j <= 5; j++)
            {
                DrawDot(CenterRow + j, ShellColumn[(int)ShellPosition.DeepSpace]);
            }
            for (var j = -5; j <= 5; j++)
            {
                DrawDot(CenterRow + j, ShellColumn[(int)ShellPosition.HighOrbit] + DispHiOrb[j + 5] / 2);
            }
            for (var j = -5; j <= 5; j++)
            {
                DrawDot(CenterRow + j, ShellColumn[(int)ShellPosition.Orbit] + DispOrbit[j + 5] / 2);
            }
            for (var j = -4; j <= 4; j++)
            {
                var delta = Disp2[j + 4] / 2;
                var col = ShellColumn[(int)ShellPosition.Ground];
                DrawDot(CenterRow + j, col + delta);
                DrawDot(CenterRow + j, col - delta);
            }

            foreach (var (row, col, text) in TargetGlyphRows())
            {
                if (row < 0 || row >= h)
                {
                    continue;
                }
                var col2 = Math.Max(col, 0);
                foreach (var ch2 in text)
                {
                    if (col2 >= w)
                    {
                        break;
                    }
                    fb.Set(x + col2, y + row, new Cell(new Rune(ch2), AttackFg, Bg));
                    col2++;
                }
            }
        }

        foreach (var pos in new[] { ShellPosition.DeepSpace, ShellPosition.HighOrbit, ShellPosition.Orbit, ShellPosition.SubOrbit })
        {
            var total = ShipAndTransportTypes.Sum(t => _state.Enemy[pos, t.ToAttackType()]);
            if (total <= 0)
            {
                continue;
            }

            var shapes = Math.Min(12, PascalMath.PascalRound(total / 500.0) + 1);
            for (var i = 0; i < shapes; i++)
            {
                var (rowDelta, colDelta) = EnemyOffset[i];
                var row = CenterRow + rowDelta;
                var col = ShellColumn[(int)pos] + colDelta;
                if (row >= 0 && row < h && col >= 0 && col < w)
                {
                    fb.Set(x + col, y + row, new Cell(new Rune('▼'), EnemyFg, Bg));
                }
            }
        }

        for (var i = 0; i < _state.Groups.Count; i++)
        {
            var g = _state.Groups[i];
            if (g.Sta == GroupStatus.Destroyed)
            {
                continue;
            }

            var (rowDelta, colDelta) = PlayerOffset[i % PlayerOffset.Length];
            var row = CenterRow + rowDelta;
            var col = ShellColumn[(int)g.Pos] + colDelta;
            if (row >= 0 && row < h && col >= 0 && col < w)
            {
                fb.Set(x + col, y + row, new Cell(new Rune('►'), i == _highlightedGroupIndex ? HighlightFg : AttackFg, Bg));
            }
        }
    }

    // DrawObject (ATTCOMM.PAS:1093-1129): the target's own ASCII silhouette, verbatim.
    private IEnumerable<(int Row, int Col, string Text)> TargetGlyphRows()
    {
        if (_target is Planet)
        {
            yield return (3, 69, "▄▄▄");
            yield return (4, 66, "▄███████▄");
            yield return (5, 65, "▐█████████▌");
            yield return (6, 66, "▀███████▀");
            yield return (7, 69, "▀▀▀");
            yield break;
        }

        if (_target is not Starbase starbase)
        {
            yield break;
        }

        switch (starbase.Kind)
        {
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
}
