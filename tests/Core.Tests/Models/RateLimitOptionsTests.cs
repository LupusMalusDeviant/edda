using Edda.Core.Models;

namespace Edda.Core.Tests.Models;

/// <summary>
/// Unit tests for <see cref="RateLimitOptions"/> binding from the raw configuration value.
/// </summary>
public class RateLimitOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-100")]
    [InlineData("abc")]
    [InlineData("10.5")]
    [InlineData("100x")]
    public void Parse_MissingOrNonPositiveOrInvalidValue_IsDisabled(string? raw)
    {
        var options = RateLimitOptions.Parse(raw);

        options.PermitsPerMinute.Should().Be(RateLimitOptions.Disabled);
        options.IsEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("100", 100)]
    [InlineData("5000", 5000)]
    public void Parse_PositiveValue_EnablesWithThatLimit(string raw, int expected)
    {
        var options = RateLimitOptions.Parse(raw);

        options.PermitsPerMinute.Should().Be(expected);
        options.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Parse_ValueWithSurroundingWhitespace_IsParsed()
    {
        // int.TryParse trims surrounding whitespace by default.
        RateLimitOptions.Parse(" 100 ").PermitsPerMinute.Should().Be(100);
    }

    [Fact]
    public void Default_Instance_IsDisabled()
    {
        var options = new RateLimitOptions();

        options.PermitsPerMinute.Should().Be(RateLimitOptions.Disabled);
        options.IsEnabled.Should().BeFalse();
    }
}
