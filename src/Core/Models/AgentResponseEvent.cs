namespace Edda.Core.Models;

/// <summary>
/// Domain event published via <c>IEventBus</c> after each completed agent turn.
/// Consumed by SSE endpoints, audit workers, and monitoring subscribers.
/// </summary>
public sealed record AgentResponseEvent
{
    /// <summary>The conversation this response belongs to.</summary>
    public required string ConversationId { get; init; }

    /// <summary>The user who initiated the turn.</summary>
    public required string UserId { get; init; }

    /// <summary>The final response content delivered to the user.</summary>
    public required string Content { get; init; }

    /// <summary>IDs of AKG rules that were active during this turn.</summary>
    public IReadOnlyList<string> ActiveRuleIds { get; init; } = [];

    /// <summary>UTC timestamp of when the response was produced.</summary>
    public required DateTimeOffset Timestamp { get; init; }
}
