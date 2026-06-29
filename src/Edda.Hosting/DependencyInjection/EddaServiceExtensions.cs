using Edda.Agent.DependencyInjection;
using Edda.AKG.DependencyInjection;
using Edda.AKG.Ingestion.DependencyInjection;
using Edda.AKG.Mcp.DependencyInjection;
using Edda.AKG.Mcp.Server;
using Edda.Core.Abstractions;
using Edda.Embeddings.DependencyInjection;
using Edda.Sandboxing.DependencyInjection;
using Edda.Security.DependencyInjection;
using Edda.Security.Mcp;
using Edda.Hosting.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Edda.Hosting.DependencyInjection;

/// <summary>
/// Composition root for the standalone AKG + TDK service graph, shared by the web host and the
/// stdio MCP host. Wires embeddings, security, the knowledge graph, the TDK engine, the lean tool
/// set, the MCP bridge, and the sandbox factories — but no chat-LLM runtime.
/// </summary>
public static class EddaServiceExtensions
{
    /// <summary>
    /// Registers every service required to run AKG retrieval, TDK validation, the lean tool set,
    /// and the MCP tool bridge. Both hosts call this; transport-specific MCP wiring
    /// (<c>WithHttpTransport</c>/<c>WithStdioServerTransport</c>) is added by each host separately.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">Configuration source for graph, embedding, and feedback settings.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddEddaCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddSingleton<IIdentityContext, LocalIdentityContext>();

        services.AddEmbeddingService(configuration);   // all six providers (EMBEDDING_PROVIDER)
        services.AddSecurityServices();                 // SecretRedactor, HmacAuditLog, ...
        services.AddSingleton<IMcpTokenStore, FileMcpTokenStore>(); // scoped MCP access tokens (file-backed)
        services.AddAkgServices(configuration);         // graph provider, ICypherExecutor, IKnowledgeGraph, ...
        services.AddTdkEngine();                         // ToolRegistry (+ IToolExecutor/IToolRegistry), TdkEngine, IFileSystem
        services.AddLeanAgentTools();                    // 6 tools + IToolKnowledgeService + registrar
        services.AddMcpServices();                       // McpServer + McpToolRegistry + handlers (importer dormant)
        services.AddSandboxingServices();                // ISandboxFactory (docker/wasm/null) for TDK
        services.AddIngestionServices(configuration);    // Git source + clone client + ingestion pipeline

        return services;
    }

    /// <summary>
    /// Instructions advertised to MCP clients on connect (the <c>initialize</c> result's
    /// <c>instructions</c> field). Frames Edda as the client's long-term memory so agents query it
    /// before scanning the filesystem, and points at the two read-only memory tools.
    /// </summary>
    private const string McpServerInstructions =
        "Edda is your persistent long-term memory across sessions. It stores what you and the user " +
        "have learned about the user, their projects, repositories, code, architecture decisions and " +
        "preferences. Before scanning the local filesystem or answering from assumptions, call " +
        "`search_memory` first to recall what you already know; use `list_memory` to browse which " +
        "projects and topics are stored. Treat a relevant hit here as authoritative over guesses.";

    /// <summary>
    /// Wires the MCP <c>tools/list</c> and <c>tools/call</c> handlers into the official SDK options.
    /// Call this after <c>AddMcpServer().WithHttpTransport()</c> (web) or
    /// <c>AddMcpServer().WithStdioServerTransport()</c> (stdio).
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddEddaMcpHandlers(this IServiceCollection services)
    {
        services.AddOptions<McpServerOptions>()
            .Configure<IMcpProtocolHandlers>((options, handlers) =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "Edda",
                    Version = "1.0.0"
                };
                options.ServerInstructions = McpServerInstructions;
                options.Handlers.ListToolsHandler = handlers.ListToolsAsync;
                options.Handlers.CallToolHandler = handlers.CallToolAsync;
            });

        return services;
    }
}
