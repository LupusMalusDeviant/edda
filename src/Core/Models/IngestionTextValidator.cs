namespace Edda.Core.Models;

/// <summary>
/// Validation for free text submitted to the entity-ingestion endpoint. Guards against empty payloads
/// (nothing to extract) and oversized texts that would waste LLM budget.
/// </summary>
public static class IngestionTextValidator
{
    /// <summary>Default maximum accepted text length in characters (used when unconfigured).</summary>
    public const int DefaultMaxChars = 20000;

    /// <summary>
    /// Resolves the effective maximum text length from a raw configuration value (for example the
    /// <c>INGESTION_MAX_TEXT_CHARS</c> environment variable). A null, empty, non-numeric, or
    /// non-positive value falls back to <see cref="DefaultMaxChars"/>.
    /// </summary>
    /// <param name="rawMaxChars">The raw configuration value.</param>
    /// <returns>The effective maximum character count.</returns>
    public static int ResolveMaxChars(string? rawMaxChars)
        => int.TryParse(rawMaxChars, out var parsed) && parsed > 0 ? parsed : DefaultMaxChars;

    /// <summary>
    /// Validates ingestion text against the given maximum length.
    /// </summary>
    /// <param name="text">The submitted text.</param>
    /// <param name="maxChars">The maximum accepted length in characters.</param>
    /// <returns>
    /// A caller-facing error message when the text is <see langword="null"/>, empty, whitespace-only, or
    /// longer than <paramref name="maxChars"/>; otherwise <see langword="null"/> (valid).
    /// </returns>
    public static string? Validate(string? text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Text must not be empty.";
        }

        if (text.Length > maxChars)
        {
            return $"Text must not exceed {maxChars} characters (was {text.Length}).";
        }

        return null;
    }
}
