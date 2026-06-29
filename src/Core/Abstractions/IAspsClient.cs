namespace Edda.Core.Abstractions;

/// <summary>
/// Type-safe HTTP client for the ASPS.ai V1 Read-Only API.
/// Handles authentication via Bearer token, retry logic, and JSON deserialization.
/// </summary>
public interface IAspsClient
{
    /// <summary>
    /// Lists all projects accessible with the configured API token.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All accessible projects, ordered by creation date descending.</returns>
    /// <exception cref="Exceptions.AspsAuthenticationException">Token is invalid or expired.</exception>
    /// <exception cref="Exceptions.AspsApiException">Unexpected API error.</exception>
    Task<IReadOnlyList<Models.AspsProject>> ListProjectsAsync(CancellationToken ct = default);

    /// <summary>
    /// Fetches a single project by its numeric slug.
    /// </summary>
    /// <param name="slug">The numeric project slug from ASPS.ai.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The project details.</returns>
    /// <exception cref="Exceptions.AspsProjectNotFoundException">Project not found (404).</exception>
    /// <exception cref="Exceptions.AspsAuthenticationException">Token is invalid or expired.</exception>
    Task<Models.AspsProject> GetProjectAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Fetches the current (latest version) Lastenheft as Markdown.
    /// </summary>
    /// <param name="slug">The numeric project slug.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The latest Lastenheft version with Markdown content.</returns>
    Task<Models.AspsLastenheft> GetLastenheftAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Fetches the current Pflichtenheft as Markdown, if available.
    /// </summary>
    /// <param name="slug">The numeric project slug.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The latest Pflichtenheft or null if not yet generated.</returns>
    Task<Models.AspsPflichtenheft?> GetPflichtenheftAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Fetches all tasks grouped by epic for the given project.
    /// </summary>
    /// <param name="slug">The numeric project slug.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of epics and tasks with dependencies.</returns>
    Task<Models.AspsTaskCollection> GetTasksAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Fetches the task dependency graph as Mermaid diagram code.
    /// </summary>
    /// <param name="slug">The numeric project slug.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Mermaid graph definition string.</returns>
    Task<string> GetTaskGraphAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Fetches the offer document if available.
    /// </summary>
    /// <param name="slug">The numeric project slug.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The offer or null if not yet generated.</returns>
    Task<Models.AspsOffer?> GetOfferAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Checks API connectivity and token validity.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the API is reachable and the token is valid.</returns>
    Task<bool> HealthCheckAsync(CancellationToken ct = default);

    /// <summary>
    /// Lists all Lastenheft versions for a project.
    /// </summary>
    /// <param name="slug">The numeric project slug.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All Lastenheft versions ordered by version number.</returns>
    Task<IReadOnlyList<Models.AspsDocumentVersion>> GetLastenheftVersionsAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Fetches a specific Lastenheft version by its ID.
    /// </summary>
    /// <param name="slug">The numeric project slug.</param>
    /// <param name="versionId">The version ID to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The specified Lastenheft version with Markdown content.</returns>
    Task<Models.AspsLastenheft> GetLastenheftVersionAsync(string slug, int versionId, CancellationToken ct = default);

    /// <summary>
    /// Lists all Pflichtenheft versions for a project.
    /// </summary>
    /// <param name="slug">The numeric project slug.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All Pflichtenheft versions ordered by version number.</returns>
    Task<IReadOnlyList<Models.AspsDocumentVersion>> GetPflichtenheftVersionsAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Fetches a specific Pflichtenheft version by its ID.
    /// </summary>
    /// <param name="slug">The numeric project slug.</param>
    /// <param name="versionId">The version ID to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The specified Pflichtenheft version with Markdown content.</returns>
    Task<Models.AspsPflichtenheft> GetPflichtenheftVersionAsync(string slug, int versionId, CancellationToken ct = default);

    /// <summary>
    /// Fetches a single task by its model ID.
    /// </summary>
    /// <param name="slug">The numeric project slug.</param>
    /// <param name="taskId">The task model ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The requested task.</returns>
    Task<Models.AspsTask> GetTaskAsync(string slug, int taskId, CancellationToken ct = default);

    /// <summary>
    /// Updates the impact and effort priority of a task (Olympia-Podest, 1-3).
    /// </summary>
    /// <param name="slug">The numeric project slug.</param>
    /// <param name="taskId">The task model ID.</param>
    /// <param name="impactPriority">Impact priority value (1-3) or null to leave unchanged.</param>
    /// <param name="effortPriority">Effort priority value (1-3) or null to leave unchanged.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated priority values.</returns>
    Task<Models.AspsTaskPriority> UpdateTaskPriorityAsync(string slug, int taskId, int? impactPriority, int? effortPriority, CancellationToken ct = default);

