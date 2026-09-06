using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// Worlds/Ministry menu bar's F7 shortcut (NWSWIND.PAS: NewsWindow). Single-pane, unlike
/// <see cref="StatusWindow"/>/<see cref="FleetWindow"/> -- one scrollable list, not two panes
/// sharing a scroll position, since a news line has no fixed columns to head with a second row.
/// <see cref="Empire.News"/> in two blocks, general first then local (<c>NWSWIND.PAS</c>'s own
/// <c>LocalNews</c> set, <see cref="LocalHeadlines"/>), each preserving the list's own existing
/// order -- <c>InitNewsDataArray</c> never sorts either block, just filters twice.
///
/// Message text (<see cref="FormatLine"/>) ports <c>GetNewsLine</c> (INTRFACE.PAS:1073-1212)
/// verbatim for every <see cref="NewsType"/> this port actually produces today (confirmed via
/// <c>grep -rhoE "NewsType\.[A-Za-z0-9]+" src/Reconstructed4021.Core --include=*.cs</c> --
/// 62 distinct values, all covered here). A <see cref="NewsType"/> nothing yet calls <c>AddNews</c>
/// with (empty-empire transfers, message headlines, holocaust/self-destruct variants, a handful of
/// others -- see NEWS.PAS's own full headline list against that grep) falls back to its own bare
/// enum name rather than a fabricated-sounding message -- there's nothing to verify a guessed
/// template against since no real data reaches it yet.
///
/// <c>LackArticle</c>'s 5 random verb phrases (<c>Rnd(1,5)</c>) collapse to one fixed verb
/// (" lacks ") here -- a real simplification, not an oversight: this port has no other precedent for
/// re-rolling flavor text on every *read* of an already-recorded event (<c>TurnStartGreetingWindow</c>'s
/// own <c>Random.Shared.Next(1,4)</c> picks once per turn-start screen, not once per redraw of a
/// stored record), and there is no natural once-per-item seed to hang a random pick off here.
///
/// <c>*</c>/<c>@</c> substitution: <c>*</c> resolves through <see cref="NewsItem.Subject"/> first
/// (the common case -- most headlines carry a real object), falling back to
/// <see cref="NewsItem.Position"/> as a capital-relative coordinate (<see cref="RelativeCoordinate"/>,
/// the same simplification <see cref="FleetWindow"/> already made for Pascal's own resolved-place-name
/// <c>GetName</c>) when only a bare coordinate was recorded. <c>@</c> is
/// <see cref="NewsItem.OtherEmpire"/>'s name directly. The four Global combat headlines'
/// second embedded empire name (Pascal packs it into <c>Parm2</c> as a raw ordinal) is this port's
/// own typed <see cref="NewsItem.Defender"/> field instead -- no decoding needed.
///
/// Port-only addition, no Pascal equivalent: Up/Down move a highlighted-row marker with Enter jumping
/// to that item's own Close Up, same as <see cref="StatusWindow"/>/<see cref="FleetWindow"/> -- only
/// when the item actually has a real <see cref="NewsItem.Subject"/> to jump to; Enter no-ops on a
/// <see cref="NewsItem.Position"/>-only or empire-only item.
/// </summary>
internal sealed class NewsWindow : Window
{
    private const int NoOfLines = 19; // NWSWIND.PAS: NoOfLines:=InitHeight-2, InitHeight=21.

