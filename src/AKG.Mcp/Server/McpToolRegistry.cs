using Edda.AKG.Mcp.Adapter;
using Edda.AKG.Mcp.Models;
using Edda.Core.Abstractions;

namespace Edda.AKG.Mcp.Server;

/// <summary>
/// Exposes the allow-listed subset of internally registered <see cref="IAgentTool"/> instances
/// as MCP tool definitions. Acts as a bridge between the internal <see cref="IToolRegistry"/>
/// and the MCP protocol layer, applying an <see cref="McpExposurePolicy"/> so that only safe tools are
/// advertised to and invokable by external clients. The policy is resolved per call via a factory, so
/// settings-driven changes (UI) take effect without a restart.
/// </summary>
public sealed class McpToolRegistry : IMcpToolRegistry
{
    private readonly IToolRegistry _toolRegistry;
    private readonly Func<McpExposurePolicy> _policyFactory;

    /// <summary>
    /// Initializes a new <see cref="McpToolRegistry"/>.
    /// </summary>
    /// <param name="toolRegistry">The internal tool registry to enumerate tools from.</param>
    /// <param name="policyFactory">
    /// Supplies the current exposure policy on each call (resolved live from settings + environment).
    /// </param>
    public McpToolRegistry(IToolRegistry toolRegistry, Func<McpExposurePolicy> policyFactory)
    {
        _toolRegistry = toolRegistry;
        _policyFactory = policyFactory;
    }

    /// <summary>
    /// Returns the allow-listed internal tools converted to their MCP protocol representation.
    /// Tools not permitted by the current <see cref="McpExposurePolicy"/> are omitted.
    /// </summary>
    /// <returns>An immutable list of exposable MCP tool definitions.</returns>
    public IReadOnlyList<McpToolDefinition> GetMcpTools()
    {
        var policy = _policyFactory();
        return _toolRegistry.GetAvailableTools()
            .Where(tool => policy.IsExposed(tool.Name))
            .Select(McpAdapter.ToMcpTool)
            .ToList();
    }

    /// <summary>
    /// Determines whether a tool may be invoked via MCP, per the current <see cref="McpExposurePolicy"/>.
    /// Used by <see cref="McpServer"/> as a defense-in-depth guard before dispatching a call.
    /// </summary>
    /// <param name="toolName">The tool name requested by an external client.</param>
    /// <returns><see langword="true"/> if the tool is exposable; otherwise <see langword="false"/>.</returns>
    public bool IsExposed(string? toolName) => _policyFactory().IsExposed(toolName);
}
