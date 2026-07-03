using Edda.Core.Abstractions;

namespace Edda.Agent.Tools.Memory;

/// <summary>
/// Shared C2 role gate for the mutating memory tools (remember, forget, consolidate_memory,
/// rate_memory, manage_*): writing requires <see cref="Edda.Core.Models.TenantRole.Editor"/> or
/// higher. Tools never throw (Regel 5) — a denied mutation returns <c>ToolResult.Fail</c> with
/// <see cref="InsufficientRoleMessage"/>.
/// </summary>
internal static class MemoryToolAuthorization
{
    /// <summary>The uniform failure message returned when the role does not permit the mutation.</summary>
    internal const string InsufficientRoleMessage =
        "Insufficient role: mutating memory requires the Editor role in this tenant.";

    /// <summary>
    /// Whether the current identity may mutate its own user-scoped content. A missing authorizer
    /// (tests, hosts without the AKG layer) permits the mutation — the legacy behaviour.
    /// </summary>
    /// <param name="authorizer">The central authorizer, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the mutation is permitted.</returns>
    internal static bool MayMutate(IRuleAuthorizer? authorizer) => authorizer?.CanMutateOwn() ?? true;
}
