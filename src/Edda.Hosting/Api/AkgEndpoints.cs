using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Edda.Gateway.Api;

/// <summary>
/// REST endpoint registration for the Agent Knowledge Graph (AKG) in the standalone deployment.
/// All endpoints require authentication; admin-only operations require the "AdminOnly" policy
/// (both satisfied by the local single-user scheme). Chat-LLM-dependent endpoints (entity
/// ingestion) and the short-term-memory benchmark are intentionally omitted from this build.
/// </summary>
public static class AkgEndpoints
{
    /// <summary>
    /// Maps the AKG API endpoints onto the given route builder.
    /// </summary>
    /// <param name="app">The endpoint route builder to extend.</param>
    /// <returns>The route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapAkgEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/akg/rules", AkgEndpointHandlers.GetRulesAsync)
           .WithName("AkgGetRules")
           .RequireAuthorization();

        app.MapGet("/api/akg/rules/{id}", AkgEndpointHandlers.GetRuleAsync)
           .WithName("AkgGetRule")
           .RequireAuthorization();

        app.MapGet("/api/akg/rules/{id}/neighbors", AkgEndpointHandlers.GetNeighborsAsync)
           .WithName("AkgGetNeighbors")
           .RequireAuthorization();

        app.MapGet("/api/akg/stats", AkgEndpointHandlers.GetStatsAsync)
           .WithName("AkgGetStats")
           .RequireAuthorization();

        // Admin-only: reloads all rules from the knowledge/ directory.
        app.MapPost("/api/akg/reload", AkgEndpointHandlers.ReloadAsync)
           .WithName("AkgReload")
           .RequireAuthorization("AdminOnly");

        app.MapPost("/api/akg/propose", AkgEndpointHandlers.ProposeRuleAsync)
           .WithName("AkgPropose")
           .RequireAuthorization();

        // Non-admins may only delete their own rules; admins may delete any rule.
        app.MapDelete("/api/akg/rules/{id}", AkgEndpointHandlers.DeleteRuleAsync)
           .WithName("AkgDeleteRule")
           .RequireAuthorization();

        app.MapGet("/api/akg/context", AkgEndpointHandlers.GetContextAsync)
           .WithName("AkgGetContext")
           .RequireAuthorization();

        // Admin-only: deletes all :WorldKnowledge nodes and re-seeds from knowledge/world/.
        app.MapPost("/api/akg/world-knowledge/reload",
                async (IKnowledgeGraph graph, CancellationToken ct) =>
                {
                    var count = await graph.ReloadWorldKnowledgeAsync(ct);
                    return Results.Ok(new { loaded = count });
                })
           .WithName("AkgReloadWorldKnowledge")
           .RequireAuthorization("AdminOnly");

        // Admin-only: clears all rule embeddings and rebuilds them (e.g. after an embedding-model
        // change). Re-embedding can take minutes on CPU, so it runs in the background → 202 Accepted.
        app.MapPost("/api/akg/embed/rebuild",
                (IKnowledgeGraph graph) =>
                {
                    _ = Task.Run(() => graph.ResetAndRebuildEmbeddingsAsync(CancellationToken.None));
                    return Results.Accepted(value: new { status = "embedding rebuild started" });
                })
           .WithName("AkgRebuildEmbeddings")
           .RequireAuthorization("AdminOnly");

        // Admin-only: runs a retrieval benchmark over the context-compilation pipeline and returns
        // Recall@k / Precision@k / MRR / nDCG@k plus latency percentiles for the posted dataset.
        app.MapPost("/api/akg/benchmark",
                async (BenchmarkDataset dataset, int? k, IBenchmarkRunner runner, CancellationToken ct) =>
                {
                    var report = await runner.RunAsync(dataset, k ?? 10, ct);
                    return Results.Ok(report);
                })
           .WithName("AkgBenchmark")
           .RequireAuthorization("AdminOnly");

        // Admin-only: ingests knowledge from an external source (e.g. a Git repository) into the graph.
        app.MapPost("/api/akg/ingest", IngestionEndpointHandlers.IngestAsync)
           .WithName("AkgIngest")
           .RequireAuthorization("AdminOnly");

        return app;
    }
}
