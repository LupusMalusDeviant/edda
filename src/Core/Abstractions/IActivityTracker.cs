using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Tracks the live state of long-running background activities (import, chunking, embedding)
/// so the UI can render a single global, cross-page progress indicator. Implementations must
/// be thread-safe: activities are reported both from background tasks and from UI circuits.
/// </summary>
public interface IActivityTracker
{
    /// <summary>Raised whenever any activity's state changes.</summary>
    event Action? Changed;

    /// <summary>A point-in-time snapshot of every tracked activity's current state.</summary>
    IReadOnlyList<ActivitySnapshot> Snapshots { get; }

    /// <summary>
    /// Records the current <paramref name="state"/> of the given activity <paramref name="kind"/>
    /// and raises <see cref="Changed"/>.
    /// </summary>
    /// <param name="kind">The activity being reported.</param>
    /// <param name="state">Its current lifecycle state.</param>
    /// <param name="detail">Optional human-readable detail (e.g. a source name or a count).</param>
    /// <param name="onCancel">
    /// Optional callback that aborts the running activity (e.g. cancels a token source). Registered while
    /// the activity is <see cref="ActivityState.Running"/>; supply it on the initial running report. A
    /// running report with no callback keeps any previously registered one (so progress updates need not
    /// re-supply it).
    /// </param>
    void Report(ActivityKind kind, ActivityState state, string? detail = null, Action? onCancel = null);

    /// <summary>
    /// Invokes the cancel callback registered for the given <paramref name="kind"/> (no-op if none).
    /// Best-effort — callback exceptions are swallowed.
    /// </summary>
    /// <param name="kind">The activity to cancel.</param>
    void Cancel(ActivityKind kind);
}
