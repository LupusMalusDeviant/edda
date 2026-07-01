using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace Edda.Gateway.Api;

/// <summary>
/// REST endpoint registration for the Agent Knowledge Graph (AKG) in the standalone deployment.
/// All endpoints require authentication; admin-only operations require the "AdminOnly" policy
/// (both satisfied by the local single-user scheme). Entity ingestion is available as an opt-in,
/// admin-only endpoint (M2 / ADR-0010): it needs a configured LLM provider and is gated behind
/// <c>ENABLE_INGESTION</c>, staying inert otherwise. The short-term-memory benchmark remains omitted.
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

        // Opt-in (ENABLE_INGESTION), admin-only: extracts typed entities/relations from posted text via the
        // LLM entity extractor and persists them into the LightRAG-style entity layer (M2 / ADR-0010). The
        // owner is taken from the authenticated identity (Regel 6), never from the request body.
        app.MapPost("/api/akg/entities/ingest",
                async (EntityIngestRequest request,
                       IEntityIngestionService entities,
                       IIdentityContext identity,
                       ISettingsService settings,
                       IConfiguration configuration,
                       CancellationToken ct) =>
                {
                    var enabled = settings.Current.General.EnableIngestion
                        ?? string.Equals(configuration["ENABLE_INGESTION"], "true", StringComparison.OrdinalIgnoreCase);
                    if (!enabled)
                    {
                        return Results.Problem(
                            detail: "Ingestion is disabled. Enable it under Settings or set ENABLE_INGESTION=true.",
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                    }

                    var userId = identity.UserId ?? "local";
                    var result = await entities.IngestTextAsync(request.Text, request.DomainHint, userId, "manual", ct);
                    return Results.Ok(result);
                })
           .WithName("AkgIngestEntities")
           .RequireAuthorization("AdminOnly");

        return app;
    }
}
