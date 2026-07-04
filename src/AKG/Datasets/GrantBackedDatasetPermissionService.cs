using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Datasets;

/// <summary>
/// Grant-backed <see cref="IDatasetPermissionService"/> (ADR-0014, Slice 2): resolves the caller's visible
/// datasets from the <see cref="IDatasetGrantStore"/> and the ambient identity. An identity-less context
/// (single-user/no-auth) and admins see everything; an authenticated user sees the datasets they hold a grant
/// on — plus, through <c>DatasetVisibilityFilter</c>, any rule that belongs to no dataset. Registered only when
/// dataset permissions are explicitly enabled, so the default build stays behaviour-neutral.
/// </summary>
internal sealed class GrantBackedDatasetPermissionService : IDatasetPermissionService
{
    private readonly IDatasetGrantStore _grants;
    private readonly IIdentityContext? _identity;

    /// <summary>Initializes a new <see cref="GrantBackedDatasetPermissionService"/>.</summary>
    /// <param name="grants">The grant store queried for the caller's visible datasets.</param>
    /// <param name="identity">Ambient identity (tenant + user + admin flag); null means no restriction.</param>
    public GrantBackedDatasetPermissionService(IDatasetGrantStore grants, IIdentityContext? identity = null)
    {
        _grants = grants;
        _identity = identity;
    }

    /// <inheritdoc />
    public async Task<DatasetVisibility> ResolveVisibilityAsync(CancellationToken cancellationToken = default)
    {
        // No identity (single-user/no-auth) or an admin sees every dataset — behaviour-neutral / operator override.
        if (_identity is null || _identity.IsAdmin) return DatasetVisibility.Unrestricted;

        var userId = _identity.UserId;
        if (string.IsNullOrEmpty(userId)) return DatasetVisibility.Unrestricted;

        var visible = await _grants
            .GetVisibleDatasetIdsAsync(_identity.TenantId, userId, cancellationToken)
            .ConfigureAwait(false);
        return DatasetVisibility.Restricted(visible);
    }
}
