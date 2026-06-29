using Edda.Core.Benchmark;
using FluentAssertions;

namespace Edda.Core.Tests.Benchmark;

public class TokenEstimatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Estimate_NullOrEmpty_ReturnsZero(string? text)
    {
        TokenEstimator.Estimate(text).Should().Be(0);
    }

    [Theory]
    [InlineData("abcd", 1)]       // 4 chars / 4 = 1
    [InlineData("abcde", 2)]      // ceil(5 / 4) = 2
    [InlineData("a", 1)]          // ceil(1 / 4) = 1
    [InlineData("12345678", 2)]   // 8 / 4 = 2
    public void Estimate_KnownLength_ReturnsCeilingOfQuarter(string text, int expected)
    {
        TokenEstimator.Estimate(text).Should().Be(expected);
    }
}
