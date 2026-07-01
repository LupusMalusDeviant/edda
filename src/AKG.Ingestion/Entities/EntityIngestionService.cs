using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Ingestion.Entities;

/// <summary>
/// Default <see cref="IEntityIngestionService"/>: extracts entities/relations from a text via
/// <see cref="IEntityExtractor"/> and persists them through <see cref="IEntityStore"/>. Best-effort —
/// empty input and empty extractions ingest nothing, and a store failure leaves the graph unchanged
/// rather than throwing, so opt-in entity ingestion never breaks a caller.
/// </summary>
public sealed class EntityIngestionService : IEntityIngestionService
{
    private readonly IEntityExtractor _extractor;
    private readonly IEntityStore _store;
    private readonly ILogger<EntityIngestionService> _logger;

    /// <summary>Initializes a new instance of the <see cref="EntityIngestionService"/> class.</summary>
    /// <param name="extractor">Extracts entities and relations from text.</param>
    /// <param name="store">Persists the extracted entities and relations.</param>
    /// <param name="logger">Logger for best-effort diagnostics.</param>
    public EntityIngestionService(
        IEntityExtractor extractor,
        IEntityStore store,
        ILogger<EntityIngestionService> logger)
    {
        _extractor = extractor;
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<EntityIngestionResult> IngestTextAsync(
        string text,
        string? domainHint,
        string userId,
        string sourceType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return EntityIngestionResult.Empty;

        var extraction = await _extractor
            .ExtractAsync(text, domainHint, cancellationToken)
            .ConfigureAwait(false);
        if (extraction.Entities.Count == 0)
            return EntityIngestionResult.Empty;

        try
        {
            return await _store
                .IngestAsync(extraction, userId, sourceType, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Entity ingestion skipped — store failure (best-effort) | {Component}", "AKG");
            return EntityIngestionResult.Empty;
        }
    }
}
