namespace Edda.Core.Models;

/// <summary>
/// Input to the AKG context compiler. Describes the current user task and conversation state.
/// </summary>
public sealed record TaskContext
{
    /// <summary>The user's current task or query, used for keyword and semantic matching.</summary>
    public required string Task { get; init; }

    /// <summary>Pre-extracted concepts from the task text (e.g. via NLP or keyword extraction).</summary>
    public IReadOnlyList<string> Concepts { get; init; } = [];

    /// <summary>Recent conversation messages, used for contextual relevance in semantic phases.</summary>
    public IReadOnlyList<AgentMessage> RecentMessages { get; init; } = [];

    /// <summary>File path hints from the current working context (e.g. open files in editor).</summary>
    public IReadOnlyList<string> FileHints { get; init; } = [];

    /// <summary>
    /// User ID for scoping. null = only global rules are compiled into context.
    /// </summary>
    public string? UserId { get; init; }
}
