using Edda.Core.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Edda.AKG.Ingestion.Llm;

/// <summary>
/// <see cref="ILlmChatClient"/> that resolves the active provider, model and credentials at call time
/// from <see cref="ISettingsService"/> and <see cref="ICredentialStore"/> (with environment-variable
/// fallback), then delegates to a concrete client built by <see cref="ILlmChatClientFactory"/>. This is
/// what lets provider and key changes take effect without a process restart (see ADR-0004).
/// </summary>
public sealed class ResolvingLlmChatClient : ILlmChatClient
{
    private const string ProviderEnvKey = "INGESTION_LLM_PROVIDER";
    private const string ModelEnvKey = "INGESTION_LLM_MODEL";
    private const string BaseUrlEnvKey = "INGESTION_LLM_BASE_URL";
    private const string ApiKeyEnvKey = "INGESTION_LLM_API_KEY";
    private const string DefaultProvider = "openrouter";

    private readonly ISettingsService _settings;
    private readonly ICredentialStore _credentials;
    private readonly ILlmChatClientFactory _factory;
    private readonly IIdentityContext _identity;
    private readonly IConfiguration _configuration;

    /// <summary>Initializes a new instance of the <see cref="ResolvingLlmChatClient"/> class.</summary>
    /// <param name="settings">Source of the current LLM-enrichment settings.</param>
    /// <param name="credentials">Encrypted store the API key is resolved from.</param>
    /// <param name="factory">Factory that builds the concrete client for the resolved provider.</param>
    /// <param name="identity">Identity context used to scope credential keys.</param>
    /// <param name="configuration">Configuration used for environment-variable fallback.</param>
    public ResolvingLlmChatClient(
        ISettingsService settings,
        ICredentialStore credentials,
        ILlmChatClientFactory factory,
        IIdentityContext identity,
        IConfiguration configuration)
    {
        _settings = settings;
        _credentials = credentials;
        _factory = factory;
        _identity = identity;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public string ProviderName => ResolveProvider();

    /// <inheritdoc />
    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var config = await ResolveConfigAsync(cancellationToken).ConfigureAwait(false);
        var client = _factory.Create(config);
        return await client.CompleteAsync(systemPrompt, userPrompt, cancellationToken).ConfigureAwait(false);
    }

    private string ResolveProvider()
    {
        var settings = _settings.Current.LlmEnrichment;
        return FirstNonEmpty(settings.Provider, _configuration[ProviderEnvKey]) ?? DefaultProvider;
    }

    private async Task<LlmProviderConfig> ResolveConfigAsync(CancellationToken cancellationToken)
    {
        var settings = _settings.Current.LlmEnrichment;
        var provider = ResolveProvider();
        var userId = _identity.UserId ?? "local";

        var apiKey = await _credentials
                .RetrieveAsync($"{userId}:llm:{provider}", cancellationToken).ConfigureAwait(false)
            ?? _configuration[ApiKeyEnvKey];
        var accessKeyId = await _credentials
            .RetrieveAsync($"{userId}:llm:{provider}:accesskey", cancellationToken).ConfigureAwait(false);

        return new LlmProviderConfig
        {
            Provider = provider,
            Model = FirstNonEmpty(settings.Model, _configuration[ModelEnvKey]),
            BaseUrl = FirstNonEmpty(settings.BaseUrl, _configuration[BaseUrlEnvKey]),
            Region = settings.Region,
            ApiKey = apiKey,
            AccessKeyId = accessKeyId,
        };
    }

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
