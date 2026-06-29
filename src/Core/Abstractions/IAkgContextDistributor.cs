namespace Edda.Core.Abstractions;

/// <summary>
/// Extracts and formats AKG knowledge context scoped to specific domains.
/// Used to provide coding agents with only the knowledge relevant to their task.
/// </summary>
public interface IAkgContextDistributor
{
    /// <summary>
    /// Exports AKG rules for the specified domains as a Markdown context string.
    /// Includes domain-specific knowledge rules and any implied or required rules
    /// from those domains.
    /// </summary>
    /// <param name="domains">Domain names to include (e.g. ["frontend", "security"]).</param>
    /// <param name="userId">User ID for user-scoped rules.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Formatted Markdown context ready for injection into a coding agent.</returns>
    Task<string> ExportContextAsync(
        IReadOnlyList<string> domains,
        string userId,
        CancellationToken ct = default);
}
