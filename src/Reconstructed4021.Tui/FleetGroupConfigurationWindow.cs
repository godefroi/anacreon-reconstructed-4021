using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// GetGroups (ATTCOMM.PAS:777-1070) — Fleet Group Configuration: splits an attacking fleet's ships
/// into up to <see cref="FleetGroupConfiguration.MaxGroups"/> combat groups before the Tactical Battle
/// Display opens. Same shape and precedent as <see cref="ResourceDistributionEditor"/> (a
/// <see cref="Window"/> built from <see cref="Label"/> children with a manual cursor, not a custom
/// <c>Draw()</c>) — the pure transfer math lives in <see cref="FleetGroupConfiguration"/> (Core), this
/// class only drives the grid's own input loop.
///
/// Two independent cursors: <see cref="selectedGroup"/> (Up/Down, which of the 9 rows) and a separate,
/// *staged* <see cref="currentType"/> (Left/Right, cycling fgt..trn) — GetGroups' own <c>CurTyp</c> is a
/// loose preview value, not committed to a group's real <c>Typ</c> until ships actually transfer
/// (<c>ChangeGroupNumber</c>/the space-bar load-all). Committing eagerly on every Left/Right keystroke
/// instead would be a real behavioral difference, not just a cosmetic one: arrowing away from a group
/// that already holds ships and back again, without ever loading anything, must leave that group
/// untouched — confirmed by reading <c>LoadShips</c>' own <c>IF Typ&lt;&gt;CurTyp</c> gate, which only
/// fires on an actual transfer.
///
/// Up/Down deliberately diverges from real Pascal's own unconditional <c>CurTyp:=Gp[CG].Typ</c>
/// (ATTCOMM.PAS:1011,1019) — see <see cref="AdoptSelectedGroupType"/>.
///
/// Esc always commits and closes — real Pascal's <c>GetGroups</c> has no cancel path at all
/// (<c>Exit:=False</c> hardcoded right after its own loop) — never a "discard changes" exit.
/// <see cref="Committed"/> exposes the finalized group list (<see cref="FleetGroupConfiguration.Finalize"/>),
/// which may legitimately be empty (nothing assigned) — the caller is responsible for skipping the
/// battle entirely in that case, matching <c>AttackCommand</c>'s own <c>IF NoOfGroups&gt;0</c> gate.
///
/// Added/removed as a direct child of the running <see cref="GameShell"/> (via its own AddModal), same
/// convention as <see cref="CloseUpWindow"/>/<see cref="ResourceDistributionEditor"/>.
/// </summary>
internal sealed class FleetGroupConfigurationWindow : Window
{
    private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue); // SYSDispWind = 23
    private static readonly TgAttribute BorderAttribute = new(StandardColor.LightGray, StandardColor.Black); // SYSWBorder = 7
    private static readonly TgAttribute SelectedAttribute = new(StandardColor.Black, StandardColor.LightGray); // SYSDispSelect = 112

    // fgt..trn (ATTCOMM.PAS's own AttackTypes subrange ChangeGroupType cycles) -- AttackType's declared
    // order happens to keep these 7 contiguous, but spelling them out here (rather than slicing the
    // enum) keeps this array's meaning obvious without a reader having to check that fact first.
    private static readonly AttackType[] _groupTypeCycle = [
        AttackType.Fighter, AttackType.HunterKiller, AttackType.Jumpship, AttackType.Jumptransport,
        AttackType.Penetrator, AttackType.Starship, AttackType.Transport,
    ];

    private readonly ShipCounts pool;
    private readonly CargoHold cargoPool;
    private readonly GroupRecord[] groups = new GroupRecord[FleetGroupConfiguration.MaxGroups];

    private readonly Label[] poolCells = new Label[9]; // fgt,hk,jmp,jtn,pen,str,trn,men,ninj
    private readonly Label[] groupRows = new Label[FleetGroupConfiguration.MaxGroups];
    private readonly Label promptLabel;
    private readonly Label errorLabel;

    private int selectedGroup;
    private AttackType currentType = AttackType.Fighter;
    private string? editBuffer;

    /// <summary>Fired once the player presses Esc while browsing (not mid-edit) -- GetGroups' own `UNTIL Ch=ESCKey`. Read <see cref="Groups"/> from the same event.</summary>
    public event EventHandler? Committed;

    /// <summary>The finalized group list once <see cref="Committed"/> fires -- see FleetGroupConfiguration.Finalize's own doc comment for the compaction/auto-load it applies. May be empty.</summary>
    public IReadOnlyList<GroupRecord> Groups { get; private set; } = [];

    public FleetGroupConfigurationWindow(ShipCounts fleetShips, CargoHold fleetCargo)
    {
        pool = CombatEngine.CloneShips(fleetShips);
        cargoPool = CombatEngine.CloneCargo(fleetCargo);
        for (var i = 0; i < groups.Length; i++) {
            groups[i] = new GroupRecord { Typ = AttackType.Fighter, Num = 0 };
        }

        Title = "Fleet configuration";
        Width = 80;
        Height = 21;
        X = Pos.Center();
        Y = Pos.Center();
        BorderStyle = LineStyle.Single;
        CanFocus = true;
        SetScheme(new Scheme(DispWindAttribute));
        Border.View?.SetScheme(new Scheme(BorderAttribute));

        AddAt(0, 0, "  fgt   hk  jmp  jtn  pen  str  trn  men ninj");
        for (var i = 0; i < poolCells.Length; i++) {
            poolCells[i] = AddAt(i * 5, 1, string.Empty);
        }

        for (var i = 0; i < groupRows.Length; i++) {
            groupRows[i] = AddAt(1, 3 + i, string.Empty);
        }

        AddAt(0, 14, "Up/Down: select group.  Left/Right: select type to add.");
        AddAt(0, 15, "Enter: add amount.  Space: add all.  M/N: load men/ninja.  Esc: done.");

        promptLabel = AddAt(0, 17, string.Empty);
        errorLabel = AddAt(0, 18, string.Empty);

        KeyDown += OnKeyDown;
        Refresh();
    }

    private Label AddAt(int x, int y, string text)
    {
        var label = new Label { X = x, Y = y, Text = text };
        Add(label);
        return label;
    }

    // UpdateDisplay (ATTCOMM.PAS:820-844) + UpdateGroupDisplay (ATTCOMM.PAS:794-818), combined into one refresh.
    private void Refresh()
    {
        for (var i = 0; i < _groupTypeCycle.Length; i++) {
            poolCells[i].Text = $"{pool[_groupTypeCycle[i].AsShipType()!.Value],5}";
        }
        poolCells[7].Text = $"{cargoPool[CargoType.Legion],5}";
        poolCells[8].Text = $"{cargoPool[CargoType.NinjaLegion],5}";

        for (var i = 0; i < poolCells.Length; i++) {
            var highlighted = i < _groupTypeCycle.Length && _groupTypeCycle[i] == currentType;
            poolCells[i].SetScheme(new Scheme(highlighted ? SelectedAttribute : DispWindAttribute));
            poolCells[i].SetNeedsDraw();
        }

        for (var i = 0; i < groups.Length; i++) {
            var g = groups[i];
            var line = $"{i + 1}: {g.Num,4}  {TypeName(g.Typ)}";
            if (g.Gat > 0) {
                line += $" ({g.Gat} {TypeName(g.GatTyp!.Value)})";
            }
            groupRows[i].Text = line;
            groupRows[i].SetScheme(new Scheme(i == selectedGroup ? SelectedAttribute : DispWindAttribute));
            groupRows[i].SetNeedsDraw();
        }
    }

    private static string TypeName(AttackType type) => type switch {
        AttackType.Fighter => "fighters", AttackType.HunterKiller => "hunter-killers", AttackType.Jumpship => "jumpships",
        AttackType.Jumptransport => "jumptransports", AttackType.Penetrator => "penetrators", AttackType.Starship => "starships",
        AttackType.Transport => "transports", AttackType.Legion => "legions", AttackType.NinjaLegion => "ninja legions",
        _ => type.ToString(),
    };

    private void SetError(string message) => errorLabel.Text = message;

    // Real Pascal unconditionally does CurTyp:=Gp[CG].Typ on Up/Down (ATTCOMM.PAS:1011,1019) -- but every
    // group's Typ starts at fgt (the same default the Pascal init loop uses, ATTCOMM.PAS:995), so an
    // untouched group's Typ tells you nothing about what the player actually wants there. Deliberate
    // divergence: only adopt the group's Typ once it holds something real (Num or Gat > 0) -- browsing
    // past still-empty groups now leaves the staged currentType alone instead of stomping it back to fgt.
    private void AdoptSelectedGroupType()
    {
        var g = groups[selectedGroup];
        if (g.Num > 0 || g.Gat > 0) {
            currentType = g.Typ;
        }
    }

    private void OnKeyDown(object? sender, Key key)
    {
        if (editBuffer is not null) {
            HandleEditKey(key);
            return;
        }

        switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
            case KeyCode.CursorUp:
                if (selectedGroup > 0) {
                    selectedGroup--;
                    AdoptSelectedGroupType();
                    Refresh();
                }
                key.Handled = true;
                return;
            case KeyCode.CursorDown:
                if (selectedGroup < groups.Length - 1) {
                    selectedGroup++;
                    AdoptSelectedGroupType();
                    Refresh();
                }
                key.Handled = true;
                return;
            case KeyCode.CursorRight: // RArrKey: forward, wrapping trn->fgt
                currentType = _groupTypeCycle[(Array.IndexOf(_groupTypeCycle, currentType) + 1) % _groupTypeCycle.Length];
                Refresh();
                key.Handled = true;
                return;
            case KeyCode.CursorLeft: // LArrKey: backward, wrapping fgt->trn
                var idx = Array.IndexOf(_groupTypeCycle, currentType) - 1;
                currentType = _groupTypeCycle[idx < 0 ? _groupTypeCycle.Length - 1 : idx];
                Refresh();
                key.Handled = true;
                return;
            case KeyCode.Enter:
                BeginEdit("");
                key.Handled = true;
                return;
            case KeyCode.Esc:
                Commit();
                key.Handled = true;
                return;
        }

        var ch = (char)key.AsRune.Value;
        if (ch is '+' or '-' || char.IsDigit(ch)) {
            BeginEdit(ch.ToString());
            key.Handled = true;
        } else if (ch == ' ') {
            // LoadShips(Sh,Cr,Gp,CG,CurTyp,Sh[CurTyp]) -- load everything the pool has of currentType.
            var ship = currentType.AsShipType()!.Value;
            FleetGroupConfiguration.ChangeGroupType(pool, cargoPool, groups[selectedGroup], currentType);
            FleetGroupConfiguration.LoadShips(pool, groups[selectedGroup], pool[ship]);
            SetError("");
            Refresh();
            key.Handled = true;
        } else if (ch is 'm' or 'M') {
            FleetGroupConfiguration.LoadTroops(cargoPool, groups[selectedGroup], CargoType.Legion);
            SetError("");
            Refresh();
            key.Handled = true;
        } else if (ch is 'n' or 'N') {
            FleetGroupConfiguration.LoadTroops(cargoPool, groups[selectedGroup], CargoType.NinjaLegion);
            SetError("");
            Refresh();
            key.Handled = true;
        }
    }

    // ChangeGroupNumber (ATTCOMM.PAS:921-949): a small inline line-editor for one transfer amount,
    // seeded by whichever character opened it.
    private void BeginEdit(string seed)
    {
        editBuffer = seed;
        RenderEditPrompt();
    }

    private void HandleEditKey(Key key)
    {
        switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
            case KeyCode.Backspace:
                if (editBuffer!.Length > 0) {
                    editBuffer = editBuffer[..^1];
                    RenderEditPrompt();
                }
                key.Handled = true;
                return;
            case KeyCode.Esc:
                // (Line=EscKey) -- Trans:=0, a no-op transfer, not a full-screen cancel.
                CommitEdit(0);
                key.Handled = true;
                return;
            case KeyCode.Enter:
                SubmitEdit();
                key.Handled = true;
                return;
        }

        var ch = (char)key.AsRune.Value;
        if (char.IsDigit(ch) || (ch is '+' or '-' && editBuffer!.Length == 0)) {
            editBuffer += ch;
            RenderEditPrompt();
            key.Handled = true;
        }
    }

    private void SubmitEdit()
    {
        // A bare Enter (nothing typed) loads everything the pool has of currentType -- ChangeGroupNumber's
        // own (Line='') branch reads Sh[Gp[CG].Typ] (the group's still-*old*, pre-switch type) which is a
        // confusing corner case not worth reproducing exactly; reusing Space's own "load all of
        // currentType" meaning here instead, called out as a deliberate simplification rather than a
        // silent guess.
        if (editBuffer!.Length == 0 || editBuffer is "+" or "-") {
            var ship = currentType.AsShipType()!.Value;
            FleetGroupConfiguration.ChangeGroupType(pool, cargoPool, groups[selectedGroup], currentType);
            CommitEdit(pool[ship]);
            return;
        }

        if (!int.TryParse(editBuffer, out var amount)) {
            SetError("numbers only.");
            editBuffer = "";
            RenderEditPrompt();
            return;
        }

        FleetGroupConfiguration.ChangeGroupType(pool, cargoPool, groups[selectedGroup], currentType);
        CommitEdit(amount);
    }

    private void CommitEdit(int amount)
    {
        if (amount != 0) {
            FleetGroupConfiguration.LoadShips(pool, groups[selectedGroup], amount);
        }

        editBuffer = null;
        promptLabel.Text = "";
        SetError("");
        Refresh();
    }

    private void RenderEditPrompt() =>
        promptLabel.Text = $"Add how many {TypeName(currentType)} to this group: {editBuffer}";

    private void Commit()
    {
        Groups = FleetGroupConfiguration.Finalize(groups, cargoPool);
        Committed?.Invoke(this, EventArgs.Empty);
    }
}
