using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Runs a rule's TDK validator against its own <c>validatorFixtures</c> (F5): every <c>pass</c>
/// snippet must yield no violations, every <c>fail</c> snippet must yield at least one. Runs the
/// sandbox directly and never records confidence outcomes — a self-test must not skew rule weights.
/// </summary>
public interface ITdkFixtureVerifier
{
    /// <summary>Verifies every rule that declares fixtures in the configured knowledge directory.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An aggregated report over all rules that declared fixtures.</returns>
    Task<TdkFixtureVerificationReport> VerifyAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a single parsed rule's fixtures. Returns a report with <c>HasFixtures = false</c>
    /// when the rule declares no validator or no fixtures.
    /// </summary>
    /// <param name="rule">The parsed rule to verify.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rule's fixture-verification report.</returns>
    Task<TdkFixtureRuleReport> VerifyRuleAsync(KnowledgeRule rule, CancellationToken cancellationToken = default);
}
