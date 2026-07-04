using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Dataset-aware write gate (ADR-0014, Slice 2b): widens the C2 rule-mutation check with dataset grants — a
/// caller who holds at least an Editor grant on the rule's dataset may mutate it, even without the tenant role
/// the sync <see cref="IRuleAuthorizer"/> would otherwise require. When no dataset grant applies, the decision
/// falls through to the wrapped <see cref="IRuleAuthorizer"/> unchanged, so non-dataset rules and the
/// dataset-disabled build behave exactly as before.
/// </summary>
public interface IDatasetWriteAuthorizer
{
    /// <summary>Ensures the caller may mutate the rule (an Editor+ dataset grant, or else the C2 role matrix).</summary>
    /// <param name="rule">The rule being mutated (its id identifies the dataset).</param>
    /// <param name="userId">The acting user.</param>
    /// <param name="isAdmin">Caller-supplied operator flag; bypasses the role matrix.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="UnauthorizedAccessException">When neither the dataset grant nor the role matrix permits it.</exception>
    Task EnsureCanMutateAsync(
        KnowledgeRule rule, string userId, bool isAdmin = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same gate for callers that hold only the rule id and owner (e.g. the recycle bin's soft-deleted rows).
    /// </summary>
    /// <param name="ruleId">The mutated rule's id (identifies the dataset).</param>
    /// <param name="ownerId">The mutated rule's owner; <see langword="null"/> means a global rule.</param>
    /// <param name="userId">The acting user.</param>
    /// <param name="isAdmin">Caller-supplied operator flag; bypasses the role matrix.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="UnauthorizedAccessException">When neither the dataset grant nor the role matrix permits it.</exception>
    Task EnsureCanMutateAsync(
        string ruleId, string? ownerId, string userId, bool isAdmin = false, CancellationToken cancellationToken = default);
}
