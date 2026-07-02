using System.Collections.ObjectModel;

namespace Edda.Core.Models;

/// <summary>
/// The persisted incremental-sync manifest for one ingestion source instance (C5): a map from stable item id
/// to a content-hash of the item's source content. On the next run an item whose hash is unchanged is skipped.
/// </summary>
public sealed record IngestionSyncState
{
    /// <summary>Item id → content-hash of the last successfully ingested (or skipped) version.</summary>
    public IReadOnlyDictionary<string, string> ItemHashes { get; init; } =
        ReadOnlyDictionary<string, string>.Empty;
}
