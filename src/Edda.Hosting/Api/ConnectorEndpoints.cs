using Edda.Core.Abstractions;
using Edda.Core.Models;
using Edda.Security.Credentials;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Edda.Gateway.Api;

/// <summary>
/// REST endpoints for managing knowledge-source connectors and their configured instances, mirroring the
/// Sources UI so sources can be listed, created, updated, deleted and run over the API (e.g. for scripting
/// or other agents). Reads require authentication; writes require the "AdminOnly" policy. Secrets are
/// write-only — stored in the credential store under <c>{userId}:source:{id}:{field}</c>, never returned
/// and never part of the persisted (non-secret) settings.
/// </summary>
public static class ConnectorEndpoints
{
    /// <summary>Maps the connector and source-instance endpoints onto the given route builder.</summary>
    /// <param name="app">The endpoint route builder to extend.</param>
    /// <returns>The route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapConnectorEndpoints(this IEndpointRouteBuilder app)
    {
        // Available connector types (descriptors with their fields), so a client can render/validate input.
        app.MapGet("/api/connectors", (IConnectorRegistry registry) => Results.Ok(registry.Describe()))
           .WithName("ConnectorsList")
           .RequireAuthorization();

        // Configured source instances (non-secret values only).
        app.MapGet("/api/sources", (ISettingsService settings) => Results.Ok(settings.Current.Sources))
           .WithName("SourcesList")
           .RequireAuthorization();

        // Admin-only: create a source instance. Secrets go to the credential store, never into settings.
        app.MapPost("/api/sources",
                async (SaveSourceRequest body, IConnectorRegistry registry, ISettingsService settings,
                       ICredentialStore store, IAuditLog audit, IIdentityContext identity, CancellationToken ct) =>
                {
                    if (!IsKnownType(registry, body.TypeId))
                        return Results.Problem(
                            detail: $"Unknown connector type '{body.TypeId}'.",
                            statusCode: StatusCodes.Status400BadRequest);
                    if (string.IsNullOrWhiteSpace(body.Name))
                        return Results.Problem(
                            detail: "Name must not be empty.",
                            statusCode: StatusCodes.Status400BadRequest);

                    var userId = identity.UserId ?? "local";
                    var id = Guid.NewGuid().ToString("N");
                    await StoreSecretsAsync(store, userId, id, body.Secrets, ct);

                    var instance = new ConnectorInstanceConfig
                    {
                        Id = id,
                        TypeId = body.TypeId,
                        Name = body.Name.Trim(),
                        Values = body.Values ?? new Dictionary<string, string>(),
                    };
                    var current = settings.Current;
                    await settings.SaveAsync(current with { Sources = current.Sources.Append(instance).ToList() }, ct);
                    await audit.LogAsync(AuditEvent.ConfigChanged, userId,
                        $"Knowledge source created via API: {instance.Name} ({instance.TypeId})", cancellationToken: ct);
                    return Results.Created($"/api/sources/{id}", instance);
                })
           .WithName("SourcesCreate")
           .RequireAuthorization("AdminOnly");

        // Admin-only: update an existing source instance (id immutable). Provided secrets overwrite stored ones.
        app.MapPut("/api/sources/{id}",
                async (string id, SaveSourceRequest body, IConnectorRegistry registry, ISettingsService settings,
                       ICredentialStore store, IAuditLog audit, IIdentityContext identity, CancellationToken ct) =>
                {
                    var current = settings.Current;
                    var existing = current.Sources.FirstOrDefault(s => s.Id == id);
                    if (existing is null)
                        return Results.NotFound();
                    if (!IsKnownType(registry, body.TypeId))
                        return Results.Problem(
                            detail: $"Unknown connector type '{body.TypeId}'.",
                            statusCode: StatusCodes.Status400BadRequest);

                    var userId = identity.UserId ?? "local";
                    await StoreSecretsAsync(store, userId, id, body.Secrets, ct);

                    var updated = new ConnectorInstanceConfig
                    {
                        Id = id,
                        TypeId = body.TypeId,
                        Name = string.IsNullOrWhiteSpace(body.Name) ? existing.Name : body.Name.Trim(),
                        Values = body.Values ?? existing.Values,
                    };
                    await settings.SaveAsync(
                        current with { Sources = current.Sources.Where(s => s.Id != id).Append(updated).ToList() }, ct);
                    await audit.LogAsync(AuditEvent.ConfigChanged, userId,
                        $"Knowledge source updated via API: {updated.Name}", cancellationToken: ct);
                    return Results.Ok(updated);
                })
           .WithName("SourcesUpdate")
           .RequireAuthorization("AdminOnly");

        // Admin-only: delete a source instance and best-effort remove its per-instance secrets.
        app.MapDelete("/api/sources/{id}",
                async (string id, IConnectorRegistry registry, ISettingsService settings,
                       ICredentialStore store, IAuditLog audit, IIdentityContext identity, CancellationToken ct) =>
                {
                    var current = settings.Current;
                    var existing = current.Sources.FirstOrDefault(s => s.Id == id);
                    if (existing is null)
                        return Results.NotFound();

                    var userId = identity.UserId ?? "local";
                    await settings.SaveAsync(current with { Sources = current.Sources.Where(s => s.Id != id).ToList() }, ct);

                    var descriptor = registry.Describe()
                        .FirstOrDefault(d => string.Equals(d.TypeId, existing.TypeId, StringComparison.OrdinalIgnoreCase));
                    foreach (var field in (descriptor?.Fields ?? []).Where(f => f.Type == ConnectorFieldType.Secret))
                    {
                        var name = $"source:{id}:{field.Key}";
                        if (CredentialKeyScheme.IsValidName(name))
                            await store.DeleteAsync(CredentialKeyScheme.Scope(userId, name), ct);
                    }

                    await audit.LogAsync(AuditEvent.ConfigChanged, userId,
                        $"Knowledge source deleted via API: {existing.Name}", cancellationToken: ct);
                    return Results.NoContent();
                })
           .WithName("SourcesDelete")
           .RequireAuthorization("AdminOnly");

        // Admin-only: run the configured source now via its connector (server-side credentials).
        app.MapPost("/api/sources/{id}/run",
                async (string id, IConnectorRegistry registry, ISettingsService settings,
                       IAuditLog audit, IIdentityContext identity, CancellationToken ct) =>
                {
                    var existing = settings.Current.Sources.FirstOrDefault(s => s.Id == id);
                    if (existing is null)
                        return Results.NotFound();

                    var result = await registry.RunAsync(existing, ct);
                    await audit.LogAsync(AuditEvent.ConfigChanged, identity.UserId ?? "local",
                        $"Knowledge source run via API: {existing.Name}", cancellationToken: ct);
                    return Results.Ok(result);
                })
           .WithName("SourcesRun")
           .RequireAuthorization("AdminOnly");

        return app;
    }

