using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Represents the identity of the current user or request context.
/// Populated from OIDC JWT claims, or set as a clone identity when AGENT_CLONE=true.
/// </summary>
public interface IIdentityContext
{
    /// <summary>
    /// Unique user identifier from the OIDC sub-claim.
    /// Null only for unauthenticated requests (/health, /api/setup).
    /// </summary>
    string? UserId { get; }

    /// <summary>Display name from the preferred_username or name claim.</summary>
    string? Username { get; }

    /// <summary>
    /// Tenant ID for multi-tenancy isolation.
    /// Populated from a custom claim or defaulted to a system tenant.
    /// </summary>
    string TenantId { get; }

    /// <summary>
    /// True when this agent instance is running as a clone container (AGENT_CLONE=true).
    /// Clone agents skip loading personal files (memory, userdata, learnings).
    /// </summary>
    bool IsClone { get; }

    /// <summary>
    /// True when the user holds the admin/operator role.
    /// Admins can manage global AKG rules and perform privileged operations.
    /// </summary>
    bool IsAdmin { get; }

    /// <summary>
    /// The identity's membership role within its tenant (C2, ADR-0012). Defaults to the safest role —
    /// providers that do not map roles yield <see cref="TenantRole.Viewer"/>.
    /// </summary>
    TenantRole Role { get; }
}
