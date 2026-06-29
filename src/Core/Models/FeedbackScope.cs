namespace Edda.Core.Models;

/// <summary>
/// Classification of user feedback, used by the multi-agent prototype
/// orchestrator to decide which sub-agents to rerun on an iteration build.
/// The default is <see cref="Mixed"/> which causes every sub-agent to run.
/// </summary>
public enum FeedbackScope
{
    /// <summary>
    /// Unknown, ambiguous, or structural feedback — run all sub-agents.
    /// </summary>
    Mixed = 0,

    /// <summary>
    /// Pure UI / design / layout change — only <c>UiSubAgent</c> runs,
    /// backend and security contributions are carried over byte-identical.
    /// </summary>
    Ui = 1,

    /// <summary>
    /// Mock data or API behaviour change — only <c>BackendMockSubAgent</c> runs.
    /// </summary>
    Backend = 2,

    /// <summary>
    /// Auth or hardening change — only <c>SecuritySubAgent</c> runs.
    /// </summary>
    Security = 3,
}
