using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Dispatches webhook callbacks to the ASPS.ai platform after each pipeline step.
/// Sends HMAC-SHA256 signed HTTP POST requests with step results.
/// Dispatch is fire-and-forget with retry — failures do not block step execution.
/// </summary>
public interface IAspsWebhookDispatcher
{
    /// <summary>
    /// Sends a step completion webhook to the configured callback URL.
    /// The request body is signed with HMAC-SHA256 using the shared secret.
    /// </summary>
    /// <param name="runId">Unique pipeline run identifier.</param>
    /// <param name="slug">ASPS project slug.</param>
    /// <param name="step">The completed pipeline step.</param>
    /// <param name="result">The step execution result including payload.</param>
    /// <param name="webhookUrl">Target URL for the callback POST.</param>
    /// <param name="webhookSecret">HMAC-SHA256 secret for request signing.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DispatchStepCompletedAsync(
        string runId,
        string slug,
        PipelineStep step,
        StepResult result,
        string webhookUrl,
        string webhookSecret,
        CancellationToken ct);
}
