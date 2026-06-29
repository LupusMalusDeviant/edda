using Edda.Core.Abstractions;

namespace Edda.AKG.Ingestion.GitLab;

/// <summary>
/// Default <see cref="IGitLabClientFactory"/> backed by the GitLab REST API. Uses an
/// <see cref="IHttpClientFactory"/> so each created <see cref="HttpGitLabClient"/> gets a pooled
/// <see cref="HttpClient"/> with proper lifetime management.
/// </summary>
public sealed class HttpGitLabClientFactory : IGitLabClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>Initializes a new instance of the <see cref="HttpGitLabClientFactory"/> class.</summary>
    /// <param name="httpClientFactory">Factory supplying pooled HTTP clients.</param>
    public HttpGitLabClientFactory(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    /// <inheritdoc />
    public IGitLabClient Create(string baseUrl, string? token)
        => new HttpGitLabClient(_httpClientFactory.CreateClient(nameof(HttpGitLabClient)), baseUrl, token);
}
