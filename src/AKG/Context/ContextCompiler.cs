using System.Text;
using Edda.AKG.Graph;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Context;

/// <summary>
/// Orchestrates the AKG context compilation pipeline:
/// Phase 1 — Keyword + domain-relevance scoring of all in-scope rules,
/// Phase 1b — Feedback multiplier applied to keyword scores (F32),
/// Phase 2 — Semantic boosting via embeddings (skipped if unavailable),
/// Phase 3 — Graph expansion of top-ranked rules,
/// Phase 4 — World knowledge injection.
/// </summary>
internal sealed class ContextCompiler : IContextCompiler
{
    private const int MaxActiveRules = 15;

    /// <summary>
    /// Soft character budget for the assembled knowledge section. Rules are included in relevance
    /// order until the budget is reached.
    /// </summary>
    private const int MaxContextChars = 12_000;

    /// <summary>Max entities surfaced from the entity layer (F49) and max neighbors listed per entity.</summary>
    private const int EntityMatchLimit = 5;
    private const int EntityNeighborLimit = 5;

    /// <summary>Number of repository/upload heads stage 1 pre-prunes to (ADR-0009).</summary>
    private const int TopKHeads = 3;

    private readonly ICypherExecutor _cypher;
    private readonly IEmbeddingService _embeddings;
    private readonly IHeadVectorStore? _headVectorStore;
    private readonly KeywordScorer _keywordScorer;
    private readonly SemanticBooster _semanticBooster;
    private readonly RetrievalOptions _retrievalOptions;
    private readonly GraphExpander _graphExpander;
    private readonly WorldKnowledgeFetcher _worldFetcher;
    private readonly ToolboxResolver _toolboxResolver;
    private readonly DomainActivationResolver _domainResolver;
    private readonly IRuleFeedbackService? _feedbackService;
    private readonly IEntityStore? _entityStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ContextCompiler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="ContextCompiler"/>.
    /// </summary>
    /// <param name="cypher">Cypher executor for rule queries.</param>
    /// <param name="embeddingService">Embedding service for semantic boosting.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="loggerFactory">Factory for creating sub-component loggers.</param>
    /// <param name="timeProvider">Time provider for temporal-validity ("now") comparisons.</param>
    /// <param name="feedbackService">
    /// Optional feedback service for applying confidence multipliers (F32).
    /// When null the compiler behaves identically to pre-F32 behaviour.
    /// </param>
    /// <param name="entityStore">
    /// Optional entity store (F49). When provided, a compact "related entities" section from the
    /// LightRAG-style entity layer is appended to the compiled context. Null disables the phase.
    /// </param>
    /// <param name="headVectorStore">
    /// Optional head-vector store (ADR-0009). When provided, stage 1 pre-prunes the candidate set to the
    /// most query-relevant repository/upload subtrees before loading rules. Null disables pre-pruning
    /// (the compiler then performs the full in-scope scan as before).
    /// </param>
    /// <param name="options">
    /// Optional retrieval thresholds/limits bound from <c>RETRIEVAL_*</c> configuration. Null uses the
    /// defaults (the historical hard-coded values), so behaviour is unchanged unless overridden.
    /// </param>
    public ContextCompiler(
        ICypherExecutor cypher,
        IEmbeddingService embeddingService,
        ILogger<ContextCompiler> logger,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider,
        IRuleFeedbackService? feedbackService = null,
        IEntityStore? entityStore = null,
        IHeadVectorStore? headVectorStore = null,
        RetrievalOptions? options = null)
    {
        _cypher = cypher;
        _embeddings = embeddingService;
        _headVectorStore = headVectorStore;
        _timeProvider = timeProvider;
        _entityStore = entityStore;
        _retrievalOptions = options ?? new RetrievalOptions();
        _keywordScorer = new KeywordScorer();
        _semanticBooster = new SemanticBooster(
            embeddingService,
            cypher,
            loggerFactory.CreateLogger<SemanticBooster>(),
            _retrievalOptions);
        _graphExpander = new GraphExpander(cypher, timeProvider);
        _worldFetcher = new WorldKnowledgeFetcher(cypher);
        _toolboxResolver = new ToolboxResolver();
        _domainResolver = new DomainActivationResolver(cypher, timeProvider);
        _feedbackService = feedbackService;
        _logger = logger;
    }

