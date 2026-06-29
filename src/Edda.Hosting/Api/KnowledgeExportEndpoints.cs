using System.Text;
using System.Text.Json;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Edda.Gateway.Api;

/// <summary>
/// REST endpoint that exports the knowledge graph as a portable <see cref="KnowledgeBundle"/> (JSON
/// download). This is the canonical, lossless interchange format for moving rules between Edda instances;
/// the matching importer re-embeds locally on import (see ADR-0007). Requires authentication.
/// </summary>
public static class KnowledgeExportEndpoints
{
    /// <summary>Maps the knowledge-export endpoint onto the given route builder.</summary>
    /// <param name="app">The endpoint route builder to extend.</param>
    /// <returns>The route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapKnowledgeExportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/knowledge/export",
                async (IKnowledgeGraph graph, IIdentityContext identity, CancellationToken ct) =>
                {
                    var rules = await graph.GetRulesAsync(userId: identity.UserId, cancellationToken: ct);
                    var bundle = new KnowledgeBundle { SchemaVersion = 1, Rules = rules };
                    var json = JsonSerializer.Serialize(bundle, KnowledgeBundleSerialization.Options);
                    return Results.File(Encoding.UTF8.GetBytes(json), "application/json", "edda-knowledge-bundle.json");
                })
           .WithName("KnowledgeExport")
           .RequireAuthorization();

        return app;
    }
}
