namespace Edda.Core.Models;

/// <summary>
/// Represents the resolved identity of an authenticated user, populated from OIDC claims.
/// </summary>
public sealed record UserIdentity
{
    /// <summary>Unique user identifier from the OIDC sub-claim.</summary>
    public required string UserId { get; init; }

    /// <summary>Display name from the preferred_username or name claim.</summary>
    public required string Username { get; init; }

    /// <summary>Tenant identifier for multi-tenancy isolation.</summary>
    public required string TenantId { get; init; }

    /// <summary>All raw claims from the identity token, for advanced authorization logic.</summary>
    public IReadOnlyDictionary<string, string> Claims { get; init; } =
        new Dictionary<string, string>();
}
