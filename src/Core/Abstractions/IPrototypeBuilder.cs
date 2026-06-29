using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Generates clickable HTML/CSS/JS prototypes from an ASPS.ai project's Lastenheft.
/// Uses a Claude Code container via <see cref="ICloneOrchestrator"/> to produce the prototype,
/// which is then validated and pushed to a git branch for deployment.
/// </summary>
public interface IPrototypeBuilder
{
    /// <summary>
    /// Generates an HTML prototype from the project's Lastenheft.
    /// Uses a Claude Code container to produce clickable HTML/CSS/JS.
    /// </summary>
    /// <param name="internalProjectId">The internal project ID from ASPS import.</param>
    /// <param name="config">Design and framework configuration for the prototype.</param>
    /// <param name="userId">The user who requested the prototype.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Build result with prototype path and branch name.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the project is not imported or has no Lastenheft.</exception>
    Task<PrototypeBuildResult> BuildPrototypeAsync(
        string internalProjectId,
        PrototypeBuildConfig config,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Regenerates the prototype incorporating user feedback.
    /// Previous prototype plus feedback produces an improved version.
    /// </summary>
    /// <param name="internalProjectId">The internal project ID.</param>
    /// <param name="feedback">User feedback items targeting specific pages or elements.</param>
    /// <param name="userId">The user who provided feedback.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Build result for the new prototype version.</returns>
    Task<PrototypeBuildResult> RebuildWithFeedbackAsync(
        string internalProjectId,
        IReadOnlyList<PrototypeFeedbackItem> feedback,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the current prototype status and version for a project.
    /// </summary>
    /// <param name="internalProjectId">The internal project ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current prototype state including version and deployment URL.</returns>
    Task<PrototypeStatus> GetStatusAsync(
        string internalProjectId,
        CancellationToken ct = default);
}
