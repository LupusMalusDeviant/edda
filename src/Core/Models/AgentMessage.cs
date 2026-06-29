namespace Edda.Core.Models;

/// <summary>
/// Represents a single message in an agent conversation.
/// Supports user, assistant, tool, and system roles.
/// </summary>
public sealed record AgentMessage
{
    /// <summary>The role of the message sender.</summary>
    public required MessageRole Role { get; init; }

    /// <summary>Text content of the message. Null for pure tool-result messages.</summary>
    public string? Content { get; init; }

    /// <summary>Tool calls requested by the assistant. Only set when Role=Assistant.</summary>
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }

    /// <summary>Results from tool executions. Only set when Role=Tool.</summary>
    public IReadOnlyList<ToolResult>? ToolResults { get; init; }

    /// <summary>
    /// Multimodal content parts (text and/or images).
    /// When set, providers use this list instead of <see cref="Content"/> for the LLM request.
    /// Only valid for <see cref="MessageRole.User"/> messages.
    /// </summary>
    public IReadOnlyList<ContentPart>? ContentParts { get; init; }

    /// <summary>Creates a user message with the given text content.</summary>
    /// <param name="content">The user message text.</param>
    /// <returns>A new user <see cref="AgentMessage"/>.</returns>
    public static AgentMessage User(string content) =>
        new() { Role = MessageRole.User, Content = content };

    /// <summary>
    /// Creates a user message with multimodal content (text + images).
    /// </summary>
    /// <param name="parts">Ordered list of content parts to send to the model.</param>
    /// <returns>A new multimodal user <see cref="AgentMessage"/>.</returns>
    public static AgentMessage UserWithParts(IReadOnlyList<ContentPart> parts) =>
        new() { Role = MessageRole.User, ContentParts = parts };

    /// <summary>Creates an assistant message, optionally with tool calls.</summary>
    public static AgentMessage Assistant(string? content, IReadOnlyList<ToolCall>? toolCalls = null) =>
        new() { Role = MessageRole.Assistant, Content = content, ToolCalls = toolCalls };

    /// <summary>Creates a tool-results message containing one or more tool results.</summary>
    public static AgentMessage FromToolResults(IReadOnlyList<ToolResult> results) =>
        new() { Role = MessageRole.Tool, ToolResults = results };
}

/// <summary>Defines the role of a message participant in the conversation.</summary>
public enum MessageRole
{
    /// <summary>Message from the human user.</summary>
    User,

    /// <summary>Message from the AI assistant.</summary>
    Assistant,

    /// <summary>Tool execution results returned to the model.</summary>
    Tool,

    /// <summary>System-level instruction message.</summary>
    System
}
