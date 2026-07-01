using Edda.Agent.Infrastructure;
using Edda.Agent.Knowledge;
using Edda.Agent.Registry;
using Edda.Agent.Tdk;
using Edda.Agent.Tools.Knowledge;
using Edda.Agent.Tools.Memory;
using Edda.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.DependencyInjection;

/// <summary>
/// Extension methods for registering the lean AKG+TDK tool layer with the DI container.
/// This is the standalone counterpart to the full Edda agent runtime: it wires only the
/// tool registry/executor, the TDK engine, and the read/knowledge/memory tools — no chat-LLM
/// runtime, no multiagent, scheduling, web, code, or Docker tools.
/// </summary>
public static class AgentToolsServiceExtensions
{
    /// <summary>
    /// Registers the tool execution core and the TDK engine:
    /// <see cref="PhysicalFileSystem"/> as <see cref="IFileSystem"/>, the <see cref="ToolRegistry"/>
    /// (exposed as both <see cref="IToolRegistry"/> and <see cref="IToolExecutor"/>), and
    /// <see cref="TdkEngine"/> as <see cref="ITdkEngine"/>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Requires <see cref="ISandboxFactory"/> (from the Sandboxing project) and
    /// <see cref="IRuleConfidenceStore"/> (from the AKG project) to be registered by the host.
    /// </remarks>
    public static IServiceCollection AddTdkEngine(this IServiceCollection services)
    {
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();

        services.AddSingleton<ToolRegistry>();
        services.AddSingleton<IToolExecutor>(sp => sp.GetRequiredService<ToolRegistry>());
        services.AddSingleton<IToolRegistry>(sp => sp.GetRequiredService<ToolRegistry>());

        services.AddSingleton<ITdkEngine>(sp =>
            new TdkEngine(
                sp.GetRequiredService<ISandboxFactory>(),
                sp.GetRequiredService<IRuleConfidenceStore>(),
                sp.GetRequiredService<ILogger<TdkEngine>>()));

        return services;
    }

    /// <summary>
    /// Registers the lean set of agent tools as <see cref="IAgentTool"/> singletons and schedules
    /// their registration into <see cref="IToolRegistry"/> via a hosted startup service.
    /// Tools: <c>manage_memory</c>, <c>manage_userdata</c>, <c>manage_learnings</c>,
    /// <c>remember</c>, <c>recall</c>, <c>forget</c>, <c>search_memory</c>, <c>list_memory</c>,
    /// <c>analyze_coverage</c>, <c>tdk_validate</c>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Requires <see cref="IKnowledgeGraph"/> (AKG), <see cref="ITdkEngine"/>, <see cref="IFileSystem"/>,
    /// and <see cref="TimeProvider"/> to be registered by the host.
    /// </remarks>
    public static IServiceCollection AddLeanAgentTools(this IServiceCollection services)
    {
        // Memory tools — user-scoped markdown stores under data/users/{userId}/.
        services.AddSingleton<IAgentTool, ManageMemoryTool>();
        services.AddSingleton<IAgentTool, ManageUserdataTool>();
        services.AddSingleton<IAgentTool, ManageLearningsTool>();

        // Episodic memory (M3 / ADR-0011): per-fact SourceType=memory graph nodes. recall is read-only;
        // remember/forget are mutating (MCP default-deny via McpExposurePolicy.WriteToolNames).
        services.AddSingleton<IAgentTool, RememberTool>();
        services.AddSingleton<IAgentTool, RecallTool>();
        services.AddSingleton<IAgentTool, ForgetTool>();

        // Knowledge / TDK tools.
        services.AddSingleton<IAgentTool, KnowledgeGetContextTool>();
        services.AddSingleton<IAgentTool, KnowledgeListRulesTool>();
        services.AddSingleton<IAgentTool, AnalyzeCoverageTool>();
        services.AddSingleton<IAgentTool, TdkValidateTool>();

        // F43 — custom-tool AKG rule management (auto-AKG on custom tool CRUD).
        services.AddSingleton<IToolKnowledgeService, ToolKnowledgeService>();

        // Wire all IAgentTool singletons into IToolRegistry at host startup.
        services.AddHostedService<ToolRegistrationService>();

        return services;
    }
}

/// <summary>
/// Hosted service that registers all <see cref="IAgentTool"/> instances into
/// <see cref="IToolRegistry"/> when the host starts, so tools are available before the first request.
/// </summary>
internal sealed class ToolRegistrationService : IHostedService
{
    /// <summary>
    /// Initializes the service and immediately registers all tools in the registry.
    /// </summary>
    /// <param name="registry">The tool registry to populate.</param>
    /// <param name="tools">All registered <see cref="IAgentTool"/> implementations.</param>
    public ToolRegistrationService(IToolRegistry registry, IEnumerable<IAgentTool> tools)
    {
        foreach (var tool in tools)
            registry.Register(tool);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
