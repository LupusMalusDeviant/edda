using Edda.AKG.Graph;
using Edda.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Providers;

/// <summary>
/// <see cref="IGraphDatabaseProvider"/> implementation backed by a fully in-process, in-memory graph.
/// Requires no external database — this is the zero-infra dev mode (<c>GRAPH_PROVIDER=memory</c>) that lets
/// the app run without Neo4j/Memgraph/Docker. State lives in the shared <see cref="InMemoryCypherExecutor"/>
/// for the process lifetime only; nothing is persisted across restarts.
/// </summary>
public sealed class MemoryGraphDatabaseProvider : IGraphDatabaseProvider
{
    private readonly InMemoryCypherExecutor _executor;
    private readonly ILogger<MemoryGraphDatabaseProvider> _logger;

    /// <summary>
    /// Initializes a new <see cref="MemoryGraphDatabaseProvider"/> with an empty in-memory graph.
    /// </summary>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    public MemoryGraphDatabaseProvider(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<MemoryGraphDatabaseProvider>();
        _executor = new InMemoryCypherExecutor();
        _logger.LogInformation(
            "In-memory graph provider initialized (zero-infra dev mode; data is not persisted) | {Component}",
            "GraphProvider");
    }

    /// <inheritdoc />
    public string Name => "memory";

    /// <inheritdoc />
    public ICypherExecutor CreateExecutor() => _executor;

    /// <inheritdoc />
    public Task<bool> IsHealthyAsync(CancellationToken ct = default) => Task.FromResult(true);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
