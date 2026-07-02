using Edda.Core.Models;

namespace Edda.Core.Tests.Models;

/// <summary>
/// Unit tests for <see cref="IngestionTextValidator"/>.
/// </summary>
public class IngestionTextValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Validate_EmptyOrWhitespace_ReturnsError(string? text)
    {
        IngestionTextValidator.Validate(text, IngestionTextValidator.DefaultMaxChars)
            .Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Validate_TextWithinLimit_ReturnsNull()
    {
        IngestionTextValidator.Validate("A reasonable sentence.", maxChars: 100).Should().BeNull();
    }

    [Fact]
    public void Validate_TextExactlyAtLimit_ReturnsNull()
    {
        var text = new string('x', 50);
        IngestionTextValidator.Validate(text, maxChars: 50).Should().BeNull();
    }

    [Fact]
    public void Validate_TextOneOverLimit_ReturnsError()
    {
        var text = new string('x', 51);
        IngestionTextValidator.Validate(text, maxChars: 50).Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(null, IngestionTextValidator.DefaultMaxChars)]
    [InlineData("", IngestionTextValidator.DefaultMaxChars)]
    [InlineData("   ", IngestionTextValidator.DefaultMaxChars)]
    [InlineData("abc", IngestionTextValidator.DefaultMaxChars)]
    [InlineData("0", IngestionTextValidator.DefaultMaxChars)]
    [InlineData("-5", IngestionTextValidator.DefaultMaxChars)]
    [InlineData("5000", 5000)]
    [InlineData("1", 1)]
    public void ResolveMaxChars_InvalidOrValid_FallsBackToDefaultOrParses(string? raw, int expected)
    {
        IngestionTextValidator.ResolveMaxChars(raw).Should().Be(expected);
    }
}
