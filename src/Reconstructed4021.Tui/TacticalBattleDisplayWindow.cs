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
/// instead, without blocking input. No Esc anywhere in the top-level menu either — real Pascal's own
/// <c>Menu</c> has no cancel key at all, the only ways out are <see cref="InteractiveCombatState.IsOver"/>
/// becoming true. Added to <see cref="GameShell"/> via <c>AddModal(..., dismissOnOutsideClick: false)</c>
/// for exactly that reason.
/// </summary>
internal sealed class TacticalBattleDisplayWindow : Window
{
    private static readonly TgAttribute AttackWindAttribute = new(StandardColor.White, StandardColor.Black);
    private static readonly TgAttribute GroupWindAttribute = new(StandardColor.LightGray, StandardColor.Blue);
    private static readonly TgAttribute EnemyWindAttribute = new(DosColors.Red, StandardColor.Black);
    private static readonly TgAttribute BorderAttribute = new(StandardColor.LightGray, StandardColor.Black);
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
    private readonly Label enemyLabel;
    private readonly Label autoTargetLabel;
    private object? pendingMessageTimeout;

    // Non-null while a sub-interaction (Move/Retreat/Target/GroupStatus, all drawn into GroupWindow
    // itself) owns the next keypress instead of the top-level E/M/T/R/G/D/A dispatch.
    private Action<Key>? activePrompt;

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
        var commandBox = new Window {
            X = 0, Y = 14, Width = 35, Height = 12,
            BorderStyle = LineStyle.Single, CanFocus = false,
        };
        commandBox.SetScheme(new Scheme(GroupWindAttribute));
        commandBox.Border.View?.SetScheme(new Scheme(BorderAttribute));
        commandBoxContent = new Label { X = 1, Y = 0, Width = Dim.Fill(1), Height = Dim.Fill() };
        commandBox.Add(commandBoxContent);
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
        SetCommandBoxContent([
            "", "<E>ngage", "<M>ove", "<G>roup status", "<T>arget", "<D>etails", "<R>etreat", "<A>uto-target", "", "Command",
        ]);
    }

    // GroupWindow's own real behavior once content exceeds its 10 interior rows: real Pascal's CRT
    // window auto-scrolls, keeping the newest lines visible -- approximated here by only ever showing
    // the last 10 lines of whatever's accumulated so far.
    private void SetCommandBoxContent(IReadOnlyList<string> lines)
    {
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

    // GroupMove (ATTCOMM.PAS:352-461): ActivateWindow(GroupWindow); ClrScr; once, then each group's
    // own status+prompt lines accumulate below the last, without clearing between groups. A group not
    // currently eligible for either Advance or Retreat is silently skipped -- no lines for it at all.
    // Esc at any point cancels every move queued this pass and returns straight to the menu; this
    // port's own simplification of GroupMove's real "Esc mid-loop, then still maybe ask Maneuver?"
    // edge case (ambiguous even against source) to "Esc always cancels immediately" -- net-equivalent
    // in practice, since real Pascal's own CancelAdvance fires on every path that isn't an explicit Y
    // to Maneuver anyway.
    private void HandleMove()
    {
        if (state.IsOver) {
            return;
        }
        MoveNextGroup(state.Groups, 0, [], anyQueued: false);
    }

    private void MoveNextGroup(IReadOnlyList<GroupRecord> groups, int index, List<string> lines, bool anyQueued)
    {
        if (index >= groups.Count) {
            FinishMove(lines, anyQueued);
            return;
        }

        var g = groups[index];
        var canAdvance = state.CanAdvance(g);
        var canRetreat = state.CanRetreat(g);
        if (g.Sta == GroupStatus.Destroyed || (!canAdvance && !canRetreat)) {
            MoveNextGroup(groups, index + 1, lines, anyQueued);
            return;
        }

        var choices = "S" + (canAdvance ? "/A" : "") + (canRetreat ? "/R" : "");
        lines.Add(GroupLine(g, index + 1));
        lines.Add($"  Move ({choices}/Esc) → ");
        SetCommandBoxContent(lines);

        activePrompt = key => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode == KeyCode.Esc) {
                key.Handled = true;
                state.CancelAllQueuedMoves();
                ShowCommandMenu();
                return;
            }

            var ch = char.ToUpperInvariant((char)key.AsRune.Value);
            if (ch != 'S' && !(ch == 'A' && canAdvance) && !(ch == 'R' && canRetreat)) {
                return;
            }
            key.Handled = true;

            var queued = anyQueued;
            if (ch == 'A') {
                state.QueueAdvance(g);
                queued = true;
            } else if (ch == 'R') {
                state.QueueRetreat(g);
                queued = true;
            }
            MoveNextGroup(groups, index + 1, lines, queued);
        };
    }

    private void FinishMove(List<string> lines, bool anyQueued)
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

    // GroupTarget (ATTCOMM.PAS:509-546): ActivateWindow(GroupWindow); ClrScr; once, then each live
    // group's status line plus a "New target" prompt accumulate the same way GroupMove's do. A single
    // ATSymb keypress selects the target (TargetChoices, matching source's own restricted GetCharacter
    // set exactly -- not a ListView, an earlier pass here built a picker that doesn't match how real
    // Pascal actually does this at all). Enter keeps the group's current target; Esc aborts the rest
    // of the sequence (groups already handled keep their new Trg). No round consumed.
    private void HandleTarget() => TargetNextGroup(state.Groups, 0, []);

    private void TargetNextGroup(IReadOnlyList<GroupRecord> groups, int index, List<string> lines)
    {
        if (index >= groups.Count) {
            ShowCommandMenu();
            return;
        }

        var g = groups[index];
        if (g.Sta == GroupStatus.Destroyed) {
            TargetNextGroup(groups, index + 1, lines);
            return;
        }

        lines.Add(GroupLine(g, index + 1));
        lines.Add("   New target → ");
        SetCommandBoxContent(lines);

        activePrompt = key => {
            var code = key.NoAlt.NoCtrl.NoShift.KeyCode;
            if (code == KeyCode.Enter) {
                key.Handled = true;
                TargetNextGroup(groups, index + 1, lines);
                return;
            }
            if (code == KeyCode.Esc) {
                key.Handled = true;
                ShowCommandMenu();
                return;
            }

            var ch = char.ToUpperInvariant((char)key.AsRune.Value);
            var match = Array.Find(TargetChoices, c => c.Key == ch);
            if (match.Key == '\0') {
                return; // not a legal key -- ignored, matches GetCharacter's own restricted set
            }
            key.Handled = true;
            state.SetTarget(g, match.Type);
            TargetNextGroup(groups, index + 1, lines);
        };
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
                    Move(col, row);
                    AddRune(PlayerMarkerRune);
                }
            }
        }
    }
}
