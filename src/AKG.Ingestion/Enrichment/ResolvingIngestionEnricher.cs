using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Configuration;

namespace Edda.AKG.Ingestion.Enrichment;

/// <summary>
/// <see cref="IIngestionEnricher"/> that applies the LLM enricher only when enrichment is enabled in the
/// current settings (with environment-variable fallback), and otherwise returns the item unchanged. This
/// lets enrichment be toggled at runtime without a restart (see ADR-0004).
/// </summary>
public sealed class ResolvingIngestionEnricher : IIngestionEnricher
{
    private const string EnricherEnvKey = "INGESTION_ENRICHER";
    private const string LlmMode = "llm";

    private readonly ISettingsService _settings;
    private readonly IConfiguration _configuration;
    private readonly LlmIngestionEnricher _llmEnricher;

    /// <summary>Initializes a new instance of the <see cref="ResolvingIngestionEnricher"/> class.</summary>
    /// <param name="settings">Source of the current LLM-enrichment settings.</param>
    /// <param name="configuration">Configuration used for environment-variable fallback.</param>
    /// <param name="llmEnricher">The LLM enricher delegated to when enrichment is enabled.</param>
    public ResolvingIngestionEnricher(
        ISettingsService settings,
        IConfiguration configuration,
        LlmIngestionEnricher llmEnricher)
    {
        _settings = settings;
        _configuration = configuration;
        _llmEnricher = llmEnricher;
    }

    /// <inheritdoc />
    public async Task<IngestionItem> EnrichAsync(
        IngestionItem item,
        IReadOnlyCollection<string> knownIds,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled())
            return item;

        return await _llmEnricher.EnrichAsync(item, knownIds, cancellationToken).ConfigureAwait(false);
    }

    private bool IsEnabled()
    {
        var enabled = _settings.Current.LlmEnrichment.Enabled;
        if (enabled.HasValue)
            return enabled.Value;

        return string.Equals(_configuration[EnricherEnvKey], LlmMode, StringComparison.OrdinalIgnoreCase);
    }
}
