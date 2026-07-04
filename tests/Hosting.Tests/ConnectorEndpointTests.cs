using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Edda.Hosting.Tests.Infrastructure;

namespace Edda.Hosting.Tests;

/// <summary>
/// Integration tests for the connector/source REST endpoints (<c>/api/connectors</c>, <c>/api/sources</c>):
/// the connector registry is wired, and the validation (unknown type / empty name → 400) and not-found (404)
/// branches behave. These paths short-circuit before any settings write, so the tests need no isolated
/// settings store; the successful-create path is covered by the connector/registry unit tests.
/// </summary>
public sealed class ConnectorEndpointTests
{
    private static async Task<string> FirstConnectorTypeAsync(HttpClient client)
    {
        using var doc = JsonDocument.Parse(
            await (await client.GetAsync("/api/connectors")).Content.ReadAsStringAsync());
        return doc.RootElement[0].GetProperty("typeId").GetString()!;
    }

    [Fact]
    public async Task ListConnectors_ReturnsRegisteredTypes()
    {
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", null));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/connectors");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CreateSource_UnknownType_ReturnsBadRequest()
    {
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", null));
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/sources", new { typeId = "does-not-exist", name = "X" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateSource_EmptyName_ReturnsBadRequest()
    {
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", null));
        var client = factory.CreateClient();
        var typeId = await FirstConnectorTypeAsync(client);

        var response = await client.PostAsJsonAsync("/api/sources", new { typeId, name = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateSource_Nonexistent_ReturnsNotFound()
    {
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", null));
        var client = factory.CreateClient();
        var typeId = await FirstConnectorTypeAsync(client);

        var response = await client.PutAsJsonAsync("/api/sources/does-not-exist", new { typeId, name = "X" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteSource_Nonexistent_ReturnsNotFound()
    {
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", null));
        var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/sources/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
