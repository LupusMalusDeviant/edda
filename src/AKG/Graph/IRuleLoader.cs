namespace Edda.AKG.Graph;

/// <summary>
/// Loads knowledge-rule Markdown files from a directory into the graph (idempotent upsert).
/// </summary>
internal interface IRuleLoader
{
    /// <summary>
    /// Parses and upserts every rule file under <paramref name="directory"/> into the graph.
    /// </summary>
    /// <param name="directory">Absolute path to the knowledge directory to scan.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of rules imported.</returns>
    Task<int> LoadFromDirectoryAsync(string directory, CancellationToken ct);
}
