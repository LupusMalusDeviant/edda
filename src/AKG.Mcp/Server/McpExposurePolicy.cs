namespace Edda.AKG.Mcp.Server;

/// <summary>
/// Decides which internal agent tools may be exposed to external MCP clients.
/// <para>
/// Default-deny: only tools whose names are explicitly allow-listed are advertised
/// via <c>tools/list</c> and accepted by <c>tools/call</c>. This prevents dangerous
/// tools (shell execution, credential management, file or plugin manipulation) from
/// leaking to remote clients when the MCP server is enabled.
/// </para>
/// <para>
/// Read-only guarantee: mutating tools (see <see cref="WriteToolNames"/>) are blocked from MCP exposure
/// even if explicitly allow-listed, so connected LLMs get read access only — unless write access is
/// deliberately enabled (<c>MCP_ALLOW_WRITE_TOOLS=true</c>).
/// </para>
/// </summary>
public sealed class McpExposurePolicy
{
    /// <summary>
    /// Safe-by-default tool names exposed when no explicit allow-list is configured.
    /// Limited to the two read-only long-term-memory tools — the intended external surface and USP.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultExposedTools =
    [
        "search_memory",
        "list_memory"
    ];

    /// <summary>
    /// Mutating tools that must never reach external MCP clients (read-only guarantee). Even if one of
    /// these is allow-listed via <c>MCP_EXPOSED_TOOLS</c>, it stays blocked unless write access is
    /// explicitly enabled (<c>MCP_ALLOW_WRITE_TOOLS=true</c>).
    /// </summary>
    public static readonly IReadOnlyList<string> WriteToolNames =
    [
        "manage_memory",
        "manage_userdata",
        "manage_learnings",
        "remember",
        "forget"
    ];

    private static readonly HashSet<string> WriteTools = new(WriteToolNames, StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _allowed;
    private readonly bool _allowWriteTools;

    /// <summary>
    /// Initializes a new <see cref="McpExposurePolicy"/> with an explicit allow-list.
    /// </summary>
    /// <param name="allowedToolNames">Tool names permitted for MCP exposure. Compared case-insensitively.</param>
    /// <param name="allowWriteTools">
    /// When false (default), mutating tools (<see cref="WriteToolNames"/>) are never exposed, even if
    /// allow-listed — keeping external MCP access read-only. Set true only for trusted setups.
    /// </param>
    public McpExposurePolicy(IEnumerable<string> allowedToolNames, bool allowWriteTools = false)
    {
        _allowed = new HashSet<string>(allowedToolNames, StringComparer.OrdinalIgnoreCase);
        _allowWriteTools = allowWriteTools;
    }

    /// <summary>The set of tool names this policy permits for MCP exposure.</summary>
    public IReadOnlyCollection<string> AllowedToolNames => _allowed;

    /// <summary>
    /// Determines whether a tool may be exposed to external MCP clients. A tool is exposable only when it
    /// is allow-listed AND (write access is enabled OR the tool is not a mutating tool).
    /// </summary>
    /// <param name="toolName">The internal tool name to check.</param>
    /// <returns><see langword="true"/> if the tool may be exposed; otherwise <see langword="false"/>.</returns>
    public bool IsExposed(string? toolName) =>
        !string.IsNullOrWhiteSpace(toolName)
        && _allowed.Contains(toolName)
        && (_allowWriteTools || !WriteTools.Contains(toolName));

    /// <summary>
    /// Builds a policy from a comma-separated configuration value (e.g. the
    /// <c>MCP_EXPOSED_TOOLS</c> environment variable). Falls back to
    /// <see cref="DefaultExposedTools"/> when the value is null, blank, or contains no names.
    /// </summary>
    /// <param name="configuredCsv">Comma-separated tool names, or null/blank for the safe default.</param>
    /// <param name="allowWriteTools">Whether mutating tools may be exposed (default false → read-only).</param>
    /// <returns>A configured <see cref="McpExposurePolicy"/>.</returns>
    public static McpExposurePolicy FromConfiguration(string? configuredCsv, bool allowWriteTools = false)
    {
        if (string.IsNullOrWhiteSpace(configuredCsv))
            return new McpExposurePolicy(DefaultExposedTools, allowWriteTools);

        var names = configuredCsv.Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return names.Length == 0
            ? new McpExposurePolicy(DefaultExposedTools, allowWriteTools)
            : new McpExposurePolicy(names, allowWriteTools);
    }
}
