using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Security.Taint;

/// <summary>
/// Per-turn stateful taint tracker. Correlates tool-call IDs with taint labels and
/// enforces sink restrictions before tool execution to prevent unsafe data flows
/// (e.g. web content → shell_execute, credentials → http_request).
/// Thread-safe for concurrent tool execution within a single agent turn.
/// </summary>
internal sealed class TaintTracker : ITaintTracker
{
    private readonly TaintSinkRegistry _sinks;
    private readonly IAuditLog _auditLog;
    private readonly ILogger<TaintTracker> _logger;

    // toolCallId → aggregated TaintLabel (OR of all labels recorded for that ID)
    private readonly ConcurrentDictionary<string, TaintLabel> _taintMap = new();

    // toolCallIds that have been explicitly declassified
    private readonly ConcurrentDictionary<string, byte> _declassified = new();

    /// <summary>
    /// Initializes a new <see cref="TaintTracker"/>.
    /// </summary>
    /// <param name="sinks">Registry defining which labels are forbidden in which tool sinks.</param>
    /// <param name="auditLog">Audit log for declassification events.</param>
    /// <param name="logger">Structured logger for observability.</param>
    internal TaintTracker(
        TaintSinkRegistry sinks,
        IAuditLog auditLog,
        ILogger<TaintTracker> logger)
    {
        _sinks    = sinks;
        _auditLog = auditLog;
        _logger   = logger;
    }

    /// <inheritdoc/>
    public void RecordResult(string toolCallId, TaintLabel label)
    {
        if (label == TaintLabel.None) return;
        _taintMap.AddOrUpdate(toolCallId, label, (_, existing) => existing | label);
    }

    /// <inheritdoc/>
    public TaintCheckResult Check(ToolCall call)
    {
        var forbidden = _sinks.GetForbiddenLabels(call.Name);
        if (forbidden == TaintLabel.None) return TaintCheckResult.Allow();

        // Search all argument values for references to previous tool-call IDs.
        // LLMs reference earlier results by embedding tool_use_id / call_id strings in arguments.
        foreach (var (_, argValue) in call.Arguments)
        {
            foreach (var refId in ExtractToolCallIds(argValue))
            {
                if (_declassified.ContainsKey(refId)) continue;
                if (!_taintMap.TryGetValue(refId, out var label)) continue;

                var violation = label & forbidden;
                if (violation != TaintLabel.None)
                {
                    _logger.LogWarning(
                        "Taint violation: tool {Tool} would receive taint label {Label} from result {Ref}",
                        call.Name, violation, refId);

                    return TaintCheckResult.Block(
                        violation,
                        call.Name,
                        $"Data with taint label '{violation}' must not flow into tool '{call.Name}'.");
                }
            }
        }

        return TaintCheckResult.Allow();
    }

    /// <inheritdoc/>
    public void Declassify(string toolCallId, string justification)
    {
        _declassified.TryAdd(toolCallId, 0);

        // Fire-and-forget audit log — declassification must be recorded even if caller
        // does not await. No ConfigureAwait needed since we discard the task.
        _ = _auditLog.LogAsync(
            AuditEvent.TaintDeclassify,
            "system",
            $"Taint declassified for result '{toolCallId}': {justification}",
            new Dictionary<string, object?> { ["toolCallId"] = toolCallId, ["justification"] = justification });

        _logger.LogInformation(
            "Taint declassified for result {ToolCallId}: {Justification}",
            toolCallId, justification);
    }

    /// <inheritdoc/>
    public void Reset()
    {
        _taintMap.Clear();
        _declassified.Clear();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Extracts tool-call ID references from an argument value using a heuristic regex.
    /// Matches common LLM tool-call ID formats: <c>toolu_01…</c>, <c>call_01…</c>.
    /// </summary>
    private static IEnumerable<string> ExtractToolCallIds(object? argValue)
    {
        string? text = argValue switch
        {
            string s                                              => s,
            JsonElement { ValueKind: JsonValueKind.String } je   => je.GetString(),
            _                                                     => null
        };

        if (text is null) return [];

        return Regex
            .Matches(text, @"(toolu_[a-zA-Z0-9]+|call_[a-zA-Z0-9]+)")
            .Select(m => m.Value);
    }
}
