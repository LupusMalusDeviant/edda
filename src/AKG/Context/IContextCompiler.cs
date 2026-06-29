using Edda.Core.Models;

namespace Edda.AKG.Context;

/// <summary>
/// Compiles the active knowledge context for a task across the four retrieval phases
/// (keyword → semantic → MMR → conflict resolution).
/// </summary>
internal interface IContextCompiler
{
    /// <summary>Selects and ranks the most relevant rules for the given task context.</summary>
    /// <param name="context">The task context (query, domains, budget).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The compiled context result.</returns>
    Task<ContextResult> CompileAsync(TaskContext context, CancellationToken ct);
}
