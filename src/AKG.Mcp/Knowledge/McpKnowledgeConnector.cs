using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Mcp.Knowledge;

/// <summary>
/// <see cref="IKnowledgeConnector"/> for an external MCP server (TypeId <c>mcp</c>). Configures the
/// <see cref="McpKnowledgeSource"/> declaratively and delegates the run to the ingestion pipeline. The
/// bearer token is resolved server-side from the credential store (key
/// <c>{userId}:source:{instanceId}:token</c>) so it is never taken from a request (see ADR-0006).
/// </summary>
public sealed class McpKnowledgeConnector : IKnowledgeConnector
{
    private const string EnrichField = "enrich";
    private const string TokenField = "token";

    private readonly IIngestionPipeline _pipeline;
    private readonly ICredentialStore _credentials;
    private readonly IIdentityContext _identity;

    /// <summary>Initializes a new instance of the <see cref="McpKnowledgeConnector"/> class.</summary>
    /// <param name="pipeline">The ingestion pipeline the run is delegated to.</param>
    /// <param name="credentials">Credential store the per-instance token is resolved from.</param>
    /// <param name="identity">Identity context used to scope the credential key.</param>
    public McpKnowledgeConnector(
        IIngestionPipeline pipeline,
        ICredentialStore credentials,
        IIdentityContext identity)
    {
        _pipeline = pipeline;
        _credentials = credentials;
        _identity = identity;
    }

    /// <inheritdoc />
    public string TypeId => "mcp";

    /// <inheritdoc />
    public ConnectorDescriptor Describe() => new()
    {
        TypeId = TypeId,
        DisplayName = "MCP-Server (extern)",
        Description = "Ruft ein Tool eines externen MCP-Servers auf und importiert dessen Ergebnis als Wissen.",
        Fields =
        [
            new ConnectorField { Key = McpKnowledgeSource.ServerUrlKey, Label = "MCP-Server-URL", Type = ConnectorFieldType.Url, Required = true, Help = "Streamable-HTTP-Endpoint des MCP-Servers." },
            new ConnectorField { Key = McpKnowledgeSource.ToolNameKey, Label = "Tool-Name", Type = ConnectorFieldType.Text, Required = true, Help = "z. B. search oder list_documents." },
            new ConnectorField { Key = McpKnowledgeSource.ArgumentsKey, Label = "Argumente (JSON)", Type = ConnectorFieldType.Text, Help = "JSON-Objekt, z. B. {\"query\":\"…\"}. Leer = keine Argumente." },
            new ConnectorField { Key = McpKnowledgeSource.LabelKey, Label = "Bezeichnung", Type = ConnectorFieldType.Text, Help = "Name des Quell-Knotens (leer = Tool-Name)." },
            new ConnectorField { Key = McpKnowledgeSource.DomainKey, Label = "Domäne", Type = ConnectorFieldType.Text, Help = "Optionale Wissens-Domäne." },
            new ConnectorField { Key = TokenField, Label = "Bearer-Token", Type = ConnectorFieldType.Secret, Help = "Optional; verschlüsselt gespeichert." },
            new ConnectorField { Key = EnrichField, Label = "LLM-Anreicherung", Type = ConnectorFieldType.Boolean, Default = "false" },
        ],
    };

    /// <inheritdoc />
    public async Task<IngestionResult> RunAsync(
        ConnectorInstanceConfig instance,
        CancellationToken cancellationToken = default)
    {
        var serverUrl = Value(instance, McpKnowledgeSource.ServerUrlKey);
        var toolName = Value(instance, McpKnowledgeSource.ToolNameKey);
        if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(toolName))
            return Failed(instance.Id, "MCP-Server-URL und Tool-Name sind erforderlich.");

        var enrich = string.Equals(Value(instance, EnrichField), "true", StringComparison.OrdinalIgnoreCase);

        var userId = _identity.UserId ?? "local";
        var token = await _credentials
            .RetrieveAsync($"{userId}:source:{instance.Id}:{TokenField}", cancellationToken).ConfigureAwait(false);

        var settings = instance.Values.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        var request = new IngestionRequest
        {
            SourceKind = "mcp",
            Source = new IngestionSourceConfig { Token = token, Settings = settings },
            EnableEnrichment = enrich,
        };

        try
        {
            return await _pipeline.IngestAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Failed(instance.Id, ex.Message);
        }
    }

    private static string? Value(ConnectorInstanceConfig instance, string key) =>
        instance.Values.TryGetValue(key, out var value) ? value : null;

    private static IngestionResult Failed(string itemId, string message) =>
        new() { Failed = 1, Errors = [new IngestionError { ItemId = itemId, Message = message }] };
}
