using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Connectors;

/// <summary>
/// <see cref="IKnowledgeConnector"/> for Git repositories. Translates a configured instance into a Git
/// ingestion run over the existing pipeline, resolving the per-instance access token server-side from the
/// credential store (key <c>{userId}:source:{instanceId}:token</c>) so it is never taken from a request.
/// </summary>
public sealed class GitKnowledgeConnector : IKnowledgeConnector
{
    private const string RepoUrlField = "repoUrl";
    private const string ReferenceField = "reference";
    private const string IncludeGlobsField = "includeGlobs";
    private const string EnrichField = "enrich";
    private const string TokenField = "token";

    private readonly IIngestionPipeline _pipeline;
    private readonly ICredentialStore _credentials;
    private readonly IIdentityContext _identity;

    /// <summary>Initializes a new instance of the <see cref="GitKnowledgeConnector"/> class.</summary>
    /// <param name="pipeline">The ingestion pipeline the run is delegated to.</param>
    /// <param name="credentials">Credential store the per-instance token is resolved from.</param>
    /// <param name="identity">Identity context used to scope the credential key.</param>
    public GitKnowledgeConnector(
        IIngestionPipeline pipeline,
        ICredentialStore credentials,
        IIdentityContext identity)
    {
        _pipeline = pipeline;
        _credentials = credentials;
        _identity = identity;
    }

    /// <inheritdoc />
    public string TypeId => "git";

    /// <inheritdoc />
    public ConnectorDescriptor Describe() => new()
    {
        TypeId = TypeId,
        DisplayName = "Git-Repository",
        Description = "Klont ein Git-Repository und ingestiert dessen Markdown- und Code-Dateien.",
        Fields =
        [
            new ConnectorField
            {
                Key = RepoUrlField,
                Label = "Repository-URL",
                Type = ConnectorFieldType.Url,
                Required = true,
                Help = "HTTPS-Clone-URL des Repositories.",
            },
            new ConnectorField
            {
                Key = ReferenceField,
                Label = "Branch/Tag",
                Type = ConnectorFieldType.Text,
                Help = "Leer = Default-Branch.",
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
                Help = "Für private Repositories; verschlüsselt gespeichert.",
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
        var repoUrl = Value(instance, RepoUrlField);
        if (string.IsNullOrWhiteSpace(repoUrl))
        {
            return new IngestionResult
            {
                Failed = 1,
                Errors = [new IngestionError { ItemId = instance.Id, Message = "Repository-URL fehlt." }],
            };
        }

        var reference = Value(instance, ReferenceField);
        var globs = SplitLines(Value(instance, IncludeGlobsField));
        var enrich = string.Equals(Value(instance, EnrichField), "true", StringComparison.OrdinalIgnoreCase);

        var userId = _identity.UserId ?? "local";
        var token = await _credentials
            .RetrieveAsync($"{userId}:source:{instance.Id}:{TokenField}", cancellationToken).ConfigureAwait(false);

        var request = new IngestionRequest
        {
            SourceKind = "git",
            Source = new IngestionSourceConfig
            {
                RepositoryUrl = repoUrl,
                Reference = string.IsNullOrWhiteSpace(reference) ? null : reference,
                IncludeGlobs = globs,
                Token = token,
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
