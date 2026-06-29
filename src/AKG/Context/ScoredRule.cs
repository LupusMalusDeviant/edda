using Edda.Core.Models;

namespace Edda.AKG.Context;

/// <summary>
/// Pairs a <see cref="KnowledgeRule"/> with its computed relevance score.
/// Used internally during context compilation scoring phases.
/// </summary>
internal sealed class ScoredRule
{
    /// <summary>Gets or sets the knowledge rule being scored.</summary>
    internal required KnowledgeRule Rule { get; init; }

    /// <summary>Gets or sets the computed relevance score. Higher is more relevant.</summary>
    internal double Score { get; set; }
}
