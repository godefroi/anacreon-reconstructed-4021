using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;

namespace Reconstructed4021.Tui2;

// GetGroups (ATTCOMM.PAS:777-1070) -- Fleet Group Configuration: splits an attacking fleet's ships
// into up to FleetGroupConfiguration.MaxGroups combat groups before the Tactical Battle Display
// opens. Ported from Reconstructed4021.Tui's own FleetGroupConfigurationWindow (same two-cursor
// shape: selectedGroup, Up/Down, which of the 9 rows; a separate staged currentType, Left/Right,
// cycling fgt..trn) -- see that class's own doc comment for the AdoptSelectedGroupType divergence
// and the SubmitEdit/Esc-always-commits simplifications, all preserved here unchanged.
internal sealed class FleetGroupConfigurationOverlay : IOverlay
{
    private const int FrameWidth = 80;
    private const int FrameHeight = 21;

    private const ConsoleColor BorderFg = ConsoleColor.Gray; // SYSWBorder = 7.
    private const ConsoleColor BorderBg = ConsoleColor.Black;
    private const ConsoleColor ContentFg = ConsoleColor.Gray; // SYSDispWind = 23.
    private const ConsoleColor ContentBg = ConsoleColor.DarkBlue;
    private const ConsoleColor SelectedFg = ConsoleColor.Black; // SYSDispSelect = 112.
    private const ConsoleColor SelectedBg = ConsoleColor.Gray;

    private static readonly AttackType[] GroupTypeCycle = [
        AttackType.Fighter, AttackType.HunterKiller, AttackType.Jumpship, AttackType.Jumptransport,
        AttackType.Penetrator, AttackType.Starship, AttackType.Transport,
    ];

    private readonly ShipCounts _pool;
    private readonly CargoHold _cargoPool;
    private readonly GroupRecord[] _groups = new GroupRecord[FleetGroupConfiguration.MaxGroups];
    private readonly Action<IReadOnlyList<GroupRecord>> _onCommitted;

    private int _selectedGroup;
    private AttackType _currentType = AttackType.Fighter;
    private string? _editBuffer;
    private string _prompt = string.Empty;
    private string _error = string.Empty;

    public bool IsDismissed { get; private set; }

    public FleetGroupConfigurationOverlay(ShipCounts fleetShips, CargoHold fleetCargo, Action<IReadOnlyList<GroupRecord>> onCommitted)
    {
        _pool = CombatEngine.CloneShips(fleetShips);
        _cargoPool = CombatEngine.CloneCargo(fleetCargo);
        for (var i = 0; i < _groups.Length; i++)
        {
            _groups[i] = new GroupRecord { Typ = AttackType.Fighter, Num = 0 };
        }

        _onCommitted = onCommitted;
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_editBuffer is not null)
        {
            HandleEditKey(key);
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                if (_selectedGroup > 0)
                {
                    _selectedGroup--;
                    AdoptSelectedGroupType();
                }

                return;
            case ConsoleKey.DownArrow:
                if (_selectedGroup < _groups.Length - 1)
                {
                    _selectedGroup++;
                    AdoptSelectedGroupType();
                }

                return;
            case ConsoleKey.RightArrow:
                _currentType = GroupTypeCycle[(Array.IndexOf(GroupTypeCycle, _currentType) + 1) % GroupTypeCycle.Length];
                return;
            case ConsoleKey.LeftArrow:
                var idx = Array.IndexOf(GroupTypeCycle, _currentType) - 1;
                _currentType = GroupTypeCycle[idx < 0 ? GroupTypeCycle.Length - 1 : idx];
                return;
            case ConsoleKey.Enter:
                BeginEdit(string.Empty);
                return;
            case ConsoleKey.Escape:
                Commit();
                return;
        }

