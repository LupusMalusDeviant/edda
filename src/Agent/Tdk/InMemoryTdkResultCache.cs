using System.Collections.Concurrent;
using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.Agent.Tdk;

/// <summary>
/// Process-local, thread-safe <see cref="ITdkResultCache"/> backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Bounded by a soft entry cap: once the cap is reached, new keys are not added (existing entries keep being
/// served), which keeps memory bounded without LRU bookkeeping — sufficient for a validation-result cache.
/// </summary>
internal sealed class InMemoryTdkResultCache : ITdkResultCache
{
    /// <summary>Maximum number of distinct cached outcomes before new keys are dropped.</summary>
    private const int MaxEntries = 1000;

    private readonly ConcurrentDictionary<string, TdkCachedOutcome> _cache = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public TdkCachedOutcome? Get(string key) => _cache.TryGetValue(key, out var outcome) ? outcome : null;

    /// <inheritdoc />
    public void Set(string key, TdkCachedOutcome outcome)
    {
        // Refresh an existing key freely; only the introduction of a NEW key is capped, so the cache never
        // grows past the bound but re-validations of already-seen tuples always update in place.
        if (_cache.Count >= MaxEntries && !_cache.ContainsKey(key))
            return;

        _cache[key] = outcome;
    }
}
