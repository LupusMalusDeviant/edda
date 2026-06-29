using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.Agent.Tdk;

/// <summary>
/// No-op implementation of <see cref="ITdkEngine"/> used for Phase 6.
/// Always reports no violations, allowing the pipeline to continue unimpeded.
/// The real TDK engine (with rule-based validation) is implemented in Phase 8.
/// </summary>
internal sealed class NullTdkEngine : ITdkEngine
{
    /// <inheritdoc/>
    /// <remarks>Always returns <see cref="TdkResult.NoViolations"/>.</remarks>
    public Task<TdkResult> ValidateAsync(
        string response,
        IReadOnlyList<KnowledgeRule> rules,
        AgentRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(TdkResult.NoViolations);
}
