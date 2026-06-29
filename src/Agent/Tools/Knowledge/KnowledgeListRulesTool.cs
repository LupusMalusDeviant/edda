using System.Text.Json;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Tools.Knowledge;

/// <summary>
/// Browses long-term memory: lists stored knowledge entries from the AKG, optionally filtered by
/// domain, type, or tag. Returns a JSON array of entries with their ID, domain, type, priority, and
/// tags. Exposed to MCP clients as the <c>list_memory</c> tool.
/// </summary>
internal sealed class KnowledgeListRulesTool : IAgentTool
{
    private readonly IKnowledgeGraph _kg;
    private readonly ILogger<KnowledgeListRulesTool> _logger;

    /// <inheritdoc />
    public ToolDefinition Definition { get; } = new()
    {
        Name = "list_memory",
        Description = "Browse your long-term memory: lists stored knowledge entries, optionally filtered " +
                      "by topic/domain, type, or tag, so you can discover which projects, repositories and " +
                      "topics you have memory about. Pair with search_memory.",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                domain = new { type = "string", description = "Filter by topic or domain, e.g. a project or repository name. Omit for everything." },
                type = new { type = "string", description = "Filter by entry type (e.g. 'guideline', 'constraint'). Omit for all types." },
                tag = new { type = "string", description = "Filter by tag. Omit for no tag filter." }
            }
        }
    };

    /// <summary>
    /// Initializes a new <see cref="KnowledgeListRulesTool"/>.
    /// </summary>
    /// <param name="knowledgeGraph">The AKG used to query rules.</param>
    /// <param name="logger">Structured logger.</param>
    public KnowledgeListRulesTool(IKnowledgeGraph knowledgeGraph, ILogger<KnowledgeListRulesTool> logger)
    {
        _kg = knowledgeGraph;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(
        ToolCall call,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var domain = ToolArgumentHelper.GetString(call.Arguments, "domain");
            var type = ToolArgumentHelper.GetString(call.Arguments, "type");
            var tag = ToolArgumentHelper.GetString(call.Arguments, "tag");
            var userId = context.UserId;

            _logger.LogInformation(
                "list_memory domain={Domain} type={Type} tag={Tag} userId={UserId}",
                domain, type, tag, userId);

            var rules = await _kg.GetRulesAsync(domain, type, tag, userId, cancellationToken);

            var summary = rules.Select(r => new
            {
                id = r.Id,
                domain = r.Domain,
                type = r.Type,
                priority = r.Priority.ToString(),
                tags = r.Tags
            }).ToList();

            return ToolResult.Ok(call.Id, Definition.Name,
                JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = false }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "list_memory failed");
            return ToolResult.Fail(call.Id, Definition.Name, ex.Message);
        }
    }
}
