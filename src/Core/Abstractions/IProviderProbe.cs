using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Tests connectivity to an embedding or LLM provider endpoint and, where the provider supports it, lists
/// the models the source reports as available (e.g. Ollama <c>/api/tags</c>, OpenAI-compatible
/// <c>/v1/models</c>). Backs the configuration UI's "test connection" action and the model picker, and
/// works for all reachable Ollama deployments alike — same host, a remote host over http/https, or a
/// hosted Ollama API — since each is just a base URL plus an optional token.
/// </summary>
public interface IProviderProbe
{
    /// <summary>Probes a provider endpoint for reachability and available models.</summary>
    /// <param name="provider">
    /// Provider key (e.g. <c>ollama</c>, <c>openai</c>, <c>custom</c>, <c>anthropic</c>, <c>gemini</c>,
    /// <c>google</c>, <c>voyage</c>, <c>openrouter</c>, <c>bedrock</c>).
    /// </param>
    /// <param name="baseUrl">Configured base URL; null or empty falls back to the provider default.</param>
    /// <param name="apiKey">Optional API key / bearer token (for hosted or secured instances).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Reachability, a status message, and any discovered model ids.</returns>
    Task<ProbeResult> ProbeAsync(
        string provider,
        string? baseUrl,
        string? apiKey,
        CancellationToken cancellationToken = default);
}
