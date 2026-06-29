using Edda.Core.Abstractions;

namespace Edda.Agent.Providers.Embeddings;

/// <summary>
/// No-op implementation of <see cref="IEmbeddingService"/> for use when no embedding provider is configured.
/// Always returns empty arrays. Phase 2 (semantic boosting) is skipped when this service is active.
/// </summary>
public sealed class NullEmbeddingService : IEmbeddingService
{
    /// <inheritdoc/>
    public int Dimensions => 0;

    /// <inheritdoc/>
    public bool IsAvailable => false;

    /// <inheritdoc/>
    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        => Task.FromResult(Array.Empty<float>());

    /// <inheritdoc/>
    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<float[]> result = texts.Select(_ => Array.Empty<float>()).ToList();
        return Task.FromResult(result);
    }
}
