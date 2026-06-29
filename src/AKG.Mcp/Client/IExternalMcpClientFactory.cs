namespace Edda.AKG.Mcp.Client;

/// <summary>
/// Builds an <see cref="IExternalMcpClient"/> for a specific external MCP server and optional bearer
/// token, resolved per ingestion run from a connector's configuration — so several MCP servers with
/// different tokens can be configured side by side (see ADR-0006).
/// </summary>
public interface IExternalMcpClientFactory
{
    /// <summary>Creates a client for the given MCP server URL and optional bearer token.</summary>
    /// <param name="serverUrl">Base URL of the external MCP server.</param>
    /// <param name="bearerToken">Optional token sent as <c>Authorization: Bearer …</c>.</param>
    /// <returns>A configured <see cref="IExternalMcpClient"/>.</returns>
    IExternalMcpClient Create(string serverUrl, string? bearerToken);
}
