namespace Edda.Core.Models;

/// <summary>
/// Classifies the origin and trustworthiness of data flowing through the agent tool pipeline.
/// Labels form a lattice and can be combined via bitwise OR to express multiple origins.
/// Higher label values represent stricter data-flow restrictions.
/// </summary>
[Flags]
public enum TaintLabel
{
    /// <summary>Data originates from trusted internal sources. No flow restrictions apply.</summary>
    None = 0,

    /// <summary>
    /// Data returned by external network requests (web_fetch, web_search, http_request).
    /// May contain prompt injection embedded in web page content.
    /// </summary>
    ExternalNetwork = 1 << 0,

    /// <summary>
    /// Data supplied directly by the end user (chat message, file upload).
    /// Sanitized by InputSanitizer but still considered untrusted for shell/code sinks.
    /// </summary>
    UserInput = 1 << 1,

    /// <summary>
    /// Data that may identify a person (email, phone number, name, address).
    /// Must not be forwarded to external tools without explicit consent.
    /// </summary>
    Pii = 1 << 2,

    /// <summary>
    /// Data containing credentials, API keys, or secrets retrieved from ICredentialStore.
    /// Must never flow into tool output, logs, or external HTTP requests.
    /// </summary>
    Secret = 1 << 3,

    /// <summary>
    /// Output from a spawned clone or external MCP tool (untrusted agent output).
    /// May contain injection attempts from a compromised or adversarial sub-agent.
    /// </summary>
    UntrustedAgent = 1 << 4,
}

/// <summary>
/// Result of a taint check performed prior to tool execution.
/// Indicates whether the pending tool call is allowed to proceed given the taint labels
/// of any data that would flow into it from previous tool results.
/// </summary>
/// <param name="IsAllowed">Whether the tool call may proceed.</param>
/// <param name="ViolatingLabel">The label that caused the violation, if any.</param>
/// <param name="BlockedToolName">The tool that would have received tainted data, if blocked.</param>
/// <param name="Reason">Human-readable explanation used for logging and error messages.</param>
public sealed record TaintCheckResult(
    bool IsAllowed,
    TaintLabel? ViolatingLabel = null,
    string? BlockedToolName = null,
    string? Reason = null)
{
    /// <summary>Returns an allowed verdict with no violation information.</summary>
    public static TaintCheckResult Allow() => new(true);

    /// <summary>
    /// Returns a blocked verdict with the violating label, blocked tool, and a reason message.
    /// </summary>
    /// <param name="label">The taint label that triggered the block.</param>
    /// <param name="tool">The tool that would have received tainted data.</param>
    /// <param name="reason">Human-readable explanation.</param>
    public static TaintCheckResult Block(TaintLabel label, string tool, string reason)
        => new(false, label, tool, reason);
}
