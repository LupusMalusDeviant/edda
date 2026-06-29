using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// A source of knowledge that can be ingested into the AKG (e.g. a Git repository, Jira, Awork).
/// A source yields raw, source-neutral <see cref="IngestionItem"/>s; mapping to typed knowledge
/// rules and relations happens in a later pipeline stage. Implementations stream their items so
/// large sources do not need to be materialized at once.
/// </summary>
public interface IIngestionSource
{
    /// <summary>
    /// Discriminator identifying the kind of source this implementation handles (e.g. "git").
    /// Matched against <see cref="IngestionRequest.SourceKind"/> to select the source.
    /// </summary>
    string SourceKind { get; }

    /// <summary>
    /// Fetches raw ingestion items from the configured source.
    /// </summary>
    /// <param name="config">Source-specific configuration (repository URL, reference, globs, …).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An asynchronous stream of source-neutral ingestion items.</returns>
    IAsyncEnumerable<IngestionItem> FetchAsync(
        IngestionSourceConfig config,
        CancellationToken cancellationToken = default);
}
