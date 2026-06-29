using System.Collections.Concurrent;
using System.Diagnostics;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Edda.Security.OutputFilter;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Registry;

/// <summary>
/// Central registry for all agent tools. Implements both <see cref="IToolRegistry"/> (registration)
/// and <see cref="IToolExecutor"/> (execution with timeout, redaction, and audit logging).
/// Tool registrations are stored in a thread-safe <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </summary>
public sealed class ToolRegistry : IToolExecutor, IToolRegistry
{
    private const int ExecutionTimeoutSeconds = 90;

    private readonly ConcurrentDictionary<string, ToolEntry> _tools = new();
    private readonly ISecretRedactor _redactor;
    private readonly IAuditLog _auditLog;
    private readonly ILogger<ToolRegistry> _logger;

    /// <summary>
    /// Initializes a new <see cref="ToolRegistry"/>.
    /// </summary>
    /// <param name="redactor">Used to redact secrets from tool output before returning.</param>
    /// <param name="auditLog">Used to write tool execution audit entries.</param>
    /// <param name="logger">Structured logger for observability.</param>
    public ToolRegistry(
        ISecretRedactor redactor,
        IAuditLog auditLog,
        ILogger<ToolRegistry> logger)
    {
        _redactor = redactor;
        _auditLog = auditLog;
        _logger = logger;
    }

    /// <inheritdoc/>
    public void Register(IAgentTool tool)
    {
        var entry = new ToolEntry(
            tool.Definition,
            (call, ctx, ct) => tool.ExecuteAsync(call, ctx, ct),
            tool);
        _tools[tool.Definition.Name] = entry;
        _logger.LogDebug("Registered IAgentTool '{ToolName}'", tool.Definition.Name);
    }

    /// <inheritdoc/>
    public void Register(
        ToolDefinition definition,
        Func<ToolCall, ToolExecutionContext, CancellationToken, Task<ToolResult>> handler)
    {
        var entry = new ToolEntry(definition, handler, null);
        _tools[definition.Name] = entry;
        _logger.LogDebug("Registered lambda tool '{ToolName}'", definition.Name);
    }

    /// <inheritdoc/>
    public async Task<ToolResult> ExecuteAsync(
        ToolCall call,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_tools.TryGetValue(call.Name, out var entry))
        {
            var errorMsg = $"Tool '{call.Name}' is not registered.";
            _logger.LogWarning("Tool execution failed: {Error}", errorMsg);
            await _auditLog.LogAsync(
                AuditEvent.ToolError,
                context.UserId ?? "unknown",
                errorMsg,
                new Dictionary<string, object?> { ["tool"] = call.Name },
                cancellationToken).ConfigureAwait(false);
            return ToolResult.Fail(call.Id, call.Name, errorMsg);
        }

        // ── Taint check: block calls whose arguments carry forbidden labels ──
        if (context.TaintTracker is { } tracker)
        {
            var taintCheck = tracker.Check(call);
            if (!taintCheck.IsAllowed)
            {
                _logger.LogWarning(
                    "Taint violation blocked tool {ToolName}: {Reason}",
                    call.Name, taintCheck.Reason);
                await _auditLog.LogAsync(
                    AuditEvent.TaintViolation,
                    context.UserId ?? "unknown",
                    taintCheck.Reason ?? $"Taint sink violation for tool '{call.Name}'",
                    new Dictionary<string, object?>
                    {
                        ["tool"]  = call.Name,
                        ["label"] = taintCheck.ViolatingLabel?.ToString()
                    },
                    cancellationToken).ConfigureAwait(false);
                return ToolResult.Fail(call.Id, call.Name,
                    $"Security policy: {taintCheck.Reason}");
            }
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(ExecutionTimeoutSeconds));

