namespace Edda.Core.Models;

/// <summary>
/// Complete definition of a workflow DAG including nodes and directed edges.
/// </summary>
public sealed record WorkflowDefinition
{
    /// <summary>Human-readable name for this workflow.</summary>
    public required string Name { get; init; }

    /// <summary>Optional description of what the workflow does.</summary>
    public string? Description { get; init; }

    /// <summary>All task nodes that make up the workflow graph.</summary>
    public required IReadOnlyList<WorkflowNode> Nodes { get; init; }

    /// <summary>Directed edges connecting nodes. An empty list implies a single-node workflow.</summary>
    public required IReadOnlyList<WorkflowEdge> Edges { get; init; }

    /// <summary>
    /// Global input variable names available to all nodes via the <c>{{input.key}}</c>
    /// template syntax. Values are supplied when calling
    /// <c>IWorkflowEngine.StartAsync</c>.
    /// </summary>
    public IReadOnlyList<string> InputVariables { get; init; } = [];
}

/// <summary>
/// A single task unit within a workflow graph.
/// </summary>
public sealed record WorkflowNode
{
    /// <summary>Unique identifier for this node within the workflow.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// Node execution type:
    /// <list type="bullet">
    ///   <item><c>agent_task</c> — instruction sent to <see cref="Abstractions.IAgentRuntime"/>.</item>
    ///   <item><c>tool_call</c> — direct tool invocation via <see cref="Abstractions.IToolExecutor"/>.</item>
    ///   <item><c>clone</c>     — spawns a Clone via <see cref="Abstractions.ICloneOrchestrator"/>.</item>
    ///   <item><c>condition</c> — evaluates an expression and stores <c>"true"</c>/<c>"false"</c> as output.</item>
    /// </list>
    /// </summary>
    public required string Type { get; init; }

    /// <summary>Human-readable label used in logging and status responses.</summary>
    public required string Label { get; init; }

    /// <summary>
    /// The instruction or expression executed by this node.
    /// Supports <c>{{node_id.output}}</c> and <c>{{input.key}}</c> template interpolation.
    /// </summary>
    public required string Instruction { get; init; }

    /// <summary>Maximum wall-clock time allowed for this node. Default: 5 minutes.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Optional ID of a fallback node to activate when this node fails.
    /// The fallback node must also be declared in the workflow definition.
    /// </summary>
    public string? FallbackNodeId { get; init; }
}

/// <summary>
/// A directed dependency edge between two workflow nodes.
/// </summary>
public sealed record WorkflowEdge
{
    /// <summary>ID of the upstream (source) node.</summary>
    public required string FromNodeId { get; init; }

    /// <summary>ID of the downstream (target) node.</summary>
    public required string ToNodeId { get; init; }

    /// <summary>
    /// Optional condition expression that must evaluate to <c>true</c> for this edge to be
    /// followed. When <c>null</c> the edge is always followed.
    /// <para>
    /// Syntax: <c>{{ref}} operator "value"</c><br/>
    /// Operators: <c>contains</c>, <c>not_contains</c>, <c>equals</c>, <c>not_equals</c>,
    ///            <c>is_empty</c>, <c>not_empty</c>.
    /// </para>
    /// </summary>
    public string? Condition { get; init; }
}

/// <summary>
/// Lightweight handle for a registered (but not yet executed) workflow definition.
/// </summary>
/// <param name="WorkflowId">Stable identifier assigned on registration.</param>
/// <param name="Name">Human-readable workflow name.</param>
/// <param name="UserId">Owner of this workflow definition.</param>
/// <param name="NodeCount">Number of nodes in the definition.</param>
/// <param name="CreatedAt">When this definition was registered.</param>
public sealed record WorkflowHandle(
    string WorkflowId,
    string Name,
    string UserId,
    int NodeCount,
    DateTimeOffset CreatedAt);

/// <summary>
/// Handle for a started workflow execution (run).
/// </summary>
/// <param name="RunId">Unique run identifier.</param>
/// <param name="WorkflowId">The definition this run is based on.</param>
/// <param name="UserId">Owner of this run.</param>
/// <param name="State">Initial state (always <see cref="WorkflowRunState.Running"/>).</param>
/// <param name="StartedAt">When the run was started.</param>
public sealed record WorkflowRun(
    string RunId,
    string WorkflowId,
    string UserId,
    WorkflowRunState State,
    DateTimeOffset StartedAt);

/// <summary>
/// Detailed snapshot of an in-progress or completed workflow run.
/// </summary>
/// <param name="RunId">Unique run identifier.</param>
/// <param name="State">Overall execution state.</param>
/// <param name="NodeStates">Per-node execution state keyed by node ID.</param>
/// <param name="CurrentNodeId">The node currently executing, or <c>null</c> if finished.</param>
/// <param name="Error">Error message when <see cref="State"/> is <see cref="WorkflowRunState.Failed"/>.</param>
/// <param name="CompletedAt">Timestamp when the run finished, or <c>null</c> if still running.</param>
public sealed record WorkflowRunStatus(
    string RunId,
    WorkflowRunState State,
    IReadOnlyDictionary<string, NodeRunState> NodeStates,
    string? CurrentNodeId,
    string? Error,
    DateTimeOffset? CompletedAt);

/// <summary>Overall execution state of a workflow run.</summary>
public enum WorkflowRunState
{
    /// <summary>The run is currently executing.</summary>
    Running,

    /// <summary>All nodes completed successfully (or were skipped).</summary>
    Completed,

    /// <summary>One or more nodes failed without a fallback, or no progress was possible.</summary>
    Failed,

    /// <summary>The run was cancelled by the user.</summary>
    Cancelled
}

/// <summary>Execution state of an individual node within a run.</summary>
public enum NodeRunState
{
    /// <summary>Not yet started — waiting for predecessors.</summary>
    Pending,

    /// <summary>Currently executing.</summary>
    Running,

    /// <summary>Finished successfully.</summary>
    Completed,

    /// <summary>Execution failed.</summary>
    Failed,

    /// <summary>Skipped because an incoming conditional edge evaluated to false.</summary>
    Skipped
}

/// <summary>
/// Thrown by <see cref="Abstractions.IWorkflowEngine.RegisterAsync"/> when a workflow
/// definition contains structural errors such as cycles or undefined node references.
/// </summary>
public sealed class WorkflowValidationException : Exception
{
    /// <summary>
    /// Initialises the exception with a descriptive validation message.
    /// </summary>
    /// <param name="message">Description of the validation error.</param>
    public WorkflowValidationException(string message) : base(message) { }
}
