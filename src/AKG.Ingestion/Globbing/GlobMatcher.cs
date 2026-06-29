using System.Text;
using System.Text.RegularExpressions;

namespace Edda.AKG.Ingestion.Globbing;

/// <summary>
/// Minimal, dependency-free glob matcher for relative paths. Supports <c>*</c> (any run of
/// non-separator characters), <c>**</c> (any characters including separators) and <c>?</c> (a single
/// non-separator character). Matching is case-insensitive and operates on '/'-separated paths.
/// </summary>
public static class GlobMatcher
{
    /// <summary>
    /// Returns true when <paramref name="path"/> matches the glob <paramref name="glob"/>.
    /// </summary>
    /// <param name="glob">The glob pattern (e.g. <c>docs/**</c>, <c>**/*.md</c>).</param>
    /// <param name="path">The relative path to test (e.g. <c>docs/adr/0001-foo.md</c>).</param>
    /// <returns>True if the path matches the pattern; otherwise false.</returns>
    public static bool IsMatch(string glob, string path)
    {
        if (string.IsNullOrEmpty(glob))
            return false;

        var pattern = ToRegex(glob);
        return Regex.IsMatch(path.Replace('\\', '/'), pattern, RegexOptions.IgnoreCase);
    }

    private static string ToRegex(string glob)
    {
        var normalized = glob.Replace('\\', '/');
        var sb = new StringBuilder("^");

        for (var i = 0; i < normalized.Length; i++)
        {
            var c = normalized[i];
            switch (c)
            {
                case '*':
                    if (i + 1 < normalized.Length && normalized[i + 1] == '*')
                    {
                        // "**" matches any characters including '/'.
                        sb.Append(".*");
                        i++;
                        // Treat "**/" as a single "match any prefix" token.
                        if (i + 1 < normalized.Length && normalized[i + 1] == '/')
                            i++;
                    }
                    else
                    {
                        // "*" matches any run of non-separator characters.
                        sb.Append("[^/]*");
                    }

                    break;
                case '?':
                    sb.Append("[^/]");
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }

        sb.Append('$');
        return sb.ToString();
    }
}
