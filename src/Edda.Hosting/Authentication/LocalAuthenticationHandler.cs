using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Edda.Hosting.Authentication;

/// <summary>
/// Authentication handler for the standalone local deployment.
/// <para>
/// When <c>EDDA_AUTH_TOKEN</c> is unset, every request is authenticated as a single local
/// administrator (intended for loopback-only binding). When the variable is set, the request must
/// present that token as <c>Authorization: Bearer &lt;token&gt;</c> or a <c>?token=</c> query value;
/// otherwise authentication fails with 401.
/// </para>
/// </summary>
public sealed class LocalAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>The authentication scheme name registered by <see cref="LocalAuthExtensions"/>.</summary>
    public const string SchemeName = "EddaLocal";

    private readonly string? _token;

    /// <summary>
    /// Initializes a new <see cref="LocalAuthenticationHandler"/>.
    /// </summary>
    /// <param name="options">Authentication scheme options monitor.</param>
    /// <param name="logger">Logger factory.</param>
    /// <param name="encoder">URL encoder.</param>
    public LocalAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
        _token = Environment.GetEnvironmentVariable("EDDA_AUTH_TOKEN");
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!string.IsNullOrWhiteSpace(_token))
        {
            var provided = ExtractToken();
            if (!string.Equals(provided, _token, StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.Fail("Missing or invalid token."));
        }

        var claims = new[]
        {
            new Claim("sub", "local"),
            new Claim(ClaimTypes.Name, "local"),
            new Claim(ClaimTypes.Role, "admin"),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private string? ExtractToken()
    {
        var auth = Request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return auth["Bearer ".Length..].Trim();

        return Request.Query.TryGetValue("token", out var q) ? q.ToString() : null;
    }
}

/// <summary>
/// DI extension that registers the local authentication scheme and authorization policies.
/// </summary>
public static class LocalAuthExtensions
{
    /// <summary>
    /// Registers the <see cref="LocalAuthenticationHandler"/> scheme plus a default policy and an
    /// <c>AdminOnly</c> policy, both requiring an authenticated user. In the local single-user model
    /// the handler always authenticates (unless a token is configured), so both policies pass.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddEddaLocalAuth(this IServiceCollection services)
    {
        services
            .AddAuthentication(LocalAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, LocalAuthenticationHandler>(
                LocalAuthenticationHandler.SchemeName, _ => { });

        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder(LocalAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser()
                .Build();

            options.AddPolicy("AdminOnly", policy =>
            {
                policy.AddAuthenticationSchemes(LocalAuthenticationHandler.SchemeName);
                policy.RequireAuthenticatedUser();
            });
        });

        return services;
    }
}
