using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Datasets;

/// <summary>
/// Owner-gated <see cref="IDatasetSharingService"/> (ADR-0014, Slice 2). The tenant and acting user come from
/// the ambient identity (never from arguments); only a tenant admin or the dataset's Owner may grant or revoke.
/// A freshly ingested dataset has no Owner grant yet, so an admin bootstraps the first grant.
/// </summary>
internal sealed class DatasetSharingService : IDatasetSharingService
{
    private readonly IDatasetGrantStore _grants;
    private readonly IIdentityContext? _identity;

    /// <summary>Initializes a new <see cref="DatasetSharingService"/>.</summary>
    /// <param name="grants">The grant store mutated by share/unshare.</param>
    /// <param name="identity">Ambient identity supplying the tenant and acting user; null falls back to the default tenant.</param>
    public DatasetSharingService(IDatasetGrantStore grants, IIdentityContext? identity = null)
    {
        _grants = grants;
        _identity = identity;
    }

    private string Tenant => _identity?.TenantId ?? Tenants.DefaultTenantId;

    /// <inheritdoc />
    public async Task ShareAsync(
        string datasetId, string targetUserId, TenantRole role, CancellationToken cancellationToken = default)
    {
        await EnsureCanShareAsync(datasetId, cancellationToken).ConfigureAwait(false);
        await _grants.GrantAsync(
            new DatasetGrant { TenantId = Tenant, DatasetId = datasetId, UserId = targetUserId, Role = role },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UnshareAsync(
        string datasetId, string targetUserId, CancellationToken cancellationToken = default)
    {
        await EnsureCanShareAsync(datasetId, cancellationToken).ConfigureAwait(false);
        await _grants.RevokeAsync(Tenant, datasetId, targetUserId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures the ambient caller may manage grants on the dataset: a tenant admin always may; otherwise the
    /// caller must hold the <see cref="TenantRole.Owner"/> role on the dataset.
    /// </summary>
    /// <param name="datasetId">The dataset being shared.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="UnauthorizedAccessException">When the caller is neither admin nor dataset Owner.</exception>
    private async Task EnsureCanShareAsync(string datasetId, CancellationToken cancellationToken)
    {
        if (_identity?.IsAdmin == true) return;

        var callerId = _identity?.UserId;
        if (!string.IsNullOrEmpty(callerId))
        {
            var role = await _grants
                .GetRoleAsync(Tenant, datasetId, callerId, cancellationToken).ConfigureAwait(false);
            if (role == TenantRole.Owner) return;
        }

        throw new UnauthorizedAccessException(
            $"The current identity is not authorized to share dataset '{datasetId}'.");
    }
}
