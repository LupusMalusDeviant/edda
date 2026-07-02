using Edda.Agent.Tdk;

namespace Edda.Agent.Tests.Tdk;

/// <summary>Unit tests for <see cref="TdkResultCacheKey"/>: stable, collision-resistant cache keys (F13).</summary>
public class TdkResultCacheKeyTests
{
    [Fact]
    public void Compute_SameInputs_ProducesSameKey()
    {
        var a = TdkResultCacheKey.Compute("r1", "script", "python", "code");
        var b = TdkResultCacheKey.Compute("r1", "script", "python", "code");
        a.Should().Be(b);
    }

    [Theory]
    [InlineData("r2", "script", "python", "code")]  // different ruleId
    [InlineData("r1", "script2", "python", "code")] // different validator
    [InlineData("r1", "script", "csharp", "code")]  // different language
    [InlineData("r1", "script", "python", "code2")] // different code
    public void Compute_AnyFieldDiffers_ProducesDifferentKey(string ruleId, string script, string lang, string code)
    {
        var baseline = TdkResultCacheKey.Compute("r1", "script", "python", "code");
        TdkResultCacheKey.Compute(ruleId, script, lang, code).Should().NotBe(baseline);
    }

    [Fact]
    public void Compute_FieldBoundaryShift_DoesNotCollide()
    {
        // ("ab","c",…) must not collide with ("a","bc",…) — the NUL separator keeps field boundaries distinct.
        var k1 = TdkResultCacheKey.Compute("ab", "c", "python", "x");
        var k2 = TdkResultCacheKey.Compute("a", "bc", "python", "x");
        k1.Should().NotBe(k2);
    }

    [Fact]
    public void Compute_ReturnsHexSha256()
        => TdkResultCacheKey.Compute("r", "s", "l", "c").Should().MatchRegex("^[0-9A-F]{64}$");
}
