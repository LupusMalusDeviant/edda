namespace Edda.Core.Models;

/// <summary>
/// Progress snapshot for a single coding agent at a specific point in time.
/// Combines status file data, git metrics, and elapsed time.
/// </summary>
/// <param name="TaskId">The task this agent is working on.</param>
/// <param name="AgentRole">Agent role (frontend, backend, etc.).</param>
/// <param name="State">Current agent state.</param>
/// <param name="CommitCount">Number of commits made so far.</param>
/// <param name="LastCommitMessage">Most recent commit message. Null if no commits yet.</param>
/// <param name="ElapsedTime">Time since the agent was spawned.</param>
/// <param name="ModifiedFiles">Files modified by this agent (from git diff).</param>
/// <param name="Error">Error detail if the agent failed. Null on success.</param>
public sealed record AgentProgressSnapshot(
    string TaskId,
    string AgentRole,
    DevAgentState State,
    int CommitCount,
    string? LastCommitMessage,
    TimeSpan ElapsedTime,
    IReadOnlyList<string> ModifiedFiles,
    string? Error);

/// <summary>
/// Aggregated progress snapshot for an entire ASPS project.
/// Shows overall state, current layer, and per-agent snapshots.
/// </summary>
/// <param name="ProjectId">The internal project ID.</param>
/// <param name="OverallState">Overall project state.</param>
/// <param name="CurrentLayer">Index of the currently executing layer (0-based).</param>
/// <param name="TotalLayers">Total number of topological layers.</param>
/// <param name="AgentSnapshots">Per-agent progress snapshots.</param>
/// <param name="Timestamp">When this snapshot was taken.</param>
public sealed record ProjectProgressSnapshot(
    string ProjectId,
    DevProjectState OverallState,
    int CurrentLayer,
    int TotalLayers,
    IReadOnlyList<AgentProgressSnapshot> AgentSnapshots,
    DateTimeOffset Timestamp);
