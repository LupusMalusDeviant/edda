using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Tools.Knowledge;

/// <summary>
/// Searches the long-term memory (AKG knowledge graph) for what is known about a query and returns
/// the most relevant stored knowledge. Exposed to MCP clients as the <c>search_memory</c> tool.
/// </summary>
internal sealed class KnowledgeGetContextTool : IAgentTool
{
    private readonly IKnowledgeGraph _kg;
    private readonly ILogger<KnowledgeGetContextTool> _logger;

    /// <inheritdoc />
    public ToolDefinition Definition { get; } = new()
    {
        Name = "search_memory",
        Description = "Search your long-term memory for what you already know about the user, their " +
                      "projects, repositories, code, decisions and preferences. Returns the most relevant " +
                      "stored knowledge for the query. Call this FIRST — before scanning the filesystem or " +
                      "answering from assumptions — whenever a question touches the user's projects, repos, " +
                      "code, or past decisions.",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                query = new { type = "string", description = "What to recall: a topic, question, project or repository name, or keywords." }
            },
            required = new[] { "query" }
        }
    };

    /// <summary>
    /// Initializes a new <see cref="KnowledgeGetContextTool"/>.
    /// </summary>
    /// <param name="knowledgeGraph">The AKG used to compile context.</param>
    /// <param name="logger">Structured logger.</param>
    public KnowledgeGetContextTool(IKnowledgeGraph knowledgeGraph, ILogger<KnowledgeGetContextTool> logger)
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
            var query = ToolArgumentHelper.GetRequiredString(call.Arguments, "query");
            var userId = context.UserId;

            _logger.LogInformation("search_memory query length={Len} userId={UserId}",
                query.Length, userId);

            var concepts = ExtractConcepts(query);
            var taskCtx = new TaskContext
            {
                Task = query,
                Concepts = concepts,
                UserId = userId
            };

            var result = await _kg.CompileContextAsync(taskCtx, cancellationToken);
            return ToolResult.Ok(call.Id, Definition.Name, result.FormattedContext);
        }
        catch (ArgumentException ex)
        {
            return ToolResult.Fail(call.Id, Definition.Name, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "search_memory failed");
            return ToolResult.Fail(call.Id, Definition.Name, ex.Message);
        }
    }

    /// <summary>
    /// Extracts key concepts from a message for AKG relevance scoring.
    /// Mirrors the logic used in <c>AgentRuntime</c>.
    /// </summary>
    /// <param name="message">The message to extract concepts from.</param>
    /// <returns>Up to 20 unique lower-case concept tokens longer than 3 characters.</returns>
    private static IReadOnlyList<string> ExtractConcepts(string message)
        => message.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                  .Select(w => w.Trim('.', ',', '!', '?', '"', '\'').ToLowerInvariant())
                  .Where(w => w.Length > 3)
                  .Distinct()
                  .Take(20)
                  .ToList();
}
