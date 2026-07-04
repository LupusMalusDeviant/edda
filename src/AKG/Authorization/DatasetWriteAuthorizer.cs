using Edda.AKG.Graph;
using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Authorization;

/// <summary>
/// Dataset-aware <see cref="IDatasetWriteAuthorizer"/> (ADR-0014, Slice 2b): a caller with at least an Editor
/// grant on the rule's dataset may mutate it; otherwise the decision delegates to the wrapped
/// <see cref="IRuleAuthorizer"/> (the C2 owner/role matrix). Registered only when dataset permissions are
/// enabled — the disabled build uses the pass-through, keeping behaviour identical.
/// </summary>
internal sealed class DatasetWriteAuthorizer : IDatasetWriteAuthorizer
{
    private readonly IRuleAuthorizer _inner;
    private readonly IDatasetGrantStore _grants;
    private readonly IIdentityContext? _identity;

    /// <summary>Initializes a new <see cref="DatasetWriteAuthorizer"/>.</summary>
    /// <param name="inner">The sync C2 authorizer the decision delegates to when no dataset grant applies.</param>
    /// <param name="grants">The grant store consulted for the caller's role on the rule's dataset.</param>
    /// <param name="identity">Ambient identity supplying the tenant; null falls back to the default tenant.</param>
    public DatasetWriteAuthorizer(
        IRuleAuthorizer inner, IDatasetGrantStore grants, IIdentityContext? identity = null)
    {
        _inner = inner;
        _grants = grants;
        _identity = identity;
    }

    private string Tenant => _identity?.TenantId ?? Tenants.DefaultTenantId;

    /// <inheritdoc />
    public async Task EnsureCanMutateAsync(
        KnowledgeRule rule, string userId, bool isAdmin = false, CancellationToken cancellationToken = default)
    {
        if (await HasDatasetEditorGrantAsync(rule.Id, userId, cancellationToken).ConfigureAwait(false)) return;
        _inner.EnsureCanMutate(rule, userId, isAdmin);
    }

    /// <inheritdoc />
    public async Task EnsureCanMutateAsync(
        string ruleId, string? ownerId, string userId, bool isAdmin = false, CancellationToken cancellationToken = default)
    {
        if (await HasDatasetEditorGrantAsync(ruleId, userId, cancellationToken).ConfigureAwait(false)) return;
        _inner.EnsureCanMutate(ownerId, userId, isAdmin);
    }

    /// <summary>
    /// Whether the user holds at least an Editor grant on the rule's dataset. A Viewer grant is read-only and
    /// does not permit mutation; a rule that belongs to no dataset yields <see langword="false"/> (delegate).
    /// </summary>
    /// <param name="ruleId">The rule id whose dataset is checked.</param>
    /// <param name="userId">The acting user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when an Editor-or-higher grant exists.</returns>
    private async Task<bool> HasDatasetEditorGrantAsync(
        string ruleId, string userId, CancellationToken cancellationToken)
    {
        var datasetId = DatasetMembership.DatasetIdOf(ruleId);
        if (datasetId is null || string.IsNullOrEmpty(userId)) return false;
        var role = await _grants.GetRoleAsync(Tenant, datasetId, userId, cancellationToken).ConfigureAwait(false);
        return role >= TenantRole.Editor;
    }
}
