using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Edda.AKG.Mcp.Tests.Client;

/// <summary>
/// In-memory HTTP message handler for testing <see cref="Edda.AKG.Mcp.Client.ExternalMcpClient"/>.
/// Returns a preconfigured JSON response without making real network calls.
/// </summary>
internal sealed class FakeMcpHttpHandler : HttpMessageHandler
{
    private readonly string _responseJson;
    private readonly HttpStatusCode _statusCode;
    private readonly string? _sessionId;

    /// <summary>
    /// All requests received by this handler, for assertion purposes.
    /// </summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    public FakeMcpHttpHandler(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK, string? sessionId = null)
    {
        _responseJson = responseJson;
        _statusCode = statusCode;
        _sessionId = sessionId;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);

        var content = new StringContent(_responseJson, Encoding.UTF8);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        var response = new HttpResponseMessage(_statusCode) { Content = content };

        if (_sessionId is not null)
        {
            response.Headers.TryAddWithoutValidation("mcp-session-id", _sessionId);
        }

        return Task.FromResult(response);
    }
}
