using System.Text;
using Edda.AKG.Ingestion.Sources;
using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Connectors;

/// <summary>
/// Preset <see cref="IKnowledgeConnector"/> for Jira Cloud (TypeId <c>jira</c>) — a thin configuration of
/// the generic HTTP source (<see cref="HttpApiSource"/>). Searches issues via the Jira REST API v2 and maps
/// summary/description into knowledge. Authenticates with HTTP Basic using <c>base64(email:api-token)</c>;
/// the API token is resolved server-side from the credential store (see ADR-0006). For non-standard setups
/// the generic <c>custom-http</c> connector remains the full-control fallback.
/// </summary>
public sealed class JiraKnowledgeConnector : IKnowledgeConnector
{
    private const string BaseUrlField = "baseUrl";
    private const string EmailField = "email";
    private const string JqlField = "jql";
    private const string EnrichField = "enrich";
    private const string TokenField = "token";
    private const string DefaultJql = "ORDER BY created DESC";

    private readonly IIngestionPipeline _pipeline;
    private readonly ICredentialStore _credentials;
    private readonly IIdentityContext _identity;

    /// <summary>Initializes a new instance of the <see cref="JiraKnowledgeConnector"/> class.</summary>
    /// <param name="pipeline">The ingestion pipeline the run is delegated to.</param>
    /// <param name="credentials">Credential store the per-instance API token is resolved from.</param>
    /// <param name="identity">Identity context used to scope the credential key.</param>
    public JiraKnowledgeConnector(
        IIngestionPipeline pipeline,
        ICredentialStore credentials,
        IIdentityContext identity)
    {
        _pipeline = pipeline;
        _credentials = credentials;
        _identity = identity;
    }

    /// <inheritdoc />
    public string TypeId => "jira";

    /// <inheritdoc />
    public ConnectorDescriptor Describe() => new()
    {
        TypeId = TypeId,
        DisplayName = "Jira (Cloud)",
        Description = "Importiert Jira-Issues (REST API v2) als Wissen. Auth via E-Mail + API-Token (Basic).",
        Fields =
        [
            new ConnectorField { Key = BaseUrlField, Label = "Jira-URL", Type = ConnectorFieldType.Url, Required = true, Help = "z. B. https://deine-domain.atlassian.net" },
            new ConnectorField { Key = EmailField, Label = "E-Mail", Type = ConnectorFieldType.Text, Required = true, Help = "Konto-E-Mail für die Basic-Auth." },
            new ConnectorField { Key = TokenField, Label = "API-Token", Type = ConnectorFieldType.Secret, Required = true, Help = "Atlassian-API-Token; verschlüsselt gespeichert." },
            new ConnectorField { Key = JqlField, Label = "JQL", Type = ConnectorFieldType.Text, Default = DefaultJql, Help = "Filter/Sortierung der Issues." },
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
            return Failed(instance.Id, "Jira-URL und E-Mail sind erforderlich.");

        var userId = _identity.UserId ?? "local";
        var apiToken = await _credentials
            .RetrieveAsync($"{userId}:source:{instance.Id}:{TokenField}", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(apiToken))
            return Failed(instance.Id, "API-Token fehlt.");

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{apiToken}"));
        var jql = Value(instance, JqlField) is { Length: > 0 } j ? j : DefaultJql;
        var enrich = string.Equals(Value(instance, EnrichField), "true", StringComparison.OrdinalIgnoreCase);

        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [HttpApiSource.BaseUrlKey] = baseUrl!,
            [HttpApiSource.ListPathKey] = $"rest/api/2/search?jql={Uri.EscapeDataString(jql)}&fields=summary,description",
            [HttpApiSource.AuthHeaderKey] = "Authorization",
            [HttpApiSource.AuthTemplateKey] = "Basic {token}",
            [HttpApiSource.ItemsPathKey] = "issues",
            [HttpApiSource.IdFieldKey] = "key",
            [HttpApiSource.TitleFieldKey] = "fields.summary",
            [HttpApiSource.BodyFieldKey] = "fields.description",
            [HttpApiSource.UrlFieldKey] = "self",
            [HttpApiSource.SourceLabelKey] = "jira",
            [HttpApiSource.PageModeKey] = "offset",
            [HttpApiSource.PageParamKey] = "startAt",
            [HttpApiSource.PageSizeParamKey] = "maxResults",
            [HttpApiSource.PageSizeKey] = "50",
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