    private static bool IsKnownType(IConnectorRegistry registry, string? typeId)
        => !string.IsNullOrWhiteSpace(typeId)
           && registry.Describe().Any(d => string.Equals(d.TypeId, typeId, StringComparison.OrdinalIgnoreCase));

    private static async Task StoreSecretsAsync(
        ICredentialStore store,
        string userId,
        string id,
        IReadOnlyDictionary<string, string>? secrets,
        CancellationToken ct)
    {
        if (secrets is null)
            return;

        foreach (var (field, value) in secrets)
        {
            if (string.IsNullOrEmpty(value))
                continue;
            var name = $"source:{id}:{field}";
            if (CredentialKeyScheme.IsValidName(name))
                await store.StoreAsync(CredentialKeyScheme.Scope(userId, name), value, ct);
        }
    }
}

/// <summary>Request body for creating or updating a knowledge-source instance.</summary>
/// <param name="TypeId">The connector type id (must match a registered connector).</param>
/// <param name="Name">Human-readable instance name.</param>
/// <param name="Values">Non-secret field values keyed by field key.</param>
/// <param name="Secrets">Secret field values (e.g. tokens); stored encrypted, never persisted in settings.</param>
internal sealed record SaveSourceRequest(
    string TypeId,
    string Name,
    IReadOnlyDictionary<string, string>? Values,
    IReadOnlyDictionary<string, string>? Secrets);
