using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using ThreeLn.Reconstruction4021.Core;
using ThreeLn.Reconstruction4021.Core.Entities;

namespace ThreeLn.Reconstruction4021.Tests;

/// <summary>
/// Exhaustive reflection-walk structural equality for a <see cref="Game"/> graph, built specifically
/// to give GameJson's round-trip tests real teeth: hand-picked field assertions (SavGameLoaderTests'
/// own style, appropriate there since it's checking a decode against known ground truth) can't catch
/// "a future entity gets a new property and GameJson silently drops it on one side of the round
/// trip" — this walks every public property GameJson's own reflection-based serializer would have
/// seen, so a dropped field fails the same way a dropped field would fail in production.
///
/// Entity references (<see cref="Planet"/>/<see cref="Starbase"/>/<see cref="Fleet"/>/
/// <see cref="Stargate"/>/<see cref="ConstructionSite"/>/<see cref="Empire"/>) are matched
/// across the two graphs by position in their owning list (<see cref="Core.Galaxy.Galaxy"/>'s 5 lists,
/// <see cref="Game.Empires"/>) rather than by reference — the two graphs are separate object
/// instances by construction (one round-tripped through JSON), so reference equality never holds
/// even when every field matches. This is the same "stable per-kind integer id" identity GameJson's
/// own hand-written TurnHandlers section uses, reused here for the same reason.
/// </summary>
public static class DeepGraphComparer
{
    public static List<string> FindDifferences(Game a, Game b)
    {
        var diffs = new List<string>();
        var indexA = new EntityIndex(a);
        var indexB = new EntityIndex(b);
        var visited = new HashSet<(object, object)>();
        Compare(a, b, "Game", indexA, indexB, visited, diffs);
        return diffs;
    }

    private static void Compare(object? a, object? b, string path, EntityIndex indexA, EntityIndex indexB, HashSet<(object, object)> visited, List<string> diffs)
    {
        if (ReferenceEquals(a, b)) {
            return;
        }

        if (a is null || b is null) {
            diffs.Add($"{path}: null mismatch (a is {(a is null ? "null" : "non-null")}, b is {(b is null ? "null" : "non-null")})");
            return;
        }

        var type = a.GetType();
        if (type != b.GetType()) {
            diffs.Add($"{path}: type mismatch {type.Name} vs {b.GetType().Name}");
            return;
        }

        // Cross-graph identity check for the 6 entity kinds -- must agree on *which* entity this is
        // before comparing fields, and prevents infinite recursion around Owner/Capital/etc cycles.
        if (indexA.TryGetId(a, out var idA)) {
            if (!indexB.TryGetId(b, out var idB) || idA != idB) {
                diffs.Add($"{path}: entity identity mismatch ({idA} vs {(indexB.TryGetId(b, out var otherId) ? otherId.ToString() : "not found")})");
                return;
            }
        }

        if (type.IsEnum || type.IsPrimitive || type == typeof(string) || type == typeof(decimal)) {
            if (!Equals(a, b)) {
                diffs.Add($"{path}: {a} != {b}");
            }
            return;
        }

        if (a is double da && b is double db) {
            if (Math.Abs(da - db) > 1e-9) {
                diffs.Add($"{path}: {da} != {db}");
            }
            return;
        }

        if (!visited.Add((a, b))) {
            return; // already validated (or in progress) via another reference path
        }

        if (a is IDictionary dictA && b is IDictionary dictB) {
            CompareDictionary(dictA, dictB, path, indexA, indexB, visited, diffs);
            return;
        }

        if (a is IEnumerable enumA && b is IEnumerable enumB) {
            var listA = enumA.Cast<object?>().ToList();
            var listB = enumB.Cast<object?>().ToList();
            var isUnordered = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>);

            if (isUnordered) {
                CompareAsSet(listA, listB, path, indexA, indexB, visited, diffs);
            } else {
                if (listA.Count != listB.Count) {
                    diffs.Add($"{path}: length {listA.Count} vs {listB.Count}");
                }
                for (var i = 0; i < Math.Min(listA.Count, listB.Count); i++) {
                    Compare(listA[i], listB[i], $"{path}[{i}]", indexA, indexB, visited, diffs);
                }
            }
            return;
        }

        // NonPublic too: KingdomTurnHandler.Persona/State/FleetStates/DefaultPolicy are internal
        // (GameJson, in the same assembly, reads them directly) -- public-only reflection would
        // silently skip all of Kingdom's saved state and this comparer would never notice a
        // round-trip dropping it.
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
            if (prop.GetIndexParameters().Length > 0 || !prop.CanRead) {
                continue;
            }