        var ch = key.KeyChar;
        if (ch is '+' or '-' || char.IsDigit(ch))
        {
            BeginEdit(ch.ToString());
        }
        else if (ch == ' ')
        {
            var ship = _currentType.AsShipType()!.Value;
            FleetGroupConfiguration.ChangeGroupType(_pool, _cargoPool, _groups[_selectedGroup], _currentType);
            FleetGroupConfiguration.LoadShips(_pool, _groups[_selectedGroup], _pool[ship]);
            _error = string.Empty;
        }
        else if (ch is 'm' or 'M')
        {
            FleetGroupConfiguration.LoadTroops(_cargoPool, _groups[_selectedGroup], CargoType.Legion);
            _error = string.Empty;
        }
        else if (ch is 'n' or 'N')
        {
            FleetGroupConfiguration.LoadTroops(_cargoPool, _groups[_selectedGroup], CargoType.NinjaLegion);
            _error = string.Empty;
        }
    }

    // Real Pascal unconditionally does CurTyp:=Gp[CG].Typ on Up/Down (ATTCOMM.PAS:1011,1019) -- but
    // every group's Typ starts at fgt, so an untouched group's Typ tells you nothing about what the
    // player actually wants there. Only adopt the group's Typ once it holds something real (Num or
    // Gat > 0), so browsing past still-empty groups leaves the staged currentType alone.
    private void AdoptSelectedGroupType()
    {
        var g = _groups[_selectedGroup];
        if (g.Num > 0 || g.Gat > 0)
        {
            _currentType = g.Typ;
        }
    }

    private void BeginEdit(string seed) => _editBuffer = seed;

    private void HandleEditKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Backspace:
                if (_editBuffer!.Length > 0)
                {
                    _editBuffer = _editBuffer[..^1];
                }

                return;
            case ConsoleKey.Escape:
                CommitEdit(0);
                return;
            case ConsoleKey.Enter:
                SubmitEdit();
                return;
        }

        var ch = key.KeyChar;
        if (char.IsDigit(ch) || (ch is '+' or '-' && _editBuffer!.Length == 0))
        {
            _editBuffer += ch;
        }
    }

    private void SubmitEdit()
    {
        // A bare Enter (nothing typed) loads everything the pool has of currentType -- reuses Space's
        // own "load all" meaning rather than ChangeGroupNumber's own confusing (Line='') branch, which
        // reads the group's still-old, pre-switch type. Deliberate simplification, not a guess.
        if (_editBuffer!.Length == 0 || _editBuffer is "+" or "-")
        {
            var ship = _currentType.AsShipType()!.Value;
            FleetGroupConfiguration.ChangeGroupType(_pool, _cargoPool, _groups[_selectedGroup], _currentType);
            CommitEdit(_pool[ship]);
            return;
        }

        if (!int.TryParse(_editBuffer, out var amount))
        {
            _error = "numbers only.";
            _editBuffer = string.Empty;
            return;
        }

        FleetGroupConfiguration.ChangeGroupType(_pool, _cargoPool, _groups[_selectedGroup], _currentType);
        CommitEdit(amount);
    }

    private void CommitEdit(int amount)
    {
        if (amount != 0)
        {
            FleetGroupConfiguration.LoadShips(_pool, _groups[_selectedGroup], amount);
        }

        _editBuffer = null;
        _prompt = string.Empty;
        _error = string.Empty;
    }

    // Esc always commits and closes -- real Pascal's GetGroups has no cancel path at all (Exit:=False
    // hardcoded right after its own loop). Groups may legitimately come out empty; the caller skips
    // the battle entirely in that case, matching AttackCommand's own IF NoOfGroups>0 gate.
    private void Commit()
    {
        IsDismissed = true;
        _onCommitted(FleetGroupConfiguration.Finalize(_groups, _cargoPool));
    }

    // ResourceKind.DisplayName already covers every AttackType via ToResourceKind() -- matches exactly
    // except Fighter, where tui1's own grid says "fighters" rather than ResourceKind's fuller "fighter
    // squadrons", to fit this screen's 5-char pool columns.
    private static string TypeName(AttackType type) => type == AttackType.Fighter ? "fighters" : type.ToResourceKind().DisplayName;

    public void Draw(FrameBuffer fb)
    {
        var w = Math.Min(FrameWidth, fb.Width);
        var h = Math.Min(FrameHeight, fb.Height);
        var x = Math.Max(0, (fb.Width - w) / 2);
        var y = Math.Max(0, (fb.Height - h) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, w, h, BorderFg, BorderBg);
        const string titleText = " Fleet configuration ";
        fb.DrawText(x + Math.Max(1, (w - titleText.Length) / 2), y, titleText, ConsoleColor.White, BorderBg);

        var cx = x + 1;
        var cy = y + 1;
        var cw = w - 2;
        var ch = h - 2;
        for (var row = 0; row < ch; row++)
        {
            fb.DrawText(cx, cy + row, new string(' ', cw), ContentFg, ContentBg);
        }

        void At(int px, int py, string text)
        {
            if (px >= cw)
            {
                return;
            }

            fb.DrawText(cx + px, cy + py, text, ContentFg, ContentBg, maxWidth: cw - px);
        }

        At(0, 0, "  fgt   hk  jmp  jtn  pen  str  trn  men ninj");

        for (var i = 0; i < GroupTypeCycle.Length; i++)
        {
            var text = $"{_pool[GroupTypeCycle[i].AsShipType()!.Value],5}";
            var selected = GroupTypeCycle[i] == _currentType;
            fb.DrawText(cx + i * 5, cy + 1, text, selected ? SelectedFg : ContentFg, selected ? SelectedBg : ContentBg);
        }

        fb.DrawText(cx + GroupTypeCycle.Length * 5, cy + 1, $"{_cargoPool[CargoType.Legion],5}", ContentFg, ContentBg);
        fb.DrawText(cx + (GroupTypeCycle.Length + 1) * 5, cy + 1, $"{_cargoPool[CargoType.NinjaLegion],5}", ContentFg, ContentBg);

        for (var i = 0; i < _groups.Length; i++)
        {
            var g = _groups[i];
            var line = $"{i + 1}: {g.Num,4}  {TypeName(g.Typ)}";
            if (g.Gat > 0)
            {
                line += $" ({g.Gat} {TypeName(g.GatTyp!.Value)})";
            }

            var selected = i == _selectedGroup;
            var padded = line.PadRight(cw - 1);
            fb.DrawText(cx + 1, cy + 3 + i, padded, selected ? SelectedFg : ContentFg, selected ? SelectedBg : ContentBg, maxWidth: cw - 1);
        }

        At(0, 14, "Up/Down: select group.  Left/Right: select type to add.");
        At(0, 15, "Enter: add amount.  Space: add all.  M/N: load men/ninja.  Esc: done.");

        if (_editBuffer is not null)
        {
            _prompt = $"Add how many {TypeName(_currentType)} to this group: {_editBuffer}";
        }

        if (_prompt.Length > 0)
        {
            At(0, 17, _prompt);
        }

        if (_error.Length > 0)
        {
            At(0, 18, _error);
        }
    }
}
