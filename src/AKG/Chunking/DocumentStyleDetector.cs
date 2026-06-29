namespace Edda.AKG.Chunking;

/// <summary>
/// Deterministically classifies a document's overall <see cref="DocumentStyle"/> from an optional file-name
/// hint (extension) and content heuristics. Used to pick separator profiles; fine-grained structure
/// (fenced code, tables) is handled per-block by <see cref="BlockSegmenter"/> regardless of the overall style.
/// </summary>
internal static class DocumentStyleDetector
{
    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".py", ".js", ".ts", ".tsx", ".jsx", ".java", ".go", ".rb", ".rs", ".cpp", ".cc", ".c",
        ".h", ".hpp", ".php", ".sql", ".sh", ".ps1", ".kt", ".swift", ".scala", ".r", ".m", ".lua",
    };

    private static readonly HashSet<string> MarkupExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown", ".mdx",
    };

    /// <summary>Parses an explicit style name to a <see cref="DocumentStyle"/>, or null if unknown/blank.</summary>
    /// <param name="style">Style name: <c>prose</c> | <c>markdown</c> | <c>code</c> | <c>table</c> (case-insensitive).</param>
    /// <returns>The matching style, or null to fall back to auto-detection.</returns>
    public static DocumentStyle? TryParse(string? style) => style?.Trim().ToLowerInvariant() switch
    {
        "prose" => DocumentStyle.Prose,
        "markdown" => DocumentStyle.Markdown,
        "code" => DocumentStyle.Code,
        "table" => DocumentStyle.Table,
        _ => null,
    };

    /// <summary>Classifies the document style.</summary>
    /// <param name="text">The document body.</param>
    /// <param name="fileNameHint">Optional file name/path; its extension is the strongest signal.</param>
    /// <returns>The detected style.</returns>
    public static DocumentStyle Detect(string text, string? fileNameHint)
    {
        var extension = ExtensionOf(fileNameHint);
        if (extension is not null && CodeExtensions.Contains(extension))
            return DocumentStyle.Code;

        var lines = text.Split('\n');
        var nonEmpty = lines.Where(l => l.Trim().Length > 0).ToList();
        if (nonEmpty.Count == 0)
            return DocumentStyle.Prose;

        var tableLines = nonEmpty.Count(l => l.TrimStart().StartsWith('|'));
        if (tableLines >= 3 && tableLines >= nonEmpty.Count * 0.5)
            return DocumentStyle.Table;

        var hasHeadings = nonEmpty.Any(l => l.TrimStart().StartsWith('#'));
        var hasFences = CountOccurrences(text, "```") >= 2;
        if (extension is not null && MarkupExtensions.Contains(extension))
            return DocumentStyle.Markdown;
        if (hasHeadings || hasFences || tableLines > 0)
            return DocumentStyle.Markdown;

        // No hint, no Markdown structure → decide between raw code and prose by code signals.
        if (extension is null && LooksLikeCode(nonEmpty))
            return DocumentStyle.Code;

        return DocumentStyle.Prose;
    }

    private static bool LooksLikeCode(IReadOnlyList<string> nonEmptyLines)
    {
        var signals = 0;
        foreach (var raw in nonEmptyLines)
        {
            var line = raw.Trim();
            if (line.EndsWith(';') || line.EndsWith('{') || line.EndsWith('}')
                || line.StartsWith("import ") || line.StartsWith("from ") || line.StartsWith("#include")
                || line.StartsWith("def ") || line.StartsWith("class ") || line.StartsWith("function ")
                || line.StartsWith("public ") || line.StartsWith("private ") || line.StartsWith("const ")
                || line.StartsWith("var ") || line.StartsWith("let ") || line.StartsWith("func "))
            {
                signals++;
            }
        }

        return signals >= Math.Max(3, nonEmptyLines.Count * 0.4);
    }

    private static string? ExtensionOf(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;
        var dot = fileName.LastIndexOf('.');
        return dot >= 0 ? fileName[dot..].ToLowerInvariant() : null;
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var start = 0;
        int idx;
        while ((idx = text.IndexOf(token, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start = idx + token.Length;
        }

        return count;
    }
}