            object? av;
            object? bv;
            try {
                av = prop.GetValue(a);
                bv = prop.GetValue(b);
            } catch (TargetParameterCountException) {
                continue;
            } catch (TargetInvocationException) {
                // A computed property that throws given this object's current state (e.g.
                // Game.AnyHumanPlayersRemain indexing TurnHandlers for an empire with no entry --
                // a pre-existing gap, not this comparer's concern) isn't something GameJson would
                // ever have serialized either; nothing to compare, so skip rather than crash the
                // whole comparison.
                continue;
            }

            Compare(av, bv, $"{path}.{prop.Name}", indexA, indexB, visited, diffs);
        }
    }

    private static void CompareDictionary(IDictionary a, IDictionary b, string path, EntityIndex indexA, EntityIndex indexB, HashSet<(object, object)> visited, List<string> diffs)
    {
        if (a.Count != b.Count) {
            diffs.Add($"{path}: dictionary count {a.Count} vs {b.Count}");
        }

        var byKeyB = new Dictionary<object, object?>();
        foreach (DictionaryEntry entry in b) {
            byKeyB[PairKey(entry.Key, indexB)] = entry.Value;
        }

        foreach (DictionaryEntry entry in a) {
            var key = PairKey(entry.Key, indexA);
            if (!byKeyB.TryGetValue(key, out var bValue)) {
                diffs.Add($"{path}: key {Describe(entry.Key, indexA)} missing on other side");
                continue;
            }
            Compare(entry.Value, bValue, $"{path}[{Describe(entry.Key, indexA)}]", indexA, indexB, visited, diffs);
        }
    }

    private static void CompareAsSet(List<object?> listA, List<object?> listB, string path, EntityIndex indexA, EntityIndex indexB, HashSet<(object, object)> visited, List<string> diffs)
    {
        if (listA.Count != listB.Count) {
            diffs.Add($"{path}: set size {listA.Count} vs {listB.Count}");
        }

        var byKeyB = new Dictionary<object, object?>();
        foreach (var item in listB) {
            if (item is not null) {
                byKeyB[PairKey(item, indexB)] = item;
            }
        }

        foreach (var item in listA) {
            if (item is null) {
                continue;
            }

            var key = PairKey(item, indexA);
            if (!byKeyB.TryGetValue(key, out var match)) {
                diffs.Add($"{path}: element {Describe(item, indexA)} has no match on other side");
                continue;
            }

            Compare(item, match, $"{path}{{{Describe(item, indexA)}}}", indexA, indexB, visited, diffs);
        }
    }

    private static object PairKey(object element, EntityIndex index) =>
        index.TryGetId(element, out var id) ? id : element;

    private static string Describe(object element, EntityIndex index) =>
        index.TryGetId(element, out var id) ? id.ToString() : element.ToString() ?? "?";

    private readonly record struct EntityId(string Kind, int Ordinal);

    private sealed class EntityIndex
    {
        private readonly Dictionary<object, EntityId> _byObject = new(ReferenceEqualityComparer.Instance);
        private int _nextOrphanEmpireOrdinal = 1_000_000;

        public EntityIndex(Game game)
        {
            Add(game.Galaxy.Planets, "Planet");
            Add(game.Galaxy.Starbases, "Starbase");
            Add(game.Galaxy.Fleets, "Fleet");
            Add(game.Galaxy.Stargates, "Stargate");
            Add(game.Galaxy.ConstructionSites, "ConstructionSite");
            Add(game.Empires, "Empire");
            _byObject[Empire.Independent] = new EntityId("Empire", -1);
        }

        private void Add<T>(IReadOnlyList<T> list, string kind) where T : notnull
        {
            for (var i = 0; i < list.Count; i++) {
                _byObject[list[i]] = new EntityId(kind, i);
            }
        }

        /// <summary>
        /// An Empire reachable only via e.g. a Kingdom handler's own <c>State</c> dictionary (see
        /// GameJson.EntityIndex's own remarks -- <c>CombatOutcome.DestroyEmpire</c>
        /// drops a defeated empire from <see cref="Game.Empires"/> but not from other references to
        /// it) isn't in either list above. Since <see cref="Compare"/> walks both graphs in lockstep
        /// (same reflection order, same dictionary insertion order on both sides of a round trip),
        /// the Nth previously-unseen orphan encountered on side A and the Nth on side B are always the
        /// corresponding pair -- assigning them a fresh, ever-increasing ordinal on first sight (well
        /// past any real empire's ordinal, so it can never collide) gives both sides the same id.
        /// </summary>
        public bool TryGetId(object element, out EntityId id)
        {
            if (_byObject.TryGetValue(element, out id)) {
                return true;
            }

            if (element is Empire) {
                id = new EntityId("Empire", _nextOrphanEmpireOrdinal++);
                _byObject[element] = id;
                return true;
            }

            return false;
        }
    }
}
