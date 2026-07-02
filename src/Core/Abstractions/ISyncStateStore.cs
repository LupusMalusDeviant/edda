using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Persists per-source-instance ingestion sync state (an item-id → content-hash manifest) so a re-run can
/// skip items whose source content is unchanged (C5). Best-effort: implementations must not throw on a
/// missing/corrupt manifest (treated as "nothing known yet" → full ingest) and load/save are keyed by an
/// opaque instance key produced by the caller.
/// </summary>
public interface ISyncStateStore
{
    /// <summary>Loads the last persisted sync state for an instance, or an empty state if none exists.</summary>
    /// <param name="instanceKey">Opaque, per-source-instance key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persisted sync state, or an empty state when none exists or it cannot be read.</returns>
    Task<IngestionSyncState> LoadAsync(string instanceKey, CancellationToken cancellationToken = default);

    /// <summary>Persists the sync state for an instance, replacing any previous manifest.</summary>
    /// <param name="instanceKey">Opaque, per-source-instance key.</param>
    /// <param name="state">The sync state to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the state has been written.</returns>
    Task SaveAsync(string instanceKey, IngestionSyncState state, CancellationToken cancellationToken = default);
}
