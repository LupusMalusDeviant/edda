using System.Collections;

namespace Edda.AKG.Graph;

/// <summary>
/// In-process implementation of <see cref="Core.Abstractions.ICypherExecutor"/> for the zero-infra dev
/// mode (<c>GRAPH_PROVIDER=memory</c>). It does not parse Cypher generally; instead it recognizes the
/// finite, closed set of query shapes that the AKG layer actually issues and re-implements each against
/// in-memory dictionaries. Unrecognized queries throw <see cref="NotSupportedException"/> so that any
/// missing shape surfaces loudly in tests rather than silently returning wrong data.
///
/// <para>State (rules, relationship edges, chunks) is held in this instance for the process lifetime; the
/// provider hands out a single shared executor so the graph persists across calls. All operations are
/// serialized by an internal lock — correctness over throughput, which is appropriate for dev use.</para>
/// </summary>
internal sealed class InMemoryCypherExecutor : Core.Abstractions.ICypherExecutor
{
    private readonly object _gate = new();

    // Rule id → property bag (keys mirror the SET clauses of Neo4jKnowledgeGraph, e.g. id/type/domain/…).
    private readonly Dictionary<string, Dictionary<string, object?>> _rules = new(StringComparer.Ordinal);

    // Directed relationship edges between rules (excluding HAS_CHUNK; chunk nodes arrive in a later stage,
    // so there are none in this mode yet — chunk-dependent shapes are no-ops that return empty/zero).
    private readonly List<Edge> _edges = [];

