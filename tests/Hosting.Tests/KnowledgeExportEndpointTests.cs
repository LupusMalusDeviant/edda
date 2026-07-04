using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Edda.Hosting.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Edda.Hosting.Tests;

/// <summary>
/// Integration tests for the knowledge-bundle interchange (ADR-0007): the export endpoint emits a portable
/// <see cref="KnowledgeBundle"/>, and that bundle re-imports faithfully through the real importer — closing
/// the export↔import round-trip end to end (import itself has no REST endpoint; the UI drives it directly).
/// </summary>
public sealed class KnowledgeExportEndpointTests
{
    private static KnowledgeRule TestRule(string id) => new()
    {
        Id = id,
        Type = "Guideline",
        Domain = "interchange",
        Priority = RulePriority.High,
        Body = "A rule for the ADR-0007 export round-trip.",
    };

    [Fact]
    public async Task Export_ReturnsBundle_ContainingProposedRule()
    {
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", null));
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/akg/propose", TestRule("adr7-export-rule"));

        var response = await client.GetAsync("/api/knowledge/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var bundle = JsonSerializer.Deserialize<KnowledgeBundle>(json, KnowledgeBundleSerialization.Options);
        bundle.Should().NotBeNull();
        bundle!.SchemaVersion.Should().Be(1);
        bundle.Rules.Should().Contain(r => r.Id == "adr7-export-rule" && r.Priority == RulePriority.High);
    }

    [Fact]
    public async Task ExportedBundle_ReimportsFaithfully()
    {
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", null));
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/akg/propose", TestRule("adr7-roundtrip-rule"));

        var export = await client.GetAsync("/api/knowledge/export");
        var json = await export.Content.ReadAsStringAsync();

        // Feed the exported bundle back through the real importer (the same wiring the UI uses).
        var importer = factory.Services.GetRequiredService<IKnowledgeImporter>();
        var result = await importer.ImportAsync(
            "edda-knowledge-bundle.json", Encoding.UTF8.GetBytes(json), domain: null);

        result.Failed.Should().Be(0);
        result.Imported.Should().BeGreaterThan(0);
    }
}
