using Microsoft.Extensions.Logging;

namespace Edda.AKG.Mcp.Client;

/// <summary>
/// Default <see cref="IExternalMcpClientFactory"/> backing the JSON-RPC-over-HTTP
/// <see cref="ExternalMcpClient"/>. Uses an <see cref="IHttpClientFactory"/> for pooled clients and
/// attaches a bearer token as the <c>Authorization</c> header when one is supplied.
/// </summary>
public sealed class HttpExternalMcpClientFactory : IExternalMcpClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ExternalMcpClient> _logger;

    /// <summary>Initializes a new instance of the <see cref="HttpExternalMcpClientFactory"/> class.</summary>
    /// <param name="httpClientFactory">Factory supplying pooled HTTP clients.</param>
    /// <param name="logger">Logger handed to the created clients.</param>
    public HttpExternalMcpClientFactory(IHttpClientFactory httpClientFactory, ILogger<ExternalMcpClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public IExternalMcpClient Create(string serverUrl, string? bearerToken)
    {
        var http = _httpClientFactory.CreateClient(nameof(ExternalMcpClient));
        if (!string.IsNullOrWhiteSpace(bearerToken))
            http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {bearerToken}");

        return new ExternalMcpClient(serverUrl, http, _logger);
    }
}
