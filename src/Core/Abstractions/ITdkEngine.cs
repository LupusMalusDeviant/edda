using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Test-Driven Knowledge (TDK) engine. Validates agent responses against the active AKG rules.
/// A violation causes <see cref="IAgentRuntime"/> to re-query the model with targeted feedback.
/// </summary>
public interface ITdkEngine
{
    /// <summary>
    /// Validates a candidate response against the provided set of active rules.
    /// </summary>
    /// <param name="response">The agent response text to validate.</param>
    /// <param name="rules">Active AKG rules compiled for the current task context.</param>
    /// <param name="request">The original agent request that produced the response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="TdkResult"/> containing any detected violations.
    /// Returns <see cref="TdkResult.NoViolations"/> when the response is fully compliant.
    /// </returns>
    Task<TdkResult> ValidateAsync(
        string response,
        IReadOnlyList<KnowledgeRule> rules,
        AgentRequest request,
        CancellationToken cancellationToken = default);
}
