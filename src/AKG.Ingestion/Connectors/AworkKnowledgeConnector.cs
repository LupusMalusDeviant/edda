using Edda.AKG.Ingestion.Sources;
using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Connectors;

/// <summary>
/// Preset <see cref="IKnowledgeConnector"/> for awork (TypeId <c>awork</c>) — a thin configuration of the
/// generic HTTP source (<see cref="HttpApiSource"/>). Fetches a list entity (projects, tasks, …) and maps
/// name/description into knowledge. Authenticates with a Bearer token resolved server-side from the
/// credential store (see ADR-0006). For non-standard responses the generic <c>custom-http</c> connector
/// remains the full-control fallback.
/// </summary>
public sealed class AworkKnowledgeConnector : IKnowledgeConnector
{
    private const string BaseUrlField = "baseUrl";
    private const string EntityField = "entity";
    private const string EnrichField = "enrich";
    private const string TokenField = "token";
    private const string DefaultBaseUrl = "https://api.awork.io/api/v1";
    private const string DefaultEntity = "projects";

    private readonly IIngestionPipeline _pipeline;
    private readonly ICredentialStore _credentials;
    private readonly IIdentityContext _identity;

    /// <summary>Initializes a new instance of the <see cref="AworkKnowledgeConnector"/> class.</summary>
    /// <param name="pipeline">The ingestion pipeline the run is delegated to.</param>
    /// <param name="credentials">Credential store the per-instance API token is resolved from.</param>
    /// <param name="identity">Identity context used to scope the credential key.</param>
    public AworkKnowledgeConnector(
        IIngestionPipeline pipeline,
        ICredentialStore credentials,
        IIdentityContext identity)
    {
        _pipeline = pipeline;
        _credentials = credentials;
        _identity = identity;
    }

    /// <inheritdoc />
    public string TypeId => "awork";

    /// <inheritdoc />
    public ConnectorDescriptor Describe() => new()
    {
        TypeId = TypeId,
        DisplayName = "awork",
        Description = "Importiert eine awork-Liste (Projekte, Aufgaben, …) als Wissen. Auth via Bearer-Token.",
        Fields =
        [
            new ConnectorField { Key = BaseUrlField, Label = "API-Basis-URL", Type = ConnectorFieldType.Url, Default = DefaultBaseUrl, Help = "Standard: " + DefaultBaseUrl },
            new ConnectorField { Key = TokenField, Label = "API-Key", Type = ConnectorFieldType.Secret, Required = true, Help = "Bearer-Token; verschlüsselt gespeichert." },
            new ConnectorField { Key = EntityField, Label = "Entity", Type = ConnectorFieldType.Text, Default = DefaultEntity, Help = "Listen-Endpoint, z. B. projects, tasks, companies." },
            new ConnectorField { Key = EnrichField, Label = "LLM-Anreicherung", Type = ConnectorFieldType.Boolean, Default = "false" },
        ],
    };

    /// <inheritdoc />
    public async Task<IngestionResult> RunAsync(
        ConnectorInstanceConfig instance,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = Value(instance, BaseUrlField) is { Length: > 0 } b ? b : DefaultBaseUrl;

        var userId = _identity.UserId ?? "local";
        var apiKey = await _credentials
            .RetrieveAsync($"{userId}:source:{instance.Id}:{TokenField}", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(apiKey))
            return Failed(instance.Id, "API-Key fehlt.");

        var entity = Value(instance, EntityField) is { Length: > 0 } e ? e : DefaultEntity;
        var enrich = string.Equals(Value(instance, EnrichField), "true", StringComparison.OrdinalIgnoreCase);

        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [HttpApiSource.BaseUrlKey] = baseUrl,
            [HttpApiSource.ListPathKey] = entity,
            [HttpApiSource.AuthHeaderKey] = "Authorization",
            [HttpApiSource.AuthTemplateKey] = "Bearer {token}",
            [HttpApiSource.IdFieldKey] = "id",
            [HttpApiSource.TitleFieldKey] = "name",
            [HttpApiSource.BodyFieldKey] = "description",
            [HttpApiSource.SourceLabelKey] = "awork",
            [HttpApiSource.PageModeKey] = "page",
            [HttpApiSource.PageParamKey] = "page",
            [HttpApiSource.PageSizeParamKey] = "pageSize",
            [HttpApiSource.PageSizeKey] = "50",
            [HttpApiSource.MaxPagesKey] = "20",
        };

        var request = new IngestionRequest
        {
            SourceKind = "custom-http",
            Source = new IngestionSourceConfig { Token = apiKey, Settings = settings },
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
