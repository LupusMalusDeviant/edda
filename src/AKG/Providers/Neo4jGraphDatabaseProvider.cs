using System.Diagnostics.CodeAnalysis;
using Edda.AKG.Graph;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;

namespace Edda.AKG.Providers;

/// <summary>
/// <see cref="IGraphDatabaseProvider"/> implementation for Neo4j.
/// Uses the official Neo4j .NET driver via Bolt protocol.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class Neo4jGraphDatabaseProvider : IGraphDatabaseProvider
{
    private readonly IDriver _driver;
    private readonly ICypherExecutor _executor;
    private readonly Microsoft.Extensions.Logging.ILogger _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="Neo4jGraphDatabaseProvider"/>.
    /// </summary>
    /// <param name="config">Graph provider configuration with connection details.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    public Neo4jGraphDatabaseProvider(GraphProviderConfig config, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<Neo4jGraphDatabaseProvider>();

        var isNoAuth = string.Equals(config.Auth, "none", StringComparison.OrdinalIgnoreCase);
        var authToken = isNoAuth
            ? AuthTokens.None
            : AuthTokens.Basic(config.Username, config.Password);

        _driver = GraphDatabase.Driver(config.Uri, authToken);
        _executor = new Neo4jCypherExecutor(_driver);

        if (isNoAuth)
        {
            _logger.LogWarning(
                "Neo4j is configured WITHOUT authentication (NEO4J_AUTH_MODE=none): the database " +
                "accepts unauthenticated connections. If the Bolt endpoint ({Uri}) is reachable " +
                "beyond this host, the graph is exposed. Set NEO4J_AUTH_MODE=basic with " +
                "NEO4J_USERNAME/NEO4J_PASSWORD — the install scripts generate a random password. " +
                "| {Component}",
                config.Uri, "GraphProvider");
        }

        _logger.LogInformation(
            "Neo4j graph provider initialized: {Uri} | {Component}",
            config.Uri, "GraphProvider");
    }

    /// <inheritdoc />
    public string Name => "neo4j";

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
                "Neo4j health check failed | {Component}", "GraphProvider");
            return false;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _driver.DisposeAsync();
        _logger.LogDebug("Neo4j driver disposed | {Component}", "GraphProvider");
    }
}
