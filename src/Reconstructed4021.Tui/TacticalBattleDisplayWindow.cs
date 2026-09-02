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
/// Engage (ATTCOMM.PAS:220-635) — the Tactical Battle Display: a full-screen, round-by-round view over
/// an <see cref="InteractiveCombatState"/> with a fixed menu (Engage/Move/Group status/Target/Details/
/// Retreat) and no Esc — real Pascal's own <c>Menu</c> has no cancel key at all, the only ways out are
/// <see cref="InteractiveCombatState.IsOver"/> becoming true. Added to <see cref="GameShell"/> via
/// <c>AddModal(..., dismissOnOutsideClick: false)</c> for exactly that reason.
///
/// <c>DrawScreen</c>'s whole visual complex was twice mischaracterized here as decoration before this
/// pass — it isn't, and <see cref="BattleMapView"/> now reproduces all of it, not just the parts judged
/// "important enough": <c>TYPES.PAS</c>'s <c>ObjectTypes</c> is declared
/// <c>(Void,Con,Pln,Base,Gate,...)</c>, so <c>DrawScreen</c>'s own
/// <c>IF Obj IN [Con..Gate] THEN DrawGrid ELSE DrawStars</c> actually means "attacking a Planet or
/// Base" (the common case) draws the range grid, not the rare one; only a Fleet target gets the
/// starfield. <c>DrawGroupShips</c> (called every round from <c>GroupEngage</c>, right alongside
/// <c>Battle</c>) draws each live group's marker at its *current shell*, continuously. <c>DrawObject</c>
/// draws the target's own ASCII silhouette (verbatim CP437 bytes, extracted with a raw codepage-437
/// read of ATTCOMM.PAS — this project's own established caveat that normal file reads corrupt those
/// bytes). <c>DrawEnemyShips</c> draws an abstracted per-shell enemy-ship-cluster glyph alongside the
/// exact counts <see cref="RefreshEnemyTable"/> already shows. All four use real Pascal's own constant
/// tables (<c>OrbLoc</c>, <c>PlayerOffset</c>, <c>EnemyOffset</c>, <c>Disp</c>/<c>Disp2</c>,
/// ATTCOMM.PAS:46-65,1136-1142), decoded from DOS video-memory byte offsets (<c>row*160+col*2</c>) to
/// plain row/column deltas — the real curve/cluster math, not an approximation of it. Not reproduced:
/// the literal DOS video-memory poke mechanism itself (a rendering trick, not a game mechanic, same
/// precedent <see cref="ResourceDistributionEditor"/>'s own doc comment already establishes) and the
/// screen's animated reveal (<c>WarpIn</c>/<c>AdvanceGroupsSFX</c>/<c>GroupsDestroyedSFX</c>'s own
/// per-frame motion) — the end state each of those settles into is what's drawn, immediately.
/// <c>DrawStars</c>' own 100 <c>Rnd(1,720)</c> draws are still consumed from the same RNG stream even
/// though the result is purely decorative, for stream-order fidelity with real Pascal (same reasoning
/// this project applies everywhere else), and so is <c>DrawScreen</c>'s own opening
/// <c>Rnd(1,3)</c> flavor-line pick.
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

    private readonly Label messageLabel;
    private readonly BattleMapView mapView;
    private readonly Label enemyLabel;
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
        messageLabel = AddAt(0, 1, string.Empty);

        // DrawGrid/DrawStars/DrawObject/DrawEnemyShips/DrawGroupShips, all together (see this class's
        // own doc comment) -- AttackWindow's real 80x13 footprint (ATTCOMM.PAS:1175).
        mapView = new BattleMapView(state, random) { X = 0, Y = 2, Width = 80, Height = 13 };
        Add(mapView);

        // GroupWindow (ATTCOMM.PAS:1176) -- the always-visible command box, bordered same as source
        // (ThinBRD), positioned bottom-left same as source (col 1, row 14).
        var commandBox = new Window {
            X = 0, Y = 14, Width = 35, Height = 10,
            BorderStyle = LineStyle.Single, CanFocus = false,
        };
        commandBox.SetScheme(new Scheme(DispWindAttribute));
        commandBox.Border.View?.SetScheme(new Scheme(BorderAttribute));
        var commands = new[] { "<E>ngage", "<M>ove", "<G>roup status", "<T>arget", "<D>etails", "<R>etreat" };
        for (var i = 0; i < commands.Length; i++) {
            commandBox.Add(new Label { X = 1, Y = 1 + i, Text = commands[i] });
        }
        commandBox.Add(new Label { X = 1, Y = Pos.AnchorEnd(2), Text = "Command" });
        Add(commandBox);

        // EnemyWindow (ATTCOMM.PAS:1177) -- bottom-right, beside GroupWindow.
        enemyLabel = AddAt(36, 14, string.Empty);

        KeyDown += OnKeyDown;
        RefreshEnemyTable();

        // DrawScreen's own opening flavor line (ATTCOMM.PAS:1192-1196): one Rnd(1,3) draw, consumed
        // after DrawStars' own RNG use inside BattleMapView's constructor above -- matching Pascal's
        // real call order inside DrawScreen exactly (Grid/Stars, then Object, then EnemyStatus, then
        // this pick). Deferred to Initialized, same reason as TmaLogoWindow's own timer: App isn't
        // assigned yet during construction -- this window is only ever added as a child via
        // GameShell's AddModal (called right after this constructor returns), never run directly via
        // Application.Run, so FlashMessage's own App!.AddTimeout would null-ref if called here instead
        // (App walks the SuperView chain, and there's no SuperView yet at construction time). AddAt's
        // own BeginInit/EndInit call for a view added to an already-initialized parent fires this
        // synchronously during that Add() call, so the relative RNG-consumption order versus
        // everything else is unchanged.
        Initialized += (_, _) => {
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

    /// <summary>
    /// AttackWindow's whole drawn contents (ATTCOMM.PAS:1093-1212, minus the DOS-poke mechanism and the
    /// frame-by-frame animation itself -- see the outer class's own doc comment): the range grid or
    /// starfield background, the target's own silhouette, the abstracted enemy-ship-cluster glyphs, and
    /// every live group's own marker at its current shell. All positions are decoded from real Pascal's
    /// own byte-offset constants (<c>OrbLoc</c>/<c>PlayerOffset</c>/<c>EnemyOffset</c>/<c>Disp</c>/
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

        private void OnDrawingContent(object? sender, DrawEventArgs e)
        {
            e.Cancel = true;
            SetAttribute(new TgAttribute(StandardColor.LightGray, StandardColor.Blue));

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
        // as PlayerOffset's own real Pascal indexing.
        private void DrawGroupMarkers()
        {
            for (var i = 0; i < state.Groups.Count; i++) {
                var g = state.Groups[i];
                if (g.Sta == GroupStatus.Destroyed) {
                    continue;
                }
                var (rowDelta, colDelta) = PlayerOffset[i % PlayerOffset.Length];
                var row = CenterRow + rowDelta;
                var col = ShellColumn[(int)g.Pos] + colDelta;
                if (row >= 0 && row < Viewport.Height && col >= 0 && col < Viewport.Width) {
                    Move(col, row);
                    AddRune(PlayerMarkerRune);
                }
            }
        }
    }
}
