using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// A specialised sub-agent in the multi-agent prototype generation pipeline.
/// Each implementation owns a narrow slice of the prototype (UI, Backend-Mock,
/// Security) and produces a file contribution that the consistency keeper
/// merges into the versioned output directory.
/// </summary>
/// <remarks>
/// Sub-agents run in-process against <see cref="IModelClient"/> — they are not
/// spawned as Docker clones. Multiple sub-agents can execute in parallel when
/// their file ownership is disjoint; the consistency keeper guarantees
/// byte-identical carry-over of unchanged files across versions.
/// </remarks>
public interface IPrototypeSubAgent
{
    /// <summary>
    /// Stable identifier of this sub-agent (e.g. <c>"ui"</c>, <c>"backend-mock"</c>, <c>"security"</c>).
    /// Used as the file owner in <see cref="ArtefactManifest"/>.
    /// </summary>
    string AgentName { get; }

    /// <summary>
    /// Identifier of the <see cref="SkillProfile"/> injected into this agent's
    /// system prompt (e.g. <c>"prototype-ui-designer"</c>).
    /// </summary>
    string SkillProfileId { get; }

    /// <summary>
    /// Executes the sub-agent and returns its file contribution.
    /// Must never throw for content-level failures — return
    /// <see cref="SubAgentResult"/> with <c>Success = false</c> instead.
    /// </summary>
    /// <param name="context">Input context (Lastenheft, feedback, upstream files).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The sub-agent's files + report.</returns>
    Task<SubAgentResult> ExecuteAsync(SubAgentContext context, CancellationToken ct);
}
