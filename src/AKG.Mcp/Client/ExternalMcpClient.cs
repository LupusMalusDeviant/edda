using Edda.AKG.Mcp.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edda.AKG.Mcp.Client;

/// <summary>
/// HTTP client that communicates with an external MCP server using JSON-RPC 2.0
/// over the Streamable HTTP transport. Performs an <c>initialize</c> handshake
/// on first use and includes the required <c>Accept</c> header for servers
/// that support SSE (e.g. n8n).
/// </summary>
public sealed class ExternalMcpClient : IExternalMcpClient
{
    private readonly string _serverUrl;
    private readonly HttpClient _http;
    private readonly ILogger<ExternalMcpClient> _logger;
    private int _requestId;
    private bool _initialized;
    private string? _sessionId;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Initializes a new <see cref="ExternalMcpClient"/>.
    /// </summary>
    /// <param name="serverUrl">Base URL of the external MCP server.</param>
    /// <param name="http">HTTP client used for sending JSON-RPC requests.</param>
    /// <param name="logger">Logger for diagnostic events.</param>
    public ExternalMcpClient(string serverUrl, HttpClient http, ILogger<ExternalMcpClient> logger)
    {
        _serverUrl = serverUrl;
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(CancellationToken ct)
    {
        _logger.LogInformation("MCP tools/list from {Server}", _serverUrl);

        var response = await SendRpcAsync("tools/list", parameters: null, ct);
        var tools = response.GetProperty("tools");

        var result = new List<McpToolDefinition>();
        foreach (var tool in tools.EnumerateArray())
        {
            result.Add(new McpToolDefinition
            {
                Name = tool.GetProperty("name").GetString()!,
                Description = tool.GetProperty("description").GetString()!,
                InputSchema = tool.TryGetProperty("inputSchema", out var schema)
                    ? (object)schema.GetRawText()
                    : new { }
            });
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<McpToolResult> CallToolAsync(
        string name,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "MCP tools/call: {Tool} on {Server}", name, _serverUrl);

        var response = await SendRpcAsync("tools/call",
            parameters: new { name, arguments },
            ct);

        var isError = response.TryGetProperty("isError", out var errorProp)
            && errorProp.GetBoolean();

        var content = new List<McpTextContent>();
        if (response.TryGetProperty("content", out var contentArray))
        {
            foreach (var item in contentArray.EnumerateArray())
            {
                var text = item.TryGetProperty("text", out var textProp)
                    ? textProp.GetString() ?? ""
                    : "";
                content.Add(new McpTextContent(text));
            }
        }

        return new McpToolResult
        {
            Content = content,
            IsError = isError
        };
    }

    /// <summary>
    /// Sends the MCP <c>initialize</c> handshake if not yet performed.
    /// Required by servers implementing Streamable HTTP transport (e.g. n8n).
    /// </summary>
    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            _logger.LogInformation("MCP initialize handshake with {Server}", _serverUrl);

            var id = Interlocked.Increment(ref _requestId);
            var initRequest = new
            {
                jsonrpc = "2.0",
                id,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "edda", version = "1.0" }
                }
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _serverUrl);
            httpRequest.Content = JsonContent.Create(initRequest, options: JsonOptions);
            httpRequest.Headers.Accept.ParseAdd("application/json");
            httpRequest.Headers.Accept.ParseAdd("text/event-stream");

            using var response = await _http.SendAsync(httpRequest, ct);
            response.EnsureSuccessStatusCode();

            // Capture Mcp-Session-Id for Streamable HTTP transport (required by n8n and others)
            if (response.Headers.TryGetValues("mcp-session-id", out var sessionValues))
            {
                _sessionId = sessionValues.FirstOrDefault();
                _logger.LogDebug("MCP session established: {SessionId}", _sessionId);
            }

            _initialized = true;
            _logger.LogInformation("MCP initialized successfully with {Server}", _serverUrl);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<JsonElement> SendRpcAsync(
        string method,
        object? parameters,
        CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);

        var id = Interlocked.Increment(ref _requestId);
        var request = new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters ?? new { }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _serverUrl);
        httpRequest.Content = JsonContent.Create(request, options: JsonOptions);
        httpRequest.Headers.Accept.ParseAdd("application/json");
        httpRequest.Headers.Accept.ParseAdd("text/event-stream");

        if (_sessionId is not null)
        {
            httpRequest.Headers.TryAddWithoutValidation("mcp-session-id", _sessionId);
        }

        using var response = await _http.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();

        // n8n may return SSE format: parse "event: message\ndata: {...}"
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        string rawBody;
        if (contentType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            rawBody = await response.Content.ReadAsStringAsync(ct);
            rawBody = ExtractSseData(rawBody);
        }
        else
        {
            rawBody = await response.Content.ReadAsStringAsync(ct);
        }

        var json = JsonDocument.Parse(rawBody);

        if (json.RootElement.TryGetProperty("error", out var error))
        {
            var message = error.TryGetProperty("message", out var msg)
                ? msg.GetString()
                : "Unknown MCP error";
            throw new InvalidOperationException($"MCP error from {_serverUrl}: {message}");
        }

        return json.RootElement.GetProperty("result");
    }

    /// <summary>
    /// Extracts the JSON payload from an SSE-formatted response body.
    /// Looks for lines starting with <c>data:</c> and returns the first valid JSON block.
    /// </summary>
    private static string ExtractSseData(string sseBody)
    {
        foreach (var line in sseBody.Split('\n'))
        {
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                return line["data:".Length..].Trim();
            }
        }

        return sseBody;
    }
}
