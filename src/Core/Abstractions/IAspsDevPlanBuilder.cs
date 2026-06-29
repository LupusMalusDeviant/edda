using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Builds a <see cref="DevPlan"/> from an imported ASPS.ai project.
/// Uses AKG domain rules, ASPS tasks, and the Pflichtenheft to decompose
/// the project into agent-assignable <see cref="DevTask"/> entries with
/// dependency graph and <see cref="WorkflowDefinition"/>.
/// </summary>
public interface IAspsDevPlanBuilder
{
    /// <summary>
    /// Builds a DevPlan from an imported ASPS project.
    /// Uses AKG domain rules and ASPS tasks to create agent assignments
    /// with dependency graph and workflow definition.
    /// </summary>
    /// <param name="internalProjectId">The internal project ID from ASPS import.</param>
    /// <param name="config">Project configuration controlling CLI, concurrency, and git settings.</param>
    /// <param name="userId">The user who owns this plan.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A complete DevPlan ready for execution by IDevOrchestrator.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the project is not imported.</exception>
    Task<DevPlan> BuildPlanAsync(
        string internalProjectId,
        DevProjectConfig config,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Validates a DevPlan against the ASPS task graph.
    /// Checks: all tasks covered, no orphans, dependencies respected, no cycles.
    /// </summary>
    /// <param name="plan">The plan to validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Validation result with any errors or warnings found.</returns>
    Task<PlanValidationResult> ValidatePlanAsync(
        DevPlan plan,
        CancellationToken ct = default);
}
