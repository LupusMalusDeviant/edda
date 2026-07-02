using Edda.AKG.Ingestion.Llm;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Edda.Security.Credentials;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Edda.Gateway.Api;

/// <summary>
/// REST endpoints for runtime-editable application settings and encrypted credentials.
/// Reads require authentication; writes require the "AdminOnly" policy. Secrets are write-only —
/// stored values are never returned in plaintext, only their (user-scoped) names.
/// </summary>
public static class SettingsEndpoints
{
    /// <summary>
    /// Maps the settings and credentials endpoints onto the given route builder.
    /// </summary>
    /// <param name="app">The endpoint route builder to extend.</param>
    /// <returns>The route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/settings", (ISettingsService settings) => Results.Ok(settings.Current))
           .WithName("SettingsGet")
           .RequireAuthorization();

        // Admin-only: persists non-secret settings and applies them live (no restart).
        app.MapPut("/api/settings",
                async (EddaSettings updated, ISettingsService settings, IAuditLog audit,
                       IIdentityContext identity, CancellationToken ct) =>
                {
                    await settings.SaveAsync(updated, ct);
                    await audit.LogAsync(AuditEvent.ConfigChanged, identity.UserId ?? "local",
                        "Application settings updated via API", cancellationToken: ct);
                    return Results.Ok(settings.Current);
                })
           .WithName("SettingsPut")
           .RequireAuthorization("AdminOnly");

        // Admin-only: tests an LLM provider configuration with a tiny completion; persists nothing. The key
        // comes from the request body, or from the stored credential for that provider when omitted.
        app.MapPost("/api/settings/llm/test",
                async (LlmTestRequest body, ILlmChatClientFactory factory, ICredentialStore store,
                       IIdentityContext identity, CancellationToken ct) =>
                {
                    if (string.IsNullOrWhiteSpace(body.Provider))
                    {
                        return Results.Problem(
                            detail: "Provider is required.",
                            statusCode: StatusCodes.Status400BadRequest);
                    }

                    var userId = identity.UserId ?? "local";
                    var apiKey = !string.IsNullOrWhiteSpace(body.ApiKey)
                        ? body.ApiKey
                        : await store.RetrieveAsync($"{userId}:llm:{body.Provider}", ct);
                    var accessKeyId = !string.IsNullOrWhiteSpace(body.AccessKeyId)
                        ? body.AccessKeyId
                        : await store.RetrieveAsync($"{userId}:llm:{body.Provider}:accesskey", ct);

                    var config = new LlmProviderConfig
                    {
                        Provider = body.Provider,
                        Model = body.Model,
                        BaseUrl = body.BaseUrl,
                        Region = body.Region,
                        ApiKey = apiKey,
                        AccessKeyId = accessKeyId,
                    };

                    try
                    {
                        var client = factory.Create(config);
                        await client.CompleteAsync(
                            "You are a connectivity test.", "Reply with the single word: ok", ct);
                        return Results.Ok(new { ok = true, provider = client.ProviderName });
                    }
                    catch (Exception ex)
                    {
                        return Results.Ok(new { ok = false, error = ex.Message });
                    }
                })
           .WithName("SettingsLlmTest")
           .RequireAuthorization("AdminOnly");

        // Admin-only: lists the current user's stored credential names (never their values).
        app.MapGet("/api/credentials",
                async (ICredentialStore store, IIdentityContext identity, CancellationToken ct) =>
                {
                    var prefix = (identity.UserId ?? "local") + ":";
                    var keys = await store.ListAsync(ct);
                    var names = keys
                        .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                        .Select(k => k[prefix.Length..])
                        .OrderBy(n => n, StringComparer.Ordinal)
                        .ToList();
                    return Results.Ok(names);
                })
           .WithName("CredentialsList")
           .RequireAuthorization("AdminOnly");

        // Admin-only: stores (or overwrites) a secret under a user-scoped, validated key.
        app.MapPut("/api/credentials/{name}",
                async (string name, SetCredentialRequest body, ICredentialStore store, IAuditLog audit,
                       IIdentityContext identity, CancellationToken ct) =>
                {
                    if (!CredentialKeyScheme.IsValidName(name))
                    {
                        return Results.Problem(
                            detail: "Invalid credential name.",
                            statusCode: StatusCodes.Status400BadRequest);
                    }

                    if (string.IsNullOrEmpty(body.Value))
                    {
                        return Results.Problem(
                            detail: "Value must not be empty.",
                            statusCode: StatusCodes.Status400BadRequest);
                    }

                    var userId = identity.UserId ?? "local";
                    await store.StoreAsync(CredentialKeyScheme.Scope(userId, name), body.Value, ct);
                    await audit.LogAsync(AuditEvent.CredentialAccess, userId,
                        $"Credential stored: {name}", cancellationToken: ct);
                    return Results.NoContent();
                })
           .WithName("CredentialsSet")
           .RequireAuthorization("AdminOnly");

        // Admin-only: deletes a stored secret. No-op if the key does not exist.
        app.MapDelete("/api/credentials/{name}",
                async (string name, ICredentialStore store, IAuditLog audit,
                       IIdentityContext identity, CancellationToken ct) =>
                {
                    if (!CredentialKeyScheme.IsValidName(name))
                    {
                        return Results.Problem(
                            detail: "Invalid credential name.",
                            statusCode: StatusCodes.Status400BadRequest);
                    }

                    var userId = identity.UserId ?? "local";
                    await store.DeleteAsync(CredentialKeyScheme.Scope(userId, name), ct);
                    await audit.LogAsync(AuditEvent.CredentialAccess, userId,
                        $"Credential deleted: {name}", cancellationToken: ct);
                    return Results.NoContent();
                })
           .WithName("CredentialsDelete")
           .RequireAuthorization("AdminOnly");

        return app;
    }
}

/// <summary>
/// Request body for storing a credential value.
/// </summary>
/// <param name="Value">The plaintext secret to encrypt and store.</param>
internal sealed record SetCredentialRequest(string Value);

/// <summary>
/// Request body for testing an LLM provider configuration.
/// </summary>
/// <param name="Provider">Provider key (e.g. "anthropic").</param>
/// <param name="Model">Optional model identifier.</param>
/// <param name="BaseUrl">Optional API base URL.</param>
/// <param name="Region">Optional AWS region (Bedrock).</param>
/// <param name="ApiKey">Optional API key; the stored credential is used when omitted.</param>
/// <param name="AccessKeyId">Optional AWS access key id (Bedrock).</param>
internal sealed record LlmTestRequest(
    string Provider,
    string? Model,
    string? BaseUrl,
    string? Region,
    string? ApiKey,
    string? AccessKeyId);
