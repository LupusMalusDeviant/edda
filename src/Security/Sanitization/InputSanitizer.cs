using System.Text.RegularExpressions;
using Edda.Security.Models;

namespace Edda.Security.Sanitization;

/// <summary>
/// Sanitizes user input to prevent prompt injection attacks and enforce length limits.
/// All injection patterns are replaced with [FILTERED] and oversized inputs are truncated.
/// </summary>
public sealed class InputSanitizer : IInputSanitizer
{
    /// <summary>
    /// Maximum permitted input length in characters.
    /// Inputs exceeding this limit are truncated and appended with a truncation marker.
    /// </summary>
    private const int MaxInputLength = 32_000;

    /// <summary>Marker appended to inputs that exceed <see cref="MaxInputLength"/>.</summary>
    private const string TruncationMarker = " [TRUNCATED]";

    /// <summary>Replacement text inserted for matched injection patterns.</summary>
    private const string FilteredMarker = "[FILTERED]";

    /// <summary>
    /// Compiled regex patterns used to detect prompt injection attempts.
    /// Patterns are matched case-insensitively where appropriate.
    /// </summary>
    private static readonly Regex[] s_injectionPatterns =
    [
        new(@"ignore (all |previous |above )*instructions?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"disregard (all |previous |above )*instructions?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"forget (all |previous |above )*instructions?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"system prompt", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"you are now", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"act as (if )?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"pretend (you are|to be)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"new instructions?:", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"override (safety|instructions?)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"jailbreak", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"<\|.*?\|>", RegexOptions.Compiled),
        new(@"\[INST\]|\[/INST\]", RegexOptions.Compiled),
    ];

    /// <summary>
    /// Sanitizes the given input string by truncating oversized inputs and replacing
    /// known injection patterns with a safe placeholder.
    /// </summary>
    /// <param name="input">The raw user input to sanitize.</param>
    /// <returns>
    /// A <see cref="SanitizationResult"/> containing the sanitized text and a flag
    /// indicating whether any modification was made.
    /// </returns>
    public SanitizationResult Sanitize(string input)
    {
        var wasModified = false;
        var text = input;

        if (text.Length > MaxInputLength)
        {
            text = text[..MaxInputLength] + TruncationMarker;
            wasModified = true;
        }

        foreach (var pattern in s_injectionPatterns)
        {
            var replaced = pattern.Replace(text, FilteredMarker);
            if (!ReferenceEquals(replaced, text) && replaced != text)
            {
                wasModified = true;
                text = replaced;
            }
        }

        return new SanitizationResult(text, wasModified);
    }
}
