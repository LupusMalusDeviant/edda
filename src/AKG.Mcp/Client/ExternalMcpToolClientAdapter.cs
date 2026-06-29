using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Mcp.Client;

/// <summary>
/// Adapts an <see cref="IExternalMcpClient"/> to the <see cref="IMcpToolClient"/> interface.
/// Allows the Agent layer to use MCP tools without depending on AKG.Mcp directly.
/// </summary>
public sealed class ExternalMcpToolClientAdapter : IMcpToolClient
{
    private readonly IExternalMcpClient _inner;

    /// <summary>
    /// Initializes a new adapter wrapping the given MCP client.
    /// </summary>
    /// <param name="inner">The external MCP client to adapt.</param>
    public ExternalMcpToolClientAdapter(IExternalMcpClient inner)
    {
        _inner = inner;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListToolNamesAsync(CancellationToken ct)
    {
        var tools = await _inner.ListToolsAsync(ct);
        return tools.Select(t => t.Name).ToList();
    }

    /// <inheritdoc />
    public async Task<McpCallResult> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct)
    {
        var result = await _inner.CallToolAsync(toolName, arguments, ct);
        var text = result.Content.FirstOrDefault()?.Text ?? "";
        return new McpCallResult(!result.IsError, text);
    }
}
