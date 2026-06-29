using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Orchestrates the full ASPS.ai integration pipeline from project import to delivery.
/// Coordinates all 12 steps: import, plan, prototype, prompt decomposition,
/// agent spawning, monitoring, review, merge, and delivery.
/// </summary>
public interface IAspsOrchestrator
{
    /// <summary>
    /// Executes the full ASPS pipeline for a project: import → plan → prototype →
    /// multi-agent development → review → merge → deliver.
    /// </summary>
    /// <param name="aspsSlug">ASPS.ai project slug.</param>
    /// <param name="userId">User identifier for scoping.</param>
    /// <param name="config">Project configuration (coding CLI, parallelism, verify command, etc.).</param>
    /// <param name="progress">Optional progress reporter for step updates (e.g. "Step 2/8: ...").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Full orchestration result with all phase outputs.</returns>
    Task<AspsOrchestrationResult> RunAsync(
        string aspsSlug,
        string userId,
        DevProjectConfig config,
        IProgress<string>? progress,
        CancellationToken ct);
}
