namespace Edda.Core.Models;

/// <summary>
/// A single entry in the short-term memory (STM) layer.
/// STM is a short-lived layer in front of the AKG (long-term memory) in the system prompt.
/// Entries are extracted automatically from conversation turns and expire after a configurable TTL.
/// Before expiry, each entry is promoted to the AKG via the Knowledge Compiler.
/// </summary>
public sealed record ShortTermMemoryEntry
{
    /// <summary>Unique identifier (GUID string) for this entry.</summary>
    public required string Id { get; init; }

    /// <summary>Owner user ID. All queries are user-scoped.</summary>
    public required string UserId { get; init; }

    /// <summary>The conversation that produced this entry.</summary>
    public required string ConversationId { get; init; }

    /// <summary>
    /// The extracted insight as a brief Markdown string.
    /// Typically one to three bullet points from a conversation turn.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>Keywords extracted from the content for fast relevance filtering.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>UTC timestamp when this entry was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// UTC timestamp after which this entry is eligible for AKG promotion.
    /// The promotion service picks up expired entries and compiles them into KnowledgeRules.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// True once the promotion service has successfully compiled this entry into the AKG.
    /// Promoted entries are deleted after promotion.
    /// </summary>
    public bool PromotedToAkg { get; init; }

    /// <summary>
    /// Origin of this entry.
    /// Known values: <c>chat</c> (automatic extraction) |
    /// <c>violation</c> (TDK rule violation) | <c>manual</c>.
    /// </summary>
    public string Source { get; init; } = "chat";

    /// <summary>
    /// Optional embedding vector for semantic similarity search.
    /// Null when the embedding service was unavailable at the time this entry was stored.
    /// Stored as a raw float BLOB in SQLite (4 bytes per element, little-endian).
    /// </summary>
    public float[]? Embedding { get; init; }
}
