using System.Net;
using System.Net.Http.Json;
using Edda.Core.Models;
using Edda.Hosting.Tests.Infrastructure;

namespace Edda.Hosting.Tests;

/// <summary>Integration tests for the E8 batch endpoint <c>POST /api/akg/rules/batch</c>.</summary>
public sealed class BatchEndpointTests
{
    private static KnowledgeRule TestRule(string id) => new()
    {
        Id = id,
        Type = "Rule",
        Domain = "testing",
        Priority = RulePriority.Medium,
        Body = "A rule for the E8 batch endpoint test.",
    };

    [Fact]
    public async Task Batch_EmptyRuleIds_ReturnsProblem()
    {
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", null));
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/akg/rules/batch",
            new { ruleIds = Array.Empty<string>(), operation = "addTag", tag = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Batch_InvalidOperation_ReturnsProblem()
    {
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", null));
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/akg/rules/batch",
            new { ruleIds = new[] { "r1" }, operation = "nope" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Batch_AddTag_RoundTrips()
    {
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", null));
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/akg/propose", TestRule("e8-batch-rule"));

        var batch = await client.PostAsJsonAsync("/api/akg/rules/batch",
            new { ruleIds = new[] { "e8-batch-rule" }, operation = "addTag", tag = "bulk-tag" });
        batch.StatusCode.Should().Be(HttpStatusCode.OK);

        var get = await client.GetAsync("/api/akg/rules/e8-batch-rule");
        (await get.Content.ReadAsStringAsync()).Should().Contain("bulk-tag");
    }
}
