using System.Text.Json;
using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Executes individual ASPS pipeline steps independently.
/// Each step can be triggered via the external API or called in sequence
/// by the orchestrator. Steps are idempotent and checkpoint-guarded.
/// </summary>
public interface IAspsStepExecutor
{
    /// <summary>
    /// Executes a specific pipeline step for a given run.
    /// Validates that all prerequisite steps are completed before execution.
    /// Persists checkpoint state after completion.
    /// </summary>
    /// <param name="runId">Unique run identifier for checkpoint tracking.</param>
    /// <param name="aspsSlug">The ASPS project slug.</param>
    /// <param name="step">The pipeline step to execute.</param>
    /// <param name="userId">User owning the project (for scoping).</param>
    /// <param name="config">Project configuration (coding CLI, parallelism, timeouts).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Step result with success/failure status and step-specific payload.</returns>
    Task<StepResult> ExecuteStepAsync(
        string runId,
        string aspsSlug,
        PipelineStep step,
        string userId,
        DevProjectConfig config,
        CancellationToken ct);

    /// <summary>
    /// Retrieves the result of a previously executed step from the checkpoint store.
    /// </summary>
    /// <param name="runId">Unique run identifier.</param>
    /// <param name="step">The pipeline step to query.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The step result, or null if the step has not been executed.</returns>
    Task<StepResult?> GetStepResultAsync(
        string runId,
        PipelineStep step,
        CancellationToken ct);

    /// <summary>
    /// Retrieves the checkpoint for a given run ID.
    /// Used by the API layer to recover pipeline state after container restarts.
    /// </summary>
    /// <param name="runId">Unique run identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The checkpoint, or null if the run does not exist.</returns>
    Task<PipelineCheckpoint?> GetCheckpointAsync(string runId, CancellationToken ct);

    /// <summary>
    /// Records prototype approval for a pipeline run, enabling the pipeline to advance
    /// past the prototype phase. Delegates to the feedback collector if available.
    /// </summary>
    /// <param name="runId">Unique run identifier.</param>
    /// <param name="userId">User who approved the prototype.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ApprovePrototypeAsync(string runId, string userId, CancellationToken ct);

    /// <summary>
    /// Re-renders the Pflichtenheft from the stored DevPlan and re-sends it to ASPS.ai
    /// without re-running the full pipeline. Useful after Pflichtenheft format changes.
    /// </summary>
    /// <param name="aspsSlug">The ASPS project slug.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of characters sent, or null if no plan exists.</returns>
    Task<int?> ResendPflichtenheftAsync(string aspsSlug, CancellationToken ct);
}
