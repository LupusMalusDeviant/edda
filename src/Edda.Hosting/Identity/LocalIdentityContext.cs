using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.Hosting.Identity;

/// <summary>
/// Single local-user identity for the standalone, local-only deployment.
/// Every request is treated as the same local administrator, so owner-scoping and admin-gated
/// operations all resolve to one user. There is no multi-tenancy in this build.
/// </summary>
public sealed class LocalIdentityContext : IIdentityContext
{
    /// <inheritdoc />
    public string? UserId => "local";

    /// <inheritdoc />
    public string? Username => "local";

    /// <inheritdoc />
    public string TenantId => "default";

    /// <inheritdoc />
    public bool IsClone => false;

    /// <inheritdoc />
    public bool IsAdmin => true;

    /// <inheritdoc />
    public TenantRole Role => TenantRole.Owner;
}
