using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Persists per-dataset role grants (ADR-0014, Slice 2). A grant ties a (tenant, dataset, user) triple to a
/// <see cref="TenantRole"/>; the store is the source of truth for both read visibility (which datasets a user
/// may see) and the sharing/mutation gates. Backed by a local side-store, analogous to the feedback store.
/// </summary>
public interface IDatasetGrantStore
{
    /// <summary>Creates or updates a grant (upsert on the tenant/dataset/user key).</summary>
    /// <param name="grant">The grant to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task GrantAsync(DatasetGrant grant, CancellationToken cancellationToken = default);

    /// <summary>Removes a user's grant on a dataset, if any. A missing grant is a no-op.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="datasetId">The dataset (provenance head id).</param>
    /// <param name="userId">The user whose grant is removed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RevokeAsync(string tenantId, string datasetId, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the dataset ids the user has any grant on within the tenant — i.e. the datasets the user may
    /// read (any role implies at least Viewer).
    /// </summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="userId">The user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The distinct visible dataset ids.</returns>
    Task<IReadOnlyList<string>> GetVisibleDatasetIdsAsync(
        string tenantId, string userId, CancellationToken cancellationToken = default);

    /// <summary>Returns the user's role on a dataset, or <see langword="null"/> when no grant exists.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="datasetId">The dataset (provenance head id).</param>
    /// <param name="userId">The user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The granted role, or null.</returns>
    Task<TenantRole?> GetRoleAsync(
        string tenantId, string datasetId, string userId, CancellationToken cancellationToken = default);
}
