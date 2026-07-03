using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Central role enforcement for rule mutations (C2, ADR-0012): one authorizer replaces the scattered
/// owner/admin checks in the graph, the recycle bin, the batch service and the admin endpoints. It reads
/// the ambient <see cref="IIdentityContext"/>; without one it falls back to the legacy owner/admin
/// semantics, so the single-user standalone (and every pre-C2 test) behaves unchanged.
/// </summary>
public interface IRuleAuthorizer
{
    /// <summary>
    /// Whether the current identity may create or mutate its own user-scoped content
    /// (episodic memories, per-user files). Requires <see cref="TenantRole.Editor"/> or higher;
    /// without an ambient identity this is always allowed (legacy semantics).
    /// </summary>
    /// <returns><see langword="true"/> when user-scoped writes are permitted.</returns>
    bool CanMutateOwn();

    /// <summary>
    /// Ensures the caller may mutate the given rule: <see cref="TenantRole.Editor"/> for own rules,
    /// <see cref="TenantRole.Owner"/> for foreign or global ones; <paramref name="isAdmin"/> (or the
    /// ambient admin) overrides the matrix.
    /// </summary>
    /// <param name="rule">The rule being mutated.</param>
    /// <param name="userId">The acting user.</param>
    /// <param name="isAdmin">Caller-supplied operator flag; bypasses the role matrix.</param>
    /// <exception cref="UnauthorizedAccessException">When the role does not permit the mutation.</exception>
    void EnsureCanMutate(KnowledgeRule rule, string userId, bool isAdmin = false);

    /// <summary>
    /// Same matrix as the <see cref="KnowledgeRule"/> overload, for callers that only hold the owner id
    /// (e.g. the recycle bin's soft-deleted property rows).
    /// </summary>
    /// <param name="ownerId">The mutated rule's owner; <see langword="null"/> means a global rule.</param>
    /// <param name="userId">The acting user.</param>
    /// <param name="isAdmin">Caller-supplied operator flag; bypasses the role matrix.</param>
    /// <exception cref="UnauthorizedAccessException">When the role does not permit the mutation.</exception>
    void EnsureCanMutate(string? ownerId, string userId, bool isAdmin = false);

    /// <summary>
    /// Ensures the caller may administer the tenant's knowledge (reload, seed, import).
    /// Requires <see cref="TenantRole.Owner"/>; <paramref name="isAdmin"/> (or the ambient admin)
    /// overrides. Without an ambient identity only the flag counts (legacy admin-gate semantics).
    /// </summary>
    /// <param name="isAdmin">Caller-supplied operator flag; bypasses the role matrix.</param>
    /// <exception cref="UnauthorizedAccessException">When the role does not permit administration.</exception>
    void EnsureCanAdminister(bool isAdmin = false);
}
