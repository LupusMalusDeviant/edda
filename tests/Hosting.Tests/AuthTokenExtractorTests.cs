using Edda.Hosting.Authentication;

namespace Edda.Hosting.Tests;

/// <summary>
/// Unit tests for <see cref="AuthTokenExtractor"/> (A6): the Authorization: Bearer header wins and is never
/// flagged; a token is only flagged as query-sourced when it falls back to the deprecated <c>?token=</c> value.
/// </summary>
public sealed class AuthTokenExtractorTests
{
    [Fact]
    public void BearerHeader_ReturnsToken_NotFromQuery()
    {
        var (token, fromQuery) = AuthTokenExtractor.Extract("Bearer abc123", queryToken: null);

        token.Should().Be("abc123");
        fromQuery.Should().BeFalse();
    }

    [Fact]
    public void BearerHeader_IsCaseInsensitiveAndTrimmed()
    {
        var (token, fromQuery) = AuthTokenExtractor.Extract("bearer   abc123", queryToken: null);

        token.Should().Be("abc123");
        fromQuery.Should().BeFalse();
    }

    [Fact]
    public void HeaderTakesPrecedenceOverQuery()
    {
        var (token, fromQuery) = AuthTokenExtractor.Extract("Bearer header-token", "query-token");

        token.Should().Be("header-token");
        fromQuery.Should().BeFalse();
    }

    [Fact]
    public void QueryToken_WhenNoBearerHeader_IsFlaggedAsFromQuery()
    {
        var (token, fromQuery) = AuthTokenExtractor.Extract(authorizationHeader: null, "query-token");

        token.Should().Be("query-token");
        fromQuery.Should().BeTrue();
    }

    [Fact]
    public void NonBearerHeader_FallsBackToQuery()
    {
        var (token, fromQuery) = AuthTokenExtractor.Extract("Basic Zm9v", "query-token");

        token.Should().Be("query-token");
        fromQuery.Should().BeTrue();
    }

    [Fact]
    public void NoInputs_ReturnsNull_NotFromQuery()
    {
        var (token, fromQuery) = AuthTokenExtractor.Extract(authorizationHeader: null, queryToken: null);

        token.Should().BeNull();
        fromQuery.Should().BeFalse();
    }

    [Fact]
    public void EmptyInputs_ReturnNull_NotFromQuery()
    {
        var (token, fromQuery) = AuthTokenExtractor.Extract(string.Empty, string.Empty);

        token.Should().BeNull();
        fromQuery.Should().BeFalse();
    }
}
