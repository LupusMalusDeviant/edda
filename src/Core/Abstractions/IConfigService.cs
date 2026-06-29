using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Provides access to the agent's persisted configuration, including LLM provider details.
/// Backed by <c>data/agent-config.json</c> managed by FileConfigService.
/// </summary>
public interface IConfigService
{
    /// <summary>
    /// Returns <see langword="true"/> when a valid configuration file exists and
    /// at least a provider name is set. Returns <see langword="false"/> when the
    /// file is absent, malformed, or the provider is <see langword="null"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if the agent is configured and ready to process requests.</returns>
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads and deserializes the agent configuration file.
    /// Returns a default <see cref="AgentConfig"/> (all properties <see langword="null"/>) if
    /// the file is absent or cannot be parsed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized configuration, or a default instance on failure.</returns>
    Task<AgentConfig> GetConfigAsync(CancellationToken cancellationToken = default);
}
