namespace Edda.Core.Models;

/// <summary>
/// The access scopes bound to an MCP token: which tools it may use and whether mutating tools are allowed.
/// Resolved from the token store on each MCP request and used to build the per-request exposure policy.
/// </summary>
public sealed record McpTokenScopes
{
    /// <summary>
    /// The <c>HttpContext.Items</c> key under which the resolved scopes are stashed for the current MCP
    /// request (shared between the gate middleware and the policy factory).
    /// </summary>
    public const string HttpContextItemKey = "edda-mcp-token-scopes";

    /// <summary>Short identifier of the token (for display/revocation; not secret).</summary>
    public required string Id { get; init; }

    /// <summary>Tool names this token may list and call via MCP.</summary>
    public required IReadOnlyList<string> Tools { get; init; }

    /// <summary>Whether this token may use mutating tools (otherwise the read-only guarantee applies).</summary>
    public required bool AllowWrite { get; init; }
}

/// <summary>
/// Non-secret metadata about a stored MCP token, for listing in the UI. Never carries the token plaintext
/// or its hash.
/// </summary>
public sealed record McpTokenInfo
{
    /// <summary>Short identifier (for display and revocation).</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable label.</summary>
    public required string Label { get; init; }

    /// <summary>Tool names this token grants.</summary>
    public required IReadOnlyList<string> Tools { get; init; }

    /// <summary>Whether mutating tools are permitted for this token.</summary>
    public required bool AllowWrite { get; init; }

    /// <summary>When the token was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Result of creating an MCP token: the one-time plaintext token plus its stored metadata.</summary>
public sealed record McpTokenCreated
{
    /// <summary>The plaintext token — shown to the operator exactly once; never stored or recoverable.</summary>
    public required string Token { get; init; }

    /// <summary>The stored, non-secret metadata.</summary>
    public required McpTokenInfo Info { get; init; }
}
