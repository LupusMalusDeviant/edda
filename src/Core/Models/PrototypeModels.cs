namespace Edda.Core.Models;

/// <summary>
/// Configuration for prototype generation controlling design style, CSS framework,
/// and visual preferences.
/// </summary>
public sealed record PrototypeBuildConfig
{
    /// <summary>Design style: "modern", "minimal", or "corporate". Default: "modern".</summary>
    public string DesignStyle { get; init; } = "modern";

    /// <summary>CSS framework: "tailwind", "bootstrap", or "vanilla". Default: "tailwind".</summary>
    public string CssFramework { get; init; } = "tailwind";

    /// <summary>Whether to include CSS animations and transitions. Default: true.</summary>
    public bool IncludeAnimations { get; init; } = true;

    /// <summary>Whether the prototype should be responsive (mobile-first). Default: true.</summary>
    public bool Responsive { get; init; } = true;

    /// <summary>Optional comma-separated hex color codes for branding (e.g. "#1a1a2e,#16213e").</summary>
    public string? BrandColors { get; init; }

    /// <summary>Optional CSS font family override.</summary>
    public string? FontFamily { get; init; }
}

/// <summary>
/// Result of a prototype build or rebuild operation.
/// </summary>
public sealed record PrototypeBuildResult(
    bool Success,
    string? PrototypePath,
    string? BranchName,
    int Version,
    string? Error);

/// <summary>
/// Current status of a project's prototype.
/// </summary>
public sealed record PrototypeStatus(
    string InternalProjectId,
    int CurrentVersion,
    string State,
    string? GitLabPagesUrl,
    DateTimeOffset? LastBuildAt);

/// <summary>
/// A single feedback item targeting a specific page or element of the prototype.
/// </summary>
public sealed record PrototypeFeedbackItem(
    string Id,
    string Page,
    string? ElementSelector,
    string Feedback,
    string Priority,
    DateTimeOffset ReceivedAt);

/// <summary>
/// Result of running the feedback loop: either an approval or a list of feedback items.
/// </summary>
public sealed record FeedbackPollResult(
    bool IsApproval,
    IReadOnlyList<PrototypeFeedbackItem> Items);
