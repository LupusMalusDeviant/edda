using Edda.Agent.Tdk;
using Edda.Core.Models;

namespace Edda.Agent.Tests.Tdk;

/// <summary>Unit tests for <see cref="InMemoryTdkResultCache"/> (F13).</summary>
public class InMemoryTdkResultCacheTests
{
    [Fact]
    public void Get_MissingKey_ReturnsNull()
        => new InMemoryTdkResultCache().Get("nope").Should().BeNull();

    [Fact]
    public void Set_ThenGet_ReturnsSameOutcome()
    {
        var cache = new InMemoryTdkResultCache();
        var outcome = new TdkCachedOutcome(false, [new TdkViolation("r1", "m", "error")]);

        cache.Set("k", outcome);

        cache.Get("k").Should().BeSameAs(outcome);
    }

    [Fact]
    public void Set_ExistingKey_OverwritesInPlace()
    {
        var cache = new InMemoryTdkResultCache();
        cache.Set("k", new TdkCachedOutcome(false, [new TdkViolation("r1", "old", "error")]));

        var updated = new TdkCachedOutcome(true, []);
        cache.Set("k", updated);

        cache.Get("k").Should().BeSameAs(updated);
    }
}
