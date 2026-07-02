namespace Edda.Agent.Tdk;

/// <summary>
/// Decides whether a TDK validator rule applies to a code block of a given language, so the engine can
/// skip (rule × block) pairs the rule does not target <em>before</em> starting a sandbox (issue F9).
/// Matching is alias- and case-insensitive (e.g. <c>py</c> == <c>python</c>, <c>cs</c>/<c>c#</c> == <c>csharp</c>).
/// </summary>
public static class TdkLanguageMatcher
{
    /// <summary>Common fenced-code-block language aliases mapped to a canonical, lower-case language id.</summary>
    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["py"] = "python",
        ["python3"] = "python",
        ["cs"] = "csharp",
        ["c#"] = "csharp",
        ["js"] = "javascript",
        ["node"] = "javascript",
        ["ts"] = "typescript",
        ["sh"] = "bash",
        ["shell"] = "bash",
        ["zsh"] = "bash",
    };

    /// <summary>
    /// Normalises a language identifier to a canonical, lower-case form with common aliases resolved.
    /// </summary>
    /// <param name="language">A raw fence language (e.g. <c>"py"</c>, <c>"C#"</c>) or <see langword="null"/>.</param>
    /// <returns>The canonical language id (e.g. <c>"python"</c>, <c>"csharp"</c>); empty string when none was given.</returns>
    public static string Canonicalize(string? language)
    {
        var lang = language?.Trim().ToLowerInvariant() ?? string.Empty;
        return Aliases.TryGetValue(lang, out var canonical) ? canonical : lang;
    }

    /// <summary>
    /// Determines whether a rule targeting the <paramref name="appliesTo"/> languages should run against a
    /// code block written in <paramref name="blockLanguage"/>.
    /// </summary>
    /// <param name="appliesTo">
    /// The languages the rule targets. Empty (or <see langword="null"/>) means "any language" — the
    /// validator always runs, preserving the pre-F9 behaviour.
    /// </param>
    /// <param name="blockLanguage">The code block's fence language (may be empty for an unlabelled block).</param>
    /// <returns>
    /// <see langword="true"/> when the rule targets no specific language, the block carries no language, or
    /// the block language matches one of the targeted languages; otherwise <see langword="false"/>.
    /// </returns>
    public static bool Applies(IReadOnlyList<string>? appliesTo, string blockLanguage)
    {
        if (appliesTo is null || appliesTo.Count == 0)
            return true;

        var lang = Canonicalize(blockLanguage);
        if (lang.Length == 0)
            return true; // an unlabelled block is not identified as foreign — validate to avoid misses

        foreach (var target in appliesTo)
        {
            if (Canonicalize(target) == lang)
                return true;
        }

        return false;
    }
}
