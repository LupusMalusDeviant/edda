namespace Edda.Core.Models;

/// <summary>
/// Request body for sharing a dataset (ADR-0014): grants <see cref="Role"/> to <see cref="UserId"/> on the
/// dataset addressed by the route. The tenant and the acting user are taken from the authenticated identity,
/// never from this body (Regel 6).
/// </summary>
public sealed record DatasetShareRequest
{
    /// <summary>The user the role is granted to.</summary>
    public required string UserId { get; init; }

    /// <summary>The role name to grant (<c>Viewer</c>, <c>Editor</c> or <c>Owner</c>).</summary>
    public required string Role { get; init; }
}
