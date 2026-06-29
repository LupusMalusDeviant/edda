using System.Collections.Concurrent;
using Edda.Core.Abstractions;

namespace Edda.AKG.Confidence;

/// <summary>
/// Thread-safe <see cref="IRuleConfidenceStore"/> implementation using a sliding window
/// of the last 20 validation outcomes per rule.
/// The confidence multiplier is computed as <c>0.3 + (passRate × 0.7)</c>,
/// which yields values in the range [0.3, 1.0].
/// </summary>
public sealed class SlidingWindowRuleConfidenceStore : IRuleConfidenceStore
{
    private const int WindowSize = 20;
    private const double BaseMultiplier = 0.3;
    private const double VariableRange = 0.7;

    private readonly ConcurrentDictionary<string, Queue<bool>> _windows =
        new(StringComparer.Ordinal);

    private readonly object _lock = new();

    /// <inheritdoc />
    public void RecordOutcome(string ruleId, bool passed)
    {
        lock (_lock)
        {
            if (!_windows.TryGetValue(ruleId, out var window))
            {
                window = new Queue<bool>(WindowSize);
                _windows[ruleId] = window;
            }

            if (window.Count >= WindowSize)
                window.Dequeue();

            window.Enqueue(passed);
        }
    }

    /// <inheritdoc />
    public double GetMultiplier(string ruleId)
    {
        lock (_lock)
        {
            if (!_windows.TryGetValue(ruleId, out var window) || window.Count == 0)
                return 1.0;

            var passCount = window.Count(outcome => outcome);
            var passRate = (double)passCount / window.Count;
            return BaseMultiplier + passRate * VariableRange;
        }
    }
}
