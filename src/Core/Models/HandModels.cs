namespace Edda.Core.Models;

/// <summary>
/// Definition for creating a new Hand worker.
/// </summary>
public sealed record HandDefinition
{
    /// <summary>Human-readable name for this Hand.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Natural-language goal description. The Hand will pursue this goal
    /// autonomously, reporting progress and spawning sub-Clones as needed.
    /// </summary>
    public required string Goal { get; init; }

    /// <summary>How often the Hand should execute an iteration cycle.</summary>
    public required TimeSpan Interval { get; init; }

    /// <summary>
    /// Optional cron expression. If set, overrides <see cref="Interval"/>.
    /// Example: "0 8 * * *" (daily at 08:00).
    /// </summary>
    public string? CronExpression { get; init; }

    /// <summary>
    /// Maximum number of Clones this Hand may spawn simultaneously.
    /// Default: 2.
    /// </summary>
    public int MaxSubClones { get; init; } = 2;

    /// <summary>
    /// Delivery channel for progress reports.
    /// Known values: "log", "telegram", "dashboard".
    /// </summary>
    public string ReportChannel { get; init; } = "log";

    /// <summary>Optional path to a SKILL.md file to inject as system context for this Hand.</summary>
    public string? SkillProfilePath { get; init; }
}

/// <summary>
/// Reference to a running or stopped Hand.
/// </summary>
/// <param name="HandId">Unique Hand identifier.</param>
/// <param name="Name">Human-readable name.</param>
/// <param name="UserId">Owning user.</param>
/// <param name="State">Current lifecycle state.</param>
/// <param name="CreatedAt">When the Hand was first created.</param>
/// <param name="LastRunAt">Timestamp of the most recent cycle, or null if never run.</param>
public sealed record HandHandle(
    string HandId,
    string Name,
    string UserId,
    HandState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastRunAt);

/// <summary>
/// Detailed status snapshot including current cycle information and statistics.
/// </summary>
/// <param name="HandId">Unique Hand identifier.</param>
/// <param name="State">Current lifecycle state.</param>
/// <param name="Goal">The goal the Hand is pursuing.</param>
/// <param name="TotalCycles">Total number of cycles executed (successful and failed).</param>
/// <param name="SuccessfulCycles">Number of cycles that completed without error.</param>
/// <param name="FailedCycles">Number of cycles that ended with an exception.</param>
/// <param name="NextScheduledRun">Estimated timestamp of the next cycle, or null if unknown.</param>
/// <param name="CurrentActivity">Summary of the most recent cycle, if available.</param>
public sealed record HandStatus(
    string HandId,
    HandState State,
    string Goal,
    int TotalCycles,
    int SuccessfulCycles,
    int FailedCycles,
    DateTimeOffset? NextScheduledRun,
    string? CurrentActivity);

/// <summary>
/// A single progress report entry written by the Hand after each cycle.
/// </summary>
/// <param name="HandId">Hand that produced this entry.</param>
/// <param name="Timestamp">When the cycle completed.</param>
/// <param name="CycleNumber">Sequential cycle counter starting at 1.</param>
/// <param name="Summary">Short description of what the Hand did in this cycle.</param>
/// <param name="Success">True if the cycle completed without an unhandled exception.</param>
/// <param name="Error">Error message if <paramref name="Success"/> is false.</param>
public sealed record HandProgressEntry(
    string HandId,
    DateTimeOffset Timestamp,
    int CycleNumber,
    string Summary,
    bool Success,
    string? Error);

/// <summary>
/// Lifecycle states of a Hand worker.
/// </summary>
public enum HandState
{
    /// <summary>Hand is running and waiting for its next cycle.</summary>
    Active,

    /// <summary>Hand is currently executing a cycle.</summary>
    Running,

    /// <summary>Hand has been manually paused.</summary>
    Paused,

    /// <summary>Hand has been manually stopped. Progress history is retained.</summary>
    Stopped,

    /// <summary>Hand stopped automatically after exceeding the maximum consecutive failure count.</summary>
    Failed
}
