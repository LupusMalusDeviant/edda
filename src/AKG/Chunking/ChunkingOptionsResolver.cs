using Edda.Core.Models;
using Microsoft.Extensions.Configuration;

namespace Edda.AKG.Chunking;

/// <summary>
/// Resolves the effective <see cref="ChunkingOptions"/> from persisted <see cref="ChunkingSettings"/> with
/// environment-variable fallback (<c>CHUNKING_ENABLED</c>, <c>CHUNKING_MAX_CHARS</c>,
/// <c>CHUNKING_OVERLAP_CHARS</c>) and the built-in defaults. Mirrors the resolving-facade pattern used for
/// embeddings (ADR-0004): UI settings win, then environment, then defaults.
/// </summary>
internal static class ChunkingOptionsResolver
{
    /// <summary>Computes the effective chunking options.</summary>
    /// <param name="settings">The persisted chunking settings (nullable fields fall back to env/defaults).</param>
    /// <param name="configuration">Configuration used for the <c>CHUNKING_*</c> environment fallback (nullable).</param>
    /// <returns>The resolved options.</returns>
    public static ChunkingOptions Resolve(ChunkingSettings settings, IConfiguration? configuration)
    {
        var enabled = settings.Enabled
            ?? (!bool.TryParse(configuration?["CHUNKING_ENABLED"], out var parsedEnabled) || parsedEnabled);
        var maxChars = settings.MaxChars
            ?? (int.TryParse(configuration?["CHUNKING_MAX_CHARS"], out var parsedMax)
                ? parsedMax
                : ChunkingOptions.DefaultMaxChars);
        var overlap = settings.OverlapChars
            ?? (int.TryParse(configuration?["CHUNKING_OVERLAP_CHARS"], out var parsedOverlap)
                ? parsedOverlap
                : ChunkingOptions.DefaultOverlapChars);

        return new ChunkingOptions
        {
            Enabled = enabled,
            MaxChars = maxChars > 0 ? maxChars : ChunkingOptions.DefaultMaxChars,
            OverlapChars = Math.Max(0, overlap),
        };
    }
}
