using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Configuration;

namespace Edda.Gateway.Api;

/// <summary>
/// Handler for the knowledge-ingestion endpoint. A thin wrapper over <see cref="IIngestionPipeline"/>,
/// which collects per-item errors and never throws.
/// </summary>
internal static class IngestionEndpointHandlers
{
    /// <summary>
    /// Ingests knowledge from the requested source (admin-only). Gated behind the
    /// <c>ENABLE_INGESTION</c> flag; the central authorizer (C2) additionally requires the Owner role.
    /// The repository URL comes from the request and the server clones it, so the endpoint is
    /// restricted to administrators (SSRF consideration — a host allow-list can be layered on later).
    /// Credentials are never accepted from the request; they come from configuration.
    /// </summary>
    /// <param name="request">The ingestion request (source kind, source config, type mapping, enrichment).</param>
    /// <param name="identity">Authenticated caller identity.</param>
    /// <param name="authorizer">Central role enforcement (C2).</param>
    /// <param name="pipeline">The ingestion pipeline.</param>
    /// <param name="settings">Runtime settings; a persisted <c>EnableIngestion</c> override wins over the environment flag.</param>
    /// <param name="configuration">Configuration, used as fallback for the <c>ENABLE_INGESTION</c> flag.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// 200 OK with the ingestion result, 403 when the role does not permit administration,
    /// or 503 when ingestion is disabled.
    /// </returns>
    internal static async Task<IResult> IngestAsync(
        IngestionRequest request,
        IIdentityContext identity,
        IRuleAuthorizer authorizer,
        IIngestionPipeline pipeline,
        ISettingsService settings,
        IConfiguration configuration,
        CancellationToken ct)
    {
        // C2: the route policy authenticates; the authorizer enforces the tenant role centrally.
        try
        {
            authorizer.EnsureCanAdminister(identity.IsAdmin);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }

        // Live-apply: a persisted UI override wins; a null override falls back to ENABLE_INGESTION.
        var enabled = settings.Current.General.EnableIngestion
            ?? string.Equals(configuration["ENABLE_INGESTION"], "true", StringComparison.OrdinalIgnoreCase);
        if (!enabled)
        {
            return Results.Problem(
                detail: "Ingestion is disabled. Enable it under Settings or set ENABLE_INGESTION=true.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        // The public endpoint never accepts credentials from the client; connectors supply them server-side.
        var sanitized = request with { Source = request.Source with { Token = null, Username = null } };
        var result = await pipeline.IngestAsync(sanitized, ct);
        return Results.Ok(result);
    }
}
