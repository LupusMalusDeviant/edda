namespace Edda.AKG.Graph;

/// <summary>
/// Seeds and reloads <c>:WorldKnowledge</c> nodes from the world-knowledge directory.
/// </summary>
internal interface IWorldKnowledgeSeeder
{
    /// <summary>Counts the existing world-knowledge nodes in the graph.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of <c>:WorldKnowledge</c> nodes.</returns>
    Task<long> CountAsync(CancellationToken ct = default);

    /// <summary>Seeds world-knowledge nodes from the given directory (idempotent merge).</summary>
    /// <param name="directory">Absolute path to the world-knowledge directory.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of nodes seeded.</returns>
    Task<int> SeedFromDirectoryAsync(string directory, CancellationToken ct = default);

    /// <summary>Seeds the directory only if the graph currently holds no world-knowledge nodes.</summary>
    /// <param name="directory">Absolute path to the world-knowledge directory.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of nodes seeded (0 if already populated).</returns>
    Task<int> SeedIfEmptyAsync(string directory, CancellationToken ct = default);

    /// <summary>Clears and re-seeds all world-knowledge nodes from the given directory.</summary>
    /// <param name="directory">Absolute path to the world-knowledge directory.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of nodes seeded.</returns>
    Task<int> ReloadAsync(string directory, CancellationToken ct = default);
}
