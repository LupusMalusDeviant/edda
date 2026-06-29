namespace Edda.Core.Models;

/// <summary>
/// Input passed to an <see cref="Abstractions.IPrototypeSubAgent"/> for a single
/// execution. Carries everything the sub-agent needs to produce its file
/// contribution: the Lastenheft rules, project metadata, optional feedback,
/// the previous version's manifest (for incremental iterations), and any
/// upstream contributions that should be visible (e.g. UI+Backend files for
/// the Security sub-agent to review).
/// </summary>
public sealed record SubAgentContext
{
    /// <summary>Internal project identifier from the ASPS mapping store.</summary>
    public required string ProjectId { get; init; }

    /// <summary>Target prototype version this sub-agent's output belongs to.</summary>
    public required int Version { get; init; }

    /// <summary>Human-readable project name — shown in generated HTML titles.</summary>
    public required string ProjectName { get; init; }

    /// <summary>AKG rules that form the project's Lastenheft.</summary>
    public required IReadOnlyList<KnowledgeRule> AkgRules { get; init; }

    /// <summary>Working directory the orchestrator allocated for this version.</summary>
    public required string WorkDir { get; init; }

    /// <summary>
    /// Files produced by upstream sub-agents in the same build whose output
    /// this sub-agent should be aware of (e.g. Security reads UI+Backend files).
    /// Empty for sub-agents that run first in the pipeline.
    /// </summary>
    public IReadOnlyList<AgentContribution> UpstreamContributions { get; init; } = [];

    /// <summary>Manifest of the immediately preceding version, or null for the first build.</summary>
    public ArtefactManifest? PreviousManifest { get; init; }

    /// <summary>
    /// Optional natural-language feedback from the user or ASPS.ai. Set on
    /// iteration builds to scope the sub-agent's changes (e.g. "Header zu groß").
    /// </summary>
    public string? Feedback { get; init; }
}

/// <summary>
/// Outcome of a single sub-agent execution. Contains the file contribution
/// (merge-ready for <see cref="Abstractions.IConsistencyKeeper"/>), an optional
/// human-readable report (e.g. the Security sub-agent's findings summary),
/// and error information if the run failed.
/// </summary>
public sealed record SubAgentResult
{
    /// <summary>Name of the sub-agent that produced this result.</summary>
    public required string AgentName { get; init; }

    /// <summary>Whether the sub-agent completed successfully.</summary>
    public required bool Success { get; init; }

    /// <summary>Files produced by this sub-agent (relative path → content).</summary>
    public IReadOnlyDictionary<string, string> Files { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Optional human-readable report (e.g. Security findings) surfaced back
    /// to ASPS.ai alongside the prototype URL.
    /// </summary>
    public string? Report { get; init; }

    /// <summary>Error message when <see cref="Success"/> is false.</summary>
    public string? Error { get; init; }

    /// <summary>
    /// Provider stop reason (<c>"end_turn"</c>, <c>"max_tokens"</c>,
    /// <c>"stop_sequence"</c>, <c>"tool_use"</c>) — crucial for diagnosing
    /// zero-file responses (typically <c>max_tokens</c> or an unexpected
    /// <c>stop_sequence</c>).
    /// </summary>
    public string? StopReason { get; init; }

    /// <summary>Input tokens consumed by system + user prompt.</summary>
    public int? InputTokens { get; init; }

    /// <summary>Output tokens produced by the model.</summary>
    public int? OutputTokens { get; init; }

    /// <summary>Total character count of the raw LLM response.</summary>
    public int? ResponseLength { get; init; }

    /// <summary>
    /// Convenience accessor: constructs an <see cref="AgentContribution"/>
    /// ready for <see cref="Abstractions.IConsistencyKeeper.MergeAsync"/>.
    /// </summary>
    public AgentContribution ToContribution() => new(AgentName, Files);
}