        var sw = Stopwatch.StartNew();
        ToolResult result;
        try
        {
            _logger.LogInformation(
                "Tool executing: {ToolName} | UserId: {UserId} | ConversationId: {ConversationId}",
                call.Name, context.UserId, context.ConversationId);

            result = await entry.Handler(call, context, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var errorMsg = ex.Message;
            _logger.LogWarning(ex, "Tool '{ToolName}' threw an exception after {ElapsedMs} ms",
                call.Name, sw.ElapsedMilliseconds);
            await _auditLog.LogAsync(
                AuditEvent.ToolError,
                context.UserId ?? "unknown",
                $"Tool '{call.Name}' failed: {errorMsg}",
                new Dictionary<string, object?> { ["tool"] = call.Name, ["error"] = errorMsg },
                cancellationToken).ConfigureAwait(false);
            return ToolResult.Fail(call.Id, call.Name, errorMsg);
        }

        sw.Stop();

        // Redact secrets from successful output
        if (result.Success && result.Content is not null)
        {
            var redacted = _redactor.Redact(result.Content);
            result = result with { Content = redacted };
        }

        // ── Taint label recording: tag result with its data-origin label ─────
        if (context.TaintTracker is { } taintTracker)
        {
            var taintLabel = call.Name switch
            {
                "web_fetch"               => TaintLabel.ExternalNetwork,
                "web_search"              => TaintLabel.ExternalNetwork,
                "http_request"            => TaintLabel.ExternalNetwork,
                "manage_credentials"      => TaintLabel.Secret,
                "autonomous_research"     => TaintLabel.UntrustedAgent,
                "parallel_research"       => TaintLabel.UntrustedAgent,
                "dag_research"            => TaintLabel.UntrustedAgent,
                _                         => TaintLabel.None
            };
            taintTracker.RecordResult(result.ToolCallId, taintLabel);
        }

        var auditEvent = result.Success ? AuditEvent.ToolExecute : AuditEvent.ToolError;
        await _auditLog.LogAsync(
            auditEvent,
            context.UserId ?? "unknown",
            $"Tool '{call.Name}' {(result.Success ? "succeeded" : "failed")} in {sw.ElapsedMilliseconds} ms",
            new Dictionary<string, object?>
            {
                ["tool"] = call.Name,
                ["success"] = result.Success,
                ["elapsedMs"] = sw.ElapsedMilliseconds
            },
            cancellationToken).ConfigureAwait(false);

        if (result.Success)
            _logger.LogInformation(
                "Tool completed: {ToolName} | Success: {Success} | Elapsed: {ElapsedMs} ms",
                call.Name, result.Success, sw.ElapsedMilliseconds);
        else
            _logger.LogWarning(
                "Tool completed: {ToolName} | Success: {Success} | Elapsed: {ElapsedMs} ms | Error: {Error}",
                call.Name, result.Success, sw.ElapsedMilliseconds, result.Error ?? "(no message)");

        return result;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ToolResult>> ExecuteManyAsync(
        IReadOnlyList<ToolCall> calls,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var tasks = calls.Select(c => ExecuteAsync(c, context, cancellationToken));
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public IReadOnlyList<ToolDefinition> GetAvailableTools() =>
        _tools.Values.Select(e => e.Definition).ToList();

    /// <inheritdoc/>
    public IReadOnlyList<ToolDefinition> GetFilteredTools(IReadOnlyList<string> names)
    {
        var nameSet = new HashSet<string>(names, StringComparer.Ordinal);
        return _tools.Values
            .Where(e => nameSet.Contains(e.Definition.Name))
            .Select(e => e.Definition)
            .ToList();
    }

    /// <inheritdoc/>
    public IAgentTool? GetTool(string name) =>
        _tools.TryGetValue(name, out var entry) ? entry.ToolInstance : null;

    /// <inheritdoc/>
    public void Unregister(string name)
    {
        if (_tools.TryRemove(name, out _))
            _logger.LogDebug("Unregistered tool '{ToolName}'", name);
    }

    /// <summary>Internal storage record for a registered tool.</summary>
    private sealed record ToolEntry(
        ToolDefinition Definition,
        Func<ToolCall, ToolExecutionContext, CancellationToken, Task<ToolResult>> Handler,
        IAgentTool? ToolInstance);
}
