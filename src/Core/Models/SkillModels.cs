namespace Edda.Core.Models;

/// <summary>
/// A fully parsed skill profile that can be injected into a Clone or Hand's system prompt.
/// Describes the specialised capabilities, tool preferences, and constraints for an agent.
/// </summary>
public sealed record SkillProfile
{
    /// <summary>Unique identifier — the file name without extension (e.g., "researcher").</summary>
    public required string Name { get; init; }

    /// <summary>Human-readable display name shown in UIs and system prompts.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Brief single-line description of this profile's purpose.</summary>
    public required string Description { get; init; }

    /// <summary>
    /// Domain names this profile specialises in.
    /// Passed to ContextCompiler to boost AKG rules from these domains.
    /// </summary>
    public IReadOnlyList<string> FocusDomains { get; init; } = [];

    /// <summary>
    /// Tool names this profile preferentially uses.
    /// The ToolLoop receives a filtered tool list prioritising these tools
    /// when this profile is active.
    /// </summary>
    public IReadOnlyList<string> PreferredTools { get; init; } = [];

    /// <summary>
    /// Tool names explicitly blocked for this profile.
    /// Example: a "researcher" profile blocks "shell_execute".
    /// </summary>
    public IReadOnlyList<string> BlockedTools { get; init; } = [];

    /// <summary>
    /// Natural-language competency description injected as a system-prompt section.
    /// Contains the full Markdown body of the profile file (after the frontmatter).
    /// </summary>
    public required string CompetencyText { get; init; }

    /// <summary>
    /// Optional behavioural constraints expressed as AKG rule IDs.
    /// These rules are always included in Context Compilation regardless of scoring.
    /// </summary>
    public IReadOnlyList<string> RequiredRuleIds { get; init; } = [];
}

/// <summary>
/// Lightweight listing entry used for skill profile discovery without full parsing.
/// </summary>
/// <param name="Name">Unique profile identifier (file name without extension).</param>
/// <param name="DisplayName">Human-readable display name.</param>
/// <param name="Description">Brief single-line description.</param>
/// <param name="IsBuiltIn"><see langword="true"/> for built-in profiles; <see langword="false"/> for user-defined.</param>
public sealed record SkillProfileSummary(
    string Name,
    string DisplayName,
    string Description,
    bool IsBuiltIn);
