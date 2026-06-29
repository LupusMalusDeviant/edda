using System.Security.Cryptography;
using System.Text;
using Edda.Core.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Edda.Embeddings;

/// <summary>
/// <see cref="IEmbeddingService"/> that resolves the embedding provider, model and key at call time from
/// <see cref="ISettingsService"/> and <see cref="ICredentialStore"/> (with environment-variable fallback),
/// then delegates to a concrete service built by <see cref="IEmbeddingProviderFactory"/>. A single instance
/// serves both DB indexing and query embedding, so stored and query vectors always share one space.
/// Provider/model changes take effect for query embedding immediately; stored DB embeddings remain until a
/// re-embed (see ADR-0004). The built service is cached and rebuilt only when the configuration changes.
/// </summary>
public sealed class ResolvingEmbeddingService : IEmbeddingService
{
    private const string ProviderEnvKey = "EMBEDDING_PROVIDER";
    private const string ModelEnvKey = "EMBEDDING_MODEL";
    private const string BaseUrlEnvKey = "EMBEDDING_BASE_URL";
    private const string DimensionsEnvKey = "EMBEDDING_DIMENSIONS";
    private const string ApiKeyConfigKey = "Embeddings:ApiKey";
    private const string ApiKeyEnvKey = "EMBEDDING_API_KEY";
    private const string RegionEnvKey = "EMBEDDING_REGION";
    private const string AccessKeyIdEnvKey = "EMBEDDING_ACCESS_KEY_ID";

    private readonly ISettingsService _settings;
    private readonly ICredentialStore _credentials;
    private readonly IEmbeddingProviderFactory _factory;
    private readonly IIdentityContext _identity;
    private readonly IConfiguration _configuration;
    private readonly object _gate = new();

    private IEmbeddingService? _metadata;
    private string? _metadataSignature;
    private IEmbeddingService? _keyed;
    private string? _keyedSignature;

    /// <summary>Initializes a new instance of the <see cref="ResolvingEmbeddingService"/> class.</summary>
    /// <param name="settings">Source of the current embedding settings.</param>
    /// <param name="credentials">Encrypted store the API key is resolved from.</param>
    /// <param name="factory">Factory that builds the concrete service for the resolved provider.</param>
    /// <param name="identity">Identity context used to scope credential keys.</param>
    /// <param name="configuration">Configuration used for environment-variable fallback.</param>
    public ResolvingEmbeddingService(
        ISettingsService settings,
        ICredentialStore credentials,
        IEmbeddingProviderFactory factory,
        IIdentityContext identity,
        IConfiguration configuration)
    {
        _settings = settings;
        _credentials = credentials;
        _factory = factory;
        _identity = identity;
        _configuration = configuration;
        _settings.Changed += OnSettingsChanged;
    }

    /// <inheritdoc />
    public int Dimensions => Metadata().Dimensions;

    /// <inheritdoc />
    public bool IsAvailable => Metadata().IsAvailable;

    /// <inheritdoc />
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var service = await KeyedAsync(cancellationToken).ConfigureAwait(false);
        return await service.EmbedAsync(text, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        var service = await KeyedAsync(cancellationToken).ConfigureAwait(false);
        return await service.EmbedBatchAsync(texts, cancellationToken).ConfigureAwait(false);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            _metadata = null;
            _metadataSignature = null;
            _keyed = null;
            _keyedSignature = null;
        }
    }

    private (string Provider, string? Model, string? BaseUrl, int? Dimensions, string? Region) ResolveCore()
    {
        var settings = _settings.Current.Embedding;
        var provider = (FirstNonEmpty(settings.Provider, _configuration[ProviderEnvKey]) ?? "null")
            .Trim().ToLowerInvariant();
        var model = FirstNonEmpty(settings.Model, _configuration[ModelEnvKey]);
        var baseUrl = FirstNonEmpty(settings.BaseUrl, _configuration[BaseUrlEnvKey]);
        var dimensions = settings.Dimensions
            ?? (int.TryParse(_configuration[DimensionsEnvKey], out var parsed) ? parsed : (int?)null);
        var region = FirstNonEmpty(settings.Region, _configuration[RegionEnvKey]);
        return (provider, model, baseUrl, dimensions, region);
    }

    /// <summary>
    /// Returns a key-less service used only for the synchronous <see cref="Dimensions"/> and
    /// <see cref="IsAvailable"/> properties (neither depends on the API key). Cached per configuration.
    /// </summary>
    private IEmbeddingService Metadata()
    {
        var (provider, model, baseUrl, dimensions, region) = ResolveCore();
        var signature = $"{provider}|{model}|{baseUrl}|{dimensions}|{region}";
        lock (_gate)
        {
            if (_metadata is null || _metadataSignature != signature)
            {
                _metadata = _factory.Create(new EmbeddingProviderConfig
                {
                    Provider = provider,
                    Model = model,
                    BaseUrl = baseUrl,
                    Dimensions = dimensions,
                    Region = region,
                });
                _metadataSignature = signature;
            }

            return _metadata;
        }
    }

    /// <summary>Resolves the keyed service used for actual embedding calls. Cached per configuration + key.</summary>
    private async Task<IEmbeddingService> KeyedAsync(CancellationToken cancellationToken)
    {
        var (provider, model, baseUrl, dimensions, region) = ResolveCore();
        var apiKey = await ResolveApiKeyAsync(provider, cancellationToken).ConfigureAwait(false);
        var accessKeyId = await ResolveAccessKeyIdAsync(provider, cancellationToken).ConfigureAwait(false);
        var signature = $"{provider}|{model}|{baseUrl}|{dimensions}|{region}|{Hash(apiKey)}|{Hash(accessKeyId)}";

        lock (_gate)
        {
            if (_keyed is not null && _keyedSignature == signature)
                return _keyed;
        }

        var built = _factory.Create(new EmbeddingProviderConfig
        {
            Provider = provider,
            Model = model,
            BaseUrl = baseUrl,
            Dimensions = dimensions,
            Region = region,
            ApiKey = apiKey,
            AccessKeyId = accessKeyId,
        });

        lock (_gate)
        {
            _keyed = built;
            _keyedSignature = signature;
        }

        return built;
    }

    private async Task<string?> ResolveApiKeyAsync(string provider, CancellationToken cancellationToken)
    {
        var userId = _identity.UserId ?? "local";
        var stored = await _credentials
            .RetrieveAsync($"{userId}:embed:{provider}", cancellationToken).ConfigureAwait(false);
        return FirstNonEmpty(stored, _configuration[ApiKeyConfigKey], _configuration[ApiKeyEnvKey]);
    }

    /// <summary>
    /// Resolves the AWS access key id for the Bedrock provider from the credential store
    /// (<c>{userId}:embed:{provider}:accesskey</c>), falling back to <c>EMBEDDING_ACCESS_KEY_ID</c>. Null
    /// (with a null secret) lets the AWS SDK fall back to its default credential chain.
    /// </summary>
    private async Task<string?> ResolveAccessKeyIdAsync(string provider, CancellationToken cancellationToken)
    {
        var userId = _identity.UserId ?? "local";
        var stored = await _credentials
            .RetrieveAsync($"{userId}:embed:{provider}:accesskey", cancellationToken).ConfigureAwait(false);
        return FirstNonEmpty(stored, _configuration[AccessKeyIdEnvKey]);
    }

    private static string Hash(string? value) =>
        value is null ? string.Empty : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8];

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}
