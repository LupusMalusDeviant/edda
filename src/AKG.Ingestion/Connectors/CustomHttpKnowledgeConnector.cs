using Edda.AKG.Ingestion.Sources;
using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Connectors;

/// <summary>
/// Generic <see cref="IKnowledgeConnector"/> for any JSON HTTP/REST API (TypeId <c>custom-http</c>).
/// All non-secret fields map straight onto <see cref="HttpApiSource"/> settings; the access token is
/// resolved server-side from the credential store (key <c>{userId}:source:{instanceId}:token</c>) and
/// passed via <see cref="IngestionSourceConfig.Token"/>. New source types (Jira, Awork, …) are just a
/// matching configuration of this connector — no bespoke code (see ADR-0006).
/// </summary>
public sealed class CustomHttpKnowledgeConnector : IKnowledgeConnector
{
    private const string EnrichField = "enrich";
    private const string TokenField = "token";

    private readonly IIngestionPipeline _pipeline;
    private readonly ICredentialStore _credentials;
    private readonly IIdentityContext _identity;

    /// <summary>Initializes a new instance of the <see cref="CustomHttpKnowledgeConnector"/> class.</summary>
    /// <param name="pipeline">The ingestion pipeline the run is delegated to.</param>
    /// <param name="credentials">Credential store the per-instance token is resolved from.</param>
    /// <param name="identity">Identity context used to scope the credential key.</param>
    public CustomHttpKnowledgeConnector(
        IIngestionPipeline pipeline,
        ICredentialStore credentials,
        IIdentityContext identity)
    {
        _pipeline = pipeline;
        _credentials = credentials;
        _identity = identity;
    }

    /// <inheritdoc />
    public string TypeId => "custom-http";

    /// <inheritdoc />
    public ConnectorDescriptor Describe() => new()
    {
        TypeId = TypeId,
        DisplayName = "Custom HTTP/REST-API",
        Description = "Generische Anbindung an eine JSON-API (z. B. Jira, Awork, beliebige REST-Endpoints).",
        Fields =
        [
            Field(HttpApiSource.BaseUrlKey, "Basis-URL", ConnectorFieldType.Url, required: true, help: "z. B. https://api.example.com"),
            Field(HttpApiSource.ListPathKey, "Listen-Pfad", ConnectorFieldType.Text, required: true, help: "Pfad inkl. Query, z. B. /v1/items?filter=open"),
            Field(HttpApiSource.ItemsPathKey, "Items-Pfad (JSON)", ConnectorFieldType.Text, help: "Punkt-Pfad zum Array, z. B. data.items. Leer = Wurzel ist das Array."),
            Field(HttpApiSource.IdFieldKey, "Feld: ID", ConnectorFieldType.Text, help: "Punkt-Pfad je Eintrag, z. B. id oder key."),
            Field(HttpApiSource.TitleFieldKey, "Feld: Titel", ConnectorFieldType.Text, help: "z. B. name oder fields.summary."),
            Field(HttpApiSource.BodyFieldKey, "Feld: Inhalt", ConnectorFieldType.Text, help: "z. B. description oder body."),
            Field(HttpApiSource.UrlFieldKey, "Feld: URL", ConnectorFieldType.Text, help: "Optionaler Link je Eintrag, z. B. self."),
            Field(HttpApiSource.SourceLabelKey, "Quell-Label", ConnectorFieldType.Text, help: "Provenienz/ID-Präfix, z. B. jira. Leer = custom-http."),
            Field(HttpApiSource.DomainKey, "Domäne", ConnectorFieldType.Text, help: "Optionale Wissens-Domäne für alle Einträge."),
            Field(HttpApiSource.AuthHeaderKey, "Auth-Header", ConnectorFieldType.Text, help: "z. B. Authorization oder PRIVATE-TOKEN."),
            Field(HttpApiSource.AuthTemplateKey, "Auth-Wert-Vorlage", ConnectorFieldType.Text, help: "{token} wird ersetzt, z. B. Bearer {token}."),
            Field(TokenField, "Token/Secret", ConnectorFieldType.Secret, help: "Verschlüsselt gespeichert; ersetzt {token}."),
            Field(HttpApiSource.PageModeKey, "Pagination", ConnectorFieldType.Text, help: "none | page | offset."),
            Field(HttpApiSource.PageParamKey, "Seiten-Parameter", ConnectorFieldType.Text, help: "z. B. page oder startAt."),
            Field(HttpApiSource.PageSizeParamKey, "Größen-Parameter", ConnectorFieldType.Text, help: "z. B. pageSize oder maxResults."),
            Field(HttpApiSource.PageSizeKey, "Seitengröße", ConnectorFieldType.Text, help: "z. B. 50."),
            Field(HttpApiSource.MaxPagesKey, "Max. Seiten", ConnectorFieldType.Text, help: "Obergrenze, z. B. 20."),
            new ConnectorField
            {
                Key = EnrichField,
                Label = "LLM-Anreicherung",
                Type = ConnectorFieldType.Boolean,
                Default = "false",
                Help = "Wenn aktiv, reichert der konfigurierte LLM-Provider die Knoten an.",
            },
        ],
    };

    /// <inheritdoc />
    public async Task<IngestionResult> RunAsync(
        ConnectorInstanceConfig instance,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = Value(instance, HttpApiSource.BaseUrlKey);
        var listPath = Value(instance, HttpApiSource.ListPathKey);
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(listPath))
            return Failed(instance.Id, "Basis-URL und Listen-Pfad sind erforderlich.");

        var enrich = string.Equals(Value(instance, EnrichField), "true", StringComparison.OrdinalIgnoreCase);

        var userId = _identity.UserId ?? "local";
        var token = await _credentials
            .RetrieveAsync($"{userId}:source:{instance.Id}:{TokenField}", cancellationToken).ConfigureAwait(false);

        // Non-secret field values are the source settings verbatim (descriptor keys == HttpApiSource keys).
        var settings = instance.Values.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        var request = new IngestionRequest
        {
            SourceKind = "custom-http",
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

    private static ConnectorField Field(string key, string label, ConnectorFieldType type, bool required = false, string? help = null)
        => new() { Key = key, Label = label, Type = type, Required = required, Help = help };

    private static string? Value(ConnectorInstanceConfig instance, string key) =>
        instance.Values.TryGetValue(key, out var value) ? value : null;

    private static IngestionResult Failed(string itemId, string message) =>
        new() { Failed = 1, Errors = [new IngestionError { ItemId = itemId, Message = message }] };
}
