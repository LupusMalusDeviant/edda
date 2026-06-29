namespace Edda.AKG.Graph;

/// <summary>
/// Validates structural invariants of the knowledge graph
/// (cyclic IMPLIES dependencies and dangling rule references).
/// </summary>
internal interface IGraphValidator
{
    /// <summary>Runs all structural validations against the current graph.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> if the graph is structurally valid; otherwise <see langword="false"/>.</returns>
    Task<bool> ValidateAsync(CancellationToken ct);
}
