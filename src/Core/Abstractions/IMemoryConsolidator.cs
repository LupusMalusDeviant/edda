using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Consolidates episodic memory (M3 / ADR-0011): removes normalized-duplicate memories (keeping the most
/// recent) and prunes memories that have faded below the recall-relevance threshold. Deterministic, no LLM.
/// Exposed as a service so both the <c>consolidate_memory</c> tool and the periodic background maintenance
/// (issue C10) share a single implementation.
/// </summary>
public interface IMemoryConsolidator
{
    /// <summary>Consolidates a single user's episodic memory.</summary>
    /// <param name="userId">The user whose memory is consolidated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The removal counts for that user.</returns>
    Task<MemoryConsolidationResult> ConsolidateUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Consolidates the episodic memory of every user that currently has memories.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The aggregate result across all consolidated users.</returns>
    Task<MemoryConsolidationResult> ConsolidateAllAsync(CancellationToken cancellationToken = default);
}
