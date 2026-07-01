namespace Edda.Core.Models;

/// <summary>
/// Constants for the logical multi-tenancy model (M3 / ADR-0012). The role model (Owner/Editor/Viewer)
/// is introduced together with role enforcement in a later slice, to avoid an unused type here.
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
