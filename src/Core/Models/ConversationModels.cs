namespace Edda.Core.Models;

/// <summary>
/// A single persisted message in a conversation, as stored in the conversation history.
/// </summary>
/// <param name="ConversationId">The conversation this message belongs to.</param>
/// <param name="UserId">The owner of the conversation (user-scoped).</param>
/// <param name="Role">The role of the message sender.</param>
/// <param name="Content">The text content of the message.</param>
/// <param name="Timestamp">When the message was recorded.</param>
public sealed record ConversationMessage(
    string ConversationId,
    string UserId,
    MessageRole Role,
    string Content,
    DateTimeOffset Timestamp);

/// <summary>
/// Lightweight summary of a conversation, used for listing conversations without loading messages.
/// </summary>
/// <param name="ConversationId">The conversation identifier.</param>
/// <param name="UserId">The owning user.</param>
/// <param name="LastMessageAt">Timestamp of the most recent message.</param>
/// <param name="MessageCount">Total number of messages in the conversation.</param>
public sealed record ConversationSummary(
    string ConversationId,
    string UserId,
    DateTimeOffset LastMessageAt,
    int MessageCount);

/// <summary>
/// A conversation message matched by semantic or keyword search.
/// </summary>
/// <param name="MessageId">Unique message identifier.</param>
/// <param name="ConversationId">The conversation this message belongs to.</param>
/// <param name="Content">Text content of the matched message.</param>
/// <param name="Timestamp">When the message was recorded.</param>
/// <param name="Score">
/// Cosine similarity score (0–1) for semantic matches,
/// or <c>1.0</c> for keyword fallback matches.
/// </param>
public sealed record ConversationSearchResult(
    string MessageId,
    string ConversationId,
    string Content,
    DateTimeOffset Timestamp,
    float Score);
