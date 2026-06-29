using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Persists and retrieves short-term memory (STM) entries.
/// STM is a short-lived memory layer in front of the AKG
/// (long-term, compiled knowledge). Entries expire after a TTL and are promoted to
/// the AKG by the <c>StmPromotionService</c> before deletion.
/// </summary>
public interface IShortTermMemoryStore
{
    /// <summary>
    /// Persists a new short-term memory entry.
    /// </summary>
    /// <param name="entry">The entry to store.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddAsync(ShortTermMemoryEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Returns all non-expired entries for the given user, ordered by creation time (newest first).
    /// </summary>
    /// <param name="userId">The user whose entries to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Active (non-expired) entries for the user.</returns>
    Task<IReadOnlyList<ShortTermMemoryEntry>> GetActiveAsync(
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all entries across all users that have expired but have not yet been promoted to the AKG.
    /// Called by the promotion background service.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Expired, unpromotted entries eligible for AKG compilation.</returns>
    Task<IReadOnlyList<ShortTermMemoryEntry>> GetExpiredUnpromotedAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Marks an entry as successfully promoted to the AKG.
    /// After this, the entry can be deleted.
    /// </summary>
    /// <param name="id">The entry ID to mark.</param>
    /// <param name="ct">Cancellation token.</param>
    Task MarkPromotedAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Deletes a single entry by ID.
    /// </summary>
    /// <param name="id">The entry ID to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Returns the number of active (non-expired) entries for the given user.
    /// Used for capacity enforcement (max entries per user).
    /// </summary>
    /// <param name="userId">The user to count for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Count of active entries.</returns>
    Task<int> GetCountAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Deletes the oldest <paramref name="count"/> entries for the given user to make room for new ones.
    /// Used when a user's STM is at capacity.
    /// </summary>
    /// <param name="userId">The user whose oldest entries to prune.</param>
    /// <param name="count">Number of oldest entries to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteOldestAsync(string userId, int count, CancellationToken ct = default);

    /// <summary>
    /// Returns up to <paramref name="limit"/> active (non-expired) entries for the given user,
    /// ordered by creation time descending (newest first).
    /// Used by <c>HybridMemorySearch</c> to build the full candidate corpus for BM25 + vector scoring.
    /// </summary>
    /// <param name="userId">The user whose entries to retrieve.</param>
    /// <param name="limit">Maximum number of entries to return. Default: 500.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Active entries for the user, newest first, up to <paramref name="limit"/>.</returns>
    Task<IReadOnlyList<ShortTermMemoryEntry>> GetAllAsync(
        string userId,
        int limit = 500,
        CancellationToken ct = default);
}
