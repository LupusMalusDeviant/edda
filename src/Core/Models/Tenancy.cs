namespace Edda.Core.Models;

/// <summary>
/// Constants for the logical multi-tenancy model (M3 / ADR-0012).
/// </summary>
public static class Tenants
{
    /// <summary>
    /// The single built-in tenant used by the standalone single-user build and by any data that predates
    /// multi-tenancy. Rules and callers that do not specify a tenant belong here, so existing behaviour is
    /// unchanged until tenant isolation is enforced.
    /// </summary>
    public const string DefaultTenantId = "default";
}

/// <summary>Membership role of the current identity within its tenant (C2, ADR-0012 / FR-02).</summary>
public enum TenantRole
{
    /// <summary>Read-only access to the tenant's knowledge. The deny-by-default role (enum value 0).</summary>
    Viewer = 0,

    /// <summary>May create, edit and delete own rules and use the writing memory tools.</summary>
    Editor,

    /// <summary>May additionally mutate foreign/global rules and administer the tenant's knowledge.</summary>
    Owner,
}
