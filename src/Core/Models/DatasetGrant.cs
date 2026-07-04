namespace Edda.Core.Models;

/// <summary>
/// A single dataset access grant (ADR-0014, Slice 2): it gives <see cref="UserId"/> the role
/// <see cref="Role"/> on the dataset <see cref="DatasetId"/> (a provenance head id such as <c>git:my-repo</c>)
/// within <see cref="TenantId"/>. Any grant makes the dataset readable for the user; <see cref="TenantRole"/>
/// levels gate mutation and sharing.
/// </summary>
public sealed record DatasetGrant
{
    /// <summary>The tenant the grant belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>The dataset (provenance head id, e.g. <c>git:my-repo</c>) the grant applies to.</summary>
    public required string DatasetId { get; init; }

    /// <summary>The user the role is granted to.</summary>
    public required string UserId { get; init; }

    /// <summary>The granted role (Viewer reads; Editor mutates the dataset's rules; Owner also shares).</summary>
    public required TenantRole Role { get; init; }
}
