using Edda.AKG.Mcp.Client;
using Edda.AKG.Mcp.Knowledge;
using Edda.AKG.Mcp.Server;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Mcp.DependencyInjection;

/// <summary>
/// Extension methods for registering MCP gateway services with the DI container.
/// Called from Gateway/Program.cs as part of the composition root.
/// </summary>
public static class McpServiceExtensions
{
    /// <summary>
    /// Registers the bidirectional MCP gateway: McpServer (internal→external)
    /// and McpToolImporter (external→internal).
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMcpServices(this IServiceCollection services)
    {
        services.AddHttpClient();

        // Tool-exposure allow-list (default-deny): only safe tools are advertised to and
        // invokable by external MCP clients. Configurable via MCP_EXPOSED_TOOLS
        // (comma-separated tool names); falls back to read-only AKG knowledge tools.
        var exposedTools = Environment.GetEnvironmentVariable("MCP_EXPOSED_TOOLS");
        // Read-only by default: mutating tools stay blocked unless MCP_ALLOW_WRITE_TOOLS=true.
        var allowWriteTools = string.Equals(
            Environment.GetEnvironmentVariable("MCP_ALLOW_WRITE_TOOLS"), "true", StringComparison.OrdinalIgnoreCase);
        // MCP exposure resolves from settings per call (live), falling back to MCP_* env then defaults.
        services.AddSingleton<IMcpToolRegistry>(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>();
            var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
            return new McpToolRegistry(
                sp.GetRequiredService<IToolRegistry>(),
                () =>
                {
                    // An HTTP MCP request carries its token's scopes (set by the gate middleware) — those
                    // drive exposure. stdio / non-HTTP falls back to the settings/env policy (trusted local).
                    if (httpContextAccessor.HttpContext?.Items.TryGetValue(
                            McpTokenScopes.HttpContextItemKey, out var raw) == true
                        && raw is McpTokenScopes scopes)
                    {
                        return new McpExposurePolicy(scopes.Tools, scopes.AllowWrite);
                    }

                    return ResolveMcpPolicy(settings.Current.Mcp, exposedTools, allowWriteTools);
                });
        });
        services.AddSingleton<IMcpServer, McpServer>();

        // External MCP server as a knowledge source (config-driven connector). Always available so an
        // external MCP server can be added as a source regardless of whether Edda exposes its own MCP server.
        services.AddSingleton<IExternalMcpClientFactory, HttpExternalMcpClientFactory>();
        services.AddSingleton<IIngestionSource, McpKnowledgeSource>();
        services.AddSingleton<IKnowledgeConnector, McpKnowledgeConnector>();

        // Bridge from the internal tool layer to the official MCP SDK server handlers.
        // Needs IHttpContextAccessor to derive the authenticated user for per-call scoping.
        services.AddHttpContextAccessor();
        services.AddSingleton<IMcpProtocolHandlers, McpProtocolHandlers>();

        services.AddSingleton<IMcpToolImporter>(sp =>
        {
            var toolRegistry = sp.GetRequiredService<IToolRegistry>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<McpToolImporter>>();
            var clientLogger = sp.GetRequiredService<ILogger<ExternalMcpClient>>();

            IExternalMcpClient Factory(string url, IReadOnlyDictionary<string, string>? headers)
            {
                var http = httpClientFactory.CreateClient();
                if (headers is not null)
                {
                    foreach (var (key, value) in headers)
                    {
                        http.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
                    }
                }
                return new ExternalMcpClient(url, http, clientLogger);
            }

            return new McpToolImporter(toolRegistry, Factory, logger);
        });

        // Register IMcpToolClient for the n8n MCP server (if configured)
        var n8nUrl = Environment.GetEnvironmentVariable("N8N_MCP_URL");
        if (!string.IsNullOrWhiteSpace(n8nUrl))
        {
            services.AddSingleton<IMcpToolClient>(sp =>
            {
                var n8nToken = Environment.GetEnvironmentVariable("N8N_MCP_TOKEN");
                var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                var http = httpClientFactory.CreateClient();

                if (!string.IsNullOrWhiteSpace(n8nToken))
                    http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {n8nToken}");

                var clientLogger = sp.GetRequiredService<ILogger<ExternalMcpClient>>();
                var mcpClient = new ExternalMcpClient(n8nUrl, http, clientLogger);
                return new ExternalMcpToolClientAdapter(mcpClient);
            });
        }

        return services;
    }

    /// <summary>
    /// Resolves the effective MCP exposure policy: UI settings win, then <c>MCP_*</c> env, then defaults.
    /// A disabled setting exposes nothing; write tools require an explicit opt-in (read-only by default).
    /// </summary>
    internal static McpExposurePolicy ResolveMcpPolicy(McpSettings mcp, string? envExposedCsv, bool envAllowWrite)
    {
        if (mcp.Enabled == false)
            return new McpExposurePolicy([]);

        var allowWrite = mcp.AllowWriteTools ?? envAllowWrite;
        return mcp.ExposedTools is { Count: > 0 } tools
            ? new McpExposurePolicy(tools, allowWrite)
            : McpExposurePolicy.FromConfiguration(envExposedCsv, allowWrite);
    }
}