    /// <summary>
    /// Compiles a full context result for the given task context.
    /// </summary>
    /// <param name="context">Task context with user message, concepts, and user ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="ContextResult"/> containing active rules, conflicts, exceptions,
    /// and a formatted Markdown block for the system prompt.
    /// </returns>
    public async Task<ContextResult> CompileAsync(TaskContext context, CancellationToken ct)
    {
        // Per-phase timing (TimeProvider-based, monotonic) — emitted as a debug line at the end so the
        // retrieval bottleneck can be measured instead of guessed.
        var tsStart = _timeProvider.GetTimestamp();

        // Stage 0: embed the query once — shared by stage-1 head pre-pruning and Phase 2 semantic boosting
        // (avoids a second embed). Empty when the embedding service is unavailable.
        var queryEmbedding = await EmbedQueryAsync(context.Task, ct).ConfigureAwait(false);

        // Stage 1 (ADR-0009): pre-prune to the most query-relevant repo/upload heads.
        // Empty result → no pruning (the load below falls back to the full in-scope scan).
        var headPrefixes = await SelectHeadPrefixesAsync(queryEmbedding, context.UserId, ct).ConfigureAwait(false);

        // Phase 1: Load in-scope rules (scoped to the selected head subtrees when stage 1 found any),
        // resolve which domains are active for this task, and score by keyword relevance plus a bonus
        // for rules in active domains.
        var allRules = await LoadRulesAsync(context, headPrefixes, ct).ConfigureAwait(false);
        var tsLoad = _timeProvider.GetTimestamp();
        var activeDomains = await _domainResolver.ResolveAsync(context, ct).ConfigureAwait(false);
        var tsDomain = _timeProvider.GetTimestamp();
        if (activeDomains.Count > 0)
            _logger.LogDebug(
                "Active domains for task: {Domains} | {Component}",
                string.Join(", ", activeDomains), "AKG");

        // B5 (opt-in): expand the query with co-occurring tags/concepts from the curated knowledge —
        // deterministic, keyword path only; the embedding path keeps embedding the raw query.
        IReadOnlySet<string>? expandedTerms = null;
        if (_retrievalOptions.QueryExpansionTerms > 0)
        {
            var queryTerms = KeywordScorer.Tokenize(context.Task.ToLowerInvariant());
            foreach (var concept in context.Concepts)
                queryTerms.Add(concept.ToLowerInvariant());
            expandedTerms = QueryExpander.Expand(queryTerms, allRules, _retrievalOptions.QueryExpansionTerms);
            if (expandedTerms.Count > 0)
                _logger.LogDebug(
                    "Query expanded with {Count} co-occurrence term(s) | {Component}",
                    expandedTerms.Count, "AKG");
        }

        var scored = _keywordScorer.Score(
            allRules, context, activeDomains, expandedTerms, _retrievalOptions.QueryExpansionWeight);

        // Phase 1b: Apply feedback confidence multipliers (F32)
        scored = await ApplyFeedbackMultipliersAsync(scored, context.UserId, ct).ConfigureAwait(false);
        var tsScore = _timeProvider.GetTimestamp();

        // Phase 2: Semantic boosting (skipped if embedding service is unavailable). Reuses the stage-0 query
        // embedding so the query is embedded only once per compilation.
        var boosted = await _semanticBooster.BoostAsync(scored, context, ct, queryEmbedding).ConfigureAwait(false);
        var tsSemantic = _timeProvider.GetTimestamp();

        // Phase 3: Graph expansion of top MaxActiveRules
        var top = boosted.Take(MaxActiveRules).Select(s => s.Rule).ToList();
        var expanded = await _graphExpander.ExpandAsync(top, context.UserId, ct).ConfigureAwait(false);
        var tsExpand = _timeProvider.GetTimestamp();

        // Phase 4: World knowledge injection
        var worldKnowledge = await _worldFetcher.FetchAsync(context.Concepts, ct).ConfigureAwait(false);
        var tsWorld = _timeProvider.GetTimestamp();

        // Merge and deduplicate all active rules
        var seen = expanded.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var allActive = new List<KnowledgeRule>(expanded);

        foreach (var wk in worldKnowledge)
        {
            if (seen.Add(wk.Id))
                allActive.Add(wk);
        }

        // Post-processing: suppress contradictions, enforce the context budget, then detect & format.
        // No rules are pinned — every rule is subject to contradiction suppression and the character budget.
        var protectedIds = new HashSet<string>(StringComparer.Ordinal);

        var mergedCount = allActive.Count;
        var afterSuppress = SuppressContradictions(allActive, protectedIds);
        var budgeted = ApplyContextBudget(afterSuppress, protectedIds, MaxContextChars);
        allActive = budgeted.ToList();

        if (allActive.Count != mergedCount)
            _logger.LogDebug(
                "Context hygiene: {Suppressed} contradiction(s) suppressed, {Dropped} rule(s) over budget | {Component}",
                mergedCount - afterSuppress.Count, afterSuppress.Count - budgeted.Count, "AKG");

        var conflicts = DetectConflicts(allActive);
        var exceptions = DetectExceptions(allActive);
        var formatted = FormatContext(allActive, conflicts, exceptions);

        // Phase 5 (F49): surface the related-entity neighborhood from the entity layer (LightRAG-style
        // local context). Additive + best-effort — never breaks compilation.
        var entitySection = await BuildEntityContextAsync(context, ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(entitySection))
            formatted = string.IsNullOrEmpty(formatted) ? entitySection : $"{formatted}\n\n{entitySection}";

        _logger.LogDebug(
            "Context compiled: active={Active} conflicts={Conflicts} | {Component}",
            allActive.Count, conflicts.Count, "AKG");

        _logger.LogDebug(
            "Context compile timing (ms): load={Load:F0} domain={Domain:F0} score={Score:F0} "
            + "semantic={Semantic:F0} expand={Expand:F0} world={World:F0} total={Total:F0} | {Component}",
            _timeProvider.GetElapsedTime(tsStart, tsLoad).TotalMilliseconds,
            _timeProvider.GetElapsedTime(tsLoad, tsDomain).TotalMilliseconds,
            _timeProvider.GetElapsedTime(tsDomain, tsScore).TotalMilliseconds,
            _timeProvider.GetElapsedTime(tsScore, tsSemantic).TotalMilliseconds,
            _timeProvider.GetElapsedTime(tsSemantic, tsExpand).TotalMilliseconds,
            _timeProvider.GetElapsedTime(tsExpand, tsWorld).TotalMilliseconds,
            _timeProvider.GetElapsedTime(tsStart).TotalMilliseconds,
            "AKG");

        return new ContextResult
        {
            ActiveRules = allActive,
            Conflicts = conflicts,
            Exceptions = exceptions,
            FormattedContext = formatted,
        };
    }

