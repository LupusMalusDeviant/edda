using Edda.Agent.Tdk;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Tools.Knowledge;

/// <summary>
/// Validates a code snippet or response against the active AKG rules that carry a
/// Test-Driven Knowledge (TDK) validator script. Exposes Edda's self-enforcing knowledge
/// as a callable tool — the differentiator no plain memory layer offers: rules can actively
/// reject non-compliant output rather than merely describe conventions.
/// </summary>
internal sealed class TdkValidateTool : IAgentTool
{
    private readonly ITdkEngine _tdkEngine;
    private readonly IKnowledgeGraph _knowledgeGraph;
    private readonly ILogger<TdkValidateTool> _logger;

    /// <inheritdoc />
    public ToolDefinition Definition { get; } = new()
    {
        Name = "tdk_validate",
        Description = "Validates code (or a response containing fenced code blocks) against the knowledge " +
                      "graph's Test-Driven Knowledge (TDK) validators. Compiles the active rules for the " +
                      "given topic, runs their sandboxed validator scripts against the code, and returns any " +
                      "rule violations with suggestions. Use this to check generated code against enforced " +
                      "engineering rules before delivering it.",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                code = new
                {
                    type = "string",
                    description = "The code or response text to validate. May contain fenced code blocks."
                },
                query = new
                {
                    type = "string",
                    description = "Optional topic used to scope which rules apply. Defaults to the code itself."
                }
            },
            required = new[] { "code" }
        }
    };

    /// <summary>
    /// Initializes a new <see cref="TdkValidateTool"/>.
    /// </summary>
    /// <param name="tdkEngine">The TDK engine that runs rule validators against the code.</param>
    /// <param name="knowledgeGraph">The AKG used to compile the active rules for the topic.</param>
    /// <param name="logger">Structured logger.</param>
    public TdkValidateTool(
        ITdkEngine tdkEngine,
        IKnowledgeGraph knowledgeGraph,
        ILogger<TdkValidateTool> logger)
    {
        _tdkEngine = tdkEngine;
        _knowledgeGraph = knowledgeGraph;
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
            var code = ToolArgumentHelper.GetRequiredString(call.Arguments, "code");
            var queryArg = ToolArgumentHelper.GetString(call.Arguments, "query");
            var query = string.IsNullOrWhiteSpace(queryArg) ? code : queryArg;
            var userId = context.UserId;

            var taskContext = new TaskContext
            {
                Task = query,
                Concepts = ExtractConcepts(query),
                UserId = userId
            };
            var akgContext = await _knowledgeGraph.CompileContextAsync(taskContext, cancellationToken);

            var request = new AgentRequest
            {
                UserMessage = query,
                ConversationId = context.ConversationId,
                Identity = new ToolIdentity(userId)
            };

            var tdkResult = await _tdkEngine.ValidateAsync(
                code, akgContext.ActiveRules, request, cancellationToken);

            _logger.LogInformation(
                "tdk_validate: {ViolationCount} violation(s), {EngineErrorCount} engine error(s) across " +
                "{RuleCount} active rules | userId={UserId}",
                tdkResult.Violations.Count, tdkResult.EngineErrors.Count, akgContext.ActiveRules.Count, userId);

            var body = tdkResult.HasViolations
                ? TdkFeedbackFormatter.Format(tdkResult.Violations)
                : "✓ No knowledge-base violations detected.";

            // A validator that failed to run must be visible: the check was incomplete, not clean.
            if (tdkResult.HasEngineErrors)
            {
                body += "\n\n" + TdkFeedbackFormatter.FormatEngineErrors(tdkResult.EngineErrors);
            }

            return ToolResult.Ok(call.Id, Definition.Name, body);
        }
        catch (ArgumentException ex)
        {
            return ToolResult.Fail(call.Id, Definition.Name, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "tdk_validate failed");
            return ToolResult.Fail(call.Id, Definition.Name, ex.Message);
        }
    }

    /// <summary>
    /// Extracts key concepts from text for AKG relevance scoring.
    /// Mirrors the logic used in <c>AgentRuntime</c> and <see cref="KnowledgeGetContextTool"/>.
    /// </summary>
    /// <param name="message">The text to extract concepts from.</param>
    /// <returns>Up to 20 unique lower-case concept tokens longer than 3 characters.</returns>
    private static IReadOnlyList<string> ExtractConcepts(string message)
        => message.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                  .Select(w => w.Trim('.', ',', '!', '?', '"', '\'').ToLowerInvariant())
                  .Where(w => w.Length > 3)
                  .Distinct()
                  .Take(20)
                  .ToList();

    /// <summary>
    /// Minimal identity context for the synthetic validation request.
    /// TDK only reads <see cref="AgentRequest.UserMessage"/>; identity is required by the
    /// request record but unused by the validator protocol.
    /// </summary>
    private sealed class ToolIdentity(string? userId) : IIdentityContext
    {
        public string? UserId { get; } = userId;
        public string? Username => null;
        public string TenantId => "system";
        public bool IsClone => false;
        public bool IsAdmin => false;
        // The synthetic validation identity never mutates knowledge — the safest role suffices (C2).
        public TenantRole Role => TenantRole.Viewer;
    }
}
