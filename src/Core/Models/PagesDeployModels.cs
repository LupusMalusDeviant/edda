namespace Edda.Core.Models;

/// <summary>
/// Result of deploying a prototype to GitLab Pages.
/// </summary>
public sealed record PagesDeployResult(
    bool Success,
    string? PagesUrl,
    string? GitLabProjectUrl,
    int Version,
    string? PipelineId,
    string? Error);

/// <summary>
/// Information about a specific Pages deployment version.
/// </summary>
public sealed record PagesDeployment(
    int Version,
    string PagesUrl,
    string BranchName,
    string PipelineStatus,
    DateTimeOffset DeployedAt);
