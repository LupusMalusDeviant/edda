using Edda.Core.Abstractions;
using Edda.Core.Models;
using Edda.Gateway.Api;
using Edda.Web.Components;
using Edda.Web.Services;
using Edda.Hosting.Authentication;
using Edda.Hosting.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Optional JSON config file (kept for parity; harmless if absent).
builder.Configuration.AddJsonFile("data/agent-config.json", optional: true, reloadOnChange: false);

// Shared AKG + TDK + embeddings + tools + MCP service graph (also used by the stdio host).
builder.Services.AddEddaCore(builder.Configuration);

// Local authentication: loopback-friendly single admin, optional EDDA_AUTH_TOKEN bearer.
// Provides the default + "AdminOnly" policies that the AKG endpoints and /mcp require.
builder.Services.AddEddaLocalAuth();

// Blazor Server UI.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<ILocalizationService, LocalizationService>();
// Connection-test + model-listing for the provider config UI (Ollama /api/tags, OpenAI-compatible /v1/models, …).
builder.Services.AddSingleton<IProviderProbe, ProviderProbe>();
builder.Services.AddHealthChecks();

// MCP server over HTTP/SSE (opt-in via MCP_SERVER_ENABLED). Exposes only allow-listed tools.
var mcpEnabled = string.Equals(
    builder.Configuration["MCP_SERVER_ENABLED"], "true", StringComparison.OrdinalIgnoreCase);
if (mcpEnabled)
{
    builder.Services.AddMcpServer().WithHttpTransport();
    builder.Services.AddEddaMcpHandlers();
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

// MCP gate: every /mcp request must present a valid MCP token; its stored scopes drive which tools are
// exposed. Without a valid token there is no MCP access (401). stdio MCP is local-trusted and unaffected.
if (mcpEnabled)
{
    app.UseWhen(
        context => context.Request.Path.StartsWithSegments("/mcp"),
        branch => branch.Use(async (context, next) =>
        {
            var tokenStore = context.RequestServices.GetRequiredService<IMcpTokenStore>();
            var header = context.Request.Headers.Authorization.ToString();
            var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? header["Bearer ".Length..].Trim()
                : context.Request.Query.TryGetValue("token", out var q) ? q.ToString() : null;

            var scopes = await tokenStore.ResolveAsync(token, context.RequestAborted);
            if (scopes is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("MCP token required.");
                return;
            }

            context.Items[McpTokenScopes.HttpContextItemKey] = scopes;
            await next();
        }));
}

// Static assets with build-time fingerprinting (@Assets[...]): content-hashed URLs + immutable caching,
// so changed CSS/JS busts the browser cache automatically instead of serving a stale file.
app.MapStaticAssets();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapAkgEndpoints();
app.MapSettingsEndpoints();
app.MapConnectorEndpoints();
app.MapKnowledgeExportEndpoints();
if (mcpEnabled)
{
    app.MapMcp("/mcp");
}

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// Load persisted runtime settings into memory at startup (defaults until the first save).
await app.Services.GetRequiredService<ISettingsService>().ReloadAsync();

app.Run();
