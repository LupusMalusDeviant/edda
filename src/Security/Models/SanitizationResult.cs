namespace Edda.Security.Models;

/// <summary>
/// Represents the result of input sanitization, including whether the input was modified.
/// </summary>
/// <param name="Text">The sanitized (and possibly truncated or filtered) text.</param>
/// <param name="WasModified">True if the original input was altered during sanitization.</param>
public sealed record SanitizationResult(string Text, bool WasModified);
