using System.Text;

namespace Edda.AKG.Chunking;

/// <summary>
/// Character-based recursive text splitter (the well-known LangChain/HuggingFace
/// <c>RecursiveCharacterTextSplitter</c> approach): it breaks text on a priority-ordered list of separators,
/// descending to finer separators only for parts that are still too large, then greedily merges the parts
/// into chunks that stay within the size limit, with a configurable overlap between adjacent chunks.
/// Separators are kept attached to their preceding part so the concatenation of all parts is lossless.
/// </summary>
internal static class RecursiveTextSplitter
{
    /// <summary>Splits <paramref name="text"/> into chunks no larger than <paramref name="maxChars"/>.</summary>
    /// <param name="text">The text to split.</param>
    /// <param name="maxChars">The maximum chunk size in characters (must be positive).</param>
    /// <param name="overlap">Overlap between adjacent chunks in characters (0 to disable).</param>
    /// <param name="separators">Priority-ordered separators; an empty trailing entry enables character wrap.</param>
    /// <returns>The resulting chunks in order; each is at most <paramref name="maxChars"/> characters.</returns>
    public static List<string> Split(string text, int maxChars, int overlap, IReadOnlyList<string> separators)
    {
        var units = BreakIntoUnits(text, maxChars, separators, 0);
        return Merge(units, maxChars, overlap);
    }

    private static List<string> BreakIntoUnits(string text, int maxChars, IReadOnlyList<string> seps, int level)
    {
        var result = new List<string>();
        if (text.Length == 0)
            return result;
        if (text.Length <= maxChars)
        {
            result.Add(text);
            return result;
        }

        if (level >= seps.Count)
        {
            result.AddRange(HardWrap(text, maxChars));
            return result;
        }

        foreach (var part in SplitKeepSeparator(text, seps[level]))
        {
            if (part.Length == 0)
                continue;
            if (part.Length <= maxChars)
                result.Add(part);
            else
                result.AddRange(BreakIntoUnits(part, maxChars, seps, level + 1));
        }

        return result;
    }

    private static IEnumerable<string> SplitKeepSeparator(string text, string separator)
    {
        if (separator.Length == 0)
        {
            foreach (var ch in text)
                yield return ch.ToString();
            yield break;
        }

        var start = 0;
        int idx;
        while ((idx = text.IndexOf(separator, start, StringComparison.Ordinal)) >= 0)
        {
            yield return text[start..(idx + separator.Length)];
            start = idx + separator.Length;
        }

        if (start < text.Length)
            yield return text[start..];
    }

    private static IEnumerable<string> HardWrap(string text, int maxChars)
    {
        for (var i = 0; i < text.Length; i += maxChars)
            yield return text.Substring(i, Math.Min(maxChars, text.Length - i));
    }

    private static List<string> Merge(List<string> units, int maxChars, int overlap)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var unit in units)
        {
            if (current.Length > 0 && current.Length + unit.Length > maxChars)
            {
                var emitted = current.ToString();
                chunks.Add(emitted);
                current.Clear();

                if (overlap > 0)
                {
                    var tail = Tail(emitted, overlap);
                    if (tail.Length + unit.Length <= maxChars)
                        current.Append(tail);
                }
            }

            current.Append(unit);
        }

        if (current.Length > 0)
            chunks.Add(current.ToString());

        return chunks;
    }

    private static string Tail(string value, int count)
        => value.Length <= count ? value : value[^count..];
}
