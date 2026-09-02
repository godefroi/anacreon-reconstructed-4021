using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Types;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// Engage (ATTCOMM.PAS:220-635) — the Tactical Battle Display: a full-screen, round-by-round view over
/// an <see cref="InteractiveCombatState"/> with a fixed menu (Engage/Move/Group status/Target/Details/
/// Retreat) and no Esc — real Pascal's own <c>Menu</c> has no cancel key at all, the only ways out are
/// <see cref="InteractiveCombatState.IsOver"/> becoming true. Added to <see cref="GameShell"/> via
/// <c>AddModal(..., dismissOnOutsideClick: false)</c> for exactly that reason.
///
/// Decorative art (<c>DrawObject</c>/<c>DrawGrid</c>/<c>DrawStars</c>) is deliberately not reproduced —
/// tracked in docs/OPEN_GAPS.md, not silently dropped. The enemy force table (<c>EnemyStatus</c>) and
/// the single-line, auto-overwritten message row (<c>AttReport</c>) are the two things real Pascal
/// always keeps on screen, and are the two things this class actually renders every round.
///
/// One real, deliberate adaptation from source: <c>AttReport</c>'s non-empty calls block for a full
/// second (<c>Delay(1000)</c>) before the next screen update -- freezing the whole game loop is a
/// DOS-era artifact, not a mechanic worth reproducing, so this class shows the message immediately and
/// clears it on a one-shot <c>Application.AddTimeout</c> (the <see cref="TmaLogoWindow"/> idiom)
/// instead, without blocking input.
/// </summary>
internal sealed class TacticalBattleDisplayWindow : Window
{
    private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue); // SYSDispWind = 23
    private static readonly TgAttribute BorderAttribute = new(StandardColor.LightGray, StandardColor.Black); // SYSWBorder = 7
    private static readonly ShellPosition[] _allShellPositions = Enum.GetValues<ShellPosition>();
    private static readonly AttackType[] _allAttackTypes = Enum.GetValues<AttackType>();

    private readonly InteractiveCombatState state;
    private readonly Random random;

    private readonly Label enemyLabel;
    private readonly Label messageLabel;
    private object? pendingMessageTimeout;

    /// <summary>Fired exactly once, when <see cref="InteractiveCombatState.IsOver"/> first becomes true. The caller applies the outcome (RestoreCombatant/ResolveAttack) and dismisses this window.</summary>
    public event EventHandler? BattleEnded;

    public TacticalBattleDisplayWindow(InteractiveCombatState state, Random random, string targetName)
    {
        this.state = state;
        this.random = random;

        Title = "Tactical Battle Display";
        Width = Dim.Fill();
        Height = Dim.Fill();
        BorderStyle = LineStyle.None;
        Border.Thickness = new Thickness(0);
        CanFocus = true;
        SetScheme(new Scheme(DispWindAttribute));

        AddAt(0, 0, $"Attacking {targetName}.");
        enemyLabel = AddAt(0, 2, string.Empty);
        messageLabel = AddAt(0, 12, string.Empty);
        AddAt(0, Pos.AnchorEnd(2), "<E>ngage  <M>ove  <G>roup status  <T>arget  <D>etails  <R>etreat");

        KeyDown += OnKeyDown;
        RefreshEnemyTable();
    }

    private Label AddAt(int x, Pos y, string text)
    {
        var label = new Label { X = x, Y = y, Text = text };
        Add(label);
        return label;
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

    private void OnKeyDown(object? sender, Key key)
    {
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
                ShowGroupStatus();
                key.Handled = true;
                break;
            case 'D':
                ShowDetails();
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
    }

    private void HandleRetreat()
    {
        if (state.IsOver) {
            return;
        }
        if (MessageBox.Query(App!, "Retreat", "Are you sure?", "_Yes", "_No") != 0) {
            return;
        }
        var wasDestroyed = state.Groups.Select(g => g.Sta == GroupStatus.Destroyed).ToArray();
        state.Retreat(random);
        AfterRound(wasDestroyed, retreated: true);
    }

    // BuildRoundMessage/AttReport's own priority (ATTCOMM.PAS:326-350,483): ALL GROUPS DESTROYED and
    // THE ENEMY HAS SURRENDERED are the last things AttReport would show before Menu redraws in real
    // Pascal's own sequential Delay(1000) flashes -- reproduced here as a single priority pick instead
    // of a literal sequence of blocking flashes (see this class's own doc comment).
    private void AfterRound(bool[] wasDestroyed, bool retreated = false)
    {
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

    // GroupMove (ATTCOMM.PAS:352-461): a group not currently eligible for either Advance or Retreat is
    // silently skipped, matching Pascal exactly -- no prompt shown for it at all. Esc at any point
    // reverts every move queued this pass and ends the sequence early; this port's own simplification
    // of GroupMove's real "Esc mid-loop, then still maybe ask Maneuver?" edge case (ambiguous even
    // against source) to "Esc always cancels immediately" -- net-equivalent in practice, since real
    // Pascal's own CancelAdvance fires on every path that isn't an explicit Y to Maneuver anyway.
    private void HandleMove()
    {
        if (state.IsOver) {
            return;
        }

        var anyQueued = false;
        foreach (var g in state.Groups) {
            var canAdvance = state.CanAdvance(g);
            var canRetreat = state.CanRetreat(g);
            if (!canAdvance && !canRetreat) {
                continue;
            }

            var index = state.Groups.IndexOf(g) + 1;
            var buttons = new List<string>();
            if (canAdvance) {
                buttons.Add("_Advance");
            }
            if (canRetreat) {
                buttons.Add("_Retreat");
            }
            buttons.Add("_Stay");

            var choice = MessageBox.Query(App!, "Move",
                $"Group {index}: {g.Num} {TypeName(g.Typ)} at {PosName(g.Pos)}.", buttons.ToArray());
            if (choice is null) {
                state.CancelAllQueuedMoves();
                return;
            }

            var picked = buttons[choice.Value];
            if (picked == "_Advance") {
                state.QueueAdvance(g);
                anyQueued = true;
            } else if (picked == "_Retreat") {
                state.QueueRetreat(g);
                anyQueued = true;
            }
        }

        if (!anyQueued) {
            return;
        }

        if (MessageBox.Query(App!, "Move", "Designated groups ready for maneuver. Maneuver?", "_Yes", "_No") != 0) {
            state.CancelAllQueuedMoves();
            return;
        }

        var wasDestroyed = state.Groups.Select(g => g.Sta == GroupStatus.Destroyed).ToArray();
        state.Engage(random);
        AfterRound(wasDestroyed);
    }

    // GroupTarget (ATTCOMM.PAS:509-546): every still-live group gets a target picker in turn; Esc aborts
    // the rest of the sequence (groups already handled this pass keep their new Trg). No round consumed.
    private void HandleTarget()
    {
        var live = state.Groups.Where(g => g.Sta != GroupStatus.Destroyed).ToList();
        PromptTargetForGroup(live, 0);
    }

    private void PromptTargetForGroup(List<GroupRecord> live, int index)
    {
        if (index >= live.Count) {
            return;
        }

        var group = live[index];
        var groupNumber = state.Groups.IndexOf(group) + 1;
        var items = new List<string> { "(clear target)" };
        items.AddRange(_allAttackTypes.Select(TypeName));

        var picker = new Window {
            Title = $"Group {groupNumber} target ({TypeName(group.Typ)}, currently {(group.Trg is { } t ? TypeName(t) : "none")})",
            Width = 40,
            Height = 17,
            X = Pos.Center(),
            Y = Pos.Center(),
            BorderStyle = LineStyle.Single,
        };
        var listView = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        listView.SetSource(new System.Collections.ObjectModel.ObservableCollection<string>(items));
        picker.Add(listView);

        var dismiss = AddModal(picker);
        picker.KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode == KeyCode.Enter) {
                var selected = listView.SelectedItem ?? 0;
                state.SetTarget(group, selected == 0 ? null : _allAttackTypes[selected - 1]);
                dismiss();
                key.Handled = true;
                PromptTargetForGroup(live, index + 1);
            } else if (key.NoAlt.NoCtrl.NoShift.KeyCode == KeyCode.Esc) {
                dismiss();
                key.Handled = true;
                // Abort the rest of the sequence -- groups already handled keep their new Trg.
            }
        };
        listView.SetFocus();
    }

    // GroupStatus (ATTCOMM.PAS:548-574): per-group Num/Typ/Pos/Trg, plus Dst/GAT-count/Clk tail.
    private void ShowGroupStatus() =>
        MessageBox.Query(App!, "Group Status", string.Join('\n', state.Groups.Select(GroupStatusLine)), "OK");

    private string GroupStatusLine(GroupRecord g)
    {
        var i = state.Groups.IndexOf(g) + 1;
        var line = $"{i,2}: {g.Num,4} {TypeName(g.Typ)}  O:{PosName(g.Pos)} ({(g.Trg is { } t ? TypeName(t) : "-")})";
        if (g.Sta == GroupStatus.Destroyed) {
            line += "  Dst";
        } else if (g.Typ is AttackType.Transport or AttackType.Jumptransport) {
            line += $"  {g.Gat}";
        } else if (g.Typ == AttackType.HunterKiller && !g.Flg) {
            line += "  Clk";
        }
        return line;
    }

    // AttackDetails (ATTCOMM.PAS:576-607): last round's per-(AttackType,group) damage -- Details is
    // FillChar'd at the top of every GroupEngage round in real Pascal too, so this is never cumulative.
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

        MessageBox.Query(App!, "Engage Results", string.Join('\n', lines), "OK");
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

    // Same shape as GameShell's own AddModal (backdrop + add/remove) -- this window hosts its own
    // nested pickers (the Target list) rather than reaching back into GameShell for every sub-prompt,
    // since it has no menu bar to enable/disable and is otherwise self-contained.
    private Action AddModal(View popup)
    {
        var backdrop = new View { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = false };

        void Dismiss()
        {
            Remove(popup);
            Remove(backdrop);
            SetFocus();
        }

        Add(backdrop);
        Add(popup);
        popup.SetFocus();
        return Dismiss;
    }
}
