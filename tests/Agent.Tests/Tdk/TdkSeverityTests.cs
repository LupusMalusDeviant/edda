using Edda.Agent.Tdk;

namespace Edda.Agent.Tests.Tdk;

/// <summary>Unit tests for <see cref="TdkSeverity"/>: severity ranking and normalisation (F10).</summary>
public class TdkSeverityTests
{
    [Theory]
    [InlineData("error", 0)]
    [InlineData("ERROR", 0)]
    [InlineData(" Error ", 0)]
    [InlineData("warning", 1)]
    [InlineData("info", 2)]
    [InlineData("note", 3)]
    [InlineData("", 3)]
    [InlineData(null, 3)]
    public void Rank_ReturnsSeverityOrder(string? severity, int expected)
        => TdkSeverity.Rank(severity).Should().Be(expected);

    [Fact]
    public void Rank_OrdersErrorBeforeWarningBeforeInfoBeforeUnknown()
    {
        TdkSeverity.Rank("error").Should().BeLessThan(TdkSeverity.Rank("warning"));
        TdkSeverity.Rank("warning").Should().BeLessThan(TdkSeverity.Rank("info"));
        TdkSeverity.Rank("info").Should().BeLessThan(TdkSeverity.Rank("something-else"));
    }

    [Theory]
    [InlineData("  ERROR ", "error")]
    [InlineData("Warning", "warning")]
    [InlineData(null, "")]
    public void Normalize_TrimsAndLowercases(string? input, string expected)
        => TdkSeverity.Normalize(input).Should().Be(expected);
}
