using System.Diagnostics.CodeAnalysis;
using Edda.AKG.Graph;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;

namespace Edda.AKG.Providers;

/// <summary>
/// <see cref="IGraphDatabaseProvider"/> implementation for Memgraph.
/// Memgraph is 100% Cypher-compatible and speaks the Bolt protocol,
/// so it reuses the same Neo4j .NET driver and <see cref="Neo4jCypherExecutor"/>.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class MemgraphGraphDatabaseProvider : IGraphDatabaseProvider
{
    private readonly IDriver _driver;
    private readonly ICypherExecutor _executor;
    private readonly Microsoft.Extensions.Logging.ILogger _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="MemgraphGraphDatabaseProvider"/>.
    /// </summary>
    /// <param name="config">Graph provider configuration with connection details.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    public MemgraphGraphDatabaseProvider(GraphProviderConfig config, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<MemgraphGraphDatabaseProvider>();

        // Memgraph defaults to no authentication; honor the config if set
        var authToken = string.Equals(config.Auth, "none", StringComparison.OrdinalIgnoreCase)
            ? AuthTokens.None
            : AuthTokens.Basic(config.Username, config.Password);

        _driver = GraphDatabase.Driver(config.Uri, authToken);
        _executor = new Neo4jCypherExecutor(_driver);

        _logger.LogInformation(
            "Memgraph graph provider initialized: {Uri} | {Component}",
            config.Uri, "GraphProvider");
    }

    /// <inheritdoc />
    public string Name => "memgraph";

    /// <inheritdoc />
    public ICypherExecutor CreateExecutor() => _executor;

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            await using var session = _driver.AsyncSession();
            var cursor = await session.RunAsync("RETURN 1 AS n");
            await cursor.ConsumeAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Memgraph health check failed | {Component}", "GraphProvider");
            return false;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _driver.DisposeAsync();
        _logger.LogDebug("Memgraph driver disposed | {Component}", "GraphProvider");
    }
}
