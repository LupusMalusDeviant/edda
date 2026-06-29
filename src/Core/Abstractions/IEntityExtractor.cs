using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Extracts entities and relations from unstructured text (LightRAG-style). Implementations are
/// best-effort: on any LLM or parse failure they return <see cref="EntityExtractionResult.Empty"/>
/// rather than throwing, so callers (e.g. the knowledge compiler) are never broken by extraction.
/// </summary>
public interface IEntityExtractor
{
    /// <summary>
    /// Extracts entities and relations from the given text.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="domainHint">Optional domain hint to focus extraction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The extracted entities and relations; empty if nothing could be extracted.</returns>
    Task<EntityExtractionResult> ExtractAsync(
        string text,
        string? domainHint = null,
        CancellationToken cancellationToken = default);
}
