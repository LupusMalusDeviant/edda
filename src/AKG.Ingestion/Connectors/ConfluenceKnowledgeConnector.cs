using System.Text;
using Edda.AKG.Ingestion.Sources;
using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Connectors;

/// <summary>
/// Preset <see cref="IKnowledgeConnector"/> for Confluence Cloud (TypeId <c>confluence</c>) — a thin
/// configuration of the generic HTTP source (<see cref="HttpApiSource"/>). Searches pages via the Confluence
/// REST API (<c>content/search</c> with CQL) and maps title/storage-body into knowledge. Authenticates with
/// HTTP Basic using <c>base64(email:api-token)</c>; the API token is resolved server-side from the credential
/// store (see ADR-0006). For non-standard setups the generic <c>custom-http</c> connector remains the
/// full-control fallback.
/// </summary>
public sealed class ConfluenceKnowledgeConnector : IKnowledgeConnector
{
    private const string BaseUrlField = "baseUrl";
    private const string EmailField = "email";
    private const string CqlField = "cql";
    private const string EnrichField = "enrich";
    private const string TokenField = "token";
    private const string DefaultCql = "type=page ORDER BY lastmodified DESC";

    private readonly IIngestionPipeline _pipeline;
    private readonly ICredentialStore _credentials;
    private readonly IIdentityContext _identity;

    /// <summary>Initializes a new instance of the <see cref="ConfluenceKnowledgeConnector"/> class.</summary>
    /// <param name="pipeline">The ingestion pipeline the run is delegated to.</param>
    /// <param name="credentials">Credential store the per-instance API token is resolved from.</param>
    /// <param name="identity">Identity context used to scope the credential key.</param>
    public ConfluenceKnowledgeConnector(
        IIngestionPipeline pipeline,
        ICredentialStore credentials,
        IIdentityContext identity)
    {
        _pipeline = pipeline;
        _credentials = credentials;
        _identity = identity;
    }

    /// <inheritdoc />
    public string TypeId => "confluence";

    /// <inheritdoc />
    public ConnectorDescriptor Describe() => new()
    {
        TypeId = TypeId,
        DisplayName = "Confluence (Cloud)",
        Description = "Importiert Confluence-Seiten (REST API, CQL) als Wissen. Auth via E-Mail + API-Token (Basic).",
        Fields =
        [
            new ConnectorField { Key = BaseUrlField, Label = "Confluence-URL", Type = ConnectorFieldType.Url, Required = true, Help = "z. B. https://deine-domain.atlassian.net/wiki" },
            new ConnectorField { Key = EmailField, Label = "E-Mail", Type = ConnectorFieldType.Text, Required = true, Help = "Konto-E-Mail für die Basic-Auth." },
            new ConnectorField { Key = TokenField, Label = "API-Token", Type = ConnectorFieldType.Secret, Required = true, Help = "Atlassian-API-Token; verschlüsselt gespeichert." },
            new ConnectorField { Key = CqlField, Label = "CQL", Type = ConnectorFieldType.Text, Default = DefaultCql, Help = "Filter/Sortierung der Seiten (Confluence Query Language)." },
            new ConnectorField { Key = EnrichField, Label = "LLM-Anreicherung", Type = ConnectorFieldType.Boolean, Default = "false" },
        ],
    };

    /// <inheritdoc />
    public async Task<IngestionResult> RunAsync(
        ConnectorInstanceConfig instance,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = Value(instance, BaseUrlField);
        var email = Value(instance, EmailField);
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(email))
            return Failed(instance.Id, "Confluence-URL und E-Mail sind erforderlich.");

        var userId = _identity.UserId ?? "local";
        var apiToken = await _credentials
            .RetrieveAsync($"{userId}:source:{instance.Id}:{TokenField}", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(apiToken))
            return Failed(instance.Id, "API-Token fehlt.");

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{apiToken}"));
        var cql = Value(instance, CqlField) is { Length: > 0 } c ? c : DefaultCql;
        var enrich = string.Equals(Value(instance, EnrichField), "true", StringComparison.OrdinalIgnoreCase);

        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [HttpApiSource.BaseUrlKey] = baseUrl!,
            [HttpApiSource.ListPathKey] = $"rest/api/content/search?cql={Uri.EscapeDataString(cql)}&expand=body.storage",
            [HttpApiSource.AuthHeaderKey] = "Authorization",
            [HttpApiSource.AuthTemplateKey] = "Basic {token}",
            [HttpApiSource.ItemsPathKey] = "results",
            [HttpApiSource.IdFieldKey] = "id",
            [HttpApiSource.TitleFieldKey] = "title",
            [HttpApiSource.BodyFieldKey] = "body.storage.value",
            [HttpApiSource.UrlFieldKey] = "_links.webui",
            [HttpApiSource.SourceLabelKey] = "confluence",
            [HttpApiSource.PageModeKey] = "offset",
            [HttpApiSource.PageParamKey] = "start",
            [HttpApiSource.PageSizeParamKey] = "limit",
            [HttpApiSource.PageSizeKey] = "25",
            [HttpApiSource.MaxPagesKey] = "20",
        };

        var request = new IngestionRequest
        {
            SourceKind = "custom-http",
            Source = new IngestionSourceConfig { Token = basic, Settings = settings },
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
