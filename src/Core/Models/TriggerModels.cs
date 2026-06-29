namespace Edda.Core.Models;

/// <summary>
/// Definition of a trigger that initiates an agent task automatically.
/// Supports scheduled (cron/interval/daily), event-based (docker), and metric-based triggers.
/// </summary>
public sealed record TriggerDefinition
{
    /// <summary>Unique trigger identifier.</summary>
    public required string TriggerId { get; init; }

    /// <summary>The user who owns this trigger.</summary>
    public required string UserId { get; init; }

    /// <summary>Human-readable trigger name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Trigger type discriminator.
    /// Valid values: Daily | Interval | Cron | DockerEvent | SystemMetric.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>Instruction sent to the agent when the trigger fires.</summary>
    public required string Instruction { get; init; }

    /// <summary>Whether this trigger is currently active.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// Type-specific configuration (e.g. cron expression, interval string, docker event filter).
    /// </summary>
    public IReadOnlyDictionary<string, string> Config { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Names of delivery channels to use when the trigger fires (e.g. "telegram", "log").</summary>
    public IReadOnlyList<string> DeliveryChannels { get; init; } = [];
}

/// <summary>
/// A task queued for deferred or scheduled execution by the agent.
/// </summary>
public sealed record QueuedTask
{
    /// <summary>Unique task identifier.</summary>
    public required string TaskId { get; init; }

    /// <summary>The user who enqueued this task.</summary>
    public required string UserId { get; init; }

    /// <summary>The instruction the agent should process.</summary>
    public required string Instruction { get; init; }

    /// <summary>Execution priority; higher values run first when multiple tasks are due.</summary>
    public int Priority { get; init; }

    /// <summary>Earliest time at which this task should be executed.</summary>
    public DateTimeOffset ExecuteAfter { get; init; }

    /// <summary>Names of delivery channels for the result.</summary>
    public IReadOnlyList<string> DeliveryChannels { get; init; } = [];

    /// <summary>Current execution status.</summary>
    public TaskExecutionStatus Status { get; init; } = TaskExecutionStatus.Pending;

    /// <summary>
    /// The channel that originated this task (e.g. "telegram", "matrix", "dashboard").
    /// Set automatically from ToolExecutionContext at queue time.
    /// Used to auto-populate delivery channels when none are explicitly specified.
    /// </summary>
    public string? OriginChannel { get; init; }

    /// <summary>
    /// Channel-specific routing metadata captured at queue time.
    /// Enables delivery channels to route the result back to the exact conversation
    /// (e.g. telegram_chat_id, matrix_room_id).
    /// </summary>
    public IReadOnlyDictionary<string, string> OriginMetadata { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// The agent's response content after execution. Null while Pending or Running.
    /// Persisted for audit, retry, and later retrieval via the result action.
    /// </summary>
    public string? ResultContent { get; init; }

    /// <summary>
    /// Error message when Status is Failed. Null otherwise.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// UTC timestamp when the task completed or failed. Null while Pending or Running.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>
/// Structured notification sent to delivery channels when a queued task completes.
/// Allows channels and users to distinguish task-completion messages from regular chat.
/// </summary>
public sealed record TaskCompletionNotification
{
    /// <summary>Task ID for reference and linking.</summary>
    public required string TaskId { get; init; }

    /// <summary>Original instruction that was executed.</summary>
    public required string Instruction { get; init; }

    /// <summary>Whether the task succeeded.</summary>
    public required bool Success { get; init; }

    /// <summary>The agent's response content (on success) or error message (on failure).</summary>
    public required string Content { get; init; }

    /// <summary>UTC timestamp of completion.</summary>
    public required DateTimeOffset CompletedAt { get; init; }
}

/// <summary>Lifecycle states of a queued task.</summary>
public enum TaskExecutionStatus
{
    /// <summary>Task is waiting to be picked up by the worker.</summary>
    Pending,

    /// <summary>Task is currently being executed.</summary>
    Running,

    /// <summary>Task completed successfully.</summary>
    Completed,

    /// <summary>Task failed during execution.</summary>
    Failed,

    /// <summary>Task was cancelled before execution.</summary>
    Cancelled
}
