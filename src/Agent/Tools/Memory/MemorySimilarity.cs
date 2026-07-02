namespace Edda.Agent.Tools.Memory;

/// <summary>
/// Token-overlap similarity for episodic-memory contradiction/dedup detection (C3, reused by C4). LLM-free:
/// compares the distinct lowercase alphanumeric tokens of two facts via the Jaccard coefficient.
/// </summary>
internal static class MemorySimilarity
{
    /// <summary>
    /// Splits <paramref name="content"/> into its distinct lowercase alphanumeric tokens on non-alphanumeric
    /// boundaries (Unicode-aware via <see cref="char.IsLetterOrDigit(char)"/>).
    /// </summary>
    /// <param name="content">The text to tokenise.</param>
    /// <returns>The distinct tokens.</returns>
    public static HashSet<string> Tokenize(string content)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var start = -1;
        for (var i = 0; i < content.Length; i++)
        {
            if (char.IsLetterOrDigit(content[i]))
            {
                if (start < 0) start = i;
            }
            else if (start >= 0)
            {
                tokens.Add(content[start..i].ToLowerInvariant());
                start = -1;
            }
        }

        if (start >= 0)
            tokens.Add(content[start..].ToLowerInvariant());

        return tokens;
    }

    /// <summary>
    /// Jaccard overlap <c>|A∩B| / |A∪B|</c> of the two token sets. Returns 0 when both texts are empty.
    /// </summary>
    /// <param name="a">First text.</param>
    /// <param name="b">Second text.</param>
    /// <returns>A similarity in [0, 1].</returns>
    public static double Jaccard(string a, string b)
    {
        var ta = Tokenize(a);
        var tb = Tokenize(b);
        if (ta.Count == 0 && tb.Count == 0)
            return 0.0;

        var intersection = ta.Count(tb.Contains);
        var union = ta.Count + tb.Count - intersection;
        return union == 0 ? 0.0 : (double)intersection / union;
    }
}
