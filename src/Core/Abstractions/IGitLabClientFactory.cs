namespace Edda.Core.Abstractions;

/// <summary>
/// Factory that builds an <see cref="IGitLabClient"/> for a specific GitLab instance and access token,
/// resolved per ingestion run from a connector's configuration. This keeps the base URL and token
/// per source-instance rather than a process-wide singleton, so several GitLab instances or groups
/// with different tokens can be configured side by side (see ADR-0006).
/// </summary>
public interface IGitLabClientFactory
{
    /// <summary>Creates a client for the given GitLab base URL and optional access token.</summary>
    /// <param name="baseUrl">GitLab base URL (e.g. <c>https://git.example.com</c>).</param>
    /// <param name="token">Optional access token sent as the <c>PRIVATE-TOKEN</c> header.</param>
    /// <returns>A configured <see cref="IGitLabClient"/>.</returns>
    IGitLabClient Create(string baseUrl, string? token);
}
