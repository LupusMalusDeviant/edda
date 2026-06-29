namespace Edda.Core.Abstractions;

/// <summary>
/// Abstraction for a graph database driver provider.
/// Each implementation (Neo4j, Memgraph, FalkorDB, etc.) wraps the respective driver
/// and provides a <see cref="ICypherExecutor"/> for Cypher query execution.
/// Registered as a singleton in the DI container; selection via <c>GRAPH_PROVIDER</c> env-var.
/// </summary>
public interface IGraphDatabaseProvider : IAsyncDisposable
{
    /// <summary>
    /// Unique provider name (e.g. "neo4j", "memgraph", "falkordb").
    /// Used for logging, diagnostics, and configuration switches.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Creates a <see cref="ICypherExecutor"/> connected to this graph database.
    /// The returned executor is typically a singleton — callers should not dispose it.
    /// </summary>
    /// <returns>A Cypher executor bound to the underlying driver.</returns>
    ICypherExecutor CreateExecutor();

    /// <summary>
    /// Checks whether the graph database is reachable and responsive.
    /// Typically executes a lightweight Cypher query like <c>RETURN 1</c>.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> if the database is healthy; <see langword="false"/> otherwise.</returns>
    Task<bool> IsHealthyAsync(CancellationToken ct = default);
}
