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
    private readonly ILogger<MemoryConsolidator> _logger;

    /// <summary>Initializes a new <see cref="MemoryConsolidator"/>.</summary>
    /// <param name="graph">Graph the memory nodes are read from and pruned in.</param>
    /// <param name="timeProvider">Provides the current time for the fade threshold.</param>
    /// <param name="logger">Structured logger.</param>
    public MemoryConsolidator(IKnowledgeGraph graph, TimeProvider timeProvider, ILogger<MemoryConsolidator> logger)
    {
        _graph = graph;
        _timeProvider = timeProvider;
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

        var duplicates = FindDuplicates(owned);
        var duplicateIds = duplicates.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);

        var now = _timeProvider.GetUtcNow();
        var faded = owned
            .Where(m => !duplicateIds.Contains(m.Id))
            .Where(m => MemoryNodes.RecencyFactor(m.Created, now, MemoryNodes.DefaultDecayHalfLifeDays) <= PruneThreshold)
            .ToList();

        foreach (var memory in duplicates.Concat(faded))
            await _graph.DeleteRuleAsync(memory.Id, userId, isAdmin: false, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Memory consolidation userId={UserId} duplicates={Dup} faded={Faded}",
            userId, duplicates.Count, faded.Count);

        return new MemoryConsolidationResult(
            UsersProcessed: 1, DuplicatesRemoved: duplicates.Count, FadedRemoved: faded.Count);
    }

    /// <inheritdoc />
    public async Task<MemoryConsolidationResult> ConsolidateAllAsync(CancellationToken cancellationToken = default)
    {
        var owners = await _graph.ListOwnersAsync(MemoryNodes.MemoryType, cancellationToken).ConfigureAwait(false);

        var users = 0;
        var duplicates = 0;
        var faded = 0;
        foreach (var userId in owners)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ConsolidateUserAsync(userId, cancellationToken).ConfigureAwait(false);
            users++;
            duplicates += result.DuplicatesRemoved;
            faded += result.FadedRemoved;
        }

        return new MemoryConsolidationResult(users, duplicates, faded);
    }

    /// <summary>
    /// Returns the redundant memories in <paramref name="owned"/>: within each set of memories whose content
    /// normalizes to the same text, every entry except the most recently created is redundant.
    /// </summary>
    /// <param name="owned">The user's memory nodes.</param>
    /// <returns>The redundant memories to remove.</returns>
    private static IReadOnlyList<KnowledgeRule> FindDuplicates(IReadOnlyList<KnowledgeRule> owned)
    {
        var redundant = new List<KnowledgeRule>();
        foreach (var group in owned.GroupBy(m => MemoryNodes.Normalize(m.Body)))
        {
            var members = group.OrderByDescending(m => m.Created ?? DateOnly.MinValue).ToList();
            if (members.Count <= 1)
                continue;
            redundant.AddRange(members.Skip(1));
        }

        return redundant;
    }
}
