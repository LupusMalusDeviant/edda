using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Persists conversation history. Backed by SQLite via EF Core.
/// All operations are user-scoped — no cross-user data access is permitted.
/// </summary>
public interface IConversationStore
{
    /// <summary>
    /// Returns the message history for a conversation, most recent messages last.
    /// </summary>
    /// <param name="conversationId">The conversation identifier.</param>
    /// <param name="limit">Maximum number of messages to return (default 20 = 10 turns).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Messages in chronological order (oldest first).</returns>
    Task<IReadOnlyList<ConversationMessage>> GetHistoryAsync(
        string conversationId,
        int limit = 20,
        CancellationToken ct = default);

    /// <summary>
    /// Appends a new message to a conversation.
    /// </summary>
    /// <param name="conversationId">The conversation to append to.</param>
    /// <param name="userId">The owning user (enforces user-scoping).</param>
    /// <param name="role">The role of the message sender.</param>
    /// <param name="content">The message text.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddMessageAsync(
        string conversationId,
        string userId,
        MessageRole role,
        string content,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes all messages in a conversation. Only the owning user can clear their conversation.
    /// </summary>
    /// <param name="conversationId">The conversation to clear.</param>
    /// <param name="userId">Must match the conversation's owner.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ClearConversationAsync(
        string conversationId,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all conversations belonging to a user, ordered by most recent activity.
    /// </summary>
    /// <param name="userId">The user whose conversations to list.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Lightweight conversation summaries without message content.</returns>
    Task<IReadOnlyList<ConversationSummary>> ListConversationsAsync(
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all web-originated conversations regardless of user, ordered by most recent activity.
    /// Used for the anonymous single-user dashboard where user-scoping is not meaningful.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Lightweight conversation summaries without message content.</returns>
    Task<IReadOnlyList<ConversationSummary>> ListAllWebConversationsAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Searches conversation messages semantically for the given user using stored embeddings.
    /// Falls back to substring keyword search when the embedding service is unavailable
    /// or no embeddings have been stored yet.
    /// </summary>
    /// <param name="userId">The user whose conversations to search (user-scoped).</param>
    /// <param name="query">Natural language search query.</param>
    /// <param name="limit">Maximum number of results to return (default 5).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Matched messages ordered by descending relevance score.
    /// Semantic results include cosine similarity (0–1); keyword fallback results use score 1.0.
    /// </returns>
    Task<IReadOnlyList<ConversationSearchResult>> SearchSemanticsAsync(
        string userId,
        string query,
        int limit = 5,
        CancellationToken ct = default);
}
