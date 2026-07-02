using System.Net;
using Edda.Hosting.Tests.Infrastructure;

namespace Edda.Hosting.Tests;

/// <summary>Integration tests for the anonymous <c>/health</c> endpoint.</summary>
public sealed class HealthEndpointTests
{
    [Fact]
    public async Task GetHealth_NoToken_ReturnsOk()
    {
        using var factory = new HostingTestFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHealth_WithTokenConfigured_StillAnonymous()
    {
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", "secret"));
        var client = factory.CreateClient();

        // No Authorization header — /health is AllowAnonymous even when a token is configured.
        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
