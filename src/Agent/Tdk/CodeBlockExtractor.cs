using System.Text.RegularExpressions;

namespace Edda.Agent.Tdk;

/// <summary>
/// Extracts fenced code blocks from markdown text.
/// Supports blocks with and without a language identifier.
/// </summary>
/// <remarks>
/// Matches the pattern <c>```{language}\n{code}\n```</c>.
/// Recognised languages: python, csharp, cs, javascript, js, typescript, ts, bash, sh, sql, yaml, json.
/// Also matches unlabelled blocks (<c>``` ... ```</c>).
/// </remarks>
public static class CodeBlockExtractor
{
    // Pattern: opening ```, optional language identifier, newline, code content, closing ```
    private static readonly Regex CodeBlockPattern = new(
        @"```(?<lang>[a-zA-Z0-9_+-]*)[ \t]*\r?\n(?<code>[\s\S]*?)```",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Extracts all fenced code blocks from <paramref name="markdown"/>.
    /// </summary>
    /// <param name="markdown">The markdown text to search.</param>
    /// <returns>
    /// An ordered list of <see cref="CodeBlock"/> instances.
    /// Returns an empty list when no code blocks are found.
    /// </returns>
    public static IReadOnlyList<CodeBlock> Extract(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return [];

        var matches = CodeBlockPattern.Matches(markdown);
        var result = new List<CodeBlock>(matches.Count);

        foreach (Match m in matches)
        {
            result.Add(new CodeBlock(
                Language: m.Groups["lang"].Value.ToLowerInvariant(),
                Code: m.Groups["code"].Value));
        }

        return result;
    }
}

/// <summary>
/// A fenced code block extracted from markdown text.
/// </summary>
/// <param name="Language">
/// The language identifier in lower case (e.g. "python", "csharp").
/// Empty string when no language was specified.
/// </param>
/// <param name="Code">The raw code content, preserving all whitespace and newlines.</param>
public sealed record CodeBlock(string Language, string Code);
