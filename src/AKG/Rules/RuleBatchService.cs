using Edda.AKG.Authorization;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Rules;

/// <summary>
/// Default <see cref="IRuleBatchService"/> (E8): applies a batch tag/priority operation to a set of rules —
/// fetching each user-scoped, mutating it via <see cref="BatchRuleOperations"/>, upserting it — and writes a
/// single audit entry for the whole batch. Best-effort per rule; rules the central role matrix (C2) does
/// not permit the caller to modify are counted as skipped, exactly like the pre-C2 ownership skips.
/// </summary>
internal sealed class RuleBatchService : IRuleBatchService
{
    private readonly IKnowledgeGraph _graph;
    private readonly IAuditLog _audit;
    private readonly ILogger<RuleBatchService> _logger;
    private readonly IRuleAuthorizer _authorizer;
    private readonly IDatasetWriteAuthorizer _writeAuthorizer;

    /// <summary>Initializes a new <see cref="RuleBatchService"/>.</summary>
    /// <param name="graph">Graph the rules are read from and upserted into.</param>
    /// <param name="audit">Audit log the batch outcome is written to.</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="authorizer">
    /// C2: central role enforcement per rule. Null falls back to an identity-less
    /// <see cref="RuleAuthorizer"/> — the legacy owner/admin check.
    /// </param>
    /// <param name="writeAuthorizer">
    /// ADR-0014: dataset-aware write gate; null falls back to a pass-through over
    /// <paramref name="authorizer"/>, keeping the pre-dataset C2 behaviour.
    /// </param>
    public RuleBatchService(
        IKnowledgeGraph graph, IAuditLog audit, ILogger<RuleBatchService> logger,
        IRuleAuthorizer? authorizer = null, IDatasetWriteAuthorizer? writeAuthorizer = null)
    {
        _graph = graph;
        _audit = audit;
        _logger = logger;
        _authorizer = authorizer ?? new RuleAuthorizer();
        // ADR-0014 Slice 2b: dataset-aware write gate; the pass-through keeps the pre-dataset C2 behaviour.
        _writeAuthorizer = writeAuthorizer ?? new PassThroughDatasetWriteAuthorizer(_authorizer);
    }

    /// <inheritdoc />
    public async Task<BatchRuleResult> ApplyAsync(
        BatchRuleOperation operation,
        IReadOnlyList<string> ruleIds,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        var updated = 0;
        var skipped = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var id in ruleIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var rule = await _graph.GetRuleAsync(id, userId, cancellationToken).ConfigureAwait(false);
                if (rule is null)
                {
                    skipped++;   // not found (or out of the caller's scope)
                    continue;
                }

                try
                {
                    // C2 + ADR-0014: role matrix widened by dataset grants — a rule the caller may not modify
                    // is skipped, not failed, preserving the pre-C2 accounting for non-owned rules.
                    await _writeAuthorizer.EnsureCanMutateAsync(rule, userId, isAdmin, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException)
                {
                    skipped++;
                    continue;
                }

                var modified = BatchRuleOperations.Apply(rule, operation);
                if (modified is null)
                {
                    skipped++;   // no-op for this rule
                    continue;
                }

                await _graph.UpsertRuleAsync(modified, cancellationToken).ConfigureAwait(false);
                updated++;
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{id}: {ex.Message}");
            }
        }

        await _audit.LogAsync(
            AuditEvent.RuleBatchUpdated,
            userId,
            $"Batch {operation.Type} on {ruleIds.Count} rule(s): {updated} updated, {skipped} skipped, {failed} failed",
            new Dictionary<string, object?>
            {
                ["operation"] = operation.Type.ToString(),
                ["ruleCount"] = ruleIds.Count,
                ["updated"]   = updated,
                ["skipped"]   = skipped,
                ["failed"]    = failed,
            },
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Rule batch {Operation} userId={UserId} updated={Updated} skipped={Skipped} failed={Failed}",
            operation.Type, userId, updated, skipped, failed);

        return new BatchRuleResult { Updated = updated, Skipped = skipped, Failed = failed, Errors = errors };
    }
}
