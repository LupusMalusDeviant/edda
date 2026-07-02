using System.Net;
using System.Net.Http.Json;
using Edda.Core.Models;
using Edda.Hosting.Tests.Infrastructure;

namespace Edda.Hosting.Tests;

/// <summary>Integration tests for the AKG rule endpoints against the in-memory graph provider.</summary>
public sealed class AkgRulesEndpointTests
{
    private static KnowledgeRule TestRule(string id) => new()
    {
        Id = id,
        Type = "Rule",
        Domain = "testing",
        Priority = RulePriority.Medium,
        Body = "A rule created by the D3 integration tests.",
    };

    [Fact]
    public async Task GetRules_ReturnsOkJsonArray()
    {
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", null));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/akg/rules");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.TrimStart().Should().StartWith("[");   // a JSON array (empty or with seed rules)
    }

    [Fact]
    public async Task GetStats_ReturnsOk()
    {
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", null));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/akg/stats");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProposeThenGet_RoundTripsTheRule()
    {
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", null));
        var client = factory.CreateClient();

        var propose = await client.PostAsJsonAsync("/api/akg/propose", TestRule("d3-roundtrip-rule"));
        propose.StatusCode.Should().Be(HttpStatusCode.Created);

        var get = await client.GetAsync("/api/akg/rules/d3-roundtrip-rule");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        (await get.Content.ReadAsStringAsync()).Should().Contain("d3-roundtrip-rule");
    }

    [Fact]
    public async Task DeleteRule_ThenGet_ReturnsNotFound()
    {
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", null));
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/akg/propose", TestRule("d3-delete-rule"));

        var delete = await client.DeleteAsync("/api/akg/rules/d3-delete-rule");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await client.GetAsync("/api/akg/rules/d3-delete-rule");
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EmbedRebuild_AdminOnly_LocalUserIsAdmin_ReturnsAccepted()
    {
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", null));
        var client = factory.CreateClient();

        // AdminOnly endpoint; the local single-user has the admin role, so the policy passes → 202 Accepted.
        var response = await client.PostAsync("/api/akg/embed/rebuild", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }
}
