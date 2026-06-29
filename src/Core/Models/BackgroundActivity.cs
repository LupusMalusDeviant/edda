namespace Edda.Core.Models;

/// <summary>
/// Identifies a long-running background activity surfaced in the global, cross-page
/// progress indicator.
/// </summary>
public enum ActivityKind
{
    /// <summary>Ingestion of an external source (scrape) or an uploaded file.</summary>
    Import,

    /// <summary>Splitting rule bodies into embeddable chunks (part of the embedding rebuild).</summary>
    Chunking,

    /// <summary>Computing embedding vectors for rule chunks.</summary>
    Embedding,
}

/// <summary>The lifecycle state of a tracked <see cref="ActivityKind"/>.</summary>
public enum ActivityState
{
    /// <summary>The activity is currently in progress.</summary>
    Running,

    /// <summary>The activity finished successfully.</summary>
    Succeeded,

    /// <summary>The activity failed.</summary>
    Failed,
}

/// <summary>An immutable snapshot of a single activity's state at a point in time.</summary>
/// <param name="Kind">The activity kind.</param>
/// <param name="State">The current lifecycle state.</param>
/// <param name="Detail">Optional human-readable detail (e.g. a source name or a count).</param>
/// <param name="UpdatedAt">When this state was last reported.</param>
/// <param name="CanCancel">True while the activity is running and a cancel callback is registered.</param>
public sealed record ActivitySnapshot(
    ActivityKind Kind,
    ActivityState State,
    string? Detail,
    DateTimeOffset UpdatedAt,
    bool CanCancel);
