using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Collects, stores, and manages prototype feedback from multiple sources:
/// the Edda feedback widget, ASPS.ai share-links, and Telegram/Blazor chat.
/// Orchestrates the feedback lifecycle: store → analyze → rebuild → mark processed.
/// </summary>
public interface IFeedbackCollector
{
    /// <summary>
    /// Stores a single feedback item for a prototype version.
    /// </summary>
    /// <param name="internalProjectId">The internal project ID.</param>
    /// <param name="feedback">The feedback item to store.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StoreFeedbackAsync(
        string internalProjectId,
        PrototypeFeedbackItem feedback,
        CancellationToken ct);

    /// <summary>
    /// Retrieves all pending (unprocessed) feedback for a project.
    /// </summary>
    /// <param name="internalProjectId">The internal project ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of unprocessed feedback items, sorted by priority (critical first).</returns>
    Task<IReadOnlyList<PrototypeFeedbackItem>> GetPendingFeedbackAsync(
        string internalProjectId,
        CancellationToken ct);

    /// <summary>
    /// Polls ASPS.ai share-link feedback and imports new entries.
    /// </summary>
    /// <param name="internalProjectId">The internal project ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of newly imported feedback items.</returns>
    Task<int> SyncAspsShareFeedbackAsync(
        string internalProjectId,
        CancellationToken ct);

    /// <summary>
    /// Marks feedback items as processed after a prototype rebuild.
    /// </summary>
    /// <param name="internalProjectId">The internal project ID.</param>
    /// <param name="feedbackIds">IDs of feedback items to mark as processed.</param>
    /// <param name="ct">Cancellation token.</param>
    Task MarkProcessedAsync(
        string internalProjectId,
        IReadOnlyList<string> feedbackIds,
        CancellationToken ct);

    /// <summary>
    /// Records that the user has approved the current prototype.
    /// Transitions the project status to "developing".
    /// </summary>
    /// <param name="internalProjectId">The internal project ID.</param>
    /// <param name="userId">The user who approved.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ApprovePrototypeAsync(
        string internalProjectId,
        string userId,
        CancellationToken ct);
}
