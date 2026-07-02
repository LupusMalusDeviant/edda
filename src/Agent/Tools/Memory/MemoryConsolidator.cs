using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Tools.Memory;

/// <summary>
/// Default <see cref="IMemoryConsolidator"/>: removes normalized-duplicate memories (keeping the most recent
/// of each set) and prunes memories that have faded below a recall-relevance threshold (M3 / ADR-0011).
/// Purely deterministic — no LLM. Shared by the <c>consolidate_memory</c> tool and the periodic background
/// maintenance (issue C10).
/// </summary>
internal sealed class MemoryConsolidator : IMemoryConsolidator
{
    // Memories whose recency weight has fallen to/below this are considered forgotten and pruned.
    private const double PruneThreshold = 0.05;

    private readonly IKnowledgeGraph _graph;
    private readonly TimeProvider _timeProvider;
    private readonly double _jaccardThreshold;
    private readonly ILogger<MemoryConsolidator> _logger;

    /// <summary>Initializes a new <see cref="MemoryConsolidator"/>.</summary>
    /// <param name="graph">Graph the memory nodes are read from and pruned in.</param>
    /// <param name="timeProvider">Provides the current time for the fade threshold.</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="jaccardThreshold">
    /// C4: token-Jaccard threshold above which two memories are treated as near-duplicates and merged (the
    /// newest survives). <see cref="double.PositiveInfinity"/> (the default) disables near-duplicate detection,
    /// leaving only exact normalized-duplicate removal — the behaviour before C4.
    /// </param>
    public MemoryConsolidator(
        IKnowledgeGraph graph,
        TimeProvider timeProvider,
        ILogger<MemoryConsolidator> logger,
        double jaccardThreshold = double.PositiveInfinity)
    {
        _graph = graph;
        _timeProvider = timeProvider;
        _jaccardThreshold = jaccardThreshold;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MemoryConsolidationResult> ConsolidateUserAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var memories = await _graph
            .GetRulesAsync(type: MemoryNodes.MemoryType, userId: userId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var owned = memories
            .Where(m => string.Equals(m.OwnerId, userId, StringComparison.Ordinal))
            .ToList();

        var (exactDups, nearDups) = FindRedundant(owned);
        var removedIds = exactDups.Concat(nearDups).Select(m => m.Id).ToHashSet(StringComparer.Ordinal);

        var now = _timeProvider.GetUtcNow();
        var faded = owned
            .Where(m => !removedIds.Contains(m.Id))
            .Where(m => MemoryNodes.RecencyFactor(m.Created, now, MemoryNodes.DefaultDecayHalfLifeDays) <= PruneThreshold)
            .ToList();

        foreach (var memory in exactDups.Concat(nearDups).Concat(faded))
            await _graph.DeleteRuleAsync(memory.Id, userId, isAdmin: false, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Memory consolidation userId={UserId} duplicates={Dup} near={Near} faded={Faded}",
            userId, exactDups.Count, nearDups.Count, faded.Count);

        return new MemoryConsolidationResult(
            UsersProcessed: 1,
            DuplicatesRemoved: exactDups.Count,
            FadedRemoved: faded.Count,
            NearDuplicatesRemoved: nearDups.Count)
        {
            MergedAwayBodies = nearDups.Select(m => m.Body).ToList(),
        };
    }

    /// <inheritdoc />
    public async Task<MemoryConsolidationResult> ConsolidateAllAsync(CancellationToken cancellationToken = default)
    {
        var owners = await _graph.ListOwnersAsync(MemoryNodes.MemoryType, cancellationToken).ConfigureAwait(false);

        var users = 0;
        var duplicates = 0;
        var near = 0;
        var faded = 0;
        foreach (var userId in owners)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ConsolidateUserAsync(userId, cancellationToken).ConfigureAwait(false);
            users++;
            duplicates += result.DuplicatesRemoved;
            near += result.NearDuplicatesRemoved;
            faded += result.FadedRemoved;
        }

        // MergedAwayBodies is intentionally left empty for the aggregate run — no cross-user content collected.
        return new MemoryConsolidationResult(users, duplicates, faded, near);
    }

    /// <summary>
    /// Splits the user's memories into the redundant ones to remove: <c>Exact</c> are normalized-duplicate
    /// losers (within each set of memories whose content normalizes to the same text, every entry except the
    /// most recently created), and <c>Near</c> are token-similar near-duplicate losers among the exact-dedup
    /// survivors (C4). The newest memory of each cluster survives; Stage B is skipped entirely when
    /// near-duplicate detection is disabled (<see cref="_jaccardThreshold"/> &gt; 1.0).
    /// </summary>
    /// <param name="owned">The user's memory nodes.</param>
    /// <returns>The exact-duplicate and near-duplicate memories to remove.</returns>
    private (IReadOnlyList<KnowledgeRule> Exact, IReadOnlyList<KnowledgeRule> Near) FindRedundant(
        IReadOnlyList<KnowledgeRule> owned)
    {
        // Stage A — exact normalized dedup (behaviour unchanged; deterministic Id tie-break added).
        var exact = new List<KnowledgeRule>();
        var survivors = new List<KnowledgeRule>();
        foreach (var group in owned.GroupBy(m => MemoryNodes.Normalize(m.Body)))
        {
            var members = group
                .OrderByDescending(m => m.Created ?? DateOnly.MinValue)
                .ThenBy(m => m.Id, StringComparer.Ordinal)
                .ToList();
            survivors.Add(members[0]);
            exact.AddRange(members.Skip(1));
        }

        // Stage B — token near-duplicate dedup among the Stage-A survivors (opt-in; no-op when disabled).
        var near = new List<KnowledgeRule>();
        if (_jaccardThreshold <= 1.0)
        {
            var kept = new List<KnowledgeRule>();
            foreach (var m in survivors
                         .OrderByDescending(x => x.Created ?? DateOnly.MinValue)
                         .ThenBy(x => x.Id, StringComparer.Ordinal))
            {
                if (kept.Any(k => MemorySimilarity.Jaccard(k.Body, m.Body) > _jaccardThreshold))
                    near.Add(m);      // absorbed by an already-kept newer memory
                else
                    kept.Add(m);
            }
        }

        return (exact, near);
    }
}
