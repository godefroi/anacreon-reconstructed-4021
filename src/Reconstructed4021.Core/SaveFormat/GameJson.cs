using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Npe;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.SaveFormat;

/// <summary>
/// This port's native, ongoing save format: object-graph serialization of <see cref="Game"/>.
/// </summary>
/// <remarks>
/// <c>ReferenceHandler.Preserve</c> cannot handle the real reference cycles here, for four separate
/// reasons that all trace to the same root cause. <c>JsonObjectCreationHandling.Populate</c> rejects
/// any <c>ReferenceHandler</c> outright. A converter that delegates via a nested
/// <c>JsonSerializer.Serialize(writer, ...)</c> call starts a *fresh* <c>WriteStack</c> that doesn't
/// share the outer reference-tracking session, so real cycles blow through the depth guard instead
/// of terminating via <c>$ref</c>. And <c>Preserve</c>'s own metadata wrapping is flatly unsupported
/// on constructor-bound parameters, which both <see cref="Game"/>'s and <see cref="NewsItem"/>'s
/// constructors are. <c>ReferenceHandler</c> simply doesn't compose with the rest of
/// System.Text.Json's object model, and reshaping load-bearing domain types (<see cref="Game"/>'s
/// constructor, <see cref="NewsItem"/>'s positional shape — both cited in their own doc comments as
/// deliberate) just to appease a serializer would invert the dependency this format is supposed to
/// respect.
///
/// So: no <c>ReferenceHandler</c> at all. Every entity reference (<see cref="ISectorObject.Owner"/>,
/// <see cref="Empire.Capital"/>/<see cref="Empire.DefeatedBy"/>, <see cref="Game.CurrentEmpire"/>,
/// <see cref="NewsItem.Subject"/>/<see cref="NewsItem.OtherEmpire"/>/<see cref="NewsItem.Defender"/>,
/// <see cref="EntityVisibility{T}"/>'s sets) is instead a stable per-kind integer id — the same shape
/// of problem the `.SAV` format's own <c>IDNumber</c> solves, just for JSON — resolved through
/// <see cref="EntityIndex"/> (write) / <see cref="EntityLookup"/> (read). This sidesteps every one of
/// the incompatibilities above: no metadata wrapping, so constructor binding works; no shared object
/// identity to track, so no cycles; ids round-trip as plain values.
///
/// <see cref="Planet"/>/<see cref="Starbase"/>/<see cref="Fleet"/>/<see cref="Stargate"/>/
/// <see cref="ConstructionSite"/> have no forward-reference problem of their own (nothing needs to
/// reference one before its own fields are known), so they still flow through ordinary reflection —
/// only their <c>Owner</c> property gets a per-property <see cref="JsonConverter"/> override
/// (<see cref="EmpireRefConverter"/>, wired in via a <see cref="IJsonTypeInfoResolver"/> modifier,
/// the same mechanism <c>[JsonDerivedType]</c> would use, just applied in code so no
/// System.Text.Json attribute needs to land on the entity types themselves).
///
/// <see cref="Empire"/> itself is different: it's simultaneously referenced by other entities
/// (<c>Owner</c>) before its own data is known, forcing pre-allocated placeholder identity — the
/// exact same shape of problem <see cref="SavGameLoader"/> already solved with its own
/// placeholder-Empire-slots trick — and it's the one type this format never runs through automatic
/// reflection at all: <see cref="WriteEmpires"/>/<see cref="FillEmpireFields"/> read/write every
/// field by hand. <see cref="Game.TurnHandlers"/>/<see cref="Game.UnimplementedNpeBlobs"/> are
/// Empire-keyed with <see cref="ITurnHandler"/> having exactly one real implementor
/// (<see cref="KingdomTurnHandler"/>), and <see cref="Galaxy.Galaxy"/>'s nebula/minefield/mine-scout
/// data has no public enumerator — both hand-written for the same reason.
/// </remarks>
public static class GameJson
{
    /// <summary>For pure-value sub-objects with no entity references at all (<see cref="NpeCharacter"/>, <see cref="Npe.StateDeptRecord"/>, <c>List&lt;Coordinate&gt;</c>, <c>List&lt;LocationBookmark&gt;</c>) — safe to share statically since it carries no per-call state.</summary>
    private static readonly JsonSerializerOptions PlainOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(Game game)
    {
        var index = new EntityIndex(game);
        var graphOptions = BuildGraphOptions(index, lookup: null);

        var node = JsonSerializer.SerializeToNode(game, graphOptions)!.AsObject();
        node["currentEmpireId"] = EmpireIdOrNull(index, game.CurrentEmpire);

        // TurnHandlers/blobs/minefields/mineScoutedBy first -- see EntityIndex's own remarks on why
        // an empire can still be discovered only here, never in game.Empires (a Kingdom's own State
        // keys, or -- confirmed by SavGameWriter's own equivalent, see its remarks -- a minefield
        // owner/scout referencing an empire that was already not-InUse when the .SAV this Game was
        // originally loaded from was captured), and why "empires" must be written last so any such
        // discovery is already reflected in it.
        var turnHandlersNode = WriteTurnHandlers(game, index);
        var blobsNode = WriteBlobs(game, index);
        var minefieldsNode = WriteMinefields(game.Galaxy, index);
        var mineScoutedByNode = WriteMineScoutedBy(game.Galaxy, index);
        var messagesNode = WriteMessages(game, index);

        node["realEmpireCount"] = game.Empires.Count;
        node["empires"] = WriteEmpires(index);
        node["nebulae"] = WriteNebulae(game.Galaxy);
        node["minefields"] = minefieldsNode;
        node["mineScoutedBy"] = mineScoutedByNode;
        node["turnHandlers"] = turnHandlersNode;
        node["unimplementedNpeBlobs"] = blobsNode;
        node["messages"] = messagesNode;

        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>The inverse of <see cref="Serialize"/>.</summary>
    /// <param name="json">A JSON document previously produced by <see cref="Serialize"/>.</param>
    /// <param name="random">
    /// Seeds any reconstructed <see cref="KingdomTurnHandler"/>'s ongoing RNG stream — a loaded
    /// empire's future turns, not its saved state, need this; same rationale as
    /// <see cref="SavGameLoader"/>'s own optional <c>Random</c> parameter.
    /// </param>
    public static Game Deserialize(string json, Random? random = null)
    {
        var root = JsonNode.Parse(json)!.AsObject();

        // Placeholder Empire identity first -- Planet/Starbase/etc's Owner needs real object
        // identity to resolve against before Empire's own fields (which may themselves reference
        // Planets/Starbases, e.g. Capital) can be filled in. Same two-phase shape as
        // SavGameLoader's own placeholder-Empire-slots. empiresJson can hold more entries than
        // realEmpireCount -- "orphan" empires discovered only via e.g. a Kingdom empire's own
        // diplomatic memory (EntityIndex's own remarks) -- those still need real, stable object
        // identity, they just never join game.Empires below.
        var empiresJson = root["empires"]!.AsArray();
        var realEmpireCount = (int)root["realEmpireCount"]!;
        var empires = new List<Empire>();
        for (var i = 0; i < empiresJson.Count; i++) {
            empires.Add(new Empire { Name = "" });
        }

        var lookup = new EntityLookup(empires);
        var graphOptions = BuildGraphOptions(index: null, lookup);

        var galaxyNode = root["galaxy"]!.AsObject();
        var fleetsNode = galaxyNode["fleets"]!.AsArray();
        var galaxy = new Galaxy.Galaxy((int)galaxyNode["size"]!);
        galaxy.Planets.AddRange(galaxyNode["planets"].Deserialize<List<Planet>>(graphOptions)!);
        galaxy.Starbases.AddRange(galaxyNode["starbases"].Deserialize<List<Starbase>>(graphOptions)!);
        galaxy.Fleets.AddRange(fleetsNode.Deserialize<List<Fleet>>(graphOptions)!);
        galaxy.Stargates.AddRange(galaxyNode["stargates"].Deserialize<List<Stargate>>(graphOptions)!);
        galaxy.ConstructionSites.AddRange(galaxyNode["constructionSites"].Deserialize<List<ConstructionSite>>(graphOptions)!);
        lookup.AttachGalaxy(galaxy);

        // Fleet.Orders is filled in only now, not by the reflection pass above (FleetOrderListConverter.Read
        // skips its own value there -- see that class's own doc comment): FleetOrder.DestinationObject can
        // reference any of the 5 ISectorObject kinds, including a Fleet, which would still be forward
        // references (this same list, still mid-deserialization) if resolved eagerly -- the exact
        // two-phase shape FillEmpireFields below already uses for Empire's own cross-referencing fields.
        for (var i = 0; i < galaxy.Fleets.Count; i++) {
            galaxy.Fleets[i].Orders.AddRange(ReadFleetOrders(((JsonObject)fleetsNode[i]!)["orders"]!.AsArray(), lookup));
        }

        ReadNebulae(root["nebulae"], galaxy);
        ReadMinefields(root["minefields"], galaxy, lookup);
        ReadMineScoutedBy(root["mineScoutedBy"], galaxy, lookup);

        for (var i = 0; i < empiresJson.Count; i++) {
            FillEmpireFields(empires[i], (JsonObject)empiresJson[i]!, lookup);
        }

        var game = new Game(galaxy);
        game.Empires.AddRange(empires.Take(realEmpireCount));
        game.Year = (int)root["year"]!;
        game.ScenarioFilename = (string?)root["scenarioFilename"];
        game.CurrentEmpire = root["currentEmpireId"] is { } currentEmpireIdNode ? lookup.Empire((int)currentEmpireIdNode) : null;
        game.TimePerTurn = (int)root["timePerTurn"]!;
        game.AutoSave = (bool)root["autoSave"]!;
        game.AsyncTurns = (bool)root["asyncTurns"]!;
        game.PauseActive = (bool)root["pauseActive"]!;
        game.ReEnterGame = (bool)root["reEnterGame"]!;

        ReadTurnHandlers(root["turnHandlers"], game, lookup, random ?? new Random());
        ReadBlobs(root["unimplementedNpeBlobs"], game, lookup);
        ReadMessages(root["messages"], game, lookup);

        return game;
    }

    /// <summary>
    /// Options for the part of the graph still reflection-driven (<see cref="Game"/>'s scalars,
    /// <see cref="Galaxy.Galaxy"/> and its 5 entity lists): overrides the <c>Owner</c> property on
    /// each of the 5 <see cref="ISectorObject"/> implementors to go through
    /// <see cref="EmpireRefConverter"/>; their <c>Names</c> property (<see cref="ISectorObject.Names"/>
    /// — an object-owned per-empire bookmark dictionary, see that property's own remarks) to go
    /// through <see cref="NamesConverter"/>; and <see cref="Fleet"/>'s own <c>Orders</c> property (its
    /// <see cref="FleetOrder.DestinationObject"/> has the exact same forward-reference problem
    /// <c>Owner</c> does, only worse -- see <see cref="FleetOrderListConverter"/>'s own doc comment
    /// for why its read side is a no-op) through <see cref="FleetOrderListConverter"/>. Built fresh
    /// per call (not cached) since every converter closes over this call's <paramref name="index"/>/
    /// <paramref name="lookup"/>.
    /// </summary>
    private static JsonSerializerOptions BuildGraphOptions(EntityIndex? index, EntityLookup? lookup)
    {
        var empireRefConverter = new EmpireRefConverter(index, lookup);
        var namesConverter = new NamesConverter(index, lookup);
        var fleetOrderListConverter = new FleetOrderListConverter(index);
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(typeInfo => {
            if (typeInfo.Type != typeof(Planet) && typeInfo.Type != typeof(Starbase) && typeInfo.Type != typeof(Fleet) &&
                typeInfo.Type != typeof(Stargate) && typeInfo.Type != typeof(ConstructionSite)) {
                return;
            }

            var owner = typeInfo.Properties.First(p => p.Name == "owner");
            owner.CustomConverter = empireRefConverter;

            var names = typeInfo.Properties.First(p => p.Name == "names");
            names.CustomConverter = namesConverter;

            if (typeInfo.Type == typeof(Fleet)) {
                var orders = typeInfo.Properties.First(p => p.Name == "orders");
                orders.CustomConverter = fleetOrderListConverter;
            }
        });

        return new JsonSerializerOptions {
            TypeInfoResolver = resolver,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
        };
    }

    private static JsonNode? EmpireIdOrNull(EntityIndex index, Empire? empire) => empire is null ? null : index.EmpireId(empire);

    // ---- Empires: hand-written (see class doc comment) ----

    private static JsonArray WriteEmpires(EntityIndex index)
    {
        var array = new JsonArray();

        // index.AllEmpires, not game.Empires: by this point WriteTurnHandlers/WriteBlobs/
        // WriteMinefields/WriteMineScoutedBy have already run and may have lazily discovered
        // "orphan" empires (index's own remarks) that must round-trip too, even though they're not
        // (or no longer) members of game.Empires.
        foreach (var empire in index.AllEmpires) {
            array.Add(new JsonObject {
                ["name"] = empire.Name,
                ["password"] = empire.Password,
                ["isEmpress"] = empire.IsEmpress,
                ["capital"] = index.EncodeObjectRef(empire.Capital),
                ["status"] = empire.Status.ToString(),
                ["defeatedBy"] = EmpireIdOrNull(index, empire.DefeatedBy),
                ["npeType"] = empire.NpeType?.ToString(),
                ["defenseSettings"] = WriteDefenseSettings(empire.DefenseSettings),
                ["probesInTransit"] = JsonSerializer.SerializeToNode(empire.ProbesInTransit, PlainOptions),
                ["bookmarks"] = JsonSerializer.SerializeToNode(empire.Bookmarks, PlainOptions),
                ["totalRevolutionIndex"] = empire.TotalRevolutionIndex,
                ["revolutionFactor"] = empire.RevolutionFactor,
                ["technologyLevel"] = empire.TechnologyLevel.ToString(),
                ["technology"] = WriteTechnology(empire.Technology),
                ["foundingYear"] = empire.FoundingYear,
                ["losesIfCapitalConquered"] = empire.LosesIfCapitalConquered,
                ["planetsKnown"] = WriteIds(empire.Planets.Known, index.Planets),
                ["planetsScouted"] = WriteIds(empire.Planets.Scouted, index.Planets),
                ["starbasesKnown"] = WriteIds(empire.Starbases.Known, index.Starbases),
                ["starbasesScouted"] = WriteIds(empire.Starbases.Scouted, index.Starbases),
                ["fleetsKnown"] = WriteIds(empire.Fleets.Known, index.Fleets),
                ["fleetsScouted"] = WriteIds(empire.Fleets.Scouted, index.Fleets),
                ["stargatesKnown"] = WriteIds(empire.Stargates.Known, index.Stargates),
                ["stargatesScouted"] = WriteIds(empire.Stargates.Scouted, index.Stargates),
                ["constructionSitesKnown"] = WriteIds(empire.ConstructionSites.Known, index.ConstructionSites),
                ["constructionSitesScouted"] = WriteIds(empire.ConstructionSites.Scouted, index.ConstructionSites),
                ["news"] = WriteNews(empire.News, index),
            });
        }

        return array;
    }

    private static void FillEmpireFields(Empire empire, JsonObject node, EntityLookup lookup)
    {
        empire.Name = (string)node["name"]!;
        empire.Password = (string?)node["password"];
        empire.IsEmpress = (bool)node["isEmpress"]!;
        empire.Capital = lookup.DecodeObjectRef(node["capital"]) as IEconomicWorld;
        empire.Status = Enum.Parse<EmpireStatus>((string)node["status"]!);
        empire.DefeatedBy = node["defeatedBy"] is { } defeatedByNode ? lookup.Empire((int)defeatedByNode) : null;
        empire.NpeType = node["npeType"] is { } npeTypeNode ? Enum.Parse<NpeEmpireType>((string)npeTypeNode!) : null;
        ReadDefenseSettingsInto(empire.DefenseSettings, (JsonObject)node["defenseSettings"]!);
        empire.ProbesInTransit.AddRange(node["probesInTransit"].Deserialize<List<Coordinate>>(PlainOptions)!);
        empire.Bookmarks.AddRange(node["bookmarks"].Deserialize<List<LocationBookmark>>(PlainOptions)!);
        empire.TotalRevolutionIndex = (int)node["totalRevolutionIndex"]!;
        empire.RevolutionFactor = (int)node["revolutionFactor"]!;
        empire.TechnologyLevel = Enum.Parse<TechLevel>((string)node["technologyLevel"]!);
        ReadTechnologyInto(empire.Technology, (JsonObject)node["technology"]!);
        empire.FoundingYear = (int)node["foundingYear"]!;
        empire.LosesIfCapitalConquered = (bool)node["losesIfCapitalConquered"]!;

        MarkVisibility(empire.Planets, node["planetsKnown"]!.AsArray(), node["planetsScouted"]!.AsArray(), lookup.Planet);
        MarkVisibility(empire.Starbases, node["starbasesKnown"]!.AsArray(), node["starbasesScouted"]!.AsArray(), lookup.Starbase);
        MarkVisibility(empire.Fleets, node["fleetsKnown"]!.AsArray(), node["fleetsScouted"]!.AsArray(), lookup.Fleet);
        MarkVisibility(empire.Stargates, node["stargatesKnown"]!.AsArray(), node["stargatesScouted"]!.AsArray(), lookup.Stargate);
        MarkVisibility(empire.ConstructionSites, node["constructionSitesKnown"]!.AsArray(), node["constructionSitesScouted"]!.AsArray(), lookup.ConstructionSite);

        foreach (var newsNode in node["news"]!.AsArray()) {
            empire.News.Add(ReadNewsItem((JsonObject)newsNode!, lookup));
        }
    }

    private static JsonArray WriteIds<T>(IEnumerable<T> entities, Dictionary<T, int> ids) where T : notnull
    {
        var array = new JsonArray();
        foreach (var entity in entities) {
            array.Add(ids[entity]);
        }
        return array;
    }

    private static void MarkVisibility<T>(EntityVisibility<T> visibility, JsonArray known, JsonArray scouted, Func<int, T> resolve) where T : notnull
    {
        foreach (var idNode in known) {
            visibility.MarkKnown(resolve((int)idNode!));
        }
        foreach (var idNode in scouted) {
            visibility.MarkScouted(resolve((int)idNode!));
        }
    }

    private static JsonArray WriteNews(List<NewsItem> news, EntityIndex index)
    {
        var array = new JsonArray();

        foreach (var item in news) {
            array.Add(new JsonObject {
                ["headline"] = item.Headline.ToString(),
                ["subject"] = index.EncodeObjectRef(item.Subject),
                ["position"] = item.Position is { } position ? new JsonObject { ["x"] = position.X, ["y"] = position.Y } : null,
                ["otherEmpire"] = EmpireIdOrNull(index, item.OtherEmpire),
                ["techGrant"] = item.TechGrant is { } techGrant
                    ? new JsonObject { ["category"] = techGrant.Category.ToString(), ["ordinal"] = techGrant.Ordinal }
                    : null,
                ["parm1"] = item.Parm1,
                ["parm2"] = item.Parm2,
                ["parm3"] = item.Parm3,
                ["defender"] = EmpireIdOrNull(index, item.Defender),
            });
        }

        return array;
    }

    private static NewsItem ReadNewsItem(JsonObject node, EntityLookup lookup)
    {
        var headline = Enum.Parse<NewsType>((string)node["headline"]!);
        var subject = lookup.DecodeObjectRef(node["subject"]);
        var position = node["position"] is JsonObject positionNode
            ? new Coordinate((int)positionNode["x"]!, (int)positionNode["y"]!)
            : (Coordinate?)null;
        var otherEmpire = node["otherEmpire"] is { } otherEmpireNode ? lookup.Empire((int)otherEmpireNode) : null;
        var techGrant = node["techGrant"] is JsonObject techGrantNode
            ? new TechCatalog.TechGrantIdentity(Enum.Parse<TechCategory>((string)techGrantNode["category"]!), (int)techGrantNode["ordinal"]!)
            : (TechCatalog.TechGrantIdentity?)null;
        var defender = node["defender"] is { } defenderNode ? lookup.Empire((int)defenderNode) : null;

        return new NewsItem(headline, subject, position, otherEmpire, techGrant, (int)node["parm1"]!, (int)node["parm2"]!, (int)node["parm3"]!, defender);
    }

    /// <summary>
    /// <see cref="Fleet.Orders"/>'s real read-side decode -- see <see cref="Deserialize"/>'s own
    /// remarks on why this runs as a second pass, after <c>lookup</c> has a full galaxy attached,
    /// rather than inline via <see cref="FleetOrderListConverter"/>.
    /// </summary>
    private static List<FleetOrder> ReadFleetOrders(JsonArray array, EntityLookup lookup)
    {
        var orders = new List<FleetOrder>();

        foreach (var node in array) {
            var obj = node!.AsObject();
            var type = Enum.Parse<CommandType>((string)obj["type"]!);
            var destinationObject = lookup.DecodeObjectRef(obj["destinationObject"]);
            var destinationPosition = obj["destinationPosition"] is JsonObject positionNode
                ? new Coordinate((int)positionNode["x"]!, (int)positionNode["y"]!)
                : (Coordinate?)null;
            var transferShip = obj["transferShip"] is { } shipNode ? Enum.Parse<ShipType>((string)shipNode!) : (ShipType?)null;
            var transferCargo = obj["transferCargo"] is { } cargoNode ? Enum.Parse<CargoType>((string)cargoNode!) : (CargoType?)null;
            var transferAmount = (int)obj["transferAmount"]!;

            orders.Add(new FleetOrder(type, destinationObject, destinationPosition, transferShip, transferCargo, transferAmount));
        }

        return orders;
    }

    // ---- DefenseSettings / UnlockedTechnology: hand-written -- pure value data, but nested inside
    // Empire, which (see class doc comment) never goes through automatic reflection at all. ----

    private static JsonObject WriteDefenseSettings(DefenseSettings settings) => new() {
        ["fleets"] = WriteShellDefensePlan(settings.Fleets),
        ["starbases"] = WriteShellDefensePlan(settings.Starbases),
    };

    private static void ReadDefenseSettingsInto(DefenseSettings settings, JsonObject node)
    {
        ReadShellDefensePlanInto(settings.Fleets, (JsonObject)node["fleets"]!);
        ReadShellDefensePlanInto(settings.Starbases, (JsonObject)node["starbases"]!);
    }

    private static JsonObject WriteShellDefensePlan(ShellDefensePlan plan)
    {
        var obj = new JsonObject();
        foreach (var position in Enum.GetValues<ShellPosition>()) {
            obj[position.ToString()] = WriteIndexed<ShipType>(t => plan[position][t]);
        }
        return obj;
    }

    private static void ReadShellDefensePlanInto(ShellDefensePlan plan, JsonObject node)
    {
        foreach (var position in Enum.GetValues<ShellPosition>()) {
            ReadIndexedInto<ShipType>((JsonObject)node[position.ToString()]!, (t, v) => plan[position][t] = v);
        }
    }

    private static JsonObject WriteTechnology(UnlockedTechnology technology) => new() {
        ["ships"] = new JsonArray([.. technology.Ships.Select(t => (JsonNode)t.ToString())]),
        ["defenses"] = new JsonArray([.. technology.Defenses.Select(t => (JsonNode)t.ToString())]),
        ["constructions"] = new JsonArray([.. technology.Constructions.Select(t => (JsonNode)t.ToString())]),
        ["resources"] = new JsonArray([.. technology.Resources.Select(t => (JsonNode)t.ToString())]),
    };

    private static void ReadTechnologyInto(UnlockedTechnology technology, JsonObject node)
    {
        foreach (var s in node["ships"]!.AsArray()) {
            technology.Ships.Add(Enum.Parse<ShipType>((string)s!));
        }
        foreach (var s in node["defenses"]!.AsArray()) {
            technology.Defenses.Add(Enum.Parse<DefenseType>((string)s!));
        }
        foreach (var s in node["constructions"]!.AsArray()) {
            technology.Constructions.Add(Enum.Parse<ConstructionType>((string)s!));
        }
        foreach (var s in node["resources"]!.AsArray()) {
            technology.Resources.Add(Enum.Parse<CargoType>((string)s!));
        }
    }

    /// <summary>Shared shape behind <see cref="ShipCounts"/>/<see cref="CargoHold"/>/<see cref="DefenseCounts"/>/<see cref="IndustryLevels"/>/<see cref="ShipDistribution"/>'s own <c>this[TEnum]</c> indexers.</summary>
    private static JsonObject WriteIndexed<TEnum>(Func<TEnum, int> get) where TEnum : struct, Enum
    {
        var obj = new JsonObject();
        foreach (var v in Enum.GetValues<TEnum>()) {
            obj[v.ToString()] = get(v);
        }
        return obj;
    }

    /// <summary>See <see cref="WriteIndexed{TEnum}"/>.</summary>
    private static void ReadIndexedInto<TEnum>(JsonObject node, Action<TEnum, int> set) where TEnum : struct, Enum
    {
        foreach (var v in Enum.GetValues<TEnum>()) {
            set(v, (int)node[v.ToString()]!);
        }
    }

    // ---- Galaxy.NebulaData / MinefieldData / MineScoutedByData: hand-written (no public enumerator) ----

    private static JsonArray WriteNebulae(Galaxy.Galaxy galaxy)
    {
        var array = new JsonArray();
        foreach (var (coordinate, type) in galaxy.NebulaData) {
            array.Add(new JsonObject { ["x"] = coordinate.X, ["y"] = coordinate.Y, ["type"] = type.ToString() });
        }
        return array;
    }

    private static void ReadNebulae(JsonNode? node, Galaxy.Galaxy galaxy)
    {
        if (node is null) {
            return;
        }

        foreach (var entryNode in node.AsArray()) {
            var entry = entryNode!.AsObject();
            var coordinate = new Coordinate((int)entry["x"]!, (int)entry["y"]!);
            galaxy.SetNebula(coordinate, Enum.Parse<NebulaType>((string)entry["type"]!));
        }
    }

    private static JsonArray WriteMinefields(Galaxy.Galaxy galaxy, EntityIndex index)
    {
        var array = new JsonArray();
        foreach (var (coordinate, owner) in galaxy.MinefieldData) {
            array.Add(new JsonObject { ["x"] = coordinate.X, ["y"] = coordinate.Y, ["ownerId"] = index.EmpireId(owner) });
        }
        return array;
    }

    private static void ReadMinefields(JsonNode? node, Galaxy.Galaxy galaxy, EntityLookup lookup)
    {
        if (node is null) {
            return;
        }

        foreach (var entryNode in node.AsArray()) {
            var entry = entryNode!.AsObject();
            var coordinate = new Coordinate((int)entry["x"]!, (int)entry["y"]!);
            galaxy.SetMine(coordinate, lookup.Empire((int)entry["ownerId"]!));
        }
    }

    private static JsonArray WriteMineScoutedBy(Galaxy.Galaxy galaxy, EntityIndex index)
    {
        var array = new JsonArray();
        foreach (var (coordinate, scouts) in galaxy.MineScoutedByData) {
            array.Add(new JsonObject {
                ["x"] = coordinate.X,
                ["y"] = coordinate.Y,
                ["empireIds"] = new JsonArray([.. scouts.Select(e => (JsonNode)index.EmpireId(e))]),
            });
        }
        return array;
    }

    private static void ReadMineScoutedBy(JsonNode? node, Galaxy.Galaxy galaxy, EntityLookup lookup)
    {
        if (node is null) {
            return;
        }

        foreach (var entryNode in node.AsArray()) {
            var entry = entryNode!.AsObject();
            var coordinate = new Coordinate((int)entry["x"]!, (int)entry["y"]!);
            foreach (var idNode in entry["empireIds"]!.AsArray()) {
                galaxy.MarkMineScouted(lookup.Empire((int)idNode!), coordinate);
            }
        }
    }

    // ---- Game.TurnHandlers / Game.UnimplementedNpeBlobs / Game.Messages: hand-written (see class doc comment) ----

    private static JsonArray WriteTurnHandlers(Game game, EntityIndex index)
    {
        var array = new JsonArray();

        foreach (var (empire, handler) in game.TurnHandlers) {
            // A human has no persisted AI state -- HumanTurnHandler.PlayTurn is a no-op, see its own
            // doc comment -- so it's simply not written; ReadTurnHandlers reconstructs a fresh one on
            // load for every empire with NpeType is null, the same way ScenarioLoader registers one
            // for a freshly created game.
            if (handler is HumanTurnHandler) {
                continue;
            }

            if (handler is not KingdomTurnHandler kingdom) {
                throw new NotSupportedException(
                    $"{nameof(GameJson)} only knows how to serialize {nameof(KingdomTurnHandler)}; " +
                    $"empire '{empire.Name}' has a {handler.GetType().Name}.");
            }

            array.Add(new JsonObject {
                ["empireId"] = index.EmpireId(empire),
                ["persona"] = JsonSerializer.SerializeToNode(kingdom.Persona, PlainOptions),
                ["state"] = WriteState(kingdom.State, index),
                ["fleetStates"] = WriteFleetStates(kingdom.FleetStates, index),
            });
        }

        return array;
    }

    private static JsonArray WriteState(IReadOnlyDictionary<Empire, StateDeptRecord> state, EntityIndex index)
    {
        var array = new JsonArray();

        foreach (var (empire, record) in state) {
            array.Add(new JsonObject {
                ["empireId"] = index.EmpireId(empire),
                ["record"] = JsonSerializer.SerializeToNode(record, PlainOptions),
            });
        }

        return array;
    }

    // A KingdomTurnHandler's own FleetStates can genuinely hold a key for a fleet that's no longer in
    // game.Galaxy.Fleets: NpeToolkit.EnforceNpeDataLinks (its own real pruning, NPEINTR.PAS's
    // EnforceNPEDataLinks) only runs at the start of that empire's own PlayTurn, so a Kingdom fleet
    // destroyed mid-turn by the human (combat, most directly) leaves a dangling entry until that
    // Kingdom's own next turn -- a real, live window, not a corrupted state, and Serialize can be
    // called (via Save Game) at any point during the human's own turn, well inside it. Confirmed via a
    // real crash (KeyNotFoundException) hit live, not a hypothetical. Skipping a stale entry here
    // matches what EnforceNpeDataLinks would remove anyway on that empire's own next turn -- there's
    // nothing meaningful left to persist for a fleet that's already gone.
    private static JsonArray WriteFleetStates(IReadOnlyDictionary<Fleet, KingdomFleetState> fleetStates, EntityIndex index)
    {
        var array = new JsonArray();

        foreach (var (fleet, state) in fleetStates) {
            if (!index.Fleets.TryGetValue(fleet, out var fleetId)) {
                continue;
            }

            array.Add(new JsonObject {
                ["fleetId"] = fleetId,
                ["mission"] = state.Mission.ToString(),
                ["target"] = index.EncodeObjectRef(state.Target),
                ["homeBase"] = index.EncodeObjectRef(state.HomeBase),
                ["waiting"] = state.Waiting,
            });
        }

        return array;
    }

    private static JsonArray WriteBlobs(Game game, EntityIndex index)
    {
        var array = new JsonArray();

        foreach (var (empire, bytes) in game.UnimplementedNpeBlobs) {
            array.Add(new JsonObject {
                ["empireId"] = index.EmpireId(empire),
                ["data"] = Convert.ToBase64String(bytes),
            });
        }

        return array;
    }

    private static void ReadTurnHandlers(JsonNode? node, Game game, EntityLookup lookup, Random random)
    {
        foreach (var entryNode in node?.AsArray() ?? []) {
            var entry = entryNode!.AsObject();
            var empire = lookup.Empire((int)entry["empireId"]!);
            var persona = entry["persona"].Deserialize<NpeCharacter>(PlainOptions)!;
            var state = ReadState(entry["state"]!.AsArray(), lookup);
            var fleetStates = ReadFleetStates(entry["fleetStates"]!.AsArray(), lookup);

            var npeType = empire.NpeType ?? throw new InvalidDataException(
                $"Empire '{empire.Name}' has a saved Kingdom turn handler but no NpeType.");

            game.TurnHandlers[empire] = new KingdomTurnHandler(npeType, persona, state, fleetStates, random);
        }

        // Mirrors ScenarioLoader.RunCreatePlayerEmpire's own registration -- WriteTurnHandlers never
        // writes a HumanTurnHandler (nothing to persist), so every human empire needs a fresh one
        // reconstructed here instead. game.Empires only (not every lookup-known id): an "orphan"
        // empire reachable only via a Kingdom's own diplomatic memory is never itself a live roster
        // member TurnEngine would dispatch to.
        foreach (var empire in game.Empires) {
            if (empire.NpeType is null) {
                game.TurnHandlers[empire] = new HumanTurnHandler();
            }
        }
    }

    private static Dictionary<Empire, StateDeptRecord> ReadState(JsonArray array, EntityLookup lookup)
    {
        var state = new Dictionary<Empire, StateDeptRecord>();

        foreach (var entryNode in array) {
            var entry = entryNode!.AsObject();
            var empire = lookup.Empire((int)entry["empireId"]!);
            state[empire] = entry["record"].Deserialize<StateDeptRecord>(PlainOptions)!;
        }

        return state;
    }

    private static Dictionary<Fleet, KingdomFleetState> ReadFleetStates(JsonArray array, EntityLookup lookup)
    {
        var fleetStates = new Dictionary<Fleet, KingdomFleetState>();

        foreach (var entryNode in array) {
            var entry = entryNode!.AsObject();
            var fleet = lookup.Fleet((int)entry["fleetId"]!);

            fleetStates[fleet] = new KingdomFleetState {
                Mission = Enum.Parse<NpeMissionType>((string)entry["mission"]!),
                Target = lookup.DecodeObjectRef(entry["target"]),
                HomeBase = lookup.DecodeObjectRef(entry["homeBase"]) as IEconomicWorld,
                Waiting = (int)entry["waiting"]!,
            };
        }

        return fleetStates;
    }

    private static void ReadBlobs(JsonNode? node, Game game, EntityLookup lookup)
    {
        if (node is null) {
            return;
        }

        foreach (var entryNode in node.AsArray()) {
            var entry = entryNode!.AsObject();
            var empire = lookup.Empire((int)entry["empireId"]!);
            game.UnimplementedNpeBlobs[empire] = Convert.FromBase64String((string)entry["data"]!);
        }
    }

    /// <summary>Hand-written like <see cref="WriteBlobs"/> above -- <see cref="Message.Sender"/>/<see cref="Message.Recipients"/> are <see cref="Empire"/> references.</summary>
    private static JsonArray WriteMessages(Game game, EntityIndex index)
    {
        var array = new JsonArray();

        foreach (var message in game.Messages) {
            array.Add(new JsonObject {
                ["senderId"] = index.EmpireId(message.Sender),
                ["recipientIds"] = new JsonArray([.. message.Recipients.Select(e => (JsonNode)index.EmpireId(e))]),
                ["read"] = message.Read,
                ["intercepted"] = message.Intercepted,
                ["lines"] = new JsonArray([.. message.Lines.Select(l => (JsonNode)l)]),
            });
        }

        return array;
    }

    private static void ReadMessages(JsonNode? node, Game game, EntityLookup lookup)
    {
        if (node is null) {
            return;
        }

        foreach (var entryNode in node.AsArray()) {
            var entry = entryNode!.AsObject();
            var sender = lookup.Empire((int)entry["senderId"]!);
            var recipients = entry["recipientIds"]!.AsArray().Select(n => lookup.Empire((int)n!)).ToHashSet();
            var lines = entry["lines"]!.AsArray().Select(n => (string)n!).ToList();

            game.Messages.Add(new Message(sender, recipients, (bool)entry["read"]!, (bool)entry["intercepted"]!, lines));
        }
    }

    /// <summary>
    /// Per-kind stable integer ids for every entity cross-reference in the format — the same shape
    /// of problem the `.SAV` format's own <c>IDNumber</c> solves, just for JSON. Empire uses -1 for
    /// <see cref="Entities.Empire.Independent"/> (never a member of <see cref="Game.Empires"/>),
    /// matching <see cref="SavGameLoader"/>'s own ordinal-8 convention.
    ///
    /// Empires are the one kind assigned lazily rather than up front. <see cref="Game.Empires"/> is a
    /// permanent roster (see <see cref="Types.EmpireStatus"/>) — a defeated empire, human or NPE,
    /// stays a member forever, so a *defeated* empire is never orphaned from it. That alone doesn't
    /// eliminate the orphan case, though: an empire that was already not-InUse when the `.SAV` file
    /// this <see cref="Game"/> was originally loaded from was captured was never added to
    /// <see cref="Game.Empires"/> in the first place (<see cref="SavGameLoader"/>'s own placeholder
    /// gate, unrelated to and unchanged by <see cref="Types.EmpireStatus"/>), so a living
    /// <see cref="Turns.KingdomTurnHandler"/>'s own <c>State</c> dictionary, or a minefield's
    /// owner/scouts, can still reference one — confirmed for real (not just theorized) by
    /// <see cref="SavGameWriter"/>'s own equivalent orphan-discovery code, whose otherwise-identical
    /// simplification broke against a real reference save's minefield owner (see its remarks).
    /// <see cref="EmpireId"/> auto-registers such an orphan the first time anything asks for its id;
    /// <see cref="Serialize"/> writes the turnHandlers/blobs/minefield sections (the only places an
    /// orphan can surface) before <c>"empires"</c> so every orphan this call discovers is already
    /// known by the time <see cref="WriteEmpires"/> walks <see cref="AllEmpires"/>.
    /// </summary>
    private sealed class EntityIndex
    {
        private readonly Dictionary<Empire, int> _empireIds = new();
        private readonly List<Empire> _allEmpires = [];
        public readonly Dictionary<Planet, int> Planets;
        public readonly Dictionary<Starbase, int> Starbases;
        public readonly Dictionary<Fleet, int> Fleets;
        public readonly Dictionary<Stargate, int> Stargates;
        public readonly Dictionary<ConstructionSite, int> ConstructionSites;

        public EntityIndex(Game game)
        {
            foreach (var empire in game.Empires) {
                _empireIds[empire] = _allEmpires.Count;
                _allEmpires.Add(empire);
            }

            Planets = Build(game.Galaxy.Planets);
            Starbases = Build(game.Galaxy.Starbases);
            Fleets = Build(game.Galaxy.Fleets);
            Stargates = Build(game.Galaxy.Stargates);
            ConstructionSites = Build(game.Galaxy.ConstructionSites);
        }

        /// <summary>Every empire this call has assigned an id to so far — real <see cref="Game.Empires"/> members first (in order), any orphans discovered afterward.</summary>
        public IReadOnlyList<Empire> AllEmpires => _allEmpires;

        private static Dictionary<T, int> Build<T>(IReadOnlyList<T> list) where T : notnull
        {
            var map = new Dictionary<T, int>();
            for (var i = 0; i < list.Count; i++) {
                map[list[i]] = i;
            }
            return map;
        }

        public int EmpireId(Empire empire)
        {
            // IsIndependent is init-only and set nowhere except Empire.Independent's own static
            // initializer, so this is equivalent to ReferenceEquals(empire, Empire.Independent) --
            // no other Empire, real or orphan, can ever take this branch. That's why the flag
            // itself doesn't need its own slot in the JSON: the -1 sentinel round-trips it.
            if (empire.IsIndependent) {
                return -1;
            }

            if (_empireIds.TryGetValue(empire, out var id)) {
                return id;
            }

            id = _allEmpires.Count;
            _empireIds[empire] = id;
            _allEmpires.Add(empire);
            return id;
        }

        public JsonNode? EncodeObjectRef(object? obj) => obj switch {
            null => null,
            Planet p => new JsonObject { ["kind"] = "Planet", ["id"] = Planets[p] },
            Starbase s => new JsonObject { ["kind"] = "Starbase", ["id"] = Starbases[s] },
            // A Fleet reference (a mission's own Target/HomeBase) can outlive the fleet it points to
            // -- same dangling-reference window WriteFleetStates' own doc comment describes, just one
            // level removed (this fleet is still alive; whatever it was aiming at isn't anymore).
            // Degrades to null rather than throwing -- exactly what EnforceNpeDataLinks would leave
            // this mission pointing at anyway once it re-evaluates on that empire's own next turn.
            Fleet f => Fleets.TryGetValue(f, out var fleetId) ? new JsonObject { ["kind"] = "Fleet", ["id"] = fleetId } : null,
            Stargate g => new JsonObject { ["kind"] = "Stargate", ["id"] = Stargates[g] },
            ConstructionSite c => new JsonObject { ["kind"] = "ConstructionSite", ["id"] = ConstructionSites[c] },
            _ => throw new NotSupportedException($"Unexpected object reference type {obj.GetType()}."),
        };
    }

    /// <summary>
    /// Read-side inverse of <see cref="EntityIndex"/>. Two-phase, mirroring <see cref="Deserialize"/>
    /// itself: constructed with just the placeholder <see cref="Empire"/> list (all that exists at
    /// the point <see cref="Galaxy.Galaxy"/> is being deserialized), then <see cref="AttachGalaxy"/>
    /// once the entity lists exist too.
    /// </summary>
    private sealed class EntityLookup(List<Empire> empires)
    {
        private Galaxy.Galaxy? _galaxy;

        public void AttachGalaxy(Galaxy.Galaxy galaxy) => _galaxy = galaxy;

        public Empire Empire(int id) => id == -1 ? Entities.Empire.Independent : empires[id];
        public Planet Planet(int id) => _galaxy!.Planets[id];
        public Starbase Starbase(int id) => _galaxy!.Starbases[id];
        public Fleet Fleet(int id) => _galaxy!.Fleets[id];
        public Stargate Stargate(int id) => _galaxy!.Stargates[id];
        public ConstructionSite ConstructionSite(int id) => _galaxy!.ConstructionSites[id];

        public ISectorObject? DecodeObjectRef(JsonNode? node)
        {
            if (node is null) {
                return null;
            }

            var obj = node.AsObject();
            var id = (int)obj["id"]!;

            return (string)obj["kind"]! switch {
                "Planet" => Planet(id),
                "Starbase" => Starbase(id),
                "Fleet" => Fleet(id),
                "Stargate" => Stargate(id),
                "ConstructionSite" => ConstructionSite(id),
                var kind => throw new NotSupportedException($"Unknown object reference kind '{kind}'."),
            };
        }
    }

    /// <summary>
    /// Per-property override for <c>Owner</c> on each <see cref="ISectorObject"/> implementor — see
    /// <see cref="GameJson"/>'s class doc comment. Exactly one of <paramref name="index"/> (write) /
    /// <paramref name="lookup"/> (read) is non-null for a given instance; System.Text.Json never
    /// invokes <see cref="Read"/> against a write-only options instance or vice versa.
    /// </summary>
    private sealed class EmpireRefConverter(EntityIndex? index, EntityLookup? lookup) : JsonConverter<Empire>
    {
        public override Empire Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            lookup!.Empire(reader.GetInt32());

        public override void Write(Utf8JsonWriter writer, Empire value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(index!.EmpireId(value));
    }

    /// <summary>
    /// Per-property override for <c>Names</c> on each <see cref="ISectorObject"/> implementor — same
    /// shape as <see cref="EmpireRefConverter"/>, but for a whole <c>Dictionary&lt;Empire,string&gt;</c>
    /// rather than one <see cref="Empire"/> reference (a non-string key isn't natively JSON-safe, so
    /// this writes/reads an array of <c>{empireId, name}</c> pairs, the same shape
    /// <see cref="WriteState"/>/<see cref="ReadState"/> already use for
    /// <c>Dictionary&lt;Empire,StateDeptRecord&gt;</c>).
    /// </summary>
    private sealed class NamesConverter(EntityIndex? index, EntityLookup? lookup) : JsonConverter<Dictionary<Empire, string>>
    {
        public override Dictionary<Empire, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var result = new Dictionary<Empire, string>();
            foreach (var entry in JsonElement.ParseValue(ref reader).EnumerateArray()) {
                var empire = lookup!.Empire(entry.GetProperty("empireId").GetInt32());
                result[empire] = entry.GetProperty("name").GetString()!;
            }
            return result;
        }

        public override void Write(Utf8JsonWriter writer, Dictionary<Empire, string> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var (empire, name) in value) {
                writer.WriteStartObject();
                writer.WriteNumber("empireId", index!.EmpireId(empire));
                writer.WriteString("name", name);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
    }

    /// <summary>
    /// Per-property override for <see cref="Fleet"/>'s <c>Orders</c> -- same shape of problem
    /// <see cref="EmpireRefConverter"/> solves for <c>Owner</c>: <see cref="FleetOrder.DestinationObject"/>
    /// is an <see cref="ISectorObject"/> reference System.Text.Json's plain reflection can't serialize
    /// (an interface-typed property) or deserialize (no concrete type to construct), so it's encoded
    /// the same way any other cross-entity reference in this format is -- <see cref="EntityIndex.EncodeObjectRef"/>/
    /// <see cref="EntityLookup.DecodeObjectRef"/>, the same pair <see cref="NewsItem.Subject"/> and
    /// <see cref="Turns.KingdomFleetState.Target"/>/<c>HomeBase</c> already use.
    ///
    /// <see cref="Read"/> deliberately does nothing but skip its own value -- unlike every other
    /// cross-reference in this format, <see cref="FleetOrder.DestinationObject"/> can point at
    /// another <see cref="Fleet"/>, including one later in this very list, which is still a forward
    /// reference at the point this converter would otherwise run (mid-<c>Deserialize&lt;List&lt;Fleet&gt;&gt;</c>,
    /// before <see cref="EntityLookup.AttachGalaxy"/>). <see cref="Deserialize"/> instead fills
    /// <see cref="Fleet.Orders"/> in a real second pass via <see cref="ReadFleetOrders"/>, once every
    /// entity list is loaded -- the same two-phase shape it already uses for <see cref="Empire"/>'s
    /// own cross-referencing fields (<see cref="FillEmpireFields"/>).
    /// </summary>
    private sealed class FleetOrderListConverter(EntityIndex? index) : JsonConverter<List<FleetOrder>>
    {
        public override List<FleetOrder> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Skip();
            return [];
        }

        public override void Write(Utf8JsonWriter writer, List<FleetOrder> value, JsonSerializerOptions options)
        {
            var array = new JsonArray();

            foreach (var order in value) {
                array.Add(new JsonObject {
                    ["type"] = order.Type.ToString(),
                    ["destinationObject"] = index!.EncodeObjectRef(order.DestinationObject),
                    ["destinationPosition"] = order.DestinationPosition is { } position
                        ? new JsonObject { ["x"] = position.X, ["y"] = position.Y }
                        : null,
                    ["transferShip"] = order.TransferShip?.ToString(),
                    ["transferCargo"] = order.TransferCargo?.ToString(),
                    ["transferAmount"] = order.TransferAmount,
                });
            }

            array.WriteTo(writer, options);
        }
    }
}
