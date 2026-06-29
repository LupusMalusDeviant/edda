using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Deploys HTML prototypes to GitLab Pages for user review.
/// Manages GitLab project creation, CI/CD pipeline deployment,
/// version tracking, and deployment cleanup.
/// </summary>
public interface IGitLabPagesDeployer
{
    /// <summary>
    /// Deploys HTML content to GitLab Pages.
    /// Creates the GitLab project if it doesn't exist, commits files,
    /// triggers the CI/CD pipeline, and returns the Pages URL.
    /// </summary>
    /// <param name="internalProjectId">The internal project ID from ASPS import.</param>
    /// <param name="prototypePath">Local path to the prototype files.</param>
    /// <param name="version">Prototype version number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Deployment result with Pages URL and pipeline status.</returns>
    Task<PagesDeployResult> DeployAsync(
        string internalProjectId,
        string prototypePath,
        int version,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the current Pages URL for a project.
    /// </summary>
    /// <param name="internalProjectId">The internal project ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The Pages URL, or null if not deployed.</returns>
    Task<string?> GetPagesUrlAsync(
        string internalProjectId,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all deployed versions with their URLs and pipeline status.
    /// </summary>
    /// <param name="internalProjectId">The internal project ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Sorted list of deployments (newest first).</returns>
    Task<IReadOnlyList<PagesDeployment>> ListDeploymentsAsync(
        string internalProjectId,
        CancellationToken ct = default);

    /// <summary>
    /// Removes a specific Pages deployment version.
    /// </summary>
    /// <param name="internalProjectId">The internal project ID.</param>
    /// <param name="version">The version to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RemoveDeploymentAsync(
        string internalProjectId,
        int version,
        CancellationToken ct = default);
}
