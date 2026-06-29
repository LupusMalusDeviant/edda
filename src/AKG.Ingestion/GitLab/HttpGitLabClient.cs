using System.Text.Json;
using Edda.Core.Abstractions;

namespace Edda.AKG.Ingestion.GitLab;

/// <summary>
/// <see cref="IGitLabClient"/> backed by the GitLab REST API (<c>/api/v4/groups/:id/projects</c>).
/// Infrastructure adapter (real HTTP) — covered by parsing unit tests plus an optional integration test
/// rather than fully unit-tested. The access token is supplied from configuration, never from request
/// data.
/// </summary>
public sealed class HttpGitLabClient : IGitLabClient
{
    private const int PageSize = 100;
    private const int MaxPages = 50;

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string? _token;

    /// <summary>Initializes a new instance of the <see cref="HttpGitLabClient"/> class.</summary>
    /// <param name="http">HTTP client used for the API calls.</param>
    /// <param name="baseUrl">GitLab base URL (e.g. <c>https://git.example.com</c>).</param>
    /// <param name="token">Optional access token (sent as the <c>PRIVATE-TOKEN</c> header).</param>
    public HttpGitLabClient(HttpClient http, string baseUrl, string? token = null)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _token = token;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListGroupProjectCloneUrlsAsync(
        string groupPath,
        CancellationToken cancellationToken = default)
    {
        var encoded = Uri.EscapeDataString(groupPath);
        var urls = new List<string>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var requestUrl =
                $"{_baseUrl}/api/v4/groups/{encoded}/projects" +
                $"?include_subgroups=true&archived=false&per_page={PageSize}&page={page}";

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            if (!string.IsNullOrWhiteSpace(_token))
                request.Headers.Add("PRIVATE-TOKEN", _token);

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var batch = ParseCloneUrls(json);

            urls.AddRange(batch);
            if (batch.Count < PageSize)
                break;
        }

        return urls;
    }

    /// <summary>
    /// Extracts the <c>http_url_to_repo</c> values from a GitLab projects JSON array. Internal for
    /// unit testing.
    /// </summary>
    internal static IReadOnlyList<string> ParseCloneUrls(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        var urls = new List<string>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.TryGetProperty("http_url_to_repo", out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                var url = value.GetString();
                if (!string.IsNullOrWhiteSpace(url))
                    urls.Add(url);
            }
        }

        return urls;
    }
}
