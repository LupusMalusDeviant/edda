using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Rules;

/// <summary>
/// Default <see cref="IRuleBatchService"/> (E8): applies a batch tag/priority operation to a set of rules —
/// fetching each user-scoped, mutating it via <see cref="BatchRuleOperations"/>, upserting it — and writes a
/// single audit entry for the whole batch. Best-effort per rule; non-admins may only modify their own rules.
/// </summary>
internal sealed class RuleBatchService : IRuleBatchService
{
    private readonly IKnowledgeGraph _graph;
    private readonly IAuditLog _audit;
    private readonly ILogger<RuleBatchService> _logger;

    /// <summary>Initializes a new <see cref="RuleBatchService"/>.</summary>
    /// <param name="graph">Graph the rules are read from and upserted into.</param>
    /// <param name="audit">Audit log the batch outcome is written to.</param>
    /// <param name="logger">Structured logger.</param>
    public RuleBatchService(IKnowledgeGraph graph, IAuditLog audit, ILogger<RuleBatchService> logger)
    {
        _graph = graph;
        _audit = audit;
        _logger = logger;
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
                if (rule is null || (!isAdmin && !string.Equals(rule.OwnerId, userId, StringComparison.Ordinal)))
                {
                    skipped++;   // not found, or not owned by a non-admin caller
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