    /// <summary>
    /// Fetches all modules with their epics and tasks.
    /// </summary>
    /// <param name="slug">The numeric project slug.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All modules with nested epics and tasks.</returns>
    Task<IReadOnlyList<Models.AspsModule>> GetModulesAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Fetches all epics with their tasks.
    /// </summary>
    /// <param name="slug">The numeric project slug.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All epics with nested tasks.</returns>
    Task<IReadOnlyList<Models.AspsEpic>> GetEpicsAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Fetches all milestones for a project.
    /// </summary>
    /// <param name="slug">The numeric project slug.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All project milestones.</returns>
    Task<IReadOnlyList<Models.AspsMilestone>> GetMilestonesAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Fetches the chat history including messages and attachments.
    /// </summary>
    /// <param name="slug">The numeric project slug.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Chat history with messages and attachment references.</returns>
    Task<Models.AspsChatHistory> GetChatAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Fetches the discovery session with follow-up questions.
    /// </summary>
    /// <param name="slug">The numeric project slug.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Discovery session data with messages.</returns>
    Task<Models.AspsDiscovery> GetDiscoveryAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Fetches prototype build status, preview URL, and collected feedback.
    /// </summary>
    /// <param name="slug">The numeric project slug.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Prototype information or null if no prototype exists.</returns>
    Task<Models.AspsPrototypeInfo?> GetPrototypeAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Fetches generation and availability status flags for a project.
    /// </summary>
    /// <param name="slug">The numeric project slug.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Project status flags.</returns>
    Task<Models.AspsProjectStatus> GetStatusAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Fetches all prototype feedback items for a project.
    /// </summary>
    /// <param name="slug">The numeric project slug.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All feedback items for the project's prototypes.</returns>
    Task<IReadOnlyList<Models.AspsPrototypeFeedback>> GetFeedbackAsync(string slug, CancellationToken ct = default);

    // --- Write methods (Report endpoints) ---

    /// <summary>
    /// Reports prototype status (URL or error) to ASPS.ai.
    /// Called after the prototype step when deployment is complete.
    /// </summary>
    /// <param name="slug">ASPS project slug.</param>
    /// <param name="status">"completed" or "failed".</param>
    /// <param name="url">Prototype URL (when completed).</param>
    /// <param name="errorMessage">Error description (when failed).</param>
    /// <param name="files">List of generated prototype file paths (relative to prototype root).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Report confirmation from ASPS.ai.</returns>
    /// <exception cref="Exceptions.AspsApiException">Report delivery failed.</exception>
    Task<Models.AspsReportResult> ReportPrototypeAsync(
        string slug, string status, string? url, string? errorMessage,
        IReadOnlyList<string>? files = null,
        CancellationToken ct = default);

    /// <summary>
    /// Reports generated Pflichtenheft content to ASPS.ai.
    /// Called after the plan step when the Pflichtenheft has been generated.
    /// </summary>
    /// <param name="slug">ASPS project slug.</param>
    /// <param name="status">"completed" or "failed".</param>
    /// <param name="content">Pflichtenheft Markdown content (when completed).</param>
    /// <param name="errorMessage">Error description (when failed).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Report confirmation from ASPS.ai.</returns>
    /// <exception cref="Exceptions.AspsApiException">Report delivery failed.</exception>
    Task<Models.AspsReportResult> ReportPflichtenheftAsync(
        string slug, string status, string? content, string? errorMessage,
        CancellationToken ct = default);

    /// <summary>
    /// Reports build progress to ASPS.ai.
    /// Called at agent start (in_progress), after merge (completed), or on failure (failed).
    /// </summary>
    /// <param name="slug">ASPS project slug.</param>
    /// <param name="status">"in_progress", "completed", or "failed".</param>
    /// <param name="url">Repository URL (when completed).</param>
    /// <param name="errorMessage">Error description (when failed).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Report confirmation from ASPS.ai.</returns>
    /// <exception cref="Exceptions.AspsApiException">Report delivery failed.</exception>
    Task<Models.AspsReportResult> ReportBuildStatusAsync(
        string slug, string status, string? url, string? errorMessage,
        CancellationToken ct = default);
}
