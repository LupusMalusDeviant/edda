using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Applies a batch tag/priority operation to a set of AKG rules (E8): fetches each rule (user-scoped),
/// mutates it, upserts it, and writes a single audit entry for the batch. Best-effort per rule — a failure
/// on one rule does not abort the rest.
/// </summary>
public interface IRuleBatchService
{
    /// <summary>Applies <paramref name="operation"/> to every rule id in <paramref name="ruleIds"/>.</summary>
    /// <param name="operation">The operation to apply.</param>
    /// <param name="ruleIds">The target rule ids.</param>
    /// <param name="userId">The acting user (rules are read user-scoped; non-admins modify only their own).</param>
    /// <param name="isAdmin">Whether the user may modify rules they do not own.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The batch outcome counts.</returns>
    Task<BatchRuleResult> ApplyAsync(
        BatchRuleOperation operation,
        IReadOnlyList<string> ruleIds,
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken = default);
}
