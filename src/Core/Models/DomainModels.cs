namespace Edda.Core.Models;

/// <summary>
/// Represents a domain node in the AKG domain hierarchy.
/// Domains can have sub-domains forming a tree structure.
/// </summary>
/// <param name="Name">Unique domain name (e.g. "csharp", "security").</param>
/// <param name="Label">Human-readable display label.</param>
/// <param name="Description">Optional description of the domain's scope.</param>
/// <param name="ParentDomain">Name of the parent domain, or null for root domains.</param>
/// <param name="SubDomains">Names of direct child domains.</param>
/// <param name="IsCore">True for built-in system domains that cannot be deleted.</param>
public sealed record DomainNode(
    string Name,
    string Label,
    string? Description,
    string? ParentDomain,
    IReadOnlyList<string> SubDomains,
    bool IsCore);