    /// <inheritdoc />
    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        string cypher, object? parameters = null, CancellationToken ct = default)
    {
        var q = Normalize(cypher);
        var p = ToParameters(parameters);

        lock (_gate)
        {
            var rows = DispatchQuery(q, p);
            if (rows is null)
                throw new NotSupportedException($"InMemoryCypherExecutor: unrecognized read query: {cypher}");
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(rows);
        }
    }

    /// <inheritdoc />
    public Task ExecuteAsync(string cypher, object? parameters = null, CancellationToken ct = default)
    {
        var q = Normalize(cypher);
        var p = ToParameters(parameters);

        lock (_gate)
        {
            if (!DispatchExecute(q, p))
                throw new NotSupportedException($"InMemoryCypherExecutor: unrecognized write query: {cypher}");
        }

        return Task.CompletedTask;
    }

    // ── Read dispatch ────────────────────────────────────────────────────────────

    private List<IReadOnlyDictionary<string, object?>>? DispatchQuery(
        string q, IReadOnlyDictionary<string, object?> p)
    {
        // Order: most distinctive shapes first.
        if (q.Contains("-[]-(n:Rule)")) return FindNeighbors(p);
        if (q.Contains("RETURN count(r) AS n")) return SubtreeCount(p);
        if (q.Contains("count(r) AS total")) return StatsMain();
        if (q.Contains("count(e) AS edges")) return Single("edges", _edges.Count);
        if (q.Contains("count(DISTINCT c.parentId) AS withEmbedding")) return Single("withEmbedding", 0);
        if (q.Contains("r.domain AS domain")) return GroupBy("domain", "domain");
        if (q.Contains("r.type AS type")) return GroupBy("type", "type");
        if (q.Contains("size(split(r.id")) return RuleHeads(p);
        if (q.Contains("(r:Rule {id: $ruleId})") && q.Contains("RETURN r")) return GetRule(p);
        if (q.StartsWith("MATCH (r:Rule) WHERE", StringComparison.Ordinal) && q.EndsWith("RETURN r", StringComparison.Ordinal))
            return GetRules(q, p);
        return null;
    }

    private List<IReadOnlyDictionary<string, object?>> GetRule(IReadOnlyDictionary<string, object?> p)
    {
        var id = AsString(p, "ruleId");
        var userId = AsString(p, "userId");
        return id is not null && _rules.TryGetValue(id, out var rule) && InScope(rule, userId)
            ? [RuleRow("r", rule)]
            : [];
    }

    private List<IReadOnlyDictionary<string, object?>> GetRules(string q, IReadOnlyDictionary<string, object?> p)
    {
        var userId = AsString(p, "userId");
        var filterDomain = q.Contains("r.domain = $domain") ? AsString(p, "domain") : null;
        var filterType   = q.Contains("r.type = $type") ? AsString(p, "type") : null;
        var filterTag    = q.Contains("$tag IN r.tags") ? AsString(p, "tag") : null;

        return _rules.Values
            .Where(r => InScope(r, userId))
            .Where(r => filterDomain is null || AsString(r, "domain") == filterDomain)
            .Where(r => filterType is null || AsString(r, "type") == filterType)
            .Where(r => filterTag is null || AsStrings(r.GetValueOrDefault("tags")).Contains(filterTag))
            .Select(r => RuleRow("r", r))
            .ToList();
    }

    private List<IReadOnlyDictionary<string, object?>> RuleHeads(IReadOnlyDictionary<string, object?> p)
    {
        var userId = AsString(p, "userId");
        return _rules.Values
            .Where(r => InScope(r, userId) && !IsNestedLeaf(AsString(r, "id")))
            .Select(r => RuleRow("r", r))
            .ToList();
    }

    private List<IReadOnlyDictionary<string, object?>> FindNeighbors(IReadOnlyDictionary<string, object?> p)
    {
        var id = AsString(p, "ruleId");
        var userId = AsString(p, "userId");
        if (id is null) return [];

        var neighborIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in _edges)
        {
            if (e.Source == id) neighborIds.Add(e.Target);
            else if (e.Target == id) neighborIds.Add(e.Source);
        }

        return neighborIds
            .Where(nid => _rules.TryGetValue(nid, out var r) && InScope(r, userId))
            .Select(nid => RuleRow("n", _rules[nid]))
            .ToList();
    }

    private List<IReadOnlyDictionary<string, object?>> SubtreeCount(IReadOnlyDictionary<string, object?> p)
        => Single("n", MatchSubtree(p).Count);

    private List<IReadOnlyDictionary<string, object?>> StatsMain()
    {
        var total = _rules.Count;
        var global = _rules.Values.Count(r => AsString(r, "ownerId") is null);
        var withValidator = _rules.Values.Count(r => r.GetValueOrDefault("validatorScript") is not null);
        return
        [
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["total"] = total,
                ["globalRules"] = global,
                ["userRules"] = total - global,
                ["withValidator"] = withValidator,
            },
        ];
    }

    private List<IReadOnlyDictionary<string, object?>> GroupBy(string property, string columnName)
        => _rules.Values
            .GroupBy(r => AsString(r, property) ?? string.Empty, StringComparer.Ordinal)
            .Select(g => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [columnName] = g.Key,
                ["cnt"] = g.Count(),
            })
            .ToList();

    // ── Write dispatch ───────────────────────────────────────────────────────────

    private bool DispatchExecute(string q, IReadOnlyDictionary<string, object?> p)
    {
        if (q.Contains("MERGE (r:Rule {id: $id})")) { UpsertRule(p); return true; }
        if (q.Contains("-[e:") && q.Contains("DELETE e")) { DeleteEdges(q, p); return true; }
        if (q.Contains("MERGE (s)-[:")) { MergeEdge(q, p); return true; }
        if (q.Contains("UNWIND newer.supersedes AS olderId")) { InvalidateSuperseded(p); return true; }
        if (q.Contains("HAS_CHUNK]->(c:RuleChunk) DETACH DELETE c")) return true; // no chunks in memory mode yet
        if (q.Contains("REMOVE r.bodyHash, r.embedding")) { ResetEmbeddingProps(); return true; }
        if (q.Contains("REMOVE r.bodyHash")) { RemoveProp(p, "bodyHash"); return true; }
        if (q.Contains("ANY(p IN $prefixes") && q.Contains("DETACH DELETE r, c")) { DeleteSubtree(p); return true; }
        if (q.Contains("(r:Rule {id: $ruleId})") && q.Contains("DETACH DELETE r, c")) { DeleteRule(p); return true; }
        return false;
    }

    private void UpsertRule(IReadOnlyDictionary<string, object?> p)
    {
        var id = AsString(p, "id");
        if (id is null) return;

        if (!_rules.TryGetValue(id, out var rule))
        {
            rule = new Dictionary<string, object?>(StringComparer.Ordinal);
            _rules[id] = rule;
        }

        foreach (var key in new[]
                 {
                     "id", "type", "domain", "priority", "body", "tags", "ownerId",
                     "implies", "conflictsWith", "exceptionFor", "requires", "supersedes", "related", "chunkStyle",
                 })
        {
            rule[key] = p.GetValueOrDefault(key);
        }

        // validFrom = coalesce(existing, $now): keep the first-seen timestamp.
        if (rule.GetValueOrDefault("validFrom") is null)
            rule["validFrom"] = p.GetValueOrDefault("now");
    }

    private void DeleteEdges(string q, IReadOnlyDictionary<string, object?> p)
    {
        var relType = ExtractBetween(q, "-[e:", "]");
        var sourceId = AsString(p, "sourceId");
        if (relType is null || sourceId is null) return;
        _edges.RemoveAll(e => e.Source == sourceId && e.Rel == relType);
    }

    private void MergeEdge(string q, IReadOnlyDictionary<string, object?> p)
    {
        var relType = ExtractBetween(q, "MERGE (s)-[:", "]");
        var sourceId = AsString(p, "sourceId");
        var targetId = AsString(p, "targetId");
        if (relType is null || sourceId is null || targetId is null) return;
        if (!_edges.Any(e => e.Source == sourceId && e.Rel == relType && e.Target == targetId))
            _edges.Add(new Edge(sourceId, relType, targetId));
    }

    private void RemoveProp(IReadOnlyDictionary<string, object?> p, string property)
    {
        var id = AsString(p, "id");
        if (id is not null && _rules.TryGetValue(id, out var rule))
            rule.Remove(property);
    }

    private void ResetEmbeddingProps()
    {
        foreach (var rule in _rules.Values)
        {
            rule.Remove("bodyHash");
            rule.Remove("embedding");
            rule.Remove("embedAttempts");
        }
    }

    private void DeleteRule(IReadOnlyDictionary<string, object?> p)
    {
        var id = AsString(p, "ruleId");
        if (id is not null) RemoveRules([id]);
    }

    private void DeleteSubtree(IReadOnlyDictionary<string, object?> p)
        => RemoveRules(MatchSubtree(p));

    private void InvalidateSuperseded(IReadOnlyDictionary<string, object?> p)
    {
        var now = p.GetValueOrDefault("now");
        // Snapshot to avoid mutating while enumerating.
        foreach (var newer in _rules.Values.ToList())
        {
            if (newer.GetValueOrDefault("validUntil") is not null) continue;
            var supersedes = AsStrings(newer.GetValueOrDefault("supersedes"));
            if (supersedes.Count == 0) continue;

            var newerId = AsString(newer, "id");
            foreach (var olderId in supersedes)
            {
                if (!_rules.TryGetValue(olderId, out var older)) continue;
                if (older.GetValueOrDefault("validUntil") is not null) continue;
                if (AsString(older, "id") == newerId) continue;
                older["validUntil"] = now;
                older["invalidatedBy"] = newerId;
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>Rules matching a subtree delete: the root id itself, or any id starting with a given prefix.</summary>
    private List<string> MatchSubtree(IReadOnlyDictionary<string, object?> p)
    {
        var rootId = AsString(p, "rootId");
        var prefixes = AsStrings(p.GetValueOrDefault("prefixes"));
        return _rules.Keys
            .Where(id => id == rootId || prefixes.Any(prefix => id.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();
    }

    private void RemoveRules(IReadOnlyCollection<string> ids)
    {
        var set = new HashSet<string>(ids, StringComparer.Ordinal);
        foreach (var id in set) _rules.Remove(id);
        _edges.RemoveAll(e => set.Contains(e.Source) || set.Contains(e.Target));
    }

    /// <summary>A file-level leaf is a <c>git:</c>/<c>upload:</c> node with 3+ colon-separated id segments.</summary>
    private static bool IsNestedLeaf(string? id)
        => id is not null
           && (id.StartsWith("git:", StringComparison.Ordinal) || id.StartsWith("upload:", StringComparison.Ordinal))
           && id.Split(':').Length >= 3;

    private static bool InScope(IReadOnlyDictionary<string, object?> rule, string? userId)
    {
        var owner = AsString(rule, "ownerId");
        return owner is null || owner == userId;
    }

    private static IReadOnlyDictionary<string, object?> RuleRow(string column, Dictionary<string, object?> rule)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [column] = new Dictionary<string, object?>(rule, StringComparer.Ordinal),
        };

    private static List<IReadOnlyDictionary<string, object?>> Single(string column, object? value)
        => [new Dictionary<string, object?>(StringComparer.Ordinal) { [column] = value }];

    private static string Normalize(string cypher)
        => string.Join(' ', cypher.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static IReadOnlyDictionary<string, object?> ToParameters(object? parameters)
    {
        if (parameters is null)
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        if (parameters is IReadOnlyDictionary<string, object?> dict)
            return dict;

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in parameters.GetType().GetProperties())
            result[prop.Name] = prop.GetValue(parameters);
        return result;
    }

    private static string? AsString(IReadOnlyDictionary<string, object?> source, string key)
        => source.GetValueOrDefault(key) as string;

    private static IReadOnlyList<string> AsStrings(object? value)
        => value is IEnumerable enumerable and not string
            ? enumerable.Cast<object?>()
                .Select(x => x?.ToString())
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s!)
                .ToList()
            : [];

    private static string? ExtractBetween(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        if (from < 0) return null;
        from += start.Length;
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        return to < 0 ? null : source[from..to];
    }

    private readonly record struct Edge(string Source, string Rel, string Target);
}
