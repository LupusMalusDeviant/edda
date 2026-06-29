using Edda.AKG.Ingestion.Sources;
using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Connectors;

/// <summary>
/// <see cref="IKnowledgeConnector"/> for a whole GitLab group: scans every repository in the group
/// (including subgroups) and ingests their Markdown. The per-instance access token is resolved
/// server-side from the credential store (key <c>{userId}:source:{instanceId}:token</c>) so it is never
/// taken from a request, and the GitLab base URL is configured per instance (see ADR-0006).
/// </summary>
public sealed class GitLabGroupKnowledgeConnector : IKnowledgeConnector
{
    private const string BaseUrlField = "gitlabBaseUrl";
    private const string GroupField = "groupPath";
    private const string IncludeGlobsField = "includeGlobs";
    private const string EnrichField = "enrich";
    private const string TokenField = "token";

    private readonly IIngestionPipeline _pipeline;
    private readonly ICredentialStore _credentials;
    private readonly IIdentityContext _identity;

    /// <summary>Initializes a new instance of the <see cref="GitLabGroupKnowledgeConnector"/> class.</summary>
    /// <param name="pipeline">The ingestion pipeline the run is delegated to.</param>
    /// <param name="credentials">Credential store the per-instance token is resolved from.</param>
    /// <param name="identity">Identity context used to scope the credential key.</param>
    public GitLabGroupKnowledgeConnector(
        IIngestionPipeline pipeline,
        ICredentialStore credentials,
        IIdentityContext identity)
    {
        _pipeline = pipeline;
        _credentials = credentials;
        _identity = identity;
    }

    /// <inheritdoc />
    public string TypeId => "gitlab-group";

    /// <inheritdoc />
    public ConnectorDescriptor Describe() => new()
    {
        TypeId = TypeId,
        DisplayName = "GitLab-Gruppe (Batch)",
        Description = "Scannt alle Repositories einer GitLab-Gruppe (inkl. Untergruppen) und ingestiert deren Markdown- und Code-Dateien.",
        Fields =
        [
            new ConnectorField
            {
                Key = BaseUrlField,
                Label = "GitLab-URL",
                Type = ConnectorFieldType.Url,
                Required = true,
                Help = "Basis-URL der GitLab-Instanz, z. B. https://git.example.com.",
            },
            new ConnectorField
            {
                Key = GroupField,
                Label = "Gruppenpfad",
                Type = ConnectorFieldType.Text,
                Required = true,
                Help = "Pfad der Gruppe, z. B. intern oder intern/docs.",
            },
            new ConnectorField
            {
                Key = IncludeGlobsField,
                Label = "Pfad-Globs",
                Type = ConnectorFieldType.TextList,
                Help = "Ein Glob pro Zeile (z. B. docs/** oder src/**/*.cs). Leer = alle Code- und Doku-Dateien.",
            },
            new ConnectorField
            {
                Key = TokenField,
                Label = "Access-Token",
                Type = ConnectorFieldType.Secret,
                Help = "PRIVATE-TOKEN mit read_api/read_repository; verschlüsselt gespeichert.",
            },
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
        var baseUrl = Value(instance, BaseUrlField);
        var group = Value(instance, GroupField);
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(group))
        {
            return new IngestionResult
            {
                Failed = 1,
                Errors = [new IngestionError { ItemId = instance.Id, Message = "GitLab-URL und Gruppenpfad sind erforderlich." }],
            };
        }

        var globs = SplitLines(Value(instance, IncludeGlobsField));
        var enrich = string.Equals(Value(instance, EnrichField), "true", StringComparison.OrdinalIgnoreCase);

        var userId = _identity.UserId ?? "local";
        var token = await _credentials
            .RetrieveAsync($"{userId}:source:{instance.Id}:{TokenField}", cancellationToken).ConfigureAwait(false);

        var request = new IngestionRequest
        {
            SourceKind = "gitlab-group",
            Source = new IngestionSourceConfig
            {
                IncludeGlobs = globs,
                Token = token,
                Settings = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [GitLabGroupSource.BaseUrlSettingKey] = baseUrl,
                    [GitLabGroupSource.GroupSettingKey] = group,
                },
            },
            EnableEnrichment = enrich,
        };

        return await _pipeline.IngestAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static string? Value(ConnectorInstanceConfig instance, string key) =>
        instance.Values.TryGetValue(key, out var value) ? value : null;

    private static IReadOnlyList<string> SplitLines(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
