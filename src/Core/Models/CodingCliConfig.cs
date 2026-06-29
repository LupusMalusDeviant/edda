namespace Edda.Core.Models;

/// <summary>
/// Well-known IDs for supported coding CLI tools.
/// Used as keys in ICredentialStore and configuration storage.
/// </summary>
public static class CodingCliIds
{
    /// <summary>Anthropic Claude Code CLI.</summary>
    public const string ClaudeCode    = "claude-code";
    /// <summary>Cursor AI code editor CLI.</summary>
    public const string Cursor        = "cursor";
    /// <summary>Google Gemini CLI.</summary>
    public const string GeminiCli     = "gemini-cli";
    /// <summary>GitHub Copilot CLI extension (via gh).</summary>
    public const string GithubCopilot = "github-copilot";
    /// <summary>Aider AI pair-programming CLI.</summary>
    public const string Aider         = "aider";
    /// <summary>Codeium AI coding assistant CLI.</summary>
    public const string Codeium       = "codeium";
}

/// <summary>
/// User-configurable settings for a single coding CLI tool.
/// Persisted in data/coding-cli-config.json.
/// </summary>
public sealed record CodingCliEntry
{
    /// <summary>Unique tool identifier matching one of the constants in <see cref="CodingCliIds"/>.</summary>
    public string Id { get; init; } = "";

    /// <summary>Human-readable display name shown in the UI.</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>Binary name to search for on the system PATH (e.g. "claude", "aider").</summary>
    public string BinaryName { get; init; } = "";

    /// <summary>Whether this tool is enabled for use by the agent runtime.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Optional explicit path to the binary. When null the registry auto-detects via PATH lookup.
    /// </summary>
    public string? BinaryPath { get; init; }

    /// <summary>Optional extra command-line arguments appended to every invocation.</summary>
    public string? ExtraArgs { get; init; }

    /// <summary>
    /// Shell command to install this tool (e.g. <c>npm install -g @anthropic-ai/claude-code</c>).
    /// Null when no automated install is available. Used by the install API endpoint.
    /// </summary>
    public string? InstallCommand { get; init; }
}

/// <summary>
/// Runtime status of a coding CLI tool, merging user config with live detection results.
/// Returned by <see cref="Edda.Core.Abstractions.ICodingCliRegistry.GetAllAsync"/>.
/// </summary>
public sealed record CodingCliStatus
{
    /// <summary>The user-configurable settings for this tool.</summary>
    public CodingCliEntry Config { get; init; } = new();

    /// <summary>True if the binary was found on the system (via PATH or explicit BinaryPath).</summary>
    public bool IsInstalled { get; init; }

    /// <summary>The resolved binary path, or null if not found.</summary>
    public string? DetectedPath { get; init; }

    /// <summary>True if an API key is stored in ICredentialStore for this tool.</summary>
    public bool HasApiKey { get; init; }

    /// <summary>Human-readable hint for the required credential (e.g. "ANTHROPIC_API_KEY").</summary>
    public string? ApiKeyHint { get; init; }

    /// <summary>
    /// Shell command to install this tool, or null if no automated install is available.
    /// Propagated from <see cref="CodingCliEntry.InstallCommand"/> or the well-known tool registry.
    /// </summary>
    public string? InstallCommand { get; init; }
}
