using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Optional LLM-backed TDK judge (F16): evaluates a code block against a rule's natural-language
/// prompt and returns the same pass/violations verdict shape as script validators. Registered only
/// when <c>TDK_LLM_JUDGE=true</c> and degrades to an engine error when no LLM provider is
/// configured. Deterministic script validators remain the core mechanism (ADR-0010).
/// </summary>
public interface ITdkLlmJudge
{
    /// <summary>Judges one code block against one rule prompt.</summary>
    /// <param name="request">The evaluation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The verdict; <c>Executed=false</c> when the judge could not run or answer parseably.</returns>
    Task<TdkJudgeResult> JudgeAsync(TdkJudgeRequest request, CancellationToken cancellationToken = default);
}
