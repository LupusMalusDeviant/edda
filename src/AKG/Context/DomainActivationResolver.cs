using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Context;

/// <summary>
/// Resolves which knowledge domains are relevant ("active") for a given task context.
/// A domain is active when its name or label is referenced in the task text or extracted
/// concepts; activation propagates downward to sub-domains via <c>HAS_SUBDOMAIN</c> edges.
/// The resulting set is consumed by <see cref="KeywordScorer"/> to give rules in active domains
/// a relevance bonus, so domain-relevant knowledge surfaces even without direct keyword overlap.
/// Returns an empty set when no domain matches — a graceful fallback under which the scorer
/// behaves exactly as before (no domain boost applied).
/// </summary>
internal sealed class DomainActivationResolver
{
    /// <summary>
    /// Minimum length of a domain name/label for substring matching against the task text.
    /// Shorter terms must match a concept exactly, to avoid spurious substring hits
    /// (e.g. domain "go" must not activate on the word "good").
    /// </summary>
    private const int MinSubstringMatchLength = 4;

    private readonly ICypherExecutor _cypher;
    private readonly TimeProvider _timeProvider;

    /// <summary>How long a loaded domain hierarchy is reused before the next resolve reloads it.</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private long _cacheStamp;
    private DomainTree? _cache;

    /// <summary>
    /// Initializes a new instance of <see cref="DomainActivationResolver"/>.
    /// </summary>
    /// <param name="cypher">Cypher executor used to load the domain hierarchy.</param>
    /// <param name="timeProvider">Time provider backing the domain-hierarchy cache TTL.</param>
    internal DomainActivationResolver(ICypherExecutor cypher, TimeProvider timeProvider)
    {
        _cypher = cypher;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Determines the set of active domain names for the given task context.
    /// </summary>
    /// <param name="context">Task context providing task text and extracted concepts.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Case-insensitive set of active domain names (including the sub-domains of matched domains).
    /// Empty when no domain matches or the graph holds no domains.
    /// </returns>
    internal async Task<IReadOnlySet<string>> ResolveAsync(TaskContext context, CancellationToken ct)
    {
        var tree = await GetDomainTreeAsync(ct).ConfigureAwait(false);
        var labels = tree.Labels;
        var children = tree.Children;

        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (labels.Count == 0)
            return active;

        var taskLower = context.Task.ToLowerInvariant();
        var conceptsLower = context.Concepts
            .Select(c => c.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        // Direct matches: a domain's name or label is referenced in the task or concepts.
        foreach (var (name, label) in labels)
        {
            if (IsReferenced(name, taskLower, conceptsLower)
                || (label is not null && IsReferenced(label, taskLower, conceptsLower)))
            {
                active.Add(name);
            }
        }

        // Downward expansion: activating a domain activates all of its descendants.
        var queue = new Queue<string>(active);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!children.TryGetValue(current, out var subs)) continue;
            foreach (var sub in subs)
            {
                if (active.Add(sub))
                    queue.Enqueue(sub);
            }
        }

        return active;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="term"/> is referenced by the task:
    /// an exact concept match, or — for terms of at least <see cref="MinSubstringMatchLength"/>
    /// characters — a substring of the task text or of any concept.
    /// </summary>
    /// <param name="term">Domain name or label to test.</param>
    /// <param name="taskLower">Lower-cased task text.</param>
    /// <param name="conceptsLower">Lower-cased extracted concepts.</param>
    /// <returns>Whether the term is referenced by the task.</returns>
    private static bool IsReferenced(string term, string taskLower, HashSet<string> conceptsLower)
    {
        var key = term.ToLowerInvariant();
        if (conceptsLower.Contains(key))
            return true;
        if (key.Length < MinSubstringMatchLength)
            return false;
        return taskLower.Contains(key, StringComparison.Ordinal)
            || conceptsLower.Any(c => c.Contains(key, StringComparison.Ordinal));
    }

    /// <summary>
    /// Returns the domain hierarchy (name→label and parent→children maps), reloading it from the graph at
    /// most once per <see cref="CacheTtl"/>. The hierarchy is small and changes rarely, so caching it spares
    /// a Cypher round-trip on every context compilation; newly created domains surface within one TTL window.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cached or freshly loaded domain tree.</returns>
    private async Task<DomainTree> GetDomainTreeAsync(CancellationToken ct)
    {
        var cached = _cache;
        if (cached is not null && _timeProvider.GetElapsedTime(Volatile.Read(ref _cacheStamp)) < CacheTtl)
            return cached;

        var rows = await _cypher.QueryAsync(
            """
            MATCH (d:Domain)
            OPTIONAL MATCH (d)<-[:HAS_SUBDOMAIN]-(parent:Domain)
            RETURN d.name AS name, d.label AS label, parent.name AS parent
            """,
            ct: ct).ConfigureAwait(false);

        // Build name -> label and parent -> children maps from the domain tree.
        var labels = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var children = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var name = row.TryGetValue("name", out var n) ? n?.ToString() : null;
            if (string.IsNullOrEmpty(name)) continue;

            labels[name] = row.TryGetValue("label", out var l) ? l?.ToString() : null;

            var parent = row.TryGetValue("parent", out var p) ? p?.ToString() : null;
            if (string.IsNullOrEmpty(parent)) continue;

            if (!children.TryGetValue(parent, out var list))
            {
                list = [];
                children[parent] = list;
            }

            list.Add(name);
        }

        var tree = new DomainTree(
            labels,
            children.ToDictionary(
                kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.OrdinalIgnoreCase));
        _cache = tree;
        Volatile.Write(ref _cacheStamp, _timeProvider.GetTimestamp());
        return tree;
    }

    /// <summary>Cached domain hierarchy: name→label and parent→children (case-insensitive).</summary>
    private sealed record DomainTree(
        IReadOnlyDictionary<string, string?> Labels,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Children);
}
