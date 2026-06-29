using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Manages git operations for dev orchestrator projects.
/// Handles repository setup, branching, worktree isolation, merging, and cleanup.
/// All operations use the git CLI via shell execution for full feature support
/// (worktrees, advanced merge strategies).
/// </summary>
public interface IGitOrchestrator
{
    /// <summary>
    /// Clones or initializes a repository for the dev project.
    /// Creates the target branch from the base branch.
    /// </summary>
    /// <param name="config">Project configuration with repo URL and branch settings.</param>
    /// <param name="projectId">Unique project identifier used for local directory naming.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Information about the prepared repository.</returns>
    Task<GitRepoInfo> SetupRepositoryAsync(
        DevProjectConfig config,
        string projectId,
        CancellationToken ct = default);

    /// <summary>
    /// Creates an isolated git worktree for a coding agent.
    /// Each agent works in its own worktree to avoid file conflicts.
    /// </summary>
    /// <param name="repoPath">Path to the main repository.</param>
    /// <param name="branchName">Branch name for the worktree.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Absolute path to the created worktree directory.</returns>
    Task<string> CreateWorktreeAsync(
        string repoPath,
        string branchName,
        CancellationToken ct = default);

    /// <summary>
    /// Merges all source branches into the target branch.
    /// Returns a result indicating success or listing conflict files.
    /// </summary>
    /// <param name="repoPath">Path to the main repository.</param>
    /// <param name="sourceBranches">Branches to merge (one per agent).</param>
    /// <param name="targetBranch">Branch to merge into.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Merge result with success flag and conflict information.</returns>
    Task<MergeResult> MergeAllAsync(
        string repoPath,
        IReadOnlyList<string> sourceBranches,
        string targetBranch,
        CancellationToken ct = default);

    /// <summary>
    /// Pushes a branch to the remote repository.
    /// </summary>
    /// <param name="repoPath">Path to the local repository.</param>
    /// <param name="branch">Branch to push.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PushAsync(
        string repoPath,
        string branch,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the resolved repository path for a project based on the configured projects directory.
    /// </summary>
    /// <param name="projectId">Unique project identifier.</param>
    /// <returns>Absolute or relative path to the project's repo directory.</returns>
    string GetProjectRepoPath(string projectId);

    /// <summary>
    /// Cleans up worktrees and temporary branches after project completion.
    /// </summary>
    /// <param name="repoPath">Path to the main repository.</param>
    /// <param name="branches">Branches to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CleanupAsync(
        string repoPath,
        IReadOnlyList<string> branches,
        CancellationToken ct = default);
}
