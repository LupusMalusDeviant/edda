using System.Net;
using Edda.Hosting.Tests.Infrastructure;

namespace Edda.Hosting.Tests;

/// <summary>Integration tests for the A4 remote-bind startup guard (RemoteBindGuard in Program.cs).</summary>
public sealed class StartupGuardTests
{
    [Fact]
    public void InsecureRemoteBind_WithoutToken_RefusesToStart()
    {
        using var factory = new HostingTestFactory(
            ("EDDA_BIND", "http://0.0.0.0:8080"), ("EDDA_AUTH_TOKEN", null));

        // Building the host runs the A4 guard, which throws for a non-loopback bind without a token.
        var act = () => factory.CreateClient();

        act.Should().Throw<Exception>();
    }

    [Fact]
    public async Task InsecureRemoteBind_WithToken_StartsOk()
    {
        using var factory = new HostingTestFactory(
            ("EDDA_BIND", "http://0.0.0.0:8080"), ("EDDA_AUTH_TOKEN", "secret"));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LoopbackBind_NoToken_StartsOk()
    {
        using var factory = new HostingTestFactory(
            ("EDDA_BIND", "http://127.0.0.1:8080"), ("EDDA_AUTH_TOKEN", null));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
