using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Connectors;

/// <summary>
/// Default <see cref="IConnectorRegistry"/>. Indexes the registered <see cref="IKnowledgeConnector"/>s by
/// their <see cref="IKnowledgeConnector.TypeId"/> and routes a configured instance to the matching one.
/// </summary>
public sealed class ConnectorRegistry : IConnectorRegistry
{
    private readonly IReadOnlyDictionary<string, IKnowledgeConnector> _connectors;

    /// <summary>Initializes a new instance of the <see cref="ConnectorRegistry"/> class.</summary>
    /// <param name="connectors">All registered connectors (DI multi-binding).</param>
    public ConnectorRegistry(IEnumerable<IKnowledgeConnector> connectors)
    {
        _connectors = connectors.ToDictionary(c => c.TypeId, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IReadOnlyList<ConnectorDescriptor> Describe() =>
        _connectors.Values.Select(c => c.Describe()).ToList();

    /// <inheritdoc />
    public Task<IngestionResult> RunAsync(ConnectorInstanceConfig instance, CancellationToken cancellationToken = default)
    {
        if (_connectors.TryGetValue(instance.TypeId, out var connector))
        {
            return connector.RunAsync(instance, cancellationToken);
        }

        return Task.FromResult(new IngestionResult
        {
            Failed = 1,
            Errors = [new IngestionError { ItemId = instance.Id, Message = $"Unknown connector type '{instance.TypeId}'." }],
        });
    }
}
