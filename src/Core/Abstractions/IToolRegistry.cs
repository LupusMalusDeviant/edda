using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Registry for agent tools. Allows registering tools either as <see cref="IAgentTool"/> instances
/// or as raw lambda handlers paired with a <see cref="ToolDefinition"/>.
/// Used by <see cref="IAgentRuntime"/> to enumerate available tools for each pipeline turn.
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Registers an <see cref="IAgentTool"/> implementation, storing its definition and delegate.
    /// </summary>
    /// <param name="tool">The tool to register.</param>
    void Register(IAgentTool tool);

    /// <summary>
    /// Registers a tool using a raw definition and an execution delegate.
    /// Useful for dynamically generated tools (e.g. MCP adapter).
    /// </summary>
    /// <param name="definition">The tool definition exposed to the LLM.</param>
    /// <param name="handler">The execution delegate invoked when the tool is called.</param>
    void Register(
        ToolDefinition definition,
        Func<ToolCall, ToolExecutionContext, CancellationToken, Task<ToolResult>> handler);

    /// <summary>Returns all registered tool definitions.</summary>
    /// <returns>An unordered, immutable snapshot of all registered definitions.</returns>
    IReadOnlyList<ToolDefinition> GetAvailableTools();

    /// <summary>
    /// Returns the subset of registered tools whose names appear in <paramref name="names"/>.
    /// </summary>
    /// <param name="names">Tool names to include in the result.</param>
    /// <returns>Matching tool definitions.</returns>
    IReadOnlyList<ToolDefinition> GetFilteredTools(IReadOnlyList<string> names);

    /// <summary>
    /// Looks up a tool registered as an <see cref="IAgentTool"/> by name.
    /// Returns <see langword="null"/> if the name is unknown or the tool was registered as a lambda.
    /// </summary>
    /// <param name="name">The tool name to look up.</param>
    /// <returns>The <see cref="IAgentTool"/> instance, or <see langword="null"/>.</returns>
    IAgentTool? GetTool(string name);

    /// <summary>
    /// Removes a previously registered tool by name.
    /// Called by <see cref="IPluginLoader"/> when a plugin is unloaded, to clean up tool entries
    /// before the underlying <see cref="System.Runtime.Loader.AssemblyLoadContext"/> is released.
    /// No-op if the tool name is not registered.
    /// </summary>
    /// <param name="name">The tool name to remove.</param>
    void Unregister(string name);
}
