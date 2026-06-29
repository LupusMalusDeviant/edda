namespace Edda.Core.Models;

/// <summary>A task assigned to a clone agent for autonomous execution.</summary>
/// <param name="TaskId">Unique task identifier.</param>
/// <param name="Instruction">The instruction or query the clone should process.</param>
/// <param name="UserId">User context to propagate into the clone's identity.</param>
public sealed record CloneTask(string TaskId, string Instruction, string UserId);

/// <summary>Status snapshot of a running or completed clone task.</summary>
public sealed record CloneStatus
{
    /// <summary>The clone's unique identifier.</summary>
    public required string CloneId { get; init; }

    /// <summary>Current execution state.</summary>
    public required CloneTaskState State { get; init; }

    /// <summary>Error detail if State=Failed.</summary>
    public string? Error { get; init; }
}

/// <summary>Lifecycle states of a clone agent task.</summary>
public enum CloneTaskState
{
    /// <summary>Clone container is being started.</summary>
    Starting,

    /// <summary>Clone is actively processing the task.</summary>
    Running,

    /// <summary>Task completed successfully.</summary>
    Completed,

    /// <summary>Task failed; see CloneStatus.Error for details.</summary>
    Failed
}

/// <summary>Reference to a spawned clone container.</summary>
/// <param name="CloneId">Unique clone identifier.</param>
/// <param name="Endpoint">Base URL of the clone's HTTP API.</param>
public sealed record CloneHandle(string CloneId, string Endpoint);

/// <summary>Final result produced by a clone agent task.</summary>
public sealed record CloneResult
{
    /// <summary>Correlation identifier matching the original CloneTask.TaskId.</summary>
    public required string TaskId { get; init; }

    /// <summary>True if the clone completed the task without errors.</summary>
    public required bool Success { get; init; }

    /// <summary>The clone's output content. Null when Success=false.</summary>
    public string? Content { get; init; }

    /// <summary>Error description. Only set when Success=false.</summary>
    public string? Error { get; init; }
}
