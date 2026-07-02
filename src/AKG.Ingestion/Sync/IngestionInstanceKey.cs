using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Sync;

/// <summary>
/// Derives a stable, per-source-instance key for the incremental-sync manifest (C5) so distinct
/// repositories / APIs keep independent state. Pure and deterministic.
/// </summary>
internal static class IngestionInstanceKey
{
    /// <summary>Builds the instance key from the request's source kind and its distinguishing config.</summary>
    /// <param name="request">The ingestion request.</param>
    /// <returns>A stable key of the form <c>sourceKind|identity</c>.</returns>
    public static string For(IngestionRequest request)
    {
        var c = request.Source;
        var identity = FirstNonEmpty(
            c.CanonicalUrl,
            c.RepositoryUrl,
            Join(Setting(c, "baseUrl"), Setting(c, "listPath"), Setting(c, "sourceLabel")));
        return $"{request.SourceKind}|{identity}";
    }

    private static string? Setting(IngestionSourceConfig c, string key)
        => c.Settings.TryGetValue(key, out var v) ? v : null;

    private static string Join(params string?[] parts)
        => string.Join("|", parts.Where(p => !string.IsNullOrEmpty(p)));

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? string.Empty;
}
