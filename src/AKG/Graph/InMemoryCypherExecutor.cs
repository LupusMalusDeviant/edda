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

    // WorldKnowledge id → property bag (facts injected during context compilation, Phase 4).
    private readonly Dictionary<string, Dictionary<string, object?>> _world = new(StringComparer.Ordinal);

    // Domain name → property bag (name/label/isCore/description/ownerId) + HAS_SUBDOMAIN parent→child edges.
    private readonly Dictionary<string, Dictionary<string, object?>> _domains = new(StringComparer.Ordinal);
    private readonly List<(string Parent, string Child)> _domainEdges = [];

    // Entity layer (F49). Keyed by "ownerId\0normalizedName"; RELATES_TO edges are treated as undirected.
    private readonly Dictionary<string, Dictionary<string, object?>> _entities = new(StringComparer.Ordinal);
    private readonly List<(string SourceNorm, string TargetNorm, string? OwnerId)> _entityEdges = [];

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
        // Context-compilation reads (checked before the generic rule reads they resemble).
        if (q.Contains("r.id IN $frontier")) return Expand(p);                     // GraphExpander (Phase 3)
        if (q.Contains("NOT r.domain STARTS WITH 'tools.'")) return LoadRules(p);  // ContextCompiler Phase 1
        // WorldKnowledge (Phase 4 + seeding).
        if (q.Contains("(w:WorldKnowledge) WHERE")) return FetchWorldKnowledge(p);
        if (q.Contains("count(w) AS n")) return Single("n", _world.Count);
        // Domains.
        if (q.Contains("AS parentName")) return DomainTree(includeNode: true);
        if (q.Contains("d.label AS label")) return DomainTree(includeNode: false);
        if (q.Contains("count(d) AS cnt"))
            return Single("cnt", _domains.ContainsKey(AsString(p, "name") ?? "") ? 1 : 0);
        // Validator.
        if (q.Contains("[:IMPLIES*2..]")) return Single("cycles", CountImpliesCycles());
        if (q.Contains("r.implies AS implies")) return DanglingImplies();
        if (q.Contains("MATCH (r:Rule) RETURN r.id AS id")) return AllRuleIds();
        // Entity layer (F49) + coverage + embedding reads (Stage 3). Checked before the generic rule/head/stats
        // reads they resemble (several share tokens like "size(split(r.id" or "count(r) AS total").
        if (q.Contains("toLower(e.name) CONTAINS term")) return FindEntities(p);
        if (q.Contains("RELATES_TO]-(other:Entity)")) return RelatedEntities(p);
        if (q.Contains("AS embedded")) return EmbeddingCoverage();
        if (q.Contains("AS totalHeads")) return HeadCoverage();
        if (q.Contains("db.index.vector.queryNodes")) return [];       // semantic search — no vectors in memory mode
        if (q.Contains("collect(c.embedding)")) return [];             // chunk-embedding fetch (app-side fallback / MMR)
        if (q.Contains("h.embedding AS emb")) return [];               // head-vector app-side fetch
        if (q.Contains("c.parentId STARTS WITH $prefix")) return [];   // subtree embeddings (k-means input)
        if (q.Contains("r.body AS body")) return [];                   // rules-to-embed (rebuild)
        if (q.Contains("r.bodyHash AS hash")) return [];               // has-embedding check
        if (q.Contains("r.ownerId AS ownerId")) return [];             // dirty heads to rebuild
        // Knowledge-graph reads.
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
        // WorldKnowledge + Domain writes (Stage 2).
        if (q.Contains("MERGE (w:WorldKnowledge {id: $id})")) { UpsertWorldKnowledge(p); return true; }
        if (q.Contains("(w:WorldKnowledge) DETACH DELETE w")) { _world.Clear(); return true; }
        if (q.Contains("MERGE (ct:Domain {name: 'custom-tools'})")) { SeedToolDomainsRoot(q); return true; }
        if (q.Contains("MERGE (tb:Domain {name: $name})")) { SeedToolboxDomain(p); return true; }
        if (q.Contains("MERGE (d:Domain {name: $name})")) { CreateDomain(p); return true; }
        if (q.Contains("(d:Domain {name: $name}) DETACH DELETE d")) { DeleteDomain(p); return true; }
        // Entity layer writes (F49) + embedding/head writes (Stage 3; embedding-gated → no-ops in memory mode).
        if (q.Contains("MERGE (e:Entity {ownerId: $ownerId")) { IngestEntities(p); return true; }
        if (q.Contains("MERGE (s)-[r:RELATES_TO]->(t)")) { IngestRelations(p); return true; }
        if (q.Contains("CREATE CONSTRAINT entity_owner_name_unique")) return true;
        if (q.Contains("CREATE INDEX entity_name_index")) return true;
        if (q.Contains("CREATE (r)-[:HAS_CHUNK]->(:RuleChunk")) return true;
        if (q.Contains("SET r.embedAttempts = coalesce")) return true;
        if (q.Contains("SET h.headVectorDirty")) return true;
        if (q.Contains("(h:HeadVector {headId: $id}) DETACH DELETE h")) return true;
        if (q.Contains("CREATE (:HeadVector")) return true;
        if (q.Contains("CREATE VECTOR INDEX")) return true;
        // Knowledge-graph writes.
        // C9 batched temporal edge replace (UNWIND $targetIds + MERGE (s)-[e:…]) must be checked before
        // the legacy single-edge shapes: it contains "-[stale:…]" / "MERGE (s)-[e:" tokens that would
        // otherwise partially match those.
        if (q.Contains("UNWIND $targetIds AS targetId") && q.Contains("MERGE (s)-[e:")) { ReplaceEdges(q, p); return true; }
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
                     "validatorScript", "validatorEnabled", "validatorHash",
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

    private void ReplaceEdges(string q, IReadOnlyDictionary<string, object?> p)
    {
        var relType = ExtractBetween(q, "MERGE (s)-[e:", "]");
        var sourceId = AsString(p, "sourceId");
        if (relType is null || sourceId is null) return;

        var now = AsString(p, "now");
        var targetIds = AsStrings(p.GetValueOrDefault("targetIds"));

        // C9 temporal replace (mirrors the batched Neo4j query): close open edges of this type whose
        // target is no longer declared (instead of deleting them), re-open declared ones (keeping the
        // first-seen validFrom), and stamp validFrom on newly created edges.
        foreach (var edge in _edges)
        {
            if (edge.Source == sourceId && edge.Rel == relType
                && edge.ValidUntil is null && !targetIds.Contains(edge.Target))
            {
                edge.ValidUntil = now;
            }
        }

        foreach (var targetId in targetIds)
        {
            var existing = _edges.FirstOrDefault(
                e => e.Source == sourceId && e.Rel == relType && e.Target == targetId);
            if (existing is null)
                _edges.Add(new Edge(sourceId, relType, targetId) { ValidFrom = now });
            else
                existing.ValidUntil = null; // re-open; ValidFrom keeps the first-seen timestamp
        }
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
        var nowStr = AsString(p, "now");
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
                CloseEdgesOf(olderId, nowStr);
            }
        }
    }

    /// <summary>
    /// C9: closes all open edges touching the invalidated rule — a superseded fact's relationships end
    /// with it — except the incoming SUPERSEDES edge, which documents the supersession and stays open.
    /// </summary>
    /// <param name="ruleId">Id of the invalidated rule.</param>
    /// <param name="now">Timestamp (ISO-8601) the edges are closed at.</param>
    private void CloseEdgesOf(string ruleId, string? now)
    {
        foreach (var edge in _edges)
        {
            if (edge.ValidUntil is not null) continue;
            if (edge.Source != ruleId && edge.Target != ruleId) continue;
            if (edge.Rel == "SUPERSEDES" && edge.Target == ruleId) continue; // incoming SUPERSEDES stays open
            edge.ValidUntil = now;
        }
    }

    // ── Stage 2: context compilation, WorldKnowledge, domains, validation ────────

    /// <summary>GraphExpander Phase 3: 1-hop neighbors (any direction) of the frontier rule set, in scope.
    /// C9: only temporally open edges carry activation (mirrors the Neo4j edge filter).</summary>
    private List<IReadOnlyDictionary<string, object?>> Expand(IReadOnlyDictionary<string, object?> p)
    {
        var frontier = AsStrings(p.GetValueOrDefault("frontier")).ToHashSet(StringComparer.Ordinal);
        var userId = AsString(p, "userId");
        var now = AsString(p, "now");
        var neighborIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in _edges)
        {
            if (!IsEdgeOpen(e, now)) continue;
            if (frontier.Contains(e.Source)) neighborIds.Add(e.Target);
            if (frontier.Contains(e.Target)) neighborIds.Add(e.Source);
        }

        return neighborIds
            .Where(id => _rules.TryGetValue(id, out var r) && InScope(r, userId))
            .Select(id => RuleRow("n", _rules[id]))
            .ToList();
    }

    /// <summary>ContextCompiler Phase 1: in-scope, temporally-valid, toolbox- and prefix-filtered rules.</summary>
    private List<IReadOnlyDictionary<string, object?>> LoadRules(IReadOnlyDictionary<string, object?> p)
    {
        var userId = AsString(p, "userId");
        var toolboxes = AsStrings(p.GetValueOrDefault("toolboxes"));
        var now = AsString(p, "now");
        var prefixes = AsStrings(p.GetValueOrDefault("prefixes"));

        return _rules.Values
            .Where(r => InScope(r, userId))
            .Where(r => MatchesToolboxScope(AsString(r, "domain"), toolboxes))
            .Where(r => IsTemporallyValid(AsString(r, "validUntil"), now))
            .Where(r => MatchesPrefixScope(AsString(r, "id"), prefixes))
            .Select(r => RuleRow("r", r))
            .ToList();
    }

    private static bool MatchesToolboxScope(string? domain, IReadOnlyList<string> toolboxes)
        => domain is null || !domain.StartsWith("tools.", StringComparison.Ordinal) || toolboxes.Contains(domain);

    private static bool IsTemporallyValid(string? validUntil, string? now)
        => validUntil is null || now is null || string.CompareOrdinal(validUntil, now) > 0;

    private static bool MatchesPrefixScope(string? id, IReadOnlyList<string> prefixes)
    {
        if (prefixes.Count == 0 || id is null) return true;
        var nested = id.StartsWith("git:", StringComparison.Ordinal) || id.StartsWith("upload:", StringComparison.Ordinal);
        return !nested || prefixes.Any(prefix => id.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>WorldKnowledgeFetcher Phase 4: up to 10 world facts whose domain/tags match any task concept.</summary>
    private List<IReadOnlyDictionary<string, object?>> FetchWorldKnowledge(IReadOnlyDictionary<string, object?> p)
    {
        var concepts = AsStrings(p.GetValueOrDefault("concepts")).Select(c => c.ToLowerInvariant()).ToList();
        return _world.Values
            .Where(w => concepts.Any(c => MatchesConcept(w, c)))
            .Take(10)
            .Select(w => RuleRow("w", w))
            .ToList();
    }

    private static bool MatchesConcept(IReadOnlyDictionary<string, object?> w, string conceptLower)
    {
        var domain = AsString(w, "domain")?.ToLowerInvariant();
        if (domain is not null && domain.Contains(conceptLower, StringComparison.Ordinal)) return true;
        return AsStrings(w.GetValueOrDefault("tags"))
            .Any(t => t.ToLowerInvariant().Contains(conceptLower, StringComparison.Ordinal));
    }

    /// <summary>Domain hierarchy for DomainManager (node form) or DomainActivationResolver (flat form).</summary>
    private List<IReadOnlyDictionary<string, object?>> DomainTree(bool includeNode)
        => _domains.Values.Select(d =>
        {
            var name = AsString(d, "name");
            var parent = _domainEdges.FirstOrDefault(e => e.Child == name).Parent;
            return includeNode
                ? (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.Ordinal)
                    { ["d"] = new Dictionary<string, object?>(d, StringComparer.Ordinal), ["parentName"] = parent }
                : new Dictionary<string, object?>(StringComparer.Ordinal)
                    { ["name"] = name, ["label"] = d.GetValueOrDefault("label"), ["parent"] = parent };
        }).ToList();

    /// <summary>Count of rules that lie on an IMPLIES cycle of length ≥ 2 (GraphValidator cycle check).</summary>
    private int CountImpliesCycles()
    {
        var adjacency = _edges
            .Where(e => e.Rel == "IMPLIES")
            .GroupBy(e => e.Source, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Target).ToList(), StringComparer.Ordinal);

        return adjacency.Keys.Count(start => ReachesSelf(start, adjacency));
    }

    private static bool ReachesSelf(string start, IReadOnlyDictionary<string, List<string>> adjacency)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<(string Node, int Depth)>();
        stack.Push((start, 0));
        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            if (!adjacency.TryGetValue(node, out var next)) continue;
            foreach (var target in next)
            {
                if (target == start && depth + 1 >= 2) return true;
                if (visited.Add(target)) stack.Push((target, depth + 1));
            }
        }
        return false;
    }

    /// <summary>GraphValidator: rules that declare IMPLIES targets (id + targets) for dangling-ref checks.</summary>
    private List<IReadOnlyDictionary<string, object?>> DanglingImplies()
        => _rules.Values
            .Where(r => r.GetValueOrDefault("implies") is not null)
            .Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = AsString(r, "id"),
                ["implies"] = AsStrings(r.GetValueOrDefault("implies")).ToArray(),
            })
            .ToList();

    /// <summary>GraphValidator: all rule ids (to detect references to non-existent rules).</summary>
    private List<IReadOnlyDictionary<string, object?>> AllRuleIds()
        => _rules.Values
            .Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.Ordinal)
                { ["id"] = AsString(r, "id") })
            .ToList();

    private void UpsertWorldKnowledge(IReadOnlyDictionary<string, object?> p)
    {
        var id = AsString(p, "id");
        if (id is null) return;
        _world[id] = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["type"] = p.GetValueOrDefault("type"),
            ["domain"] = p.GetValueOrDefault("domain"),
            ["priority"] = p.GetValueOrDefault("priority"),
            ["body"] = p.GetValueOrDefault("body"),
            ["tags"] = p.GetValueOrDefault("tags"),
        };
    }

    /// <summary>Seeds the fixed <c>tools</c> + <c>custom-tools</c> domains (literal-valued MERGE) and their edge.</summary>
    private void SeedToolDomainsRoot(string q)
    {
        var names = ExtractAll(q, "{name: '", "'");
        var labels = ExtractAll(q, ".label = '", "'");
        for (var i = 0; i < names.Count; i++)
            EnsureDomain(names[i], i < labels.Count ? labels[i] : names[i], isCore: true);
        if (names.Count >= 2) AddDomainEdge(names[0], names[1]);
    }

    /// <summary>Seeds a single toolbox subdomain (params) under the <c>tools</c> root.</summary>
    private void SeedToolboxDomain(IReadOnlyDictionary<string, object?> p)
    {
        EnsureDomain("tools", label: null, isCore: true, overwrite: false);
        var name = AsString(p, "name");
        if (name is null) return;
        EnsureDomain(name, AsString(p, "label") ?? name, isCore: true);
        AddDomainEdge("tools", name);
    }

    private void CreateDomain(IReadOnlyDictionary<string, object?> p)
    {
        var name = AsString(p, "name");
        if (name is null) return;
        if (!_domains.TryGetValue(name, out var d))
        {
            d = new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = name };
            _domains[name] = d;
        }
        d["label"] = p.GetValueOrDefault("label");
        d["description"] = p.GetValueOrDefault("description");
        d["ownerId"] = p.GetValueOrDefault("ownerId");
        d["isCore"] = false;

        var parentName = AsString(p, "parentName");
        if (parentName is not null && _domains.ContainsKey(parentName))
            AddDomainEdge(parentName, name);
    }

    private void DeleteDomain(IReadOnlyDictionary<string, object?> p)
    {
        var name = AsString(p, "name");
        if (name is null) return;
        _domains.Remove(name);
        _domainEdges.RemoveAll(e => e.Parent == name || e.Child == name);
    }

    private void EnsureDomain(string name, string? label, bool isCore, bool overwrite = true)
    {
        if (_domains.TryGetValue(name, out var d) && !overwrite) return;
        if (d is null)
        {
            d = new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = name };
            _domains[name] = d;
        }
        if (label is not null) d["label"] = label;
        d["isCore"] = isCore;
    }

    private void AddDomainEdge(string parent, string child)
    {
        if (!_domainEdges.Any(e => e.Parent == parent && e.Child == child))
            _domainEdges.Add((parent, child));
    }

    /// <summary>Extracts every literal delimited by <paramref name="start"/> and <paramref name="end"/>.</summary>
    private static List<string> ExtractAll(string source, string start, string end)
    {
        var results = new List<string>();
        var idx = 0;
        while (true)
        {
            var from = source.IndexOf(start, idx, StringComparison.Ordinal);
            if (from < 0) break;
            from += start.Length;
            var to = source.IndexOf(end, from, StringComparison.Ordinal);
            if (to < 0) break;
            results.Add(source[from..to]);
            idx = to + end.Length;
        }
        return results;
    }

    // ── Stage 3: entity layer + embedding/head coverage ──────────────────────────

    /// <summary>Entity search (ContextCompiler Phase 5, F49): entities whose name contains any search term.</summary>
    private List<IReadOnlyDictionary<string, object?>> FindEntities(IReadOnlyDictionary<string, object?> p)
    {
        var terms = AsStrings(p.GetValueOrDefault("terms")).Select(t => t.ToLowerInvariant()).ToList();
        var userId = AsString(p, "userId");
        var limit = ToIntOr(p.GetValueOrDefault("limit"), int.MaxValue);
        return _entities.Values
            .Where(e => InEntityScope(e, userId))
            .Where(e => terms.Any(t =>
                (AsString(e, "name") ?? string.Empty).ToLowerInvariant().Contains(t, StringComparison.Ordinal)))
            .Take(limit)
            .Select(EntityRow)
            .ToList();
    }

    /// <summary>Entity neighborhood (F49): RELATES_TO neighbors (either direction) of a normalized entity name.</summary>
    private List<IReadOnlyDictionary<string, object?>> RelatedEntities(IReadOnlyDictionary<string, object?> p)
    {
        var nname = AsString(p, "nname");
        var userId = AsString(p, "userId");
        var limit = ToIntOr(p.GetValueOrDefault("limit"), int.MaxValue);
        if (nname is null) return [];

        var neighbors = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in _entityEdges)
        {
            if (edge.SourceNorm == nname) neighbors.Add(edge.TargetNorm);
            else if (edge.TargetNorm == nname) neighbors.Add(edge.SourceNorm);
        }

        return _entities.Values
            .Where(e => neighbors.Contains(AsString(e, "normalizedName") ?? string.Empty) && InEntityScope(e, userId))
            .Take(limit)
            .Select(EntityRow)
            .ToList();
    }

    private static bool InEntityScope(IReadOnlyDictionary<string, object?> e, string? userId)
        => userId is null || AsString(e, "ownerId") == userId;

    private static IReadOnlyDictionary<string, object?> EntityRow(IReadOnlyDictionary<string, object?> e)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = e.GetValueOrDefault("name"),
            ["type"] = e.GetValueOrDefault("type"),
            ["description"] = e.GetValueOrDefault("description"),
            ["mentions"] = e.GetValueOrDefault("mentions") ?? 0,
        };

    /// <summary>Embedding coverage for GraphStats. Memory mode stores no chunks, so nothing is embedded.</summary>
    private List<IReadOnlyDictionary<string, object?>> EmbeddingCoverage()
    {
        var total = _rules.Count;
        var failed = _rules.Values.Count(r => ToIntOr(r.GetValueOrDefault("embedAttempts"), 0) >= 5);
        return
        [
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["embedded"] = 0,
                ["pending"] = total - failed,
                ["failed"] = failed,
                ["total"] = total,
            },
        ];
    }

    /// <summary>Head-vector coverage for GraphStats: repository/upload heads exist but hold no vectors here.</summary>
    private List<IReadOnlyDictionary<string, object?>> HeadCoverage()
        =>
        [
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["totalHeads"] = _rules.Keys.Count(IsHead),
                ["withVectors"] = 0,
            },
        ];

    private static bool IsHead(string id)
        => (id.StartsWith("git:", StringComparison.Ordinal) || id.StartsWith("upload:", StringComparison.Ordinal))
           && id.Split(':').Length == 2;

    private void IngestEntities(IReadOnlyDictionary<string, object?> p)
    {
        var ownerId = AsString(p, "ownerId");
        var now = p.GetValueOrDefault("now");
        var sourceType = p.GetValueOrDefault("sourceType");
        foreach (var ent in AsDictList(p.GetValueOrDefault("entities")))
        {
            var norm = AsString(ent, "normalizedName");
            if (norm is null) continue;
            var key = (ownerId ?? string.Empty) + " " + norm;
            if (!_entities.TryGetValue(key, out var e))
            {
                e = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["id"] = ent.GetValueOrDefault("id"),
                    ["name"] = ent.GetValueOrDefault("name"),
                    ["normalizedName"] = norm,
                    ["type"] = ent.GetValueOrDefault("type"),
                    ["description"] = ent.GetValueOrDefault("description"),
                    ["ownerId"] = ownerId,
                    ["sourceType"] = sourceType,
                    ["mentions"] = 0,
                    ["createdAt"] = now,
                };
                _entities[key] = e;
            }
            e["mentions"] = ToIntOr(e.GetValueOrDefault("mentions"), 0) + 1;
            e["updatedAt"] = now;
        }
    }

    private void IngestRelations(IReadOnlyDictionary<string, object?> p)
    {
        var ownerId = AsString(p, "ownerId");
        foreach (var rel in AsDictList(p.GetValueOrDefault("relations")))
        {
            var source = AsString(rel, "sourceNorm");
            var target = AsString(rel, "targetNorm");
            if (source is null || target is null) continue;
            if (!_entityEdges.Any(e => e.SourceNorm == source && e.TargetNorm == target && e.OwnerId == ownerId))
                _entityEdges.Add((source, target, ownerId));
        }
    }

    private static int ToIntOr(object? value, int fallback)
        => value switch
        {
            int i => i,
            long l => (int)l,
            _ => int.TryParse(value?.ToString(), out var n) ? n : fallback,
        };

    /// <summary>Materializes a Cypher list parameter of records (anonymous objects or dicts) as dictionaries.</summary>
    private static IEnumerable<IReadOnlyDictionary<string, object?>> AsDictList(object? value)
    {
        if (value is not IEnumerable enumerable || value is string) yield break;
        foreach (var item in enumerable)
        {
            if (item is null) continue;
            if (item is IReadOnlyDictionary<string, object?> dict) { yield return dict; continue; }
            var reflected = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var prop in item.GetType().GetProperties())
                reflected[prop.Name] = prop.GetValue(item);
            yield return reflected;
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

    /// <summary>
    /// C9: whether an edge is temporally valid at <paramref name="now"/> — open (no validUntil) or
    /// closing in the future. ISO-8601 "O" strings compare correctly with ordinal comparison.
    /// </summary>
    /// <param name="edge">The edge to check.</param>
    /// <param name="now">The reference timestamp (ISO-8601), or null to accept only open edges.</param>
    /// <returns>True when the edge should be traversed.</returns>
    private static bool IsEdgeOpen(Edge edge, string? now)
        => edge.ValidUntil is null || (now is not null && string.CompareOrdinal(edge.ValidUntil, now) > 0);

    /// <summary>A directed relationship edge with its temporal validity (C9).</summary>
    /// <param name="source">Source rule id.</param>
    /// <param name="rel">Relationship type (e.g. IMPLIES).</param>
    /// <param name="target">Target rule id.</param>
    private sealed class Edge(string source, string rel, string target)
    {
        /// <summary>Source rule id.</summary>
        public string Source { get; } = source;

        /// <summary>Relationship type (e.g. IMPLIES).</summary>
        public string Rel { get; } = rel;

        /// <summary>Target rule id.</summary>
        public string Target { get; } = target;

        /// <summary>First-seen timestamp (ISO-8601), stamped when the edge is created.</summary>
        public string? ValidFrom { get; set; }

        /// <summary>Set when the edge is temporally closed; null = currently valid.</summary>
        public string? ValidUntil { get; set; }
    }
}
