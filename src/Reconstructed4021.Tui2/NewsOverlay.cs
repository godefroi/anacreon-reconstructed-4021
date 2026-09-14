using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Presentation;
using Reconstructed4021.Core.Types;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;

namespace Reconstructed4021.Tui2;

// F7 (NWSWIND.PAS: NewsWindow). Single-pane -- one scrollable list, general news first then local
// (NWSWIND.PAS's own LocalNews set), each block keeping the empire's own existing order.
internal sealed class NewsOverlay : IOverlay
{
    private const int NoOfLines = 19; // NWSWIND.PAS: NoOfLines:=InitHeight-2, InitHeight=21.
    private const int Width = 90;

    // Fixed at the window's own size at 80x25, not shrink-wrapped to the news list's own length --
    // real Pascal's own window is always full height regardless of how much news there is to show.
    private const int Height = NoOfLines + 2;

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

    // DATACNST.PAS's IndusNames (:121-130), 0-based, ordinal-aligned with IndustryType.
    private static readonly string[] IndustryNames = [
        "bio-tech labs", "chemical plants", "metal mines", "ship yards", "jumpship yards",
        "starship yards", "transport yards", "food factories", "trillum mines",
    ];

    // DATACNST.PAS's TechN (:152-163) -- full tech-level names, ordinal-aligned with TechLevel.
    private static readonly string[] TechLevelNames = [
        "pre-tech", "primitive", "pre-atomic", "atomic", "pre-warp", "warp", "jump", "bio-tech",
        "starship", "pre-gate", "gate",
    ];

    private readonly Empire _viewer;
    private readonly Coordinate _origin;
    private readonly ListBox<NewsItem> _list;
    private readonly Action<ISectorObject> _onSelectSubject;

    public bool IsDismissed { get; private set; }

    public NewsOverlay(Empire viewer, Coordinate origin, Action<ISectorObject> onSelectSubject)
    {
        _viewer = viewer;
        _origin = origin;
        _onSelectSubject = onSelectSubject;
        var rows = viewer.News.Where(n => !LocalHeadlines.Contains(n.Headline))
            .Concat(viewer.News.Where(n => LocalHeadlines.Contains(n.Headline)))
            .ToList();
        _list = new ListBox<NewsItem>(rows, FormatLine);
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_list.HandleKey(key))
        {
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.Enter when _list.SelectedItem?.Subject is { } subject:
                // Stacks Close Up on top instead of dismissing -- see StatusOverlay's own comment.
                _onSelectSubject(subject);
                break;
            case ConsoleKey.Escape or ConsoleKey.F7:
                IsDismissed = true;
                break;
        }
    }

    private string LocationName(NewsItem item) => item.Subject switch
    {
        { } subject => CloseUpOverlay.DisplayName(subject, _viewer),
        null when item.Position is { } position => RelativeCoordinate.Format(position, _origin),
        null => "(unknown location)",
    };

    private static string DeathToll(int tens) => tens < 100 ? $"{tens * 10} million" : $"{tens / 100.0:0.0} billion";

    private string FormatLine(NewsItem item)
    {
        var loc = LocationName(item);
        var emp = item.OtherEmpire?.Name ?? "";
        var death = DeathToll(item.Parm1);

        return item.Headline switch
        {
            NewsType.LacksRawMaterial or NewsType.ConstructionLacksRawMaterial =>
                $"{loc} lacks {item.Resource!.DisplayName}.",
            NewsType.DefensesLackResources => $"{loc} needs {item.Resource!.DisplayName} to build defenses.",
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
            NewsType.DestructionDetail => $"   {item.Parm1} {item.Resource!.DisplayName} destroyed.",
            NewsType.ShipsOrCargoTransferredToYou => $"{loc} has received the following resources from {emp}:",
            NewsType.TransferDetail => $"   {item.Parm1} {item.Resource!.DisplayName}",
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
    }

    public void Draw(FrameBuffer fb)
    {
        var width = Math.Min(Width, fb.Width);
        var height = Math.Min(Height, fb.Height);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        fb.DrawText(x + Math.Max(1, (width - 6) / 2), y, " News ", ConsoleColor.White, ConsoleColor.Black);

        if (_list.Items.Count == 0)
        {
            fb.DrawText(x + 1, y + 1, "No news.", ConsoleColor.Gray, ConsoleColor.Black);
            return;
        }

        _list.Draw(fb, x + 1, y + 1, width - 2, height - 2, ConsoleColor.Gray, ConsoleColor.Black, ConsoleColor.Black, ConsoleColor.Gray);
    }
}
