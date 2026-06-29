using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Stores MCP access tokens together with their tool scopes. Tokens gate the HTTP MCP endpoint: without a
/// valid token there is no MCP access, and a token's stored scopes determine which tools it may use. Only a
/// hash of each token is persisted — the plaintext is shown once at creation and is not recoverable.
/// </summary>
public interface IMcpTokenStore
{
    /// <summary>Lists all stored tokens (metadata only — never the plaintext or hash).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The token metadata, oldest first.</returns>
    Task<IReadOnlyList<McpTokenInfo>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a new token with the given label and scopes and returns the one-time plaintext.</summary>
    /// <param name="label">Human-readable label.</param>
    /// <param name="tools">Tool names the token may use.</param>
    /// <param name="allowWrite">Whether mutating tools are permitted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created token (plaintext shown once) plus its metadata.</returns>
    Task<McpTokenCreated> CreateAsync(
        string label,
        IReadOnlyList<string> tools,
        bool allowWrite,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes (revokes) the token with the given id.</summary>
    /// <param name="id">The short token id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if a token was removed; otherwise <see langword="false"/>.</returns>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the scopes for a presented token, or <see langword="null"/> if it is missing or unknown.
    /// Called by the MCP gate on each request.
    /// </summary>
    /// <param name="token">The presented token plaintext.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The token's scopes, or null when invalid.</returns>
    Task<McpTokenScopes?> ResolveAsync(string? token, CancellationToken cancellationToken = default);
}
