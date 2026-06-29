namespace Edda.Core.Abstractions;

/// <summary>
/// Abstraction for executing Cypher queries against a graph database.
/// All graph queries and write operations route through this interface,
/// keeping consumers (AKG, Context) independent of any specific graph driver.
/// </summary>
public interface ICypherExecutor
{
    /// <summary>
    /// Executes a read Cypher query and returns the result rows as read-only dictionaries.
    /// </summary>
    /// <param name="cypher">The Cypher query string.</param>
    /// <param name="parameters">Optional anonymous object whose properties become query parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of result rows; each row is a dictionary of column name to value.</returns>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        string cypher,
        object? parameters = null,
        CancellationToken ct = default);

    /// <summary>
    /// Executes a write Cypher query (CREATE, MERGE, SET, DELETE) without returning results.
    /// </summary>
    /// <param name="cypher">The Cypher query string.</param>
    /// <param name="parameters">Optional anonymous object whose properties become query parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ExecuteAsync(
        string cypher,
        object? parameters = null,
        CancellationToken ct = default);
}
