using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Orchestrates entity ingestion for a text: extract entities/relations, then persist them to the
/// entity store. Best-effort — returns <see cref="EntityIngestionResult.Empty"/> for empty input and
/// never throws on extraction failures.
/// </summary>
public interface IEntityIngestionService
{
    /// <summary>
    /// Extracts entities and relations from <paramref name="text"/> and ingests them for the user.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="domainHint">Optional domain hint passed to the extractor.</param>
    /// <param name="userId">Owner of the ingested entities.</param>
    /// <param name="sourceType">Provenance label (e.g. "chat", "web", "codebase").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Counts of entities and relations ingested.</returns>
    Task<EntityIngestionResult> IngestTextAsync(
        string text,
        string? domainHint,
        string userId,
        string sourceType,
        CancellationToken cancellationToken = default);
}
