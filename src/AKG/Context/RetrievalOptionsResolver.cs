using System.Globalization;
using Edda.Core.Models;
using Microsoft.Extensions.Configuration;

namespace Edda.AKG.Context;

/// <summary>
/// Resolves <see cref="RetrievalOptions"/> from the <c>RETRIEVAL_*</c> environment/configuration keys
/// (<c>RETRIEVAL_SIMILARITY_THRESHOLD</c>, <c>RETRIEVAL_VECTOR_TOP_K</c>, <c>RETRIEVAL_MMR_LAMBDA</c>,
/// <c>RETRIEVAL_MMR_TOP_N</c>, <c>RETRIEVAL_HEAD_THRESHOLD</c>), falling back to the built-in defaults —
/// the historical hard-coded values — for any key that is unset, non-numeric, or non-positive. Mirrors
/// the resolving-facade pattern used for chunking (ADR-0004). Numbers are parsed with the invariant
/// culture so a decimal point is accepted regardless of the host locale.
/// </summary>
internal static class RetrievalOptionsResolver
{
    /// <summary>Computes the effective retrieval options.</summary>
    /// <param name="configuration">Configuration providing the <c>RETRIEVAL_*</c> keys (nullable).</param>
    /// <returns>The resolved options.</returns>
    public static RetrievalOptions Resolve(IConfiguration? configuration) => new()
    {
        SimilarityThreshold = ParseDouble(
            configuration?["RETRIEVAL_SIMILARITY_THRESHOLD"], RetrievalOptions.DefaultSimilarityThreshold),
        VectorTopK = ParsePositiveInt(
            configuration?["RETRIEVAL_VECTOR_TOP_K"], RetrievalOptions.DefaultVectorTopK),
        MmrLambda = ParseDouble(
            configuration?["RETRIEVAL_MMR_LAMBDA"], RetrievalOptions.DefaultMmrLambda),
        MmrTopN = ParsePositiveInt(
            configuration?["RETRIEVAL_MMR_TOP_N"], RetrievalOptions.DefaultMmrTopN),
        HeadSimilarityThreshold = ParseDouble(
            configuration?["RETRIEVAL_HEAD_THRESHOLD"], RetrievalOptions.DefaultHeadSimilarityThreshold),
    };

    private static double ParseDouble(string? raw, double fallback)
        => double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static int ParsePositiveInt(string? raw, int fallback)
        => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : fallback;
}
