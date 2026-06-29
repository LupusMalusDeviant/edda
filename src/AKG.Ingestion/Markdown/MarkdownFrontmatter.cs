using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace Edda.AKG.Ingestion.Markdown;

/// <summary>
/// Tolerant Markdown scanning helpers used by the Git ingestion source. Unlike the strict AKG rule
/// parser, these never throw on missing or malformed frontmatter — they extract what is present and
/// leave the rest to deterministic conventions.
/// </summary>
public static class MarkdownFrontmatter
{
    private static readonly Regex MarkdownLinkRegex =
        new(@"\[[^\]]*\]\(([^)]+)\)", RegexOptions.Compiled);

    /// <summary>
    /// Splits a document into its YAML frontmatter (scalar <c>key: value</c> pairs only) and body.
    /// When no frontmatter block is present, the frontmatter map is empty and the body is the whole
    /// document.
    /// </summary>
    /// <param name="content">The full document content.</param>
    /// <returns>A tuple of the parsed scalar frontmatter and the remaining body.</returns>
    public static (IReadOnlyDictionary<string, string> Frontmatter, string Body) Split(string content)
    {
        if (string.IsNullOrEmpty(content))
            return (ReadOnlyDictionary<string, string>.Empty, string.Empty);

        var text = content.Replace("\r\n", "\n");
        var lines = text.Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
            return (ReadOnlyDictionary<string, string>.Empty, text);

        var end = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
            {
                end = i;
                break;
            }
        }

        if (end < 0)
            return (ReadOnlyDictionary<string, string>.Empty, text);

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < end; i++)
        {
            var line = lines[i];
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
                continue;

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (key.Length > 0 && value.Length > 0)
                map[key] = value;
        }

        var body = end + 1 < lines.Length ? string.Join("\n", lines[(end + 1)..]) : string.Empty;
        return (map, body);
    }

    /// <summary>
    /// Returns the text of the first level-1 heading (<c># …</c>) in the body, or null if none exists.
    /// </summary>
    /// <param name="body">The Markdown body.</param>
    /// <returns>The heading text without the leading <c># </c>, or null.</returns>
    public static string? FirstHeading(string body)
    {
        if (string.IsNullOrEmpty(body))
            return null;

        foreach (var raw in body.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimStart();
            if (line.StartsWith("# ", StringComparison.Ordinal))
                return line[2..].Trim();
        }

        return null;
    }

    /// <summary>
    /// Extracts the targets of Markdown links (<c>[text](target)</c>) that point at <c>.md</c> files.
    /// Anchors and link titles are stripped; order is preserved and duplicates are kept.
    /// </summary>
    /// <param name="body">The Markdown body.</param>
    /// <returns>The raw <c>.md</c> link targets found in the body.</returns>
    public static IReadOnlyList<string> MarkdownLinks(string body)
    {
        if (string.IsNullOrEmpty(body))
            return [];

        var targets = new List<string>();
        foreach (Match match in MarkdownLinkRegex.Matches(body))
        {
            var target = match.Groups[1].Value.Trim();

            var anchor = target.IndexOf('#', StringComparison.Ordinal);
            if (anchor >= 0)
                target = target[..anchor];

            var titleSpace = target.IndexOf(' ', StringComparison.Ordinal);
            if (titleSpace >= 0)
                target = target[..titleSpace];

            if (target.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                targets.Add(target);
        }

        return targets;
    }
}
