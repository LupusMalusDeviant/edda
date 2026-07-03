using Edda.Agent.Infrastructure;
using Edda.Agent.Knowledge;
using Edda.Agent.Registry;
using Edda.Agent.Tdk;
using Edda.Agent.Tools.Knowledge;
using Edda.Agent.Tools.Memory;
using Edda.Core.Abstractions;
using Microsoft.Extensions.Configuration;
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

        // F13: a process-local cache so identical (rule × validator × block) re-validations reuse the
        // outcome instead of re-running the sandbox — important for agent loops that iterate on the same code.
        services.AddSingleton<ITdkResultCache, InMemoryTdkResultCache>();

        // F4: the bundled tdk.py helper module, delivered next to every validator script in the sandbox.
        services.AddSingleton<ITdkHelperModule, TdkHelperModule>();

        services.AddSingleton<ITdkEngine>(sp =>
            new TdkEngine(
                sp.GetRequiredService<ISandboxFactory>(),
                sp.GetRequiredService<IRuleConfidenceStore>(),
                sp.GetRequiredService<ILogger<TdkEngine>>(),
                sp.GetRequiredService<ITdkHelperModule>(),
                resultCache: sp.GetRequiredService<ITdkResultCache>()));

        return services;
    }

    /// <summary>
    /// Registers the lean set of agent tools as <see cref="IAgentTool"/> singletons and schedules
    /// their registration into <see cref="IToolRegistry"/> via a hosted startup service.
    /// Tools: <c>manage_memory</c>, <c>manage_userdata</c>, <c>manage_learnings</c>,
    /// <c>remember</c>, <c>recall</c>, <c>forget</c>, <c>consolidate_memory</c>, <c>search_memory</c>,
    /// <c>list_memory</c>, <c>analyze_coverage</c>, <c>tdk_validate</c>.
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
        // C3: remember detects superseded facts via token-Jaccard; threshold from MEMORY_SUPERSEDE_JACCARD.
        services.AddSingleton<IAgentTool>(sp => new RememberTool(
            sp.GetRequiredService<IKnowledgeGraph>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<RememberTool>>(),
            jaccardThreshold: ParseSupersedeThreshold(sp.GetService<IConfiguration>())));
        services.AddSingleton<IAgentTool, RecallTool>();
        services.AddSingleton<IAgentTool, ForgetTool>();
        services.AddSingleton<IAgentTool, ConsolidateTool>();

        // E2: agent feedback into the confidence layer. Mutating → MCP default-deny (McpExposurePolicy.WriteToolNames).
        services.AddSingleton<IAgentTool, RateMemoryTool>();

        // C10: shared consolidation logic + opt-in periodic background maintenance. The hosted service runs
        // the consolidator for every user every MEMORY_CONSOLIDATION_INTERVAL_HOURS hours (default 0 = off).
        // C4: consolidation additionally merges token-similar near-duplicates; threshold from
        // MEMORY_CONSOLIDATE_JACCARD (default off = only exact normalized-duplicate removal, as before C4).
        services.AddSingleton<IMemoryConsolidator>(sp => new MemoryConsolidator(
            sp.GetRequiredService<IKnowledgeGraph>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<MemoryConsolidator>>(),
            jaccardThreshold: ParseConsolidateThreshold(sp.GetService<IConfiguration>())));
        services.AddHostedService(sp => new MemoryConsolidationHostedService(
            sp.GetRequiredService<IMemoryConsolidator>(),
            sp.GetRequiredService<IAuditLog>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<MemoryConsolidationHostedService>>(),
            intervalHours: ParseIntervalHours(sp.GetService<IConfiguration>())));

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

    /// <summary>
    /// Reads <c>MEMORY_CONSOLIDATION_INTERVAL_HOURS</c> from configuration (falling back to the environment),
    /// parsed as an invariant-culture number of hours. Returns <c>0</c> (disabled) when unset, non-positive,
    /// or unparseable — preserving the default-off behaviour.
    /// </summary>
    /// <param name="configuration">The host configuration, if available.</param>
    /// <returns>The consolidation interval in hours, or <c>0</c> to disable.</returns>
    private static double ParseIntervalHours(IConfiguration? configuration)
    {
        var raw = configuration?["MEMORY_CONSOLIDATION_INTERVAL_HOURS"]
                  ?? Environment.GetEnvironmentVariable("MEMORY_CONSOLIDATION_INTERVAL_HOURS");
        return double.TryParse(
            raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var hours) && hours > 0
            ? hours
            : 0;
    }

    /// <summary>
    /// Reads <c>MEMORY_SUPERSEDE_JACCARD</c> from configuration (falling back to the environment) as an
    /// invariant-culture token-Jaccard threshold for C3 contradiction detection. Returns the default
    /// <c>0.6</c> when unset, negative, or unparseable; a value greater than <c>1.0</c> disables detection.
    /// </summary>
    /// <param name="configuration">The host configuration, if available.</param>
    /// <returns>The supersede threshold; <c>0.6</c> by default.</returns>
    private static double ParseSupersedeThreshold(IConfiguration? configuration)
    {
        const double DefaultThreshold = 0.6;
        var raw = configuration?["MEMORY_SUPERSEDE_JACCARD"]
                  ?? Environment.GetEnvironmentVariable("MEMORY_SUPERSEDE_JACCARD");
        return double.TryParse(
            raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var threshold) && threshold >= 0
            ? threshold
            : DefaultThreshold;
    }

    /// <summary>
    /// Reads <c>MEMORY_CONSOLIDATE_JACCARD</c> from configuration (falling back to the environment) as an
    /// invariant-culture token-Jaccard threshold for C4 near-duplicate consolidation. Returns
    /// <see cref="double.PositiveInfinity"/> (disabled — the pre-C4 behaviour) when unset, non-positive, or
    /// unparseable; a value greater than 1.0 also disables it.
    /// </summary>
    /// <param name="configuration">The host configuration, if available.</param>
    /// <returns>The consolidation near-duplicate threshold; disabled by default.</returns>
    private static double ParseConsolidateThreshold(IConfiguration? configuration)
    {
        var raw = configuration?["MEMORY_CONSOLIDATE_JACCARD"]
                  ?? Environment.GetEnvironmentVariable("MEMORY_CONSOLIDATE_JACCARD");
        return double.TryParse(
            raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var threshold) && threshold > 0
            ? threshold
            : double.PositiveInfinity;
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
