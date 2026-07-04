using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Resolves which datasets the current caller may read (ADR-0014). A <em>dataset</em> is a provenance group —
/// an ingested source such as a Git repository or an upload, identified by its head-node id (e.g.
/// <c>git:my-repo</c>). Enforcement reads the ambient identity, mirroring the rest of the graph layer (C1/C2).
/// The default implementation grants everything, so dataset permissions stay inert — and single-user and
/// default-tenant behaviour stays unchanged — until a grant-backed service replaces it in a later slice.
/// </summary>
public interface IDatasetPermissionService
{
    /// <summary>
    /// Resolves the datasets the ambient caller may read. Returns <see cref="DatasetVisibility.Unrestricted"/>
    /// when no dataset-level restriction applies (the behaviour-neutral default).
    /// </summary>
    /// <returns>The caller's dataset read visibility.</returns>
    DatasetVisibility ResolveVisibility();
}
