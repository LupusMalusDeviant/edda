namespace Edda.Core.Models;

/// <summary>
/// Result of an infrastructure provisioning operation (e.g. starting a graph database container).
/// </summary>
/// <param name="Success">Whether the provisioning completed successfully.</param>
/// <param name="ConnectionUri">The connection URI for the provisioned service (e.g. <c>bolt://localhost:7687</c>).</param>
/// <param name="ContainerId">Docker container ID of the provisioned service, if applicable.</param>
/// <param name="Message">Human-readable status message.</param>
/// <param name="WasAlreadyRunning">Whether the service was already running and no provisioning was needed.</param>
public sealed record ProvisioningResult(
    bool Success,
    string? ConnectionUri,
    string? ContainerId,
    string Message,
    bool WasAlreadyRunning = false);

/// <summary>
/// Configuration for a graph database provider connection.
/// Read from environment variables and <c>appsettings.json</c>.
/// </summary>
public sealed record GraphProviderConfig
{
    /// <summary>
    /// Provider name: "neo4j", "memgraph", or other supported providers.
    /// Maps to <c>GRAPH_PROVIDER</c> env-var.
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// Bolt connection URI (e.g. <c>bolt://localhost:7687</c>).
    /// Maps to <c>NEO4J_URI</c> env-var (kept for backward compatibility).
    /// </summary>
    public required string Uri { get; init; }

    /// <summary>
    /// Username for authentication. Default: <c>neo4j</c>.
    /// Maps to <c>NEO4J_USERNAME</c> env-var.
    /// </summary>
    public string Username { get; init; } = "neo4j";

    /// <summary>
    /// Password for authentication. Default: <c>password</c>.
    /// Maps to <c>NEO4J_PASSWORD</c> env-var.
    /// </summary>
    public string Password { get; init; } = "password";

    /// <summary>
    /// Authentication mode: "basic" (username/password) or "none".
    /// Memgraph defaults to "none"; Neo4j defaults to "basic".
    /// </summary>
    public string Auth { get; init; } = "basic";
}

/// <summary>
/// Specification for a Docker container to be provisioned for a graph database provider.
/// Contains all provider-specific Docker configuration.
/// </summary>
public sealed record GraphContainerSpec
{
    /// <summary>Docker image name with tag (e.g. <c>neo4j:5.19</c>).</summary>
    public required string Image { get; init; }

    /// <summary>Container name (e.g. <c>agentsystem-neo4j</c>).</summary>
    public required string ContainerName { get; init; }

    /// <summary>
    /// Port mappings: host port → container port.
    /// Example: <c>{ [7687] = 7687, [7474] = 7474 }</c>.
    /// </summary>
    public required IReadOnlyDictionary<int, int> PortMappings { get; init; }

    /// <summary>
    /// Environment variables to set inside the container.
    /// Example: <c>{ ["NEO4J_AUTH"] = "neo4j/password" }</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Named volume mappings: volume name → container mount path.
    /// Example: <c>{ ["neo4j_data"] = "/data" }</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Volumes { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Docker network name the container should be attached to.
    /// Default: <c>agentsystem-net</c>.
    /// </summary>
    public string NetworkName { get; init; } = "agentsystem-net";

    /// <summary>
    /// Bolt port to probe for health checks.
    /// Default: <c>7687</c>.
    /// </summary>
    public int HealthCheckPort { get; init; } = 7687;

    /// <summary>
    /// Maximum number of health check retries before giving up.
    /// Default: 30.
    /// </summary>
    public int MaxHealthRetries { get; init; } = 30;

    /// <summary>
    /// Delay between health check retries.
    /// Default: 2 seconds.
    /// </summary>
    public TimeSpan HealthRetryDelay { get; init; } = TimeSpan.FromSeconds(2);
}
