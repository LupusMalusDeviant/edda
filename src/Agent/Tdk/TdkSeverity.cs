namespace Edda.Agent.Tdk;

/// <summary>
/// Canonical TDK severity levels and their ordering. The semantics (also documented in
/// <c>docs/tdk.md</c>) are:
/// <list type="bullet">
/// <item><description><c>error</c> — blocking: the agent must fix it before the response is acceptable.</description></item>
/// <item><description><c>warning</c> — should be addressed, but not blocking.</description></item>
/// <item><description><c>info</c> — advisory hint.</description></item>
/// </list>
/// Feedback is ordered most-severe-first and counted per level so the agent knows what to fix first.
/// </summary>
public static class TdkSeverity
{
    /// <summary>Blocking severity: the agent must fix the violation.</summary>
    public const string Error = "error";

    /// <summary>Non-blocking severity: the violation should be addressed.</summary>
    public const string Warning = "warning";

    /// <summary>Advisory severity: an informational hint.</summary>
    public const string Info = "info";

    /// <summary>Normalises a raw severity string to its canonical lower-case form.</summary>
    /// <param name="severity">The raw severity value (any case, possibly null).</param>
    /// <returns>The trimmed, lower-cased severity, or an empty string when null.</returns>
    public static string Normalize(string? severity) => severity?.Trim().ToLowerInvariant() ?? string.Empty;

    /// <summary>
    /// Sort rank for a severity: lower ranks are more severe and are listed first.
    /// <c>error</c>=0, <c>warning</c>=1, <c>info</c>=2, and any unrecognised value=3 (sorts last).
    /// </summary>
    /// <param name="severity">The raw severity value (any case, possibly null).</param>
    /// <returns>The sort rank in <c>[0, 3]</c>.</returns>
    public static int Rank(string? severity) => Normalize(severity) switch
    {
        Error => 0,
        Warning => 1,
        Info => 2,
        _ => 3,
    };
}
