using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Abstracts an LLM provider. All provider implementations must implement this interface.
/// Decorated by ResilientModelClient (retry/circuit-breaker) and ConfigurableModelClient (hot-swap).
/// </summary>
public interface IModelClient
{
    /// <summary>
    /// Sends a non-streaming completion request and returns the full response.
    /// </summary>
    /// <param name="systemPrompt">The system prompt built by SystemPromptBuilder.</param>
    /// <param name="messages">The conversation history including the current user message.</param>
    /// <param name="tools">Optional list of tools to offer the model.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The complete model response including any tool calls.</returns>
    /// <exception cref="Exceptions.ProviderException">Thrown on provider-side errors (4xx/5xx).</exception>
    Task<ModelResponse> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a streaming completion request and yields events as they arrive.
    /// TDK validation must be performed after the stream is fully consumed.
    /// </summary>
    /// <param name="systemPrompt">The system prompt built by SystemPromptBuilder.</param>
    /// <param name="messages">The conversation history including the current user message.</param>
    /// <param name="tools">Optional list of tools to offer the model.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async sequence of stream events.</returns>
    IAsyncEnumerable<StreamEvent> CompleteStreamingAsync(
        string systemPrompt,
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists models available from the configured provider.
    /// Falls back to a hardcoded static list if the provider API call fails.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Available models for the current provider configuration.</returns>
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Display name of the provider (e.g. "Anthropic", "OpenAI"). Used for logging and UI.</summary>
    string ProviderName { get; }

    /// <summary>The model identifier currently configured for completions.</summary>
    string CurrentModel { get; }
}
