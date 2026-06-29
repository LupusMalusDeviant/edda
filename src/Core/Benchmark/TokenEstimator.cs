namespace Edda.Core.Benchmark;

/// <summary>
/// Heuristic token-count estimator for benchmark reporting. Approximates the token count of a
/// string as ⌈characters / 4⌉ — the common rule of thumb for GPT-style BPE tokenizers.
/// <para>
/// This is an <b>estimate</b> for relative comparison (e.g. tokens-per-query across retrieval
/// configurations), not a model-exact count. It requires no model or network call, keeping the
/// benchmark deterministic and infrastructure-free (Regel 9).
/// </para>
/// </summary>
public static class TokenEstimator
{
    private const double CharsPerToken = 4.0;

    /// <summary>
    /// Estimates the number of tokens in <paramref name="text"/> as ⌈length / 4⌉.
    /// </summary>
    /// <param name="text">The text to estimate. Null or empty yields 0.</param>
    /// <returns>The estimated token count (0 for null/empty input).</returns>
    public static int Estimate(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return (int)Math.Ceiling(text.Length / CharsPerToken);
    }
}
