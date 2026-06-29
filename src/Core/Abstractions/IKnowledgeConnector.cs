using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// A pluggable knowledge source driven by a declarative <see cref="ConnectorDescriptor"/>. Generalizes the
/// file-based <see cref="IIngestionSource"/> so any backend (Git, Jira, Awork, a custom HTTP API) can be
/// configured through the UI and run on demand. Implementations translate a configured instance into an
/// ingestion run, resolving any secrets (tokens) server-side from the credential store (see ADR-0005).
/// </summary>
public interface IKnowledgeConnector
{
    /// <summary>Stable type discriminator (matches <see cref="ConnectorInstanceConfig.TypeId"/>).</summary>
    string TypeId { get; }

    /// <summary>Returns the declarative descriptor the UI renders to configure an instance.</summary>
    /// <returns>The connector descriptor.</returns>
    ConnectorDescriptor Describe();

    /// <summary>
    /// Runs an ingestion for the given configured instance. Best-effort: failures are reported in the
    /// returned <see cref="IngestionResult"/> rather than thrown.
    /// </summary>
    /// <param name="instance">The configured source instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ingestion result (counts and any errors).</returns>
    Task<IngestionResult> RunAsync(ConnectorInstanceConfig instance, CancellationToken cancellationToken = default);
}
