using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Registry over all available <see cref="IKnowledgeConnector"/>s. Exposes their descriptors (for the UI)
/// and routes a configured instance to the matching connector for execution.
/// </summary>
public interface IConnectorRegistry
{
    /// <summary>Returns the descriptors of all registered connector types.</summary>
    /// <returns>One descriptor per connector type.</returns>
    IReadOnlyList<ConnectorDescriptor> Describe();

    /// <summary>
    /// Runs the connector matching <see cref="ConnectorInstanceConfig.TypeId"/>. Returns a failed result
    /// (rather than throwing) when no connector handles the type.
    /// </summary>
    /// <param name="instance">The configured source instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ingestion result.</returns>
    Task<IngestionResult> RunAsync(ConnectorInstanceConfig instance, CancellationToken cancellationToken = default);
}
