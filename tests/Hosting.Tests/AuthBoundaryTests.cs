using System.Net;
using System.Net.Http.Headers;
using Edda.Hosting.Tests.Infrastructure;

namespace Edda.Hosting.Tests;

/// <summary>Integration tests for the EDDA_AUTH_TOKEN bearer boundary on the authenticated API surface.</summary>
public sealed class AuthBoundaryTests
{
    private const string Token = "secret-test-token";

    [Fact]
    public async Task Rules_TokenConfigured_ValidBearer_ReturnsOk()
    {
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", Token));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        var response = await client.GetAsync("/api/akg/rules");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Rules_TokenConfigured_NoHeader_ReturnsUnauthorized()
    {
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", Token));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/akg/rules");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Rules_TokenConfigured_WrongBearer_ReturnsUnauthorized()
    {
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", Token));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");

        var response = await client.GetAsync("/api/akg/rules");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Rules_NoTokenConfigured_ReturnsOk()
    {
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", null));
        var client = factory.CreateClient();

        // No token configured → the loopback single-user is authenticated as admin without a header.
        var response = await client.GetAsync("/api/akg/rules");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
