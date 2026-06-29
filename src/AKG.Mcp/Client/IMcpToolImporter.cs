namespace Edda.AKG.Mcp.Client;

/// <summary>
/// Imports tools from external MCP servers into the internal tool registry (external→internal bridge).
/// </summary>
public interface IMcpToolImporter
{
    /// <summary>Imports all tools from a single external MCP server.</summary>
    /// <param name="serverUrl">Base URL of the external MCP server.</param>
    /// <param name="headers">Optional HTTP headers (e.g. Authorization).</param>
    /// <param name="ct">Cancellation token.</param>
    Task ImportAsync(string serverUrl, IReadOnlyDictionary<string, string>? headers, CancellationToken ct);

    /// <summary>Imports tools from all servers configured via environment variables.</summary>
    /// <param name="ct">Cancellation token.</param>
    Task ImportAllFromEnvironmentAsync(CancellationToken ct);
}
