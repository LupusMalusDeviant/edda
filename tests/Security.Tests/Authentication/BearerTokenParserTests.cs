using Edda.Security.Authentication;

namespace Edda.Security.Tests.Authentication;

/// <summary>Unit tests for <see cref="BearerTokenParser"/>.</summary>
public class BearerTokenParserTests
{
    [Theory]
    [InlineData("Bearer abc123", "abc123")]
    [InlineData("bearer abc123", "abc123")]           // scheme is case-insensitive
    [InlineData("BEARER abc123", "abc123")]
    [InlineData("Bearer   spaced-token  ", "spaced-token")] // surrounding whitespace trimmed
    public void Parse_ValidBearerHeader_ReturnsToken(string header, string expected)
    {
        BearerTokenParser.Parse(header).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer ")]        // empty token
    [InlineData("Bearer     ")]    // whitespace-only token
    [InlineData("Basic abc123")]   // wrong scheme
    [InlineData("Token abc123")]   // wrong scheme
    [InlineData("abc123")]         // no scheme (e.g. a raw query value)
    public void Parse_MissingOrInvalidBearer_ReturnsNull(string? header)
    {
        BearerTokenParser.Parse(header).Should().BeNull();
    }
}
