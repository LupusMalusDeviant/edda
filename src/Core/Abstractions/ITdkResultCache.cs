using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Caches TDK validator outcomes keyed by the identity of a (rule × validator × code block) tuple, so the
/// engine can reuse a previously computed result instead of re-running the sandbox for an identical
/// re-validation (issue F13). Matters for agent loops that validate the same code repeatedly.
/// Implementations must be safe for concurrent use.
/// </summary>
public interface ITdkResultCache
{
    /// <summary>Returns the cached outcome for <paramref name="key"/>, or <see langword="null"/> on a miss.</summary>
    /// <param name="key">The opaque cache key identifying the (rule × validator × block) tuple.</param>
    /// <returns>The cached outcome, or <see langword="null"/> when the key is not cached.</returns>
    TdkCachedOutcome? Get(string key);

    /// <summary>Stores <paramref name="outcome"/> under <paramref name="key"/>.</summary>
    /// <param name="key">The opaque cache key identifying the (rule × validator × block) tuple.</param>
    /// <param name="outcome">The validator outcome to cache.</param>
    void Set(string key, TdkCachedOutcome outcome);
}
