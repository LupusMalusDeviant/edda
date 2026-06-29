using Edda.AKG.Mcp.Models;
using Edda.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace Edda.AKG.Mcp.Server;

/// <summary>
/// Bridges Edda's internal tool layer to the official MCP SDK server handlers.
/// <para>
/// Advertises only allow-listed tools (<see cref="McpExposurePolicy"/>) via <c>tools/list</c>
/// and dispatches <c>tools/call</c> through <see cref="McpServer"/> — which routes every call
/// through <c>IToolExecutor</c> and therefore inherits the secret-redaction, taint, audit, and
/// timeout boundary of the internal tool pipeline.
/// </para>
/// </summary>
public sealed class McpProtocolHandlers : IMcpProtocolHandlers
{
    private static readonly JsonElement DefaultObjectSchema =
        JsonSerializer.SerializeToElement(new { type = "object" });

    private readonly IMcpToolRegistry _toolRegistry;
    private readonly IMcpServer _server;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<McpProtocolHandlers> _logger;

    /// <summary>
    /// Initializes a new <see cref="McpProtocolHandlers"/>.
    /// </summary>
    /// <param name="toolRegistry">Registry providing the allow-listed MCP tool definitions.</param>
    /// <param name="server">Guarded MCP server that executes tool calls via the internal executor.</param>
    /// <param name="httpContextAccessor">Accessor used to derive the authenticated user and conversation id.</param>
    /// <param name="logger">Logger for protocol events.</param>
    public McpProtocolHandlers(
        IMcpToolRegistry toolRegistry,
        IMcpServer server,
        IHttpContextAccessor httpContextAccessor,
        ILogger<McpProtocolHandlers> logger)
    {
        _toolRegistry = toolRegistry;
        _server = server;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Builds the SDK tool definitions for all allow-listed internal tools.
    /// </summary>
    /// <returns>The exposable tools in MCP SDK representation.</returns>
    public IReadOnlyList<Tool> BuildExposedTools() =>
        _toolRegistry.GetMcpTools()
            .Select(tool => new Tool
            {
                Name = tool.Name,
                Description = tool.Description,
                InputSchema = ToObjectSchema(tool.InputSchema)
            })
            .ToList();

    /// <summary>
    /// Invokes an allow-listed tool through the guarded <see cref="McpServer"/>.
    /// </summary>
    /// <param name="toolName">The requested tool name.</param>
    /// <param name="arguments">The MCP arguments as JSON elements, or null.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The internal tool result.</returns>
    public async Task<McpToolResult> InvokeAsync(
        string? toolName,
        IDictionary<string, JsonElement>? arguments,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return new McpToolResult
            {
                Content = [new McpTextContent("Missing tool name.")],
                IsError = true
            };
        }

        var call = new McpToolCall
        {
            Name = toolName,
            Arguments = ConvertArguments(arguments)
        };

        return await _server.CallToolAsync(call, CreateExecutionContext(), ct);
    }

    /// <summary>
    /// Handler delegate for MCP <c>tools/list</c> requests (thin adapter over <see cref="BuildExposedTools"/>).
    /// </summary>
    /// <param name="context">The MCP request context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The list of allow-listed tools.</returns>
    public ValueTask<ListToolsResult> ListToolsAsync(
        RequestContext<ListToolsRequestParams> context, CancellationToken cancellationToken)
    {
        var tools = BuildExposedTools();
        _logger.LogInformation("MCP tools/list returned {Count} allow-listed tools", tools.Count);
        return ValueTask.FromResult(new ListToolsResult { Tools = [.. tools] });
    }

    /// <summary>
    /// Handler delegate for MCP <c>tools/call</c> requests (thin adapter over <see cref="InvokeAsync"/>).
    /// </summary>
    /// <param name="context">The MCP request context including tool name and arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tool result in MCP SDK representation.</returns>
    public async ValueTask<CallToolResult> CallToolAsync(
        RequestContext<CallToolRequestParams> context, CancellationToken cancellationToken)
    {
        var result = await InvokeAsync(
            context.Params?.Name, context.Params?.Arguments, cancellationToken);
        return ToCallToolResult(result);
    }

    /// <summary>
    /// Maps an internal <see cref="McpToolResult"/> to the SDK <see cref="CallToolResult"/>.
    /// </summary>
    /// <param name="result">The internal tool result.</param>
    /// <returns>The equivalent SDK result.</returns>
    public static CallToolResult ToCallToolResult(McpToolResult result) => new()
    {
        Content = [.. result.Content.Select(c => (ContentBlock)new TextContentBlock { Text = c.Text })],
        IsError = result.IsError
    };

    private ToolExecutionContext CreateExecutionContext()
    {
        var http = _httpContextAccessor.HttpContext;
        return new ToolExecutionContext
        {
            ConversationId = http?.TraceIdentifier ?? Guid.NewGuid().ToString(),
            UserId = http?.User.FindFirst("sub")?.Value
        };
    }

    private static IReadOnlyDictionary<string, object?> ConvertArguments(
        IDictionary<string, JsonElement>? arguments)
    {
        var result = new Dictionary<string, object?>();
        if (arguments is null) return result;

        foreach (var (key, value) in arguments)
            result[key] = JsonElementToObject(value);

        return result;
    }

    private static object? JsonElementToObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToObject).ToList(),
        _ => element.GetRawText()
    };

    private static JsonElement ToObjectSchema(object schema)
    {
        try
        {
            var element = JsonSerializer.SerializeToElement(schema);
            return element.ValueKind == JsonValueKind.Object
                   && element.TryGetProperty("type", out var type)
                   && type.ValueKind == JsonValueKind.String
                   && type.GetString() == "object"
                ? element
                : DefaultObjectSchema;
        }
        catch (JsonException)
        {
            return DefaultObjectSchema;
        }
    }
}