    // ── Phase 5 — Entity-layer fusion (F49) ─────────────────────────────────

    /// <summary>
    /// Builds a compact "related entities" section from the entity layer (F49) for the task's concepts:
    /// matched entities plus their 1-hop neighborhood (LightRAG-style local context). Best-effort —
    /// returns empty when no entity store is configured, no concepts are present, no entities match,
    /// or any query fails.
    /// </summary>
    private async Task<string> BuildEntityContextAsync(TaskContext context, CancellationToken ct)
    {
        if (_entityStore is null || context.Concepts.Count == 0)
            return string.Empty;

        try
        {
            var matched = await _entityStore
                .FindEntitiesAsync(context.Concepts, context.UserId, EntityMatchLimit, ct)
                .ConfigureAwait(false);
            if (matched.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("## Verwandte Entitäten (Wissensgraph)");
            foreach (var entity in matched)
            {
                var related = await _entityStore
                    .GetRelatedAsync(entity.Name, context.UserId, EntityNeighborLimit, ct)
                    .ConfigureAwait(false);
                var names = string.Join(", ", related
                    .Select(r => r.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase));

                sb.Append("- **").Append(entity.Name).Append("** (").Append(entity.Type).Append(')');
                if (!string.IsNullOrEmpty(names))
                    sb.Append(" → ").Append(names);
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Entity-context phase failed (non-fatal) | {Component}", "AKG");
            return string.Empty;
        }
    }

    // ── Phase 1b — Feedback multiplier ──────────────────────────────────────

    /// <summary>
    /// Applies confidence multipliers from <see cref="IRuleFeedbackService"/> to the scored rules.
    /// Returns the same list with adjusted scores, re-sorted by descending score.
    /// Also fires usage tracking for each rule (fire-and-forget).
    /// </summary>
    private async Task<IReadOnlyList<ScoredRule>> ApplyFeedbackMultipliersAsync(
        IReadOnlyList<ScoredRule> scored, string? userId, CancellationToken ct)
    {
        if (_feedbackService is null || scored.Count == 0)
            return scored;

        var ruleIds = scored.Select(s => s.Rule.Id).ToList();
        var multipliers = await _feedbackService
            .GetMultipliersAsync(ruleIds, userId, ct)
            .ConfigureAwait(false);

        foreach (var s in scored)
        {
            if (multipliers.TryGetValue(s.Rule.Id, out var m))
                s.Score *= m;

            // Fire-and-forget usage tracking — never blocks compilation
            _ = _feedbackService.RecordUsageAsync(s.Rule.Id, CancellationToken.None);
        }

        // Re-sort after multiplier application
        var list = scored.ToList();
        list.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        return list;
    }

    /// <summary>
    /// Embeds the query text once for reuse across stage 1 and Phase 2. Returns an empty array when the
    /// embedding service is unavailable or the call fails (both phases then degrade gracefully).
    /// </summary>
    /// <param name="task">The task/query text.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The query embedding, or an empty array.</returns>
    private async Task<float[]> EmbedQueryAsync(string task, CancellationToken ct)
    {
        if (!_embeddings.IsAvailable)
            return [];

        try
        {
            return await _embeddings.EmbedAsync(task, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Query embedding failed; skipping head pre-pruning + semantic phase | {Component}", "AKG");
            return [];
        }
    }

    /// <summary>
    /// Stage 1 (ADR-0009): selects the subtree prefixes of the top-k most query-relevant repository/upload
    /// heads. Returns empty (→ full scan) when no head store is configured, the query is not embedded, or no
    /// head clears the similarity threshold — so recall never depends on a confident head match.
    /// </summary>
    /// <param name="queryEmbedding">The query embedding from stage 0.</param>
    /// <param name="userId">User scope for the head search.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Subtree id prefixes (e.g. <c>git:repo:</c>), or empty for no pre-pruning.</returns>
    private async Task<IReadOnlyList<string>> SelectHeadPrefixesAsync(
        float[] queryEmbedding, string? userId, CancellationToken ct)
    {
        if (_headVectorStore is null || queryEmbedding.Length == 0)
            return [];

        try
        {
            var heads = await _headVectorStore
                .FindTopHeadsAsync(queryEmbedding, TopKHeads, _retrievalOptions.HeadSimilarityThreshold, userId, ct)
                .ConfigureAwait(false);
            if (heads.Count == 0)
                return [];

            _logger.LogDebug(
                "Stage 1: pre-pruned to {Count} head(s): {Heads} | {Component}",
                heads.Count, string.Join(", ", heads.Select(h => h.HeadId)), "AKG");
            return heads.Select(h => h.HeadId + ":").ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Head pre-pruning failed; falling back to full scan | {Component}", "AKG");
            return [];
        }
    }

    /// <summary>
    /// Loads rules from the graph, filtering tool rules by relevant toolbox domains. When
    /// <paramref name="headPrefixes"/> is non-empty (stage 1 pre-pruned), only rules under those subtrees
    /// plus standalone (non-git/upload) rules are loaded; otherwise all in-scope rules are loaded.
    /// Non-tool rules (coding, security, world, etc.) are always loaded; tool rules only when their
    /// domain matches a resolved toolbox.
    /// </summary>
    /// <param name="context">Task context for scoping.</param>
    /// <param name="headPrefixes">Selected subtree prefixes from stage 1; empty = no pre-pruning.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The in-scope (and possibly pre-pruned) rules.</returns>
    private async Task<IReadOnlyList<KnowledgeRule>> LoadRulesAsync(
        TaskContext context, IReadOnlyList<string> headPrefixes, CancellationToken ct)
    {
        var relevantToolboxes = _toolboxResolver.Resolve(context);

        var rows = await _cypher.QueryAsync(
            """
            MATCH (r:Rule)
            WHERE (r.ownerId IS NULL OR r.ownerId = $userId)
              AND (NOT r.domain STARTS WITH 'tools.' OR r.domain IN $toolboxes)
              AND (r.validUntil IS NULL OR r.validUntil > $now)
              AND (size($prefixes) = 0
                   OR NOT (r.id STARTS WITH 'git:' OR r.id STARTS WITH 'upload:')
                   OR any(p IN $prefixes WHERE r.id STARTS WITH p))
            RETURN r
            """,
            new
            {
                userId = context.UserId,
                toolboxes = relevantToolboxes.ToList(),
                now = _timeProvider.GetUtcNow().ToString("O"),
                prefixes = headPrefixes.ToList(),
            },
            ct).ConfigureAwait(false);

        var rules = rows
            .Select(row => NodeMapper.MapRowObject(row.TryGetValue("r", out var r) ? r : null))
            .Where(r => r.Id != "unknown")
            .ToList();

        _logger.LogDebug(
            "Rules loaded: {Count} total, toolboxes={Toolboxes} | {Component}",
            rules.Count, string.Join(", ", relevantToolboxes), "AKG");

        return rules;
    }

    /// <summary>
    /// Removes contradictory rules from the active set: a rule listed in another rule's
    /// <see cref="RuleRelations.Supersedes"/> is dropped, and for a <see cref="RuleRelations.ConflictsWith"/>
    /// pair the strictly lower-priority rule is dropped (equal priority → both kept and surfaced as a
    /// conflict). Rules in <paramref name="protectedIds"/> are never removed.
    /// </summary>
    /// <param name="active">The merged active rules.</param>
    /// <param name="protectedIds">Rule IDs that must never be dropped.</param>
    /// <returns>The active rules with contradictions resolved.</returns>
    public static IReadOnlyList<KnowledgeRule> SuppressContradictions(
        IReadOnlyList<KnowledgeRule> active, IReadOnlySet<string> protectedIds)
    {
        var byId = new Dictionary<string, KnowledgeRule>(StringComparer.Ordinal);
        foreach (var r in active) byId[r.Id] = r;

        var remove = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in active)
        {
            var rel = rule.RelatesTo;
            if (rel is null) continue;

            // SUPERSEDES: this rule replaces the listed (older) rules.
            foreach (var supersededId in rel.Supersedes)
                if (byId.ContainsKey(supersededId) && !protectedIds.Contains(supersededId))
                    remove.Add(supersededId);

            // CONFLICTS_WITH: drop the strictly lower-priority side; keep both when equal.
            foreach (var conflictId in rel.ConflictsWith)
            {
                if (!byId.TryGetValue(conflictId, out var other)) continue;
                if ((int)rule.Priority > (int)other.Priority && !protectedIds.Contains(other.Id))
                    remove.Add(other.Id);
                else if ((int)rule.Priority < (int)other.Priority && !protectedIds.Contains(rule.Id))
                    remove.Add(rule.Id);
            }
        }

        return remove.Count == 0
            ? active
            : active.Where(r => !remove.Contains(r.Id)).ToList();
    }

    /// <summary>
    /// Enforces a soft character budget on the active knowledge rules so the assembled prompt does not
    /// grow unbounded. Rules in <paramref name="protectedIds"/> are always kept; the
    /// remaining rules are included in their (relevance) order until <paramref name="maxChars"/> is reached.
    /// </summary>
    /// <param name="active">The active rules, highest relevance first.</param>
    /// <param name="protectedIds">Rule IDs that must always be included.</param>
    /// <param name="maxChars">Approximate character budget for the knowledge section.</param>
    /// <returns>The budgeted active rule list.</returns>
    public static IReadOnlyList<KnowledgeRule> ApplyContextBudget(
        IReadOnlyList<KnowledgeRule> active, IReadOnlySet<string> protectedIds, int maxChars)
    {
        var budgeted = new List<KnowledgeRule>(active.Count);
        var used = 0;
        foreach (var rule in active)
        {
            var size = EstimateSize(rule);
            if (protectedIds.Contains(rule.Id))
            {
                budgeted.Add(rule); // protected rules are always kept
                used += size;
                continue;
            }

            if (used + size > maxChars)
                continue; // drop non-protected rules that exceed the budget

            budgeted.Add(rule);
            used += size;
        }

        return budgeted;
    }

    /// <summary>Estimates the formatted character footprint of a rule (body + id/type + overhead).</summary>
    private static int EstimateSize(KnowledgeRule rule)
        => rule.Body.Length + rule.Id.Length + rule.Type.Length + 16;

    private static IReadOnlyList<RuleConflict> DetectConflicts(IReadOnlyList<KnowledgeRule> rules)
    {
        var result = new List<RuleConflict>();
        var ruleIds = rules.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var rule in rules)
        {
            if (rule.RelatesTo?.ConflictsWith is null) continue;
            foreach (var conflictId in rule.RelatesTo.ConflictsWith)
            {
                if (ruleIds.Contains(conflictId))
                    result.Add(new RuleConflict(rule.Id, conflictId,
                        $"Rule '{rule.Id}' conflicts with '{conflictId}'"));
            }
        }

        return result;
    }

    private static IReadOnlyList<RuleException> DetectExceptions(IReadOnlyList<KnowledgeRule> rules)
    {
        var result = new List<RuleException>();
        var ruleIds = rules.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var rule in rules)
        {
            if (rule.RelatesTo?.ExceptionFor is null) continue;
            foreach (var exFor in rule.RelatesTo.ExceptionFor)
            {
                if (ruleIds.Contains(exFor))
                    result.Add(new RuleException(rule.Id, exFor));
            }
        }

        return result;
    }

    private static string FormatContext(
        IReadOnlyList<KnowledgeRule> active,
        IReadOnlyList<RuleConflict> conflicts,
        IReadOnlyList<RuleException> exceptions)
    {
        var sb = new StringBuilder();

        if (active.Count > 0)
        {
            sb.AppendLine("## Active Knowledge Rules");
            sb.AppendLine();
            foreach (var rule in active)
            {
                sb.AppendLine($"### [{rule.Type}] {rule.Id}");
                if (!string.IsNullOrWhiteSpace(rule.Body))
                    sb.AppendLine(rule.Body);
                sb.AppendLine();
            }
        }

        if (conflicts.Count > 0)
        {
            sb.AppendLine("## Rule Conflicts");
            foreach (var c in conflicts)
                sb.AppendLine($"- {c.Description}");
            sb.AppendLine();
        }

        if (exceptions.Count > 0)
        {
            sb.AppendLine("## Active Exceptions");
            foreach (var e in exceptions)
                sb.AppendLine($"- Rule '{e.RuleId}' is an exception for '{e.ExceptionForRuleId}'");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
