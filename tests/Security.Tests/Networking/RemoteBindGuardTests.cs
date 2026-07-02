using Edda.Security.Networking;

namespace Edda.Security.Tests.Networking;

/// <summary>
/// Unit tests for <see cref="RemoteBindGuard.IsInsecureRemoteBind(string?, string?, bool)"/>.
/// </summary>
public class RemoteBindGuardTests
{
    [Theory]
    // Loopback binds are safe even without a token.
    [InlineData("127.0.0.1", false)]
    [InlineData("127.0.0.5", false)]      // the whole 127.0.0.0/8 range is loopback
    [InlineData("::1", false)]
    [InlineData("[::1]:8080", false)]
    [InlineData("localhost", false)]
    [InlineData("localhost:8080", false)]
    [InlineData("http://127.0.0.1:8080", false)]
    [InlineData("http://[::1]:8080", false)]
    [InlineData("http://localhost:8080/", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    // Non-loopback / wildcard binds are insecure without a token.
    [InlineData("0.0.0.0", true)]
    [InlineData("0.0.0.0:8080", true)]
    [InlineData("::", true)]
    [InlineData("+", true)]
    [InlineData("http://+:8080", true)]
    [InlineData("http://*:8080", true)]
    [InlineData("http://0.0.0.0:8080", true)]
    [InlineData("192.168.1.10", true)]
    [InlineData("edda.example.com", true)]
    [InlineData("http://127.0.0.1:8080;http://0.0.0.0:8081", true)] // one remote entry is enough
    public void IsInsecureRemoteBind_NoTokenNoOverride_ClassifiesBindByReachability(
        string bind, bool expected)
    {
        RemoteBindGuard.IsInsecureRemoteBind(bind, token: null, allowInsecure: false)
            .Should().Be(expected);
    }

    [Fact]
    public void IsInsecureRemoteBind_NullBindNoToken_ReturnsFalse()
    {
        RemoteBindGuard.IsInsecureRemoteBind(null, token: null, allowInsecure: false)
            .Should().BeFalse();
    }

    [Fact]
    public void IsInsecureRemoteBind_RemoteBindWithToken_ReturnsFalse()
    {
        RemoteBindGuard.IsInsecureRemoteBind("0.0.0.0", token: "a-secret-token", allowInsecure: false)
            .Should().BeFalse();
    }

    [Fact]
    public void IsInsecureRemoteBind_RemoteBindWithWhitespaceToken_ReturnsTrue()
    {
        // A whitespace-only token is not a real token and must not disable the guard.
        RemoteBindGuard.IsInsecureRemoteBind("0.0.0.0", token: "   ", allowInsecure: false)
            .Should().BeTrue();
    }

    [Fact]
    public void IsInsecureRemoteBind_RemoteBindNoTokenButOverride_ReturnsFalse()
    {
        RemoteBindGuard.IsInsecureRemoteBind("0.0.0.0", token: null, allowInsecure: true)
            .Should().BeFalse();
    }

    [Fact]
    public void IsInsecureRemoteBind_OverrideTakesPrecedenceOverMissingToken()
    {
        // Even a clearly remote wildcard bind is allowed once the operator opts in.
        RemoteBindGuard.IsInsecureRemoteBind("http://+:8080", token: "", allowInsecure: true)
            .Should().BeFalse();
    }
}
