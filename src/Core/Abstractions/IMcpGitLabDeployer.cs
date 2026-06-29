using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Deploys HTML prototypes to GitLab via an MCP server (e.g. n8n).
/// Files are committed as a subfolder in the infrastructure/asps repository
/// using MCP tool calls instead of direct GitLab REST API access.
/// </summary>
public interface IMcpGitLabDeployer
{
    /// <summary>
    /// Deploys prototype files to a subfolder in the GitLab infrastructure/asps repo via MCP.
    /// Target path: <c>{projectName}-{projectId}/</c>.
    /// </summary>
    /// <param name="projectName">Human-readable project name.</param>
    /// <param name="projectId">Internal project identifier.</param>
    /// <param name="prototypePath">Local path to the prototype files.</param>
    /// <param name="version">Prototype version number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Deployment result with Pages URL and status.</returns>
    Task<McpDeployResult> DeployPrototypeAsync(
        string projectName, string projectId,
        string prototypePath, int version, CancellationToken ct);

    /// <summary>
    /// Checks whether the MCP server is reachable and has the required tools.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the MCP deployer is available.</returns>
    Task<bool> IsAvailableAsync(CancellationToken ct);
}
