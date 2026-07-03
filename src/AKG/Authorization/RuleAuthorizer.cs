using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Authorization;

/// <summary>
/// Default <see cref="IRuleAuthorizer"/> (C2, ADR-0012): evaluates the Owner/Editor/Viewer matrix
/// against the ambient identity. Pure and stateless — the role is read per call, never cached.
/// Without an ambient identity (plain unit tests, hosts that register none) every check degrades to
/// the pre-C2 owner/admin semantics, so existing behaviour is preserved bit-identically.
/// </summary>
internal sealed class RuleAuthorizer : IRuleAuthorizer
{
    private readonly IIdentityContext? _identity;

    /// <summary>Initializes a new <see cref="RuleAuthorizer"/>.</summary>
    /// <param name="identity">
    /// Ambient identity providing the tenant role; <see langword="null"/> falls back to the legacy
    /// owner/admin semantics (the single-user standalone and pre-C2 tests).
    /// </param>
    public RuleAuthorizer(IIdentityContext? identity = null) => _identity = identity;

    /// <summary>Whether the ambient identity is an operator (admin) — overrides the role matrix.</summary>
    private bool AmbientAdmin => _identity?.IsAdmin == true;

    /// <inheritdoc />
    public bool CanMutateOwn()
        => _identity is null || AmbientAdmin || _identity.Role >= TenantRole.Editor;

    /// <inheritdoc />
    public void EnsureCanMutate(KnowledgeRule rule, string userId, bool isAdmin = false)
    {
        if (!MayMutate(rule.OwnerId, userId, isAdmin))
            throw new UnauthorizedAccessException(
                $"User '{userId}' is not authorized to modify rule '{rule.Id}'.");
    }

    /// <inheritdoc />
    public void EnsureCanMutate(string? ownerId, string userId, bool isAdmin = false)
    {
        if (!MayMutate(ownerId, userId, isAdmin))
            throw new UnauthorizedAccessException(
                $"User '{userId}' is not authorized to modify rules owned by '{ownerId ?? "<global>"}'.");
    }

    /// <inheritdoc />
    public void EnsureCanAdminister(bool isAdmin = false)
    {
        if (isAdmin || AmbientAdmin) return;
        // Legacy semantics without an identity: only the operator flag counts (the pre-C2 admin gates).
        if (_identity is not null && _identity.Role >= TenantRole.Owner) return;
        throw new UnauthorizedAccessException(
            "The current identity is not authorized to administer the tenant's knowledge.");
    }

    /// <summary>
    /// The permission matrix core: operators always may; without an identity the legacy owner check
    /// applies; otherwise own rules need <see cref="TenantRole.Editor"/>, foreign or global rules
    /// need <see cref="TenantRole.Owner"/>.
    /// </summary>
    /// <param name="ownerId">The mutated rule's owner; <see langword="null"/> means a global rule.</param>
    /// <param name="userId">The acting user.</param>
    /// <param name="isAdmin">Caller-supplied operator flag.</param>
    /// <returns><see langword="true"/> when the mutation is permitted.</returns>
    private bool MayMutate(string? ownerId, string userId, bool isAdmin)
    {
        if (isAdmin || AmbientAdmin) return true;

        var ownRule = string.Equals(ownerId, userId, StringComparison.Ordinal);
        if (_identity is null) return ownRule;

        return _identity.Role >= (ownRule ? TenantRole.Editor : TenantRole.Owner);
    }
}
