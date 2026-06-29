using Edda.Security.Models;

namespace Edda.Security.Sanitization;

/// <summary>
/// Sanitizes user input to prevent prompt-injection attacks and enforce length limits.
/// </summary>
public interface IInputSanitizer
{
    /// <summary>
    /// Sanitizes the input: truncates oversized text and replaces known injection patterns.
    /// </summary>
    /// <param name="input">The raw user input to sanitize.</param>
    /// <returns>The sanitized text plus a flag indicating whether anything was modified.</returns>
    SanitizationResult Sanitize(string input);
}
