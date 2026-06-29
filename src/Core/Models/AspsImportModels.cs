namespace Edda.Core.Models;

/// <summary>
/// Result of importing an ASPS.ai project into the AKG.
/// </summary>
public sealed record AspsImportResult(
    string InternalProjectId,
    string AspsSlug,
    string AkgDomainName,
    int RulesCreated,
    int TasksImported,
    bool Success,
    string? Error);

/// <summary>
/// Result of syncing an ASPS.ai project (re-fetching Lastenheft and updating AKG rules).
/// </summary>
public sealed record AspsSyncResult(
    bool ContentChanged,
    int RulesUpdated,
    int RulesAdded,
    int? NewVersion);

/// <summary>
/// Summary of an imported ASPS.ai project for listing purposes.
/// </summary>
public sealed record AspsImportedProject(
    string InternalProjectId,
    string AspsSlug,
    string ProjectName,
    string AkgDomainName,
    string Status,
    DateTimeOffset ImportedAt,
    DateTimeOffset LastSyncAt);

/// <summary>
/// Result of validating a <see cref="DevPlan"/> against the ASPS task graph.
/// </summary>
public sealed record PlanValidationResult
{
    /// <summary>True if no errors were found.</summary>
    public required bool IsValid { get; init; }

    /// <summary>Critical errors that prevent plan execution.</summary>
    public required IReadOnlyList<string> Errors { get; init; }

    /// <summary>Non-critical warnings (e.g. uncovered optional tasks).</summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>
    /// Creates a valid result with no errors or warnings.
    /// </summary>
    public static PlanValidationResult Valid() =>
        new() { IsValid = true, Errors = [], Warnings = [] };

    /// <summary>
    /// Creates an invalid result with the specified errors and warnings.
    /// </summary>
    /// <param name="errors">Critical errors preventing execution.</param>
    /// <param name="warnings">Non-critical warnings.</param>
    public static PlanValidationResult Invalid(
        IReadOnlyList<string> errors,
        IReadOnlyList<string>? warnings = null) =>
        new() { IsValid = false, Errors = errors, Warnings = warnings ?? [] };
}

/// <summary>
/// Result of spawning all coding agents for an ASPS project.
/// </summary>
/// <param name="Success">True if all agents were spawned successfully.</param>
/// <param name="RepoPath">Local path to the cloned repository.</param>
/// <param name="TargetBranch">The merge target branch.</param>
/// <param name="AgentHandles">Map of task ID to coding agent handle.</param>
/// <param name="Error">Error message if spawning failed. Null on success.</param>
public sealed record AspsSpawnResult(
    bool Success,
    string? RepoPath,
    string? TargetBranch,
    IReadOnlyDictionary<string, CodingAgentHandle> AgentHandles,
    string? Error);