    private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue);
    private static readonly TgAttribute BorderAttribute = new(StandardColor.LightGray, StandardColor.Black);
    private static readonly TgAttribute SelectedAttribute = new(StandardColor.Black, StandardColor.LightGray);

    // NEWS.PAS:125-129's own LocalNews set.
    private static readonly HashSet<NewsType> LocalHeadlines = [
        NewsType.LacksRawMaterial, NewsType.RebellionWarning1, NewsType.RebellionWarning2,
        NewsType.RebellionWarning3, NewsType.RebellionWarning4, NewsType.ProbeOk,
        NewsType.FleetOutOfFuel, NewsType.DefensesLackResources, NewsType.IndustryLacksMetals,
        NewsType.ProbeDestroyedByYou, NewsType.ProbeDestroyed, NewsType.StarbaseOutOfFuel,
        NewsType.StarbaseBlocked, NewsType.FleetBlocked, NewsType.CannotGateToDenseNebula,
        NewsType.OutOfTrillumReserves, NewsType.TrillumReservesVeryLow, NewsType.TrillumReservesLow,
        NewsType.TroopsWantOut, NewsType.RebellionQuietedByMilitary, NewsType.WorldDiscoveredByOutpost,
    ];

    // DATACNST.PAS's ThingNames (:100-119) -- 1-based ResourceTypes ordinal (index 0 unused),
    // DestructionDetail/TransferDetail's own p2 encoding (LAM=1..ion=4,fgt=5..trn=11,men=12..tri=18,
    // per this port's own call-site comments -- ShipType/DefenseType ordinal plus a fixed offset).
    private static readonly string[] ResourceNames = [
        "", "LAMs", "defense satellites", "GDMs", "ion cannons", "fighter squadrons",
        "hunter-killers", "jumpships", "jumptransports", "penetrators", "starships", "transports",
        "legions", "ninja legions", "kilotons of ambrosia", "megatons of chemicals",
        "megatons of metals", "megatons of supplies", "kilotons of trillum",
    ];

    // DATACNST.PAS's IndusNames (:121-130) -- 0-based, ordinal-aligned with IndustryType directly
    // (IndustryDestroyed's own p2 is a plain IndustryType cast, no ResourceNames-style offset).
    private static readonly string[] IndustryNames = [
        "bio-tech labs", "chemical plants", "metal mines", "ship yards", "jumpship yards",
        "starship yards", "transport yards", "food factories", "trillum mines",
    ];

    // DATACNST.PAS's TechN (:152-163) -- full tech-level names, ordinal-aligned with TechLevel.
    private static readonly string[] TechLevelNames = [
        "pre-tech", "primitive", "pre-atomic", "atomic", "pre-warp", "warp", "jump", "bio-tech",
        "starship", "pre-gate", "gate",
    ];

    private readonly IReadOnlyList<NewsItem> _rows;
    private readonly Empire _viewer;
    private readonly Coordinate _origin;
    private readonly Action<ISectorObject> _onSelectSubject;
    private readonly Label[] _lineLabels = new Label[NoOfLines];
    private int _beginIndex;
    private int _selectedIndex;

    public NewsWindow(Empire viewer, Action<ISectorObject> onSelectSubject)
    {
        _viewer = viewer;
        _origin = viewer.Capital?.Location ?? new Coordinate(50, 50);
        _onSelectSubject = onSelectSubject;
        _rows = viewer.News.Where(n => !LocalHeadlines.Contains(n.Headline))
            .Concat(viewer.News.Where(n => LocalHeadlines.Contains(n.Headline)))
            .ToList();

        Title = "News";
        Width = 90;
        Height = NoOfLines + 2;
        X = Pos.Center();
        Y = Pos.Center();
        BorderStyle = LineStyle.Single;
        CanFocus = true;
        SetScheme(new Scheme(DispWindAttribute));
        Border.View?.SetScheme(new Scheme(BorderAttribute));

        if (_rows.Count == 0) {
            Add(new Label { X = 0, Y = 0, Text = "No news." });
        }

        for (var i = 0; i < NoOfLines; i++) {
            _lineLabels[i] = new Label { X = 0, Y = i, Text = string.Empty };
            Add(_lineLabels[i]);
        }

        Redraw();

        KeyDown += (_, key) => {
            switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
                case KeyCode.CursorUp: MoveSelection(-1); key.Handled = true; break;
                case KeyCode.CursorDown: MoveSelection(1); key.Handled = true; break;
                case KeyCode.PageUp: MoveSelection(-NoOfLines); key.Handled = true; break;
                case KeyCode.PageDown: MoveSelection(NoOfLines); key.Handled = true; break;
                case KeyCode.Home: SetSelection(0); key.Handled = true; break;
                case KeyCode.End: SetSelection(_rows.Count - 1); key.Handled = true; break;
                case KeyCode.Enter when _rows.Count > 0 && _rows[_selectedIndex].Subject is { } subject:
                    _onSelectSubject(subject);
                    key.Handled = true;
                    break;
            }
        };
    }

    private int MaxBeginIndex() => Math.Max(0, _rows.Count - NoOfLines);

    private void MoveSelection(int delta) => SetSelection(_selectedIndex + delta);

    private void SetSelection(int index)
    {
        if (_rows.Count == 0) {
            return;
        }

        _selectedIndex = Math.Clamp(index, 0, _rows.Count - 1);
        if (_selectedIndex < _beginIndex) {
            _beginIndex = _selectedIndex;
        } else if (_selectedIndex >= _beginIndex + NoOfLines) {
            _beginIndex = _selectedIndex - NoOfLines + 1;
        }
        _beginIndex = Math.Clamp(_beginIndex, 0, MaxBeginIndex());
        Redraw();
    }

    private void Redraw()
    {
        for (var i = 0; i < NoOfLines; i++) {
            var index = _beginIndex + i;
            var selected = index == _selectedIndex;
            _lineLabels[i].SetScheme(new Scheme(selected ? SelectedAttribute : DispWindAttribute));
            _lineLabels[i].Text = index < _rows.Count ? FormatLine(_rows[index]) : string.Empty;
        }
    }

    private string LocationName(NewsItem item) => item.Subject switch {
        { } subject => subject.Names.GetValueOrDefault(_viewer) ?? CloseUpWindow.DescribeLocationLong(subject, _viewer),
        null when item.Position is { } position => RelativeCoordinate.Format(position, _origin),
        null => "(unknown location)",
    };

    private static string DeathToll(int tens) => tens < 100 ? $"{tens * 10} million" : $"{tens / 100.0:0.0} billion";

    private string FormatLine(NewsItem item)
    {
        var loc = LocationName(item);
        var emp = item.OtherEmpire?.Name ?? "";
        var death = DeathToll(item.Parm1);

        var line = item.Headline switch {
            NewsType.LacksRawMaterial or NewsType.ConstructionLacksRawMaterial =>
                $"{loc} lacks {ResourceNames[item.Parm1]}.",
            NewsType.DefensesLackResources => $"{loc} needs {ResourceNames[item.Parm1]} to build defenses.",
            NewsType.IndustryLacksMetals => $"{loc} cannot build up its industry due to a lack of metals.",
            NewsType.PeopleStarving => $"{death} people have died of starvation on {loc}.",
            NewsType.TechLevelIncreased => $"{loc} has advanced to {TechLevelNames[item.Parm1]} level technology.",
            NewsType.TechLevelRegressed => $"{loc} has regressed to {TechLevelNames[item.Parm1]} level technology.",
            NewsType.ConstructionCompleted => $"Construction at {loc} has been completed.",
            NewsType.RebellionWarning1 => $"The people of {loc} are dissatisfied with the empire.",
            NewsType.RebellionWarning2 => $"Riots and demonstrations are widespread on {loc}.",
            NewsType.RebellionWarning3 => $"Some signs of organized rebellion detected on {loc}.",
            NewsType.RebellionWarning4 => $"Rebel forces on {loc} are well organized and plan an attack.",
            NewsType.WorldRebelled => $"Rebel forces on {loc} have succeeded in taking over.",
            NewsType.RebellionSuppressed => $"Imperial troops ended a rebellion on {loc}.  {item.Parm1} legions lost.",
            NewsType.ProbeOk => $"Imperial probe has scouted {loc}.",
            NewsType.EmpireGainedTechnology => item.TechGrant is { } grant
                ? $"{loc} has developed {TechCatalog.DisplayName(grant)} technology."
                : $"{loc} has developed new technology.",
            NewsType.EmpireGainedTechLevel => $"{loc} has developed {TechLevelNames[item.Parm1]} level technology.",
            NewsType.EnemyEmpireDestroyed => $"{loc} was attacked by {emp}.  Attack force destroyed.",
            NewsType.EnemyEmpireRetreated => $"{loc} was attacked by {emp}.  Attack force retreated.",
            NewsType.WorldConqueredByEnemy => $"{loc} has been conquered by the empire of {emp}.",
            NewsType.WorldAddictedToAmbrosia => $"The people of {loc} are now addicted to ambrosia.",
            NewsType.WorldNoLongerAddicted => $"{loc} is no longer addicted to ambrosia.",
            NewsType.AddictsDied => $"{death} people have died on {loc} of ambrosia withdrawal.",
            NewsType.RiotDeaths => $"{death} people have died in large-scale riots on {loc}.",
            NewsType.IndustryDestroyed => $"   {item.Parm1} {IndustryNames[item.Parm2]} have been destroyed on {loc}.",
            NewsType.WorldDeclaredIndependence => $"{loc} has declared independence.",
            NewsType.WorldJoinedOtherEmpire => $"{loc} has joined the empire of {emp}.",
            NewsType.WorldIsNowCapital => $"{loc} has become the temporary capital of the empire.",
            NewsType.FleetOutOfFuel => $"{loc} is out of fuel.",
            NewsType.FleetDamagedByMines => $"{loc} suffered damage from {emp} SRM field.",
            NewsType.FleetDestroyedByMines => $"{loc} was destroyed in {emp} SRM field.",
            NewsType.EnemyFleetDamagedInMinefield => $"{emp} fleet damaged in SRM field at {loc}.",
            NewsType.ConstructionSiteDestroyed => $"{emp} has attacked and destroyed construction at {loc}.",
            NewsType.StargateDestroyed => $"Stargate at {loc} has been destroyed by {emp}.",
            NewsType.FleetDamagedByLams => $"{loc} was damaged by {emp} LAMs.",
            NewsType.FleetDestroyedByLams => $"{loc} has been destroyed by a {emp} LAM attack.",
            NewsType.EmpireAttackedWithLams => $"{loc} has been hit by {emp} LAMs.",
            NewsType.DestructionDetail => $"   {item.Parm1} {ResourceNames[item.Parm2]} destroyed.",
            NewsType.ShipsOrCargoTransferredToYou => $"{loc} has received the following resources from {emp}:",
            NewsType.TransferDetail => $"   {item.Parm1} {ResourceNames[item.Parm2]}",
            NewsType.ProbeDestroyedByYou => $"Enemy probe from {emp} destroyed at {loc}.",
            NewsType.ProbeDestroyed => $"Lost contact with probe at {loc}.",
            NewsType.ConstructionDestroyedByUnknown => $"Construction at {loc} has been destroyed by unknown force.",
            NewsType.StargateDestroyedByUnknown => $"Unknown forces have destroyed stargate at {loc}.",
            NewsType.AttackedByUnknown => $"{loc} has been attacked by unknown forces.",
            NewsType.FleetDestroyedByUnknown => $"Lost contact with {loc}.  Presume destroyed.",
            NewsType.HostileLifeKilledPopulation => $"Native alien life-forms have killed {death} on {loc}.",
            NewsType.HostileLifeAttackedTroops => $"Aliens on {loc} attack.  Casualties: {item.Parm1} men, {item.Parm2} ninja.",
            NewsType.HostileLifeJoinedTroops => $"{item.Parm1} alien legions have joined imperial forces on {loc}.",
            NewsType.StarbaseOutOfFuel => $"{loc} is out of trillum.",
            NewsType.StarbaseBlocked => $"{loc} blocked in transit.",
            NewsType.FleetBlocked => $"{loc} blocked by dense nebula.",
            NewsType.CannotGateToDenseNebula => $"{loc} unable to gate to inpenetrable nebula.",
            NewsType.StarbaseSelfDestructed => $"{emp} has destroyed {loc}.",
            NewsType.FleetsDestroyedInExplosion => $"{loc} destroyed by explosion.",
            NewsType.FleetStoppedByDisrupter => $"{loc} has been stopped by {emp} disrupter.",
            NewsType.OutOfTrillumReserves => $"All trillum deposits on {loc} have been exhausted.",
            NewsType.TrillumReservesVeryLow => $"Trillum deposits on {loc} are nearly depleted.",
            NewsType.TrillumReservesLow => $"Trillum deposits on {loc} are very low.",
            NewsType.TroopsWantOut => $"Demonstrations on {loc} call for removal of imperial troops.",
            NewsType.RebellionQuietedByMilitary => $"Imperial troops on {loc} disband angry rioters.",
            NewsType.EnemyAttackedEmpireGlobal => $"{emp} has attacked {loc} ({item.Defender?.Name}). Attack Failed.",
            NewsType.EnemyConqueredWorldGlobal => $"{emp} has conquered {loc} ({item.Defender?.Name}).",
            NewsType.EnemyConqueredCapitalGlobal => $"{emp} has attacked and conquered the {item.Defender?.Name} capital.",
            NewsType.EmpireLamStrikeGlobal => $"{emp} has hit {loc} ({item.Defender?.Name}) with LAMs.",
            NewsType.WorldRevoltedGlobal => $"{loc} has declared independence from {emp}.",
            _ => $"({item.Headline})",
        };

        return line;
    }
}
