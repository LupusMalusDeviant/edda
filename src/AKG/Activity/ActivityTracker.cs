using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Activity;

/// <summary>
/// Thread-safe, in-memory implementation of <see cref="IActivityTracker"/>. Keeps the latest
/// state per <see cref="ActivityKind"/> and raises <see cref="Changed"/> on every update.
/// Registered as a singleton so background tasks (embedding rebuild) and UI circuits share one
/// view of progress.
/// </summary>
public sealed class ActivityTracker : IActivityTracker
{
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private readonly Dictionary<ActivityKind, (ActivitySnapshot Snapshot, Action? OnCancel)> _states = new();

    /// <summary>Initializes a new instance of the <see cref="ActivityTracker"/> class.</summary>
    /// <param name="timeProvider">Clock used to timestamp state changes.</param>
    public ActivityTracker(TimeProvider timeProvider) => _timeProvider = timeProvider;

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public IReadOnlyList<ActivitySnapshot> Snapshots
    {
        get
        {
            lock (_gate)
            {
                return _states.Values.Select(v => v.Snapshot).ToList();
            }
        }
    }

    /// <inheritdoc />
    public void Report(ActivityKind kind, ActivityState state, string? detail = null, Action? onCancel = null)
    {
        lock (_gate)
        {
            // Keep a previously registered cancel callback if this (running) report doesn't supply one,
            // so progress updates need not re-pass it. Terminal states clear the callback entirely.
            var existing = _states.TryGetValue(kind, out var prev) ? prev.OnCancel : null;
            var cancel = state == ActivityState.Running ? (onCancel ?? existing) : null;
            var snapshot = new ActivitySnapshot(kind, state, detail, _timeProvider.GetUtcNow(), cancel is not null);
            _states[kind] = (snapshot, cancel);
        }

        NotifyChanged();
    }

    /// <inheritdoc />
    public void Cancel(ActivityKind kind)
    {
        Action? onCancel;
        lock (_gate)
        {
            onCancel = _states.TryGetValue(kind, out var entry) ? entry.OnCancel : null;
        }

        try
        {
            onCancel?.Invoke();
        }
        catch
        {
            // Cancellation is best-effort — a failing callback must not surface to the UI.
        }
    }

    private void NotifyChanged()
    {
        // Notify subscribers defensively: this is called from background tasks (embedding rebuild),
        // so a stale or disposed UI circuit must never abort the reporting caller.
        foreach (var handler in Changed?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action)handler)();
            }
            catch
            {
                // A failed listener (e.g. a torn-down Blazor circuit) must not break import/embedding.
            }
        }
    }
}
