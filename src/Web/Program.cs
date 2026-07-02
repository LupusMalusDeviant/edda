using System.Threading.RateLimiting;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Edda.Gateway.Api;
using Edda.Web.Components;
using Edda.Web.Services;
using Edda.Hosting.Authentication;
using Edda.Hosting.DependencyInjection;
using Edda.Security.Authentication;
using Edda.Security.Networking;

var builder = WebApplication.CreateBuilder(args);

// Optional JSON config file (kept for parity; harmless if absent).
builder.Configuration.AddJsonFile("data/agent-config.json", optional: true, reloadOnChange: false);

// Fail fast when bound to a non-loopback interface without an auth token (issue A4): an
// unauthenticated API+UI reachable off-host is almost always a misconfiguration. EDDA_BIND is the
// documented host-bind knob (docker publish / install scripts); for a direct `dotnet run` the
// process binds per ASPNETCORE_URLS, so fall back to that when EDDA_BIND is unset. Inside the
// container EDDA_BIND is always provided (see docker-compose.yml), so the container's internal
// all-interfaces Kestrel bind is never mistaken for remote exposure. Override deliberately with
// EDDA_ALLOW_INSECURE_REMOTE=true (e.g. when a trusted reverse proxy handles authentication).
var configuredBind = builder.Configuration["EDDA_BIND"];
if (string.IsNullOrWhiteSpace(configuredBind))
{
    configuredBind = builder.Configuration["ASPNETCORE_URLS"];
}

var allowInsecureRemote = string.Equals(
    builder.Configuration["EDDA_ALLOW_INSECURE_REMOTE"], "true", StringComparison.OrdinalIgnoreCase);

if (RemoteBindGuard.IsInsecureRemoteBind(
        configuredBind, builder.Configuration["EDDA_AUTH_TOKEN"], allowInsecureRemote))
{
    throw new InvalidOperationException(
        $"Refusing to start: bound to a non-loopback address ('{configuredBind}') without " +
        "EDDA_AUTH_TOKEN. This would expose the API and UI without authentication. Set " +
        "EDDA_AUTH_TOKEN, bind to loopback (EDDA_BIND=127.0.0.1), or set " +
        "EDDA_ALLOW_INSECURE_REMOTE=true to override deliberately.");
}

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

// Rate limiting on the /api and /mcp surface (issue A2): opt-in via EDDA_RATE_LIMIT_PER_MINUTE
// (0 or unset = off, preserving the historical unlimited behaviour). A fixed window per client IP
// caps token brute-force and request floods. The interactive Blazor Server circuit is excluded (see
// the path-scoped middleware below), so real-time UI sessions stay unaffected.
var rateLimitOptions = RateLimitOptions.Parse(builder.Configuration["EDDA_RATE_LIMIT_PER_MINUTE"]);
if (rateLimitOptions.IsEnabled)
{
    builder.Services.AddRateLimiter(limiterOptions =>
    {
        limiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        limiterOptions.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitOptions.PermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
    });
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// Apply the IP rate limiter to the API and MCP surface only, ahead of authentication so that
// unauthenticated brute-force attempts are throttled as well. The interactive UI and Blazor Server
// circuit are never routed through this branch, so real-time sessions stay unaffected (issue A2).
if (rateLimitOptions.IsEnabled)
{
    app.UseWhen(
        context => context.Request.Path.StartsWithSegments("/api")
                   || context.Request.Path.StartsWithSegments("/mcp"),
        branch => branch.UseRateLimiter());
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
            var token = BearerTokenParser.Parse(context.Request.Headers.Authorization.ToString());

            // A6: the '?token=' query parameter is no longer accepted — query strings leak into server
            // logs, browser history, and referrers. Only the Authorization header authenticates. Warn
            // when a legacy client still sends '?token=' so operators can migrate. (stdio MCP is a
            // separate host and does not pass through this HTTP gate.)
            if (context.Request.Query.ContainsKey("token"))
            {
                context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Edda.Web.McpAuth")
                    .LogWarning(
                        "Ignoring deprecated '?token=' query parameter on {Path}; " +
                        "use the 'Authorization: Bearer' header instead. | Mcp",
                        context.Request.Path);
            }

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
