namespace Edda.AKG.Ingestion.Llm;

/// <summary>
/// Narrow text-completion client used solely by the optional ingestion enricher (see ADR-0001).
/// This is intentionally NOT a general chat-LLM runtime and is kept local to the ingestion layer — it
/// exposes a single completion call so the enricher can condense content and propose relations.
/// Implementations are swappable providers (e.g. OpenRouter, Ollama).
/// </summary>
public interface ILlmChatClient
{
    /// <summary>Provider name for diagnostics (e.g. "openrouter", "ollama").</summary>
    string ProviderName { get; }

    /// <summary>
    /// Sends a system + user prompt and returns the model's raw text response.
    /// </summary>
    /// <param name="systemPrompt">The system instruction.</param>
    /// <param name="userPrompt">The user content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The completion text (may be empty).</returns>
    Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}
