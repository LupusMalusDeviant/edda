using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Defines, executes, and monitors DAG-based workflows.
/// A workflow is a directed acyclic graph (DAG) of nodes where each node represents
/// an agent task, tool call, clone invocation, or condition evaluation.
/// </summary>
public interface IWorkflowEngine
{
    /// <summary>
    /// Validates and persists a workflow definition without executing it.
    /// </summary>
    /// <param name="definition">The workflow DAG to register.</param>
    /// <param name="userId">Owner of the workflow definition.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A handle containing the assigned workflow ID.</returns>
    /// <exception cref="WorkflowValidationException">
    /// Thrown if the graph contains cycles, undefined node references, or invalid conditions.
    /// </exception>
    Task<WorkflowHandle> RegisterAsync(
        WorkflowDefinition definition,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Starts executing a registered workflow asynchronously.
    /// Returns immediately; use <see cref="GetStatusAsync"/> to poll for progress.
    /// </summary>
    /// <param name="workflowId">ID of a previously registered workflow definition.</param>
    /// <param name="inputs">
    /// Optional key-value pairs bound to <c>{{input.key}}</c> templates in node instructions.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A run handle containing the assigned run ID.</returns>
    Task<WorkflowRun> StartAsync(
        string workflowId,
        IReadOnlyDictionary<string, string>? inputs = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the current execution state of a workflow run.
    /// </summary>
    /// <param name="runId">The run ID returned by <see cref="StartAsync"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Detailed status including per-node states.</returns>
    Task<WorkflowRunStatus> GetStatusAsync(
        string runId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the collected outputs of all completed nodes in a run.
    /// Keys follow the pattern <c>node_id.output</c>.
    /// </summary>
    /// <param name="runId">The run ID returned by <see cref="StartAsync"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Output dictionary; may be empty if no nodes have completed yet.</returns>
    Task<IReadOnlyDictionary<string, string>> GetOutputsAsync(
        string runId,
        CancellationToken ct = default);

    /// <summary>
    /// Requests cancellation of a running workflow.
    /// Already-completed nodes are not rolled back.
    /// </summary>
    /// <param name="runId">The run ID to cancel.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CancelAsync(string runId, CancellationToken ct = default);

    /// <summary>
    /// Returns all workflow definitions owned by the given user.
    /// </summary>
    /// <param name="userId">The owner whose workflows to list.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of workflow handles, newest first.</returns>
    Task<IReadOnlyList<WorkflowHandle>> ListAsync(
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a registered workflow definition and all its associated runs.
    /// </summary>
    /// <param name="workflowId">The workflow definition to delete.</param>
    /// <param name="userId">Must match the owner to prevent cross-user deletion.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if the workflow was found and deleted; <c>false</c> if not found.</returns>
    Task<bool> DeleteAsync(string workflowId, string userId, CancellationToken ct = default);
}
