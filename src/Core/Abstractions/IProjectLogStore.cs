using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Append-only per-project log store. Pipeline components (AspsStepExecutor,
/// PrototypeOrchestrator, sub-agents, GitLab deployer, smoke-test) write
/// structured events here so the Web-UI can surface a complete timeline
/// per project independent of the container's stdout log.
/// </summary>
/// <remarks>
/// Implementations must be safe to call fire-and-forget from any component —
/// failures to persist are logged via ILogger but never thrown, so a broken
/// store never breaks a build.
/// </remarks>
public interface IProjectLogStore
{
    /// <summary>
    /// Persists a single log entry for the given project.
    /// </summary>
    /// <param name="projectId">Internal project id; the row is scoped to this.</param>
    /// <param name="level">Severity: <c>"info"</c>, <c>"warn"</c>, <c>"error"</c>.</param>
    /// <param name="component">Producer tag (e.g. <c>"PrototypeOrchestrator"</c>).</param>
    /// <param name="message">Human-readable event text.</param>
    /// <param name="data">Optional serialisable payload (dict/record); serialised to JSON.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AppendAsync(
        string projectId,
        string level,
        string component,
        string message,
        object? data = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the most recent log entries for a project in descending
    /// chronological order (newest first).
    /// </summary>
    /// <param name="projectId">Internal project id.</param>
    /// <param name="limit">Maximum number of entries to return (1–5000).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<ProjectLogEntry>> GetLogsAsync(
        string projectId,
        int limit = 500,
        CancellationToken ct = default);

    /// <summary>
    /// Removes all entries for a project. Useful for a "clear log" button.
    /// </summary>
    Task ClearAsync(string projectId, CancellationToken ct = default);
}
