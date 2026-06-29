namespace Edda.AKG.Ingestion.Llm;

/// <summary>
/// Builds <see cref="ILlmChatClient"/> instances from a <see cref="LlmProviderConfig"/>. Centralizes
/// provider selection so the enricher can resolve the active provider and key at runtime (see ADR-0004).
/// </summary>
public interface ILlmChatClientFactory
{
    /// <summary>
    /// Creates a chat client for the given provider configuration.
    /// </summary>
    /// <param name="config">The resolved provider configuration.</param>
    /// <returns>An <see cref="ILlmChatClient"/> for the configured provider.</returns>
    /// <exception cref="Edda.Core.Exceptions.ProviderException">Thrown if the provider is unknown or unsupported.</exception>
    ILlmChatClient Create(LlmProviderConfig config);
}
