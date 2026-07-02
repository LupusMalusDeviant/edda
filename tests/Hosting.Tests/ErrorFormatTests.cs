using System.Net;
using Edda.Hosting.Tests.Infrastructure;

namespace Edda.Hosting.Tests;

/// <summary>Integration tests for the RFC 7807 ProblemDetails error format and 404 handling.</summary>
public sealed class ErrorFormatTests
{
    [Fact]
    public async Task GetRules_InvalidSkip_ReturnsProblemJson()
    {
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", null));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/akg/rules?skip=-1");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task GetRules_TakeTooLarge_ReturnsBadRequest()
    {
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", null));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/akg/rules?take=5000");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetRule_Unknown_ReturnsNotFound()
    {
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", null));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/akg/rules/this-rule-does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
