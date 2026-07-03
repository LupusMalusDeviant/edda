using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Recycle bin over soft-deleted rules (E10): deletion marks a rule (deletedAt + validUntil) instead
/// of removing it; it disappears from active views and context compilation but can be restored or
/// purged here. Non-admins only see and act on their own rules.
/// </summary>
public interface IRuleRecycleBin
{
    /// <summary>Lists soft-deleted rules visible to the user (newest deletion first).</summary>
    /// <param name="userId">The acting user.</param>
    /// <param name="isAdmin">Whether the user may see all deleted rules.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The visible soft-deleted rules.</returns>
    Task<IReadOnlyList<DeletedRuleInfo>> ListAsync(
        string userId, bool isAdmin = false, CancellationToken cancellationToken = default);

    /// <summary>Restores a soft-deleted rule. Returns false when it does not exist or is not deleted.</summary>
    /// <param name="ruleId">The rule to restore.</param>
    /// <param name="userId">The acting user.</param>
    /// <param name="isAdmin">Whether the user may restore foreign rules.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the rule was restored.</returns>
    Task<bool> RestoreAsync(
        string ruleId, string userId, bool isAdmin = false, CancellationToken cancellationToken = default);

    /// <summary>Permanently removes a soft-deleted rule and its chunks. Returns false when not found.</summary>
    /// <param name="ruleId">The rule to purge.</param>
    /// <param name="userId">The acting user.</param>
    /// <param name="isAdmin">Whether the user may purge foreign rules.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the rule was purged.</returns>
    Task<bool> PurgeAsync(
        string ruleId, string userId, bool isAdmin = false, CancellationToken cancellationToken = default);
}
